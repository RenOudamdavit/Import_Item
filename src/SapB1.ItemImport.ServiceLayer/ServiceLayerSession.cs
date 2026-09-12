using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Logging;

namespace SapB1.ItemImport.ServiceLayer;

/// <summary>
/// Owns the HTTP conversation with the SAP Business One Service Layer: login, the session cookie,
/// transparent re-login when the session expires, and retries for transient transport failures.
/// </summary>
/// <remarks>
/// Service Layer sessions expire (30 minutes by default) which a long item import will certainly hit,
/// so a 401 is treated as "log in again and repeat", not as a failure. Retry policy distinguishes
/// idempotent calls from creates; see <see cref="ServiceLayerOptions.RetryWritesOnTransientFailure"/>.
/// </remarks>
public sealed class ServiceLayerSession : IAsyncDisposable
{
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private static readonly HttpStatusCode[] TransientStatusCodes =
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    };

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly CookieContainer? _cookies;
    private readonly ServiceLayerOptions _options;
    private readonly IImportLogger _logger;
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    private bool _loggedIn;
    private bool _disposed;

    private ServiceLayerSession(
        HttpClient client,
        bool ownsClient,
        CookieContainer? cookies,
        ServiceLayerOptions options,
        Uri baseUri,
        IImportLogger logger)
    {
        _client = client;
        _ownsClient = ownsClient;
        _cookies = cookies;
        _options = options;
        _logger = logger;
        BaseUri = baseUri;
    }

    /// <summary>Service Layer root, always with a trailing slash so relative URIs resolve correctly.</summary>
    public Uri BaseUri { get; }

    /// <summary>SAP Business One version reported at login, once connected.</summary>
    public string? Version { get; private set; }

    /// <summary>Builds a session with its own <see cref="HttpClient"/>.</summary>
    public static ServiceLayerSession Create(ServiceLayerOptions options, IImportLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var log = logger ?? NullImportLogger.Instance;
        var baseUri = options.Validate();
        var cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        ConfigureCertificateValidation(handler, options, log);

        HttpClient client;
        try
        {
            client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = baseUri,
                Timeout = options.Timeout,
            };
        }
        catch
        {
            handler.Dispose();
            throw;
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SapB1-ItemImport/1.0");

        return new ServiceLayerSession(client, ownsClient: true, cookies, options, baseUri, log);
    }

    /// <summary>
    /// Builds a session over a caller-supplied client. Used by tests to substitute the transport.
    /// The caller keeps ownership of <paramref name="client"/>.
    /// </summary>
    public static ServiceLayerSession CreateWithClient(
        ServiceLayerOptions options,
        HttpClient client,
        IImportLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);

        var baseUri = options.Validate();
        client.BaseAddress ??= baseUri;

        return new ServiceLayerSession(
            client,
            ownsClient: false,
            cookies: null,
            options,
            baseUri,
            logger ?? NullImportLogger.Instance);
    }

    /// <summary>Logs in, replacing any existing session.</summary>
    public async Task LoginAsync(CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoginCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    /// <summary>
    /// Sends a request, logging in first if needed.
    /// </summary>
    /// <param name="requestFactory">
    /// Builds the request. A factory rather than an instance because an <see cref="HttpRequestMessage"/>
    /// cannot be sent twice, and retries need a fresh one.
    /// </param>
    /// <param name="isIdempotent">
    /// <c>true</c> for reads and for <c>PATCH</c> with a fixed payload — safe to repeat. <c>false</c>
    /// for creates.
    /// </param>
    public async Task<ServiceLayerResponse> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        bool isIdempotent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestFactory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureLoggedInAsync(cancellationToken).ConfigureAwait(false);

        var attempt = 0;
        var reloginAttempted = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = requestFactory();
            HttpStatusCode status;
            string body;

            try
            {
                using var response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                    .ConfigureAwait(false);

                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (!CanRetry(attempt, isIdempotent))
                {
                    throw new SapConnectionException(
                        $"The request to the SAP Service Layer at {BaseUri} failed: {ex.Message}",
                        ex);
                }

                attempt++;
                _logger.Warn($"Service Layer transport error (attempt {attempt} of {_options.MaxRetries}): {ex.Message}");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (!CanRetry(attempt, isIdempotent))
                {
                    throw new SapConnectionException(
                        $"The request to the SAP Service Layer timed out after {_options.Timeout.TotalSeconds:F0}s.",
                        ex);
                }

                attempt++;
                _logger.Warn($"Service Layer request timed out (attempt {attempt} of {_options.MaxRetries}).");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (status == HttpStatusCode.Unauthorized && !reloginAttempted)
            {
                // The session expired mid-import. SAP rejected the call before processing it, so
                // repeating it after a fresh login is safe even for a create.
                reloginAttempted = true;
                _logger.Debug("Service Layer session is no longer valid; logging in again.");
                await ReloginAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (IsTransient(status) && CanRetry(attempt, isIdempotent))
            {
                attempt++;
                _logger.Warn($"Service Layer returned {(int)status} {status} (attempt {attempt} of {_options.MaxRetries}).");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return new ServiceLayerResponse(status, body);
        }
    }

    /// <summary><c>true</c> when the status is one the Service Layer uses for transient conditions.</summary>
    public static bool IsTransient(HttpStatusCode status) => Array.IndexOf(TransientStatusCodes, status) >= 0;

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    /// <summary>Ends the SAP session. Best effort: a failed logout is logged, never thrown.</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (!_loggedIn)
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "Logout"));
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.Debug($"Service Layer logout returned {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ObjectDisposedException)
        {
            _logger.Debug($"Service Layer logout failed and was ignored: {ex.Message}");
        }
        finally
        {
            _loggedIn = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await LogoutAsync(timeout.Token).ConfigureAwait(false);

        if (_ownsClient)
        {
            _client.Dispose();
        }

        _loginGate.Dispose();
    }

    private async Task EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        if (_loggedIn)
        {
            return;
        }

        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loggedIn)
            {
                await LoginCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task ReloginAsync(CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _loggedIn = false;
            ClearSessionCookies();
            await LoginCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task LoginCoreAsync(CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new LoginRequest
        {
            CompanyDB = _options.CompanyDb,
            UserName = _options.UserName,
            Password = _options.Password,
        });

        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "Login"))
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };

            HttpStatusCode status;
            string body;

            try
            {
                using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException)
                                       && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.MaxRetries)
                {
                    throw new SapConnectionException(
                        $"Could not reach the SAP Service Layer at {BaseUri}. Check the URL, the port (50000 by default) "
                        + $"and that the Service Layer is running. Last error: {ex.Message}",
                        ex);
                }

                attempt++;
                _logger.Warn($"SAP login attempt {attempt} of {_options.MaxRetries} failed: {ex.Message}");
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!IsSuccess(status))
            {
                if (IsTransient(status) && attempt < _options.MaxRetries)
                {
                    attempt++;
                    _logger.Warn($"SAP login returned {(int)status} {status}; retrying ({attempt} of {_options.MaxRetries}).");
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var (code, message) = ServiceLayerErrorParser.Parse(body);
                var codeSuffix = code is null ? string.Empty : $" [SAP {code}]";

                throw new SapConnectionException(
                    $"SAP login failed for user '{_options.UserName}' on company '{_options.CompanyDb}' "
                    + $"({(int)status} {status}){codeSuffix}: {message}");
            }

            Version = ReadVersion(body);
            _loggedIn = true;
            _logger.Info(
                $"Connected to the SAP Business One Service Layer at {BaseUri} (company {_options.CompanyDb}"
                + (Version is null ? ")." : $", version {Version})."));
            return;
        }
    }

    private static string? ReadVersion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Version", out var version)
                && version.ValueKind == JsonValueKind.String)
            {
                return version.GetString();
            }
        }
        catch (JsonException)
        {
            // The session cookie is what matters; a missing version is not worth failing over.
        }

        return null;
    }

    private void ClearSessionCookies()
    {
        if (_cookies is null)
        {
            return;
        }

        foreach (Cookie cookie in _cookies.GetCookies(BaseUri))
        {
            cookie.Expired = true;
        }
    }

    private bool CanRetry(int attempt, bool isIdempotent)
    {
        if (attempt >= _options.MaxRetries)
        {
            return false;
        }

        return isIdempotent || _options.RetryWritesOnTransientFailure;
    }

    private async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var exponential = _options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.Next(0, 250);
        var delay = TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, MaxRetryDelay.TotalMilliseconds));

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureCertificateValidation(
        HttpClientHandler handler,
        ServiceLayerOptions options,
        IImportLogger logger)
    {
        var thumbprint = options.NormalizedThumbprint();

        if (thumbprint is not null)
        {
            handler.ServerCertificateCustomValidationCallback =
                (_, certificate, _, errors) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    var matches = string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);

                    if (matches && errors != SslPolicyErrors.None)
                    {
                        logger.Debug(
                            $"Accepting the SAP server certificate on a pinned thumbprint match despite {errors}.");
                    }

                    return matches;
                };

            return;
        }

        if (options.TrustAnyServerCertificate)
        {
            logger.Warn(
                "TLS certificate validation is DISABLED for the SAP connection. This accepts any certificate, "
                + "including an attacker's. Use ServerCertificateThumbprint, or install the SAP certificate in the "
                + "machine trust store, before running this against production.");

            handler.ServerCertificateCustomValidationCallback =
                (_, _, _, _) => true;

            return;
        }

        // Otherwise: default validation against the OS trust store, which is what production should use.
    }

    private sealed class LoginRequest
    {
        // Property names match the Service Layer contract exactly; do not rename.
        public string CompanyDB { get; init; } = string.Empty;

        public string UserName { get; init; } = string.Empty;

        public string Password { get; init; } = string.Empty;
    }
}
