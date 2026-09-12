namespace SapB1.ItemImport.ServiceLayer;

/// <summary>Connection settings for the SAP Business One Service Layer.</summary>
public sealed class ServiceLayerOptions
{
    /// <summary>
    /// Service Layer root, e.g. <c>https://sap-b1.example.local:50000/b1s/v1</c>. If the
    /// <c>/b1s/v1</c> suffix is missing it is appended.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Company database, e.g. <c>SBODEMOGB</c>.</summary>
    public string CompanyDb { get; set; } = string.Empty;

    /// <summary>SAP Business One user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>SAP Business One password. Supply it through configuration or environment, never on the command line.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Per-request timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Retry attempts for transient failures (service unavailable, gateway timeout, throttling).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for the exponential backoff between retries.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Retry creates (<c>POST</c>) after a transient failure.
    /// </summary>
    /// <remarks>
    /// Off by default, and that default is deliberate: a <c>POST</c> that times out may still have
    /// been committed by SAP, so retrying it risks a duplicate item. A failed create is reported
    /// instead, and re-running the file in <c>Upsert</c> mode reconciles it safely.
    /// </remarks>
    public bool RetryWritesOnTransientFailure { get; set; }

    /// <summary>
    /// Send <c>B1S-ReplaceCollectionsOnPatch</c> when updating an item that carries prices, so the
    /// supplied price rows replace the stored collection instead of being merged by position.
    /// </summary>
    public bool ReplaceCollectionsOnPatch { get; set; } = true;

    /// <summary>Item codes per existence-probe request. Keep it modest: the codes go into the URL.</summary>
    public int ExistenceFilterChunkSize { get; set; } = 20;

    /// <summary>
    /// Expected server certificate thumbprint (SHA-1, spaces and colons ignored). Pinning is the
    /// right way to accept the self-signed certificate a default on-premise Service Layer ships with.
    /// </summary>
    public string? ServerCertificateThumbprint { get; set; }

    /// <summary>
    /// Skip TLS validation entirely. Insecure — it accepts any certificate and therefore any
    /// man-in-the-middle. Prefer <see cref="ServerCertificateThumbprint"/>, or install the SAP
    /// certificate into the machine trust store.
    /// </summary>
    public bool TrustAnyServerCertificate { get; set; }

    /// <summary>Normalises <see cref="BaseUrl"/> and rejects unusable configuration with a readable message.</summary>
    public Uri Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            problems.Add("BaseUrl is required (for example https://sap-b1:50000/b1s/v1).");
        }

        if (string.IsNullOrWhiteSpace(CompanyDb))
        {
            problems.Add("CompanyDb is required.");
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            problems.Add("UserName is required.");
        }

        if (string.IsNullOrEmpty(Password))
        {
            problems.Add("Password is required. Set it in configuration or the SAPB1_PASSWORD environment variable.");
        }

        if (MaxRetries < 0)
        {
            problems.Add("MaxRetries cannot be negative.");
        }

        if (ExistenceFilterChunkSize < 1)
        {
            problems.Add("ExistenceFilterChunkSize must be at least 1.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            problems.Add("Timeout must be greater than zero.");
        }

        if (TrustAnyServerCertificate && !string.IsNullOrWhiteSpace(ServerCertificateThumbprint))
        {
            problems.Add("Set either ServerCertificateThumbprint or TrustAnyServerCertificate, not both.");
        }

        Uri? baseUri = null;
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var normalized = BaseUrl.Trim().TrimEnd('/');

            if (!normalized.Contains("/b1s/", StringComparison.OrdinalIgnoreCase))
            {
                normalized += "/b1s/v1";
            }

            if (!Uri.TryCreate(normalized + "/", UriKind.Absolute, out baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
            {
                problems.Add($"BaseUrl '{BaseUrl}' is not a valid absolute http(s) URL.");
                baseUri = null;
            }
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException(
                "The SAP Service Layer configuration is incomplete:" + Environment.NewLine
                + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }

        return baseUri!;
    }

    /// <summary>Normalised thumbprint for comparison, or <c>null</c> when pinning is not configured.</summary>
    public string? NormalizedThumbprint()
    {
        if (string.IsNullOrWhiteSpace(ServerCertificateThumbprint))
        {
            return null;
        }

        return ServerCertificateThumbprint
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
    }
}
