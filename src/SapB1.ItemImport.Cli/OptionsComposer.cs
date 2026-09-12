using System.Globalization;
using System.Text;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Mapping;
using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Cli;

/// <summary>
/// Merges command line, environment variables and the configuration file into one settings object.
/// </summary>
/// <remarks>
/// Precedence is command line, then environment, then file, then defaults. Passwords are accepted from
/// environment or file only — never from an argument, because arguments are visible in the process list
/// and land in shell history.
/// </remarks>
public static class OptionsComposer
{
    public static ResolvedOptions Resolve(CliArguments cli, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(config);

        var sourceFile = cli.File;
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            throw new UsageException("No source file given. Pass --file <path-to-csv>.");
        }

        if (!File.Exists(sourceFile))
        {
            throw new UsageException($"The source file '{sourceFile}' does not exist.");
        }

        var serviceLayer = new ServiceLayerOptions
        {
            BaseUrl = First(cli.Url, Env("SAPB1_URL", "SAPB1_BASE_URL"), config.ServiceLayer.BaseUrl) ?? string.Empty,
            CompanyDb = First(cli.Company, Env("SAPB1_COMPANY", "SAPB1_COMPANY_DB"), config.ServiceLayer.CompanyDb) ?? string.Empty,
            UserName = First(cli.User, Env("SAPB1_USER", "SAPB1_USERNAME"), config.ServiceLayer.UserName) ?? string.Empty,
            Password = First(Env("SAPB1_PASSWORD"), config.ServiceLayer.Password) ?? string.Empty,
            ReplaceCollectionsOnPatch = config.ServiceLayer.ReplaceCollectionsOnPatch ?? true,
            RetryWritesOnTransientFailure = config.ServiceLayer.RetryWritesOnTransientFailure ?? false,
            ServerCertificateThumbprint = First(
                cli.CertificateThumbprint,
                Env("SAPB1_CERT_THUMBPRINT"),
                config.ServiceLayer.ServerCertificateThumbprint),
            TrustAnyServerCertificate = cli.TrustAnyCertificate
                || ParseBool(Env("SAPB1_TRUST_ANY_CERT"))
                || (config.ServiceLayer.TrustAnyServerCertificate ?? false),
        };

        if (config.ServiceLayer.TimeoutSeconds is { } timeoutSeconds)
        {
            if (timeoutSeconds <= 0)
            {
                throw new UsageException("ServiceLayer.TimeoutSeconds must be greater than zero.");
            }

            serviceLayer.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        if (config.ServiceLayer.MaxRetries is { } maxRetries)
        {
            serviceLayer.MaxRetries = maxRetries;
        }

        if (config.ServiceLayer.RetryBaseDelaySeconds is { } retryDelay)
        {
            if (retryDelay <= 0)
            {
                throw new UsageException("ServiceLayer.RetryBaseDelaySeconds must be greater than zero.");
            }

            serviceLayer.RetryBaseDelay = TimeSpan.FromSeconds(retryDelay);
        }

        if (config.ServiceLayer.ExistenceFilterChunkSize is { } chunkSize)
        {
            serviceLayer.ExistenceFilterChunkSize = chunkSize;
        }

        // Fails fast with a readable message rather than at the first HTTP call.
        serviceLayer.Validate();

        var import = new ImportOptions
        {
            Mode = ParseMode(First(cli.Mode, config.Import.Mode)),
            DryRun = cli.DryRun || (config.Import.DryRun ?? false),
            MaxErrors = cli.MaxErrors ?? config.Import.MaxErrors ?? 0,
            ExistenceProbeBatchSize = cli.BatchSize ?? config.Import.ExistenceProbeBatchSize ?? 50,
            AllowDuplicateItemCodes = cli.AllowDuplicates || (config.Import.AllowDuplicateItemCodes ?? false),
        };

        if (import.ExistenceProbeBatchSize < 1)
        {
            throw new UsageException("--batch-size must be 1 or greater.");
        }

        if (import.MaxErrors < 0)
        {
            throw new UsageException("--max-errors cannot be negative. Use 0 to process the whole file.");
        }

        var culture = ParseCulture(First(cli.Culture, config.Csv.Culture));

        var csv = new CsvReaderOptions
        {
            Delimiter = ParseDelimiter(First(cli.Delimiter, config.Csv.Delimiter)),
            Encoding = ParseEncoding(First(cli.Encoding, config.Csv.Encoding)),
            TrimValues = config.Csv.TrimValues ?? true,
        };

        var builder = new ItemRecordBuilderOptions
        {
            Conversion = new ValueConversionOptions
            {
                Culture = culture,
                NullToken = First(cli.NullToken, config.Mapping.NullToken) ?? "<NULL>",
            },
            UnknownColumns = ParseUnknownColumns(First(cli.UnknownColumns, config.Mapping.UnknownColumns)),
            DefaultPriceList = cli.DefaultPriceList ?? config.Mapping.DefaultPriceList ?? 1,
            ItemCodePattern = First(cli.ItemCodePattern, config.Mapping.ItemCodePattern),
            ColumnOverrides = ResolveColumnOverrides(cli.MappingPath, config.Mapping.Columns),
        };

        if (builder.DefaultPriceList < 1)
        {
            throw new UsageException("--default-price-list must be 1 or greater.");
        }

        var reportPath = First(cli.ReportPath, config.Import.ReportPath)
                         ?? Path.Combine(
                             "out",
                             $"item-import-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.csv");

        return new ResolvedOptions
        {
            SourceFile = sourceFile,
            ServiceLayer = serviceLayer,
            Import = import,
            Csv = csv,
            Builder = builder,
            ReportPath = reportPath,
            FailuresOnly = cli.FailuresOnly || (config.Import.FailuresOnly ?? false),
            LogLevel = cli.Verbose ? LogLevel.Debug : LogLevel.Information,
        };
    }

    private static IReadOnlyDictionary<string, string> ResolveColumnOverrides(
        string? mappingPath,
        Dictionary<string, string>? configured)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (configured is not null)
        {
            foreach (var (column, property) in configured)
            {
                overrides[column] = property;
            }
        }

        if (!string.IsNullOrWhiteSpace(mappingPath))
        {
            foreach (var (column, property) in AppConfig.LoadColumnMap(mappingPath))
            {
                overrides[column] = property;
            }
        }

        return overrides;
    }

    private static string? First(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static string? Env(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool ParseBool(string? value)
        => value is not null
           && (string.Equals(value, "1", StringComparison.Ordinal)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase));

    private static ImportMode ParseMode(string? value) => value switch
    {
        null => ImportMode.Upsert,
        _ => SapNameNormalizer.Normalize(value) switch
        {
            "upsert" => ImportMode.Upsert,
            "create" or "createonly" => ImportMode.CreateOnly,
            "update" or "updateonly" => ImportMode.UpdateOnly,
            _ => throw new UsageException(
                $"'{value}' is not a valid mode. Use upsert, create-only or update-only."),
        },
    };

    private static UnknownColumnBehavior ParseUnknownColumns(string? value) => value switch
    {
        null => UnknownColumnBehavior.Warn,
        _ => SapNameNormalizer.Normalize(value) switch
        {
            "ignore" => UnknownColumnBehavior.Ignore,
            "warn" or "warning" => UnknownColumnBehavior.Warn,
            "error" or "fail" => UnknownColumnBehavior.Error,
            _ => throw new UsageException(
                $"'{value}' is not a valid --unknown-columns value. Use ignore, warn or error."),
        },
    };

    private static char? ParseDelimiter(string? value)
    {
        if (value is null)
        {
            return null;
        }

        switch (value.ToLowerInvariant())
        {
            case "tab":
            case "\\t":
                return '\t';
            case "comma":
                return ',';
            case "semicolon":
                return ';';
            case "pipe":
                return '|';
        }

        if (value.Length != 1)
        {
            throw new UsageException(
                $"'{value}' is not a valid delimiter. Pass a single character, or one of comma, semicolon, tab, pipe.");
        }

        return value[0];
    }

    private static Encoding ParseEncoding(string? value)
    {
        if (value is null)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        return SapNameNormalizer.Normalize(value) switch
        {
            "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf16" or "unicode" or "utf16le" => Encoding.Unicode,
            "utf16be" => Encoding.BigEndianUnicode,
            "latin1" or "iso88591" or "windows1252" or "ansi" => Encoding.Latin1,
            _ => throw new UsageException(
                $"'{value}' is not a supported encoding. Use utf-8, utf-16, utf-16be or latin1."),
        };
    }

    private static CultureInfo ParseCulture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "invariant", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(value);
        }
        catch (CultureNotFoundException)
        {
            throw new UsageException($"'{value}' is not a known culture name. Use for example de-DE, fr-FR or invariant.");
        }
    }
}
