using SapB1.ItemImport.Core.Domain;

namespace SapB1.ItemImport.Core.Validation;

/// <summary>Outcome of turning one source row into an <see cref="ItemRecord"/>.</summary>
public sealed class ItemRecordParseResult
{
    private ItemRecordParseResult(int lineNumber, string itemCode, ItemRecord? record, IReadOnlyList<ValidationMessage> messages)
    {
        SourceLineNumber = lineNumber;
        ItemCode = itemCode;
        Record = record;
        Messages = messages;
    }

    public int SourceLineNumber { get; }

    /// <summary>Item code as read from the file — present even when the row failed validation, for reporting.</summary>
    public string ItemCode { get; }

    /// <summary>The validated record, or <c>null</c> when the row cannot be imported.</summary>
    public ItemRecord? Record { get; }

    public IReadOnlyList<ValidationMessage> Messages { get; }

    public bool IsValid => Record is not null;

    public IEnumerable<ValidationMessage> Errors
        => Messages.Where(m => m.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationMessage> Warnings
        => Messages.Where(m => m.Severity == ValidationSeverity.Warning);

    public static ItemRecordParseResult Success(ItemRecord record, IReadOnlyList<ValidationMessage> messages)
        => new(record.SourceLineNumber, record.ItemCode, record, messages);

    public static ItemRecordParseResult Failure(int lineNumber, string itemCode, IReadOnlyList<ValidationMessage> messages)
        => new(lineNumber, itemCode, record: null, messages);
}
