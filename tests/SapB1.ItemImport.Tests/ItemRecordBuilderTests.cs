using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Mapping;
using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Tests;

public sealed class ItemRecordBuilderTests
{
    [Fact]
    public void MapsColumnsToSapProperties()
    {
        var result = BuildSingle(
            "Item Code,Description,Item Group,Inventory Item,Min Stock\nA-1,Steel bracket,100,Y,25\n");

        Assert.True(result.IsValid);
        var record = result.Record!;

        Assert.Equal("A-1", record.ItemCode);
        Assert.Equal("Steel bracket", record.Fields["ItemName"]);
        Assert.Equal(100, record.Fields["ItemsGroupCode"]);
        Assert.Equal("tYES", record.Fields["InventoryItem"]);
        Assert.Equal(25d, Assert.IsType<double>(record.Fields["MinInventory"]));
    }

    [Fact]
    public void NeverWritesTheKeyAsAnUpdatableField()
    {
        var result = BuildSingle("ItemCode,ItemName\nA-1,Bracket\n");

        Assert.False(result.Record!.Fields.ContainsKey("ItemCode"));
    }

    [Fact]
    public void OmitsEmptyCellsSoUpdatesDoNotBlankSapData()
    {
        var result = BuildSingle("ItemCode,ItemName,BarCode\nA-1,Bracket,\n");

        Assert.True(result.IsValid);
        Assert.False(result.Record!.Fields.ContainsKey("BarCode"));
    }

    [Fact]
    public void NullTokenClearsAFieldExplicitly()
    {
        var result = BuildSingle("ItemCode,BarCode\nA-1,<NULL>\n");

        Assert.True(result.IsValid);
        Assert.True(result.Record!.Fields.ContainsKey("BarCode"));
        Assert.Null(result.Record.Fields["BarCode"]);
    }

    [Fact]
    public void ReportsMissingItemCodeColumnAsAHeaderError()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemName,BarCode\nBracket,123\n"));
        var builder = new ItemRecordBuilder(reader.Header);

        Assert.False(builder.IsHeaderValid);
        Assert.Contains(builder.HeaderMessages, m => m.Message.Contains("no ItemCode column", StringComparison.Ordinal));
    }

    [Fact]
    public void WarnsAboutUnknownColumnsByDefault()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,Favourite Colour\nA-1,blue\n"));
        var builder = new ItemRecordBuilder(reader.Header);

        Assert.True(builder.IsHeaderValid);
        Assert.Contains(
            builder.HeaderMessages,
            m => m.Severity == ValidationSeverity.Warning && m.Column == "Favourite Colour");
    }

    [Fact]
    public void CanTreatUnknownColumnsAsFatal()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,Favourite Colour\nA-1,blue\n"));
        var builder = new ItemRecordBuilder(
            reader.Header,
            new ItemRecordBuilderOptions { UnknownColumns = UnknownColumnBehavior.Error });

        Assert.False(builder.IsHeaderValid);
    }

    [Fact]
    public void RejectsTwoColumnsFeedingTheSameProperty()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,ItemName,Description\nA-1,a,b\n"));
        var builder = new ItemRecordBuilder(reader.Header);

        Assert.False(builder.IsHeaderValid);
        Assert.Contains(builder.HeaderMessages, m => m.Message.Contains("map to SAP property ItemName", StringComparison.Ordinal));
    }

    [Fact]
    public void RequiresAnItemCode()
    {
        var result = BuildSingle("ItemCode,ItemName\n,Bracket\n");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, m => m.Message.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnforcesTheConfiguredItemCodePattern()
    {
        var result = BuildSingle(
            "ItemCode,ItemName\nWRONG,Bracket\n",
            new ItemRecordBuilderOptions { ItemCodePattern = "^[A-Z]{1}-[0-9]+$" });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, m => m.Message.Contains("does not match", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadsASingleBarePriceIntoTheDefaultPriceList()
    {
        var result = BuildSingle("ItemCode,Price,Currency\nA-1,12.50,USD\n");

        var price = Assert.Single(result.Record!.Prices);
        Assert.Equal(1, price.PriceList);
        Assert.Equal(12.50d, price.Price!.Value, 6);
        Assert.Equal("USD", price.Currency);
    }

    [Fact]
    public void ReadsNumberedPriceColumnsIntoSeparatePriceLists()
    {
        var result = BuildSingle(
            "ItemCode,PriceList,Price,Currency,PriceList2,Price2,Currency2\nA-1,1,12.50,USD,3,11.00,EUR\n");

        Assert.Collection(
            result.Record!.Prices,
            first =>
            {
                Assert.Equal(1, first.PriceList);
                Assert.Equal(12.50d, first.Price!.Value, 6);
            },
            second =>
            {
                Assert.Equal(3, second.PriceList);
                Assert.Equal("EUR", second.Currency);
            });
    }

    [Fact]
    public void RequiresAPriceListNumberForSecondaryPriceColumns()
    {
        var result = BuildSingle("ItemCode,Price2\nA-1,12.50\n");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, m => m.Message.Contains("PriceList2 is required", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsNegativePrices()
    {
        var result = BuildSingle("ItemCode,Price\nA-1,-5\n");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, m => m.Message.Contains("must not be negative", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsTheSamePriceListTwiceInOneRow()
    {
        var result = BuildSingle("ItemCode,PriceList,Price,PriceList2,Price2\nA-1,1,10,1,20\n");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, m => m.Message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void AppliesColumnOverridesIncludingPriceColumns()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Artikelnummer"] = "ItemCode",
            ["Bezeichnung"] = "ItemName",
            ["Verkaufspreis"] = "Price",
            ["Kostenstelle"] = string.Empty,
        };

        using var reader = CsvTableReader.Create(
            new StringReader("Artikelnummer,Bezeichnung,Verkaufspreis,Kostenstelle\nA-1,Winkel,9.99,X100\n"));
        var builder = new ItemRecordBuilder(reader.Header, new ItemRecordBuilderOptions { ColumnOverrides = overrides });

        Assert.True(builder.IsHeaderValid);

        var result = builder.Build(reader.ReadRows().Single());

        Assert.True(result.IsValid);
        Assert.Equal("A-1", result.Record!.ItemCode);
        Assert.Equal("Winkel", result.Record.Fields["ItemName"]);
        Assert.Equal(9.99d, Assert.Single(result.Record.Prices).Price!.Value, 6);
        Assert.DoesNotContain("Kostenstelle", result.Record.Fields.Keys);
    }

    [Fact]
    public void PassesUserDefinedFieldsThrough()
    {
        var result = BuildSingle("ItemCode,U_Category\nA-1,Hardware\n");

        Assert.Equal("Hardware", result.Record!.Fields["U_Category"]);
    }

    [Fact]
    public void WarnsWhenARowCarriesNothingButAKey()
    {
        var result = BuildSingle("ItemCode,ItemName\nA-1,\n");

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, m => m.Message.Contains("no values", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsTheSourceLineNumberOfEachRow()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode\nA-1\nA-2\n"));
        var builder = new ItemRecordBuilder(reader.Header);
        var results = reader.ReadRows().Select(builder.Build).ToList();

        Assert.Equal(new[] { 2, 3 }, results.Select(r => r.SourceLineNumber));
    }

    private static ItemRecordParseResult BuildSingle(string csv, ItemRecordBuilderOptions? options = null)
    {
        using var reader = CsvTableReader.Create(new StringReader(csv));
        var builder = new ItemRecordBuilder(reader.Header, options);

        Assert.True(builder.IsHeaderValid, string.Join("; ", builder.HeaderMessages.Select(m => m.ToString())));

        return builder.Build(reader.ReadRows().Single());
    }
}
