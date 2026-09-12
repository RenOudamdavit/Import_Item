using System.Text.Json;

namespace SapB1.ItemImport.Cli;

/// <summary>File-based configuration. Every value is nullable so "absent" differs from "set to default".</summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public ServiceLayerConfig ServiceLayer { get; set; } = new();

    public ImportConfig Import { get; set; } = new();

    public CsvConfig Csv { get; set; } = new();

    public MappingConfig Mapping { get; set; } = new();

    /// <summary>
    /// Loads configuration from <paramref name="path"/>, or from <c>appsettings.json</c> beside the
    /// executable / in the working directory when no path is given.
    /// </summary>
    public static AppConfig Load(string? path)
    {
        var resolved = ResolvePath(path);

        if (resolved is null)
        {
            return new AppConfig();
        }

        string json;
        try
        {
            json = File.ReadAllText(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UsageException($"Could not read the configuration file '{resolved}': {ex.Message}");
        }

        try
        {
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new UsageException($"The configuration file '{resolved}' is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads a column mapping file. Accepts either <c>{ "Columns": { "Artikelnr": "ItemCode" } }</c>
    /// or a flat <c>{ "Artikelnr": "ItemCode" }</c> object.
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadColumnMap(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new UsageException($"The mapping file '{path}' does not exist.");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UsageException($"Could not read the mapping file '{path}': {ex.Message}");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new UsageException($"The mapping file '{path}' must contain a JSON object.");
            }

            if (root.TryGetProperty("Columns", out var columns) && columns.ValueKind == JsonValueKind.Object)
            {
                root = columns;
            }

            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    // Explicit null is the documented way to drop a column.
                    map[property.Name] = string.Empty;
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new UsageException(
                        $"The mapping file '{path}' maps column '{property.Name}' to a {property.Value.ValueKind}; "
                        + "expected the SAP property name as a string.");
                }

                map[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException ex)
        {
            throw new UsageException($"The mapping file '{path}' is not valid JSON: {ex.Message}");
        }

        return map;
    }

    private static string? ResolvePath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (!File.Exists(path))
            {
                throw new UsageException($"The configuration file '{path}' does not exist.");
            }

            return path;
        }

        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.CurrentDirectory, "appsettings.json"),
                     Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed class ServiceLayerConfig
{
    public string? BaseUrl { get; set; }

    public string? CompanyDb { get; set; }

    public string? UserName { get; set; }

    /// <summary>Prefer the <c>SAPB1_PASSWORD</c> environment variable; keep this empty in committed files.</summary>
    public string? Password { get; set; }

    public int? TimeoutSeconds { get; set; }

    public int? MaxRetries { get; set; }

    public double? RetryBaseDelaySeconds { get; set; }

    public bool? RetryWritesOnTransientFailure { get; set; }

    public bool? ReplaceCollectionsOnPatch { get; set; }

    public int? ExistenceFilterChunkSize { get; set; }

    public string? ServerCertificateThumbprint { get; set; }

    public bool? TrustAnyServerCertificate { get; set; }
}

public sealed class ImportConfig
{
    public string? Mode { get; set; }

    public bool? DryRun { get; set; }

    public int? MaxErrors { get; set; }

    public int? ExistenceProbeBatchSize { get; set; }

    public bool? AllowDuplicateItemCodes { get; set; }

    public string? ReportPath { get; set; }

    public bool? FailuresOnly { get; set; }
}

public sealed class CsvConfig
{
    public string? Delimiter { get; set; }

    public string? Encoding { get; set; }

    public string? Culture { get; set; }

    public bool? TrimValues { get; set; }
}

public sealed class MappingConfig
{
    public string? UnknownColumns { get; set; }

    public int? DefaultPriceList { get; set; }

    public string? NullToken { get; set; }

    public string? ItemCodePattern { get; set; }

    public Dictionary<string, string>? Columns { get; set; }
}
