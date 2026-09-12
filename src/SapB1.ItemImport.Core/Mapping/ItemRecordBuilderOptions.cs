namespace SapB1.ItemImport.Core.Mapping;

/// <summary>Configuration for turning source rows into SAP item records.</summary>
public sealed class ItemRecordBuilderOptions
{
    public ValueConversionOptions Conversion { get; set; } = new();

    public UnknownColumnBehavior UnknownColumns { get; set; } = UnknownColumnBehavior.Warn;

    /// <summary>Price list used by a bare <c>Price</c> column when no <c>PriceList</c> column is present.</summary>
    public int DefaultPriceList { get; set; } = 1;

    /// <summary>
    /// Optional regular expression every item code must match, for shops that enforce a coding
    /// standard (for example <c>^[A-Z]{3}-[0-9]{5}$</c>). Anchor it yourself.
    /// </summary>
    public string? ItemCodePattern { get; set; }

    /// <summary>
    /// Explicit source column to SAP property mapping, for files whose headers the built-in catalog
    /// cannot resolve. Loaded from the mapping file; takes precedence over the catalog.
    /// </summary>
    public IReadOnlyDictionary<string, string> ColumnOverrides { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
