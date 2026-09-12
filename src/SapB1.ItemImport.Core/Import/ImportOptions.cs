namespace SapB1.ItemImport.Core.Import;

/// <summary>How existing and missing items are treated.</summary>
public enum ImportMode
{
    /// <summary>Create items that do not exist, update those that do. Default, and safe to re-run.</summary>
    Upsert = 0,

    /// <summary>Only create. Existing items are skipped — use this to protect data maintained in SAP.</summary>
    CreateOnly = 1,

    /// <summary>Only update. Unknown item codes are reported as errors.</summary>
    UpdateOnly = 2,
}

/// <summary>Runtime behaviour of the import pipeline.</summary>
public sealed class ImportOptions
{
    public ImportMode Mode { get; set; } = ImportMode.Upsert;

    /// <summary>Validate and report without writing anything to SAP. Existence is still probed (read-only).</summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Abort after this many failed rows. <c>0</c> means never abort. Defaults to 0 so an operator gets
    /// one complete error report instead of having to fix rows one run at a time.
    /// </summary>
    public int MaxErrors { get; set; }

    /// <summary>How many item codes to check for existence per round trip.</summary>
    public int ExistenceProbeBatchSize { get; set; } = 50;

    /// <summary>
    /// Allow the same item code to appear more than once in the source file. Off by default: in a data
    /// migration a repeated key is almost always a mistake, and the last row silently winning hides it.
    /// </summary>
    public bool AllowDuplicateItemCodes { get; set; }

    internal void Validate()
    {
        if (ExistenceProbeBatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExistenceProbeBatchSize),
                ExistenceProbeBatchSize,
                "Existence probe batch size must be at least 1.");
        }

        if (MaxErrors < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxErrors), MaxErrors, "MaxErrors cannot be negative.");
        }
    }
}
