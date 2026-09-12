namespace SapB1.ItemImport.Cli;

/// <summary>Process exit codes, so a scheduler or CI job can react without parsing output.</summary>
public static class ExitCodes
{
    /// <summary>Every row was processed and none failed.</summary>
    public const int Success = 0;

    /// <summary>The whole file was processed but some rows failed; see the report.</summary>
    public const int CompletedWithFailures = 1;

    /// <summary>The run stopped early — error limit, cancellation, or SAP became unreachable.</summary>
    public const int Aborted = 2;

    /// <summary>Bad arguments, bad configuration, or an unusable source file. Nothing was sent to SAP.</summary>
    public const int Usage = 3;
}
