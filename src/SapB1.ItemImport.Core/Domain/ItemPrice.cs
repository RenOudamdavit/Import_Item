namespace SapB1.ItemImport.Core.Domain;

/// <summary>One row of the item's <c>ItemPrices</c> collection.</summary>
public sealed class ItemPrice
{
    /// <summary>Price list key (OPLN.ListNum / AbsEntry).</summary>
    public required int PriceList { get; init; }

    /// <summary>Unit price. <c>null</c> leaves the existing price untouched.</summary>
    public double? Price { get; init; }

    /// <summary>ISO currency code. <c>null</c> lets SAP apply the price list default.</summary>
    public string? Currency { get; init; }
}
