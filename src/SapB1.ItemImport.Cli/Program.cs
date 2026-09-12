using System.Globalization;
using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Mapping;

namespace SapB1.ItemImport.Cli;

/// <summary>Command line entry point for the SAP Business One item import.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        CliArguments cli;

        try
        {
            cli = CliArguments.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run with --help for the list of options.");
            return ExitCodes.Usage;
        }

        if (args.Length == 0 || cli.ShowHelp)
        {
            PrintHelp();
            return cli.ShowHelp ? ExitCodes.Success : ExitCodes.Usage;
        }

        if (cli.ShowFields)
        {
            PrintFields();
            return ExitCodes.Success;
        }

        var logger = new ConsoleImportLogger(cli.Verbose ? LogLevel.Debug : LogLevel.Information);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            // Cancel cooperatively: the report on disk stays consistent with what SAP actually received.
            eventArgs.Cancel = true;
            logger.Warn("Cancellation requested — finishing the current item and closing the report.");
            cancellation.Cancel();
        };

        AppConfig config;

        try
        {
            config = AppConfig.Load(cli.ConfigPath);
        }
        catch (UsageException ex)
        {
            logger.Error(ex.Message);
            return ExitCodes.Usage;
        }

        logger.Info(
            config.SourcePath is null
                ? "No configuration file found — using command line arguments and environment variables only."
                : $"Configuration file: {Path.GetFullPath(config.SourcePath)}");

        if (cli.TestConnection)
        {
            try
            {
                return await ConnectionTester
                    .RunAsync(OptionsComposer.ResolveServiceLayer(cli, config), logger, cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (UsageException ex)
            {
                logger.Error(ex.Message);
                return ExitCodes.Usage;
            }
            catch (ArgumentException ex)
            {
                logger.Error(ex.Message);
                return ExitCodes.Usage;
            }
            catch (SapConnectionException ex)
            {
                logger.Error(ex.Message);
                return ExitCodes.Aborted;
            }
            catch (OperationCanceledException)
            {
                logger.Warn("The connection test was cancelled.");
                return ExitCodes.Aborted;
            }
        }

        ResolvedOptions options;

        try
        {
            options = OptionsComposer.Resolve(cli, config);
        }
        catch (UsageException ex)
        {
            logger.Error(ex.Message);
            return ExitCodes.Usage;
        }
        catch (ArgumentException ex)
        {
            logger.Error(ex.Message);
            return ExitCodes.Usage;
        }

        try
        {
            var runner = new ImportRunner(options, logger);
            return await runner.RunAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (UsageException ex)
        {
            logger.Error(ex.Message);
            return ExitCodes.Usage;
        }
        catch (CsvFormatException ex)
        {
            logger.Error($"The source file could not be read — {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (SapConnectionException ex)
        {
            logger.Error(ex.Message);
            return ExitCodes.Aborted;
        }
        catch (OperationCanceledException)
        {
            logger.Warn("The import was cancelled.");
            return ExitCodes.Aborted;
        }
        catch (IOException ex)
        {
            logger.Error($"A file operation failed: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Error($"Access denied: {ex.Message}");
            return ExitCodes.Usage;
        }
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine(
            """
            sapb1-import-items — import item master data into SAP Business One via the Service Layer.

            USAGE
              sapb1-import-items --file <items.csv> [options]

            SOURCE
              -f, --file <path>              Delimited source file. Required.
                  --delimiter <char|name>    Field delimiter: a single character, or comma|semicolon|tab|pipe.
                                             Auto-detected from the header row when omitted.
                  --encoding <name>          utf-8 (default), utf-16, utf-16be or latin1. A byte-order
                                             mark in the file always wins.
                  --culture <name>           Culture for parsing numbers and dates, e.g. de-DE for
                                             comma decimals. Invariant by default.

            MAPPING
                  --mapping <path>           JSON file mapping source columns to SAP properties.
                  --unknown-columns <mode>   ignore | warn (default) | error.
                  --null-token <text>        Cell value meaning "clear this field in SAP". Default <NULL>.
                  --item-code-pattern <re>   Regular expression every item code must match.
                  --default-price-list <n>   Price list for a bare Price column. Default 1.
                  --show-fields              List the SAP properties and column aliases understood, then exit.

            BEHAVIOUR
                  --mode <mode>              upsert (default) | create-only | update-only.
                  --dry-run                  Validate and report without writing anything to SAP.
                  --max-errors <n>           Stop after n failed rows. 0 (default) means never stop.
                  --batch-size <n>           Item codes checked for existence per request. Default 50.
                  --allow-duplicates         Permit the same item code more than once in the file.

            REPORT
                  --report <path>            Report CSV. Default out/item-import-<timestamp>.csv.
                  --failures-only            Write only failed rows to the report.

            CONNECTION
                  --test-connection          Log in to SAP, report the company and version, and exit.
                                             Needs no source file. Run this first.
              -c, --config <path>            Configuration file. When omitted, looks for
                                             appsettings.Local.json then appsettings.json, in the
                                             working directory then beside the executable.
                  --url <url>                Service Layer root, e.g. https://sap:50000/b1s/v1.
                  --company <db>             Company database, e.g. SBODEMOGB.
                  --user <name>              SAP Business One user name.
                  --certificate-thumbprint <hex>
                                             Expected server certificate thumbprint (certificate pinning).
                  --trust-any-certificate    Skip TLS validation. Insecure; use pinning instead.

              The password is read from SAPB1_PASSWORD or the configuration file only — never from an
              argument, because arguments are visible in the process list and shell history.
              SAPB1_URL, SAPB1_COMPANY, SAPB1_USER, SAPB1_CERT_THUMBPRINT and SAPB1_TRUST_ANY_CERT are
              also honoured. Precedence: command line, then environment, then configuration file.

            OTHER
              -v, --verbose                  Debug-level logging.
              -h, --help                     Show this help.

            EXIT CODES
              0 success   1 completed with failed rows   2 stopped early   3 bad usage or configuration

            EXAMPLES
              # Check a file without touching SAP
              sapb1-import-items --file items.csv --dry-run

              # Create new items only, stop after 10 bad rows
              sapb1-import-items --file items.csv --mode create-only --max-errors 10

              # European export: semicolon separated, comma decimals, legacy encoding
              sapb1-import-items --file artikel.csv --delimiter semicolon --culture de-DE --encoding latin1
            """);
    }

    private static void PrintFields()
    {
        Console.Out.WriteLine("SAP Business One item properties recognised by this importer:");
        Console.Out.WriteLine();
        const string propertyHeading = "SAP property";
        const string typeHeading = "Type";
        const string maxHeading = "Max";

        Console.Out.WriteLine($"  {propertyHeading,-24} {typeHeading,-12} {maxHeading,-5} Description");
        Console.Out.WriteLine($"  {new string('-', 24)} {new string('-', 12)} {new string('-', 5)} {new string('-', 40)}");

        foreach (var field in ItemFieldCatalog.All)
        {
            var max = field.MaxLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            Console.Out.WriteLine($"  {field.SapProperty,-24} {field.Kind,-12} {max,-5} {field.Description}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            """
            Column names are matched ignoring case, spaces, underscores and hyphens, so "Item Code",
            "item_code" and "ITEMCODE" all resolve to ItemCode. Common business aliases are accepted too
            (Description, SKU, Warehouse, Preferred Vendor, Min Stock, Active, Remarks, ...).

            Prices: PriceList / Price / Currency, and numbered variants PriceList2 / Price2 / Currency2
            for further price lists. A bare Price column uses --default-price-list.

            User-defined fields: any column named U_<FieldName> is sent through unchanged.

            Anything else: name the column after the SAP property, or map it with --mapping.
            """);
    }
}
