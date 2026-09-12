namespace SapB1.ItemImport.Core.Abstractions;

/// <summary>
/// SAP refused one specific item. The row is recorded as failed and the import continues, because a
/// single bad master-data row must never abort a migration of thousands.
/// </summary>
public class SapItemRejectedException : Exception
{
    public SapItemRejectedException(string message, string? sapErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        SapErrorCode = sapErrorCode;
    }

    /// <summary>SAP Business One error code, e.g. <c>-2035</c>, when the server supplied one.</summary>
    public string? SapErrorCode { get; }
}

/// <summary>
/// The connection, session or configuration is broken. The import stops: continuing would only
/// produce thousands of identical failures.
/// </summary>
public class SapConnectionException : Exception
{
    public SapConnectionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
