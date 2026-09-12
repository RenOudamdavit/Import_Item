using System.Globalization;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Mapping;
using SapB1.ItemImport.Core.Reporting;
using SapB1.ItemImport.Core.Validation;
using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Cli;

/// <summary>Composes the reader, the mapper, the SAP gateway and the report writer into one run.</summary>
public sealed class ImportRunner
{
    private readonly ResolvedOptions _options;
    private readonly IImportLogger _logger;

    public ImportRunner(ResolvedOptions options, IImportLogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <summary>Runs the import and returns the process exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var reader = CsvTableReader.Open(_options.SourceFile, _options.Csv);

        _logger.Info(
            $"Reading '{_options.SourceFile}' ({reader.Header.Count} columns, delimiter '{Describe(reader.Delimiter)}').");

        var builder = new ItemRecordBuilder(reader.Header, _options.Builder);

        foreach (var message in builder.HeaderMessages)
        {
            if (message.Severity == ValidationSeverity.Error)
            {
                _logger.Error($"Header problem — {message}");
            }
            else
            {
                _logger.Warn($"Header — {message}");
            }
        }

        if (!builder.IsHeaderValid)
        {
            _logger.Error("The file header is unusable, so nothing was sent to SAP. Fix the columns and run again.");
            return ExitCodes.Usage;
        }

        var mapped = builder.MappedProperties;
        _logger.Info(
            mapped.Count == 0
                ? "No item properties are mapped; only price rows (if any) would be written."
                : $"Mapping {mapped.Count} propert{(mapped.Count == 1 ? "y" : "ies")}: {string.Join(", ", mapped)}.");

        if (_options.Import.DryRun)
        {
            _logger.Warn("DRY RUN — nothing will be written to SAP. Existence is still checked so the report is accurate.");
        }

        await using var gateway = ServiceLayerItemGateway.Create(_options.ServiceLayer, _logger);
        await gateway.ConnectAsync(cancellationToken).ConfigureAwait(false);

        await using var sink = CsvImportReportWriter.Create(_options.ReportPath, _options.FailuresOnly);

        var service = new ItemImportService(gateway, _options.Import, _logger);
        var rows = reader.ReadRows().Select(builder.Build);

        var summary = await service.ImportAsync(rows, sink, cancellationToken).ConfigureAwait(false);

        PrintSummary(summary);

        if (summary.Aborted)
        {
            return ExitCodes.Aborted;
        }

        return summary.Failed > 0 ? ExitCodes.CompletedWithFailures : ExitCodes.Success;
    }

    private void PrintSummary(ImportSummary summary)
    {
        var culture = CultureInfo.InvariantCulture;

        Console.Out.WriteLine();
        Console.Out.WriteLine(summary.DryRun ? "Dry run summary" : "Import summary");
        Console.Out.WriteLine("---------------");
        Console.Out.WriteLine($"  Rows read          : {summary.TotalRows.ToString(culture)}");
        Console.Out.WriteLine($"  {(summary.DryRun ? "Would create       " : "Created            ")}: {summary.Created.ToString(culture)}");
        Console.Out.WriteLine($"  {(summary.DryRun ? "Would update       " : "Updated            ")}: {summary.Updated.ToString(culture)}");
        Console.Out.WriteLine($"  Skipped            : {summary.Skipped.ToString(culture)}");
        Console.Out.WriteLine($"  Failed validation  : {summary.ValidationFailed.ToString(culture)}");
        Console.Out.WriteLine($"  Rejected by SAP    : {summary.SapRejected.ToString(culture)}");
        Console.Out.WriteLine($"  Not found in SAP   : {summary.NotFound.ToString(culture)}");
        Console.Out.WriteLine($"  Duration           : {summary.Duration.TotalSeconds.ToString("F1", culture)}s");
        Console.Out.WriteLine($"  Report             : {Path.GetFullPath(_options.ReportPath)}");

        if (summary.SampleFailures.Count > 0)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine($"First {summary.SampleFailures.Count} failure(s):");

            foreach (var failure in summary.SampleFailures)
            {
                Console.Out.WriteLine($"  line {failure.SourceLineNumber} [{failure.ItemCode}] {failure.Status}: {failure.Detail}");
            }

            if (summary.Failed > summary.SampleFailures.Count)
            {
                Console.Out.WriteLine($"  ... and {summary.Failed - summary.SampleFailures.Count} more. See the report for the full list.");
            }
        }

        if (summary.Aborted)
        {
            Console.Out.WriteLine();
            _logger.Error($"The run stopped early: {summary.AbortReason}");
        }
    }

    private static string Describe(char delimiter) => delimiter switch
    {
        '\t' => "tab",
        _ => delimiter.ToString(),
    };
}
