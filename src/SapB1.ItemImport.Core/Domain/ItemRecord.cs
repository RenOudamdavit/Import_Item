namespace SapB1.ItemImport.Core.Domain;

/// <summary>
/// One validated item, ready to be sent to SAP Business One.
/// </summary>
/// <remarks>
/// <see cref="Fields"/> holds only the properties the source row actually supplied. That distinction
/// is what makes an update safe: a column absent from the file is never written, so re-running an
/// import cannot silently blank out data maintained inside SAP.
/// </remarks>
public sealed class ItemRecord
{
    /// <summary>1-based physical line of the source file this item came from.</summary>
    public required int SourceLineNumber { get; init; }

    /// <summary>Item primary key.</summary>
    public required string ItemCode { get; init; }

    /// <summary>SAP property name to converted value. A <c>null</c> value means "clear this field".</summary>
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }

    /// <summary>Price-list rows supplied for this item; empty when the file carries no prices.</summary>
    public required IReadOnlyList<ItemPrice> Prices { get; init; }
}
