using System.Net;
using System.Text;
using System.Text.Json;
using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Logging;

namespace SapB1.ItemImport.ServiceLayer;

/// <summary>
/// <see cref="IItemGateway"/> over the SAP Business One Service Layer (OData v1 / REST).
/// </summary>
/// <remarks>
/// The Service Layer is the portable option: it needs no SAP client installation, runs against both
/// SQL Server and HANA companies, and works from Linux containers as well as Windows. For shops that
/// must go through the DI API, swap in that gateway — nothing above this interface changes.
/// </remarks>
public sealed class ServiceLayerItemGateway : IItemGateway
{
    private readonly ServiceLayerSession _session;
    private readonly ServiceLayerOptions _options;
    private readonly IImportLogger _logger;
    private readonly bool _ownsSession;

    public ServiceLayerItemGateway(
        ServiceLayerSession session,
        ServiceLayerOptions options,
        IImportLogger? logger = null,
        bool ownsSession = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        _session = session;
        _options = options;
        _logger = logger ?? NullImportLogger.Instance;
        _ownsSession = ownsSession;
    }

    /// <summary>Creates a gateway that owns its own session.</summary>
    public static ServiceLayerItemGateway Create(ServiceLayerOptions options, IImportLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var session = ServiceLayerSession.Create(options, logger);
        try
        {
            return new ServiceLayerItemGateway(session, options, logger, ownsSession: true);
        }
        catch
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public string Description => $"SAP Business One Service Layer at {_session.BaseUri}";

    public Task ConnectAsync(CancellationToken cancellationToken) => _session.LoginAsync(cancellationToken);

    public async Task<HashSet<string>> GetExistingItemCodesAsync(
        IReadOnlyCollection<string> itemCodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemCodes);

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (itemCodes.Count == 0)
        {
            return existing;
        }

        var distinct = itemCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var chunk in distinct.Chunk(_options.ExistenceFilterChunkSize))
        {
            var filter = string.Join(
                " or ",
                chunk.Select(code => $"ItemCode eq '{EscapeODataLiteral(code)}'"));

            var next = $"Items?$select=ItemCode&$filter={Uri.EscapeDataString(filter)}";

            // The Service Layer pages results (20 rows by default), so follow nextLink rather than
            // assuming one response covers the chunk.
            while (next is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = new Uri(_session.BaseUri, next);
                var response = await _session
                    .SendAsync(() => CreateProbeRequest(target, chunk.Length), isIdempotent: true, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccess)
                {
                    var (code, message) = ServiceLayerErrorParser.Parse(response.Body);
                    throw new SapConnectionException(
                        $"Could not check which items already exist ({(int)response.StatusCode} {response.StatusCode})"
                        + $"{(code is null ? string.Empty : $" [SAP {code}]")}: {message}");
                }

                next = ReadItemCodesPage(response.Body, existing);
            }
        }

        return existing;
    }

    public async Task CreateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var payload = ItemPayloadBuilder.BuildCreatePayload(record);
        var target = new Uri(_session.BaseUri, "Items");

        var response = await _session.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, target) { Content = JsonBody(payload) },
            isIdempotent: false,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            throw Translate(response, record.ItemCode, "create");
        }

        _logger.Debug($"Created item {record.ItemCode}.");
    }

    public async Task UpdateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Fields.Count == 0 && record.Prices.Count == 0)
        {
            _logger.Debug($"Item {record.ItemCode} has nothing to update; skipping the call.");
            return;
        }

        var payload = ItemPayloadBuilder.BuildUpdatePayload(record);
        var target = new Uri(_session.BaseUri, $"Items('{Uri.EscapeDataString(EscapeODataLiteral(record.ItemCode))}')");
        var replaceCollections = _options.ReplaceCollectionsOnPatch && record.Prices.Count > 0;

        var response = await _session.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Patch, target) { Content = JsonBody(payload) };

                if (replaceCollections)
                {
                    // Without this the supplied price rows are merged positionally into the stored
                    // collection, which silently rewrites unrelated price lists.
                    request.Headers.TryAddWithoutValidation("B1S-ReplaceCollectionsOnPatch", "true");
                }

                return request;
            },
            isIdempotent: true,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            throw Translate(response, record.ItemCode, "update");
        }

        _logger.Debug($"Updated item {record.ItemCode}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsSession)
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Doubles single quotes, as OData string literals require.</summary>
    public static string EscapeODataLiteral(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

    private static HttpRequestMessage CreateProbeRequest(Uri target, int chunkSize)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, target);

        // Ask for the whole chunk in one page; nextLink is still honoured if the server caps it lower.
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.maxpagesize={chunkSize}");

        return request;
    }

    private static StringContent JsonBody(string payload)
        => new(payload, Encoding.UTF8, "application/json");

    /// <summary>
    /// Reads one page of the probe response into <paramref name="existing"/> and returns the next
    /// link, or <c>null</c> when the page is the last one.
    /// </summary>
    private static string? ReadItemCodesPage(string body, HashSet<string> existing)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (root.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in values.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("ItemCode", out var itemCode)
                    && itemCode.ValueKind == JsonValueKind.String)
                {
                    var code = itemCode.GetString();
                    if (!string.IsNullOrEmpty(code))
                    {
                        existing.Add(code);
                    }
                }
            }
        }

        // v1 uses "odata.nextLink"; newer builds also accept the "@odata.nextLink" spelling.
        foreach (var name in new[] { "odata.nextLink", "@odata.nextLink" })
        {
            if (root.TryGetProperty(name, out var nextLink)
                && nextLink.ValueKind == JsonValueKind.String)
            {
                var value = nextLink.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static Exception Translate(ServiceLayerResponse response, string itemCode, string operation)
    {
        var (code, message) = ServiceLayerErrorParser.Parse(response.Body);
        var codeSuffix = code is null ? string.Empty : $" [SAP {code}]";

        // Transient and authorisation failures are about the connection, not the row: aborting beats
        // marking every remaining item as rejected.
        if (ServiceLayerSession.IsTransient(response.StatusCode)
            || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new SapConnectionException(
                $"SAP could not {operation} item '{itemCode}' because the connection or the account is not usable "
                + $"({(int)response.StatusCode} {response.StatusCode}){codeSuffix}: {message}");
        }

        return new SapItemRejectedException($"SAP refused to {operation} the item{codeSuffix}: {message}", code);
    }
}
