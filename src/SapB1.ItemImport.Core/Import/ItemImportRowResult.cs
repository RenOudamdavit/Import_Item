using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Core.Import;

/// <summary>Final disposition of one source row.</summary>
public enum RowStatus
{
    Created = 0,
    Updated = 1,

    /// <summary>Deliberately not written (for example an existing item in create-only mode).</summary>
    Skipped = 2,

    /// <summary>Rejected locally, before any call to SAP.</summary>
    ValidationFailed = 3,

    /// <summary>Sent to SAP and refused by it.</summary>
    SapRejected = 4,

    /// <summary>Update-only mode was requested but the item does not exist in SAP.</summary>
    NotFound = 5,
}

/// <summary>Per-row outcome, written to the import report.</summary>
public sealed class ItemImportRowResult
{
    public required int SourceLineNumber { get; init; }

    public required string ItemCode { get; init; }

    public required RowStatus Status { get; init; }

    /// <summary>Short explanation for a non-success status, or a note such as "dry run".</summary>
    public string? Message { get; init; }

    /// <summary>SAP error code, when SAP supplied one.</summary>
    public string? SapErrorCode { get; init; }

    /// <summary>Validation findings for the row, including warnings on rows that succeeded.</summary>
    public IReadOnlyList<ValidationMessage> Messages { get; init; } = Array.Empty<ValidationMessage>();

    public bool IsFailure => Status is RowStatus.ValidationFailed or RowStatus.SapRejected or RowStatus.NotFound;

    /// <summary>All findings plus <see cref="Message"/>, flattened for single-line reporting.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Message))
            {
                parts.Add(Message);
            }

            foreach (var message in Messages)
            {
                parts.Add(message.ToString());
            }

            return string.Join(" | ", parts);
        }
    }
}
