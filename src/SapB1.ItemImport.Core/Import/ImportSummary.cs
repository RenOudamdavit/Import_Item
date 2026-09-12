namespace SapB1.ItemImport.Core.Import;

/// <summary>Aggregate result of one import run.</summary>
public sealed class ImportSummary
{
    public required bool DryRun { get; init; }

    public required int TotalRows { get; init; }

    public required int Created { get; init; }

    public required int Updated { get; init; }

    public required int Skipped { get; init; }

    public required int ValidationFailed { get; init; }

    public required int SapRejected { get; init; }

    public required int NotFound { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary><c>true</c> when the run stopped early (error limit, cancellation or a fatal SAP failure).</summary>
    public required bool Aborted { get; init; }

    public string? AbortReason { get; init; }

    /// <summary>First few failures, so a caller can print something useful without re-reading the report.</summary>
    public IReadOnlyList<ItemImportRowResult> SampleFailures { get; init; } = Array.Empty<ItemImportRowResult>();

    public int Failed => ValidationFailed + SapRejected + NotFound;

    /// <summary><c>true</c> when every row was processed and none failed.</summary>
    public bool IsSuccess => !Aborted && Failed == 0;
}
