using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Mapping;
using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Cli;

/// <summary>Everything the run needs, after command line, environment and file configuration are merged.</summary>
public sealed class ResolvedOptions
{
    public required string SourceFile { get; init; }

    public required ServiceLayerOptions ServiceLayer { get; init; }

    public required ImportOptions Import { get; init; }

    public required CsvReaderOptions Csv { get; init; }

    public required ItemRecordBuilderOptions Builder { get; init; }

    public required string ReportPath { get; init; }

    public required bool FailuresOnly { get; init; }

    public required LogLevel LogLevel { get; init; }
}
