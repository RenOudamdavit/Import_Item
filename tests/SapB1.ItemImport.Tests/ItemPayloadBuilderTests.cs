using System.Text.Json;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Tests;

public sealed class ItemPayloadBuilderTests
{
    [Fact]
    public void CreatePayloadCarriesTheKeyAndTypedValues()
    {
        var record = new ItemRecord
        {
            SourceLineNumber = 2,
            ItemCode = "A-1",
            Fields = new Dictionary<string, object?>
            {
                ["ItemName"] = "Steel bracket",
                ["ItemsGroupCode"] = 100,
                ["InventoryItem"] = "tYES",
                ["MinInventory"] = 25.5d,
                ["BarCode"] = null,
            },
            Prices = Array.Empty<ItemPrice>(),
        };

        using var document = JsonDocument.Parse(ItemPayloadBuilder.BuildCreatePayload(record));
        var root = document.RootElement;

        Assert.Equal("A-1", root.GetProperty("ItemCode").GetString());
        Assert.Equal("Steel bracket", root.GetProperty("ItemName").GetString());
        Assert.Equal(100, root.GetProperty("ItemsGroupCode").GetInt32());
        Assert.Equal("tYES", root.GetProperty("InventoryItem").GetString());
        Assert.Equal(25.5d, root.GetProperty("MinInventory").GetDouble(), 6);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("BarCode").ValueKind);
    }

    [Fact]
    public void UpdatePayloadOmitsTheKeyBecauseItIsInTheUrl()
    {
        var record = new ItemRecord
        {
            SourceLineNumber = 2,
            ItemCode = "A-1",
            Fields = new Dictionary<string, object?> { ["ItemName"] = "Bracket" },
            Prices = Array.Empty<ItemPrice>(),
        };

        using var document = JsonDocument.Parse(ItemPayloadBuilder.BuildUpdatePayload(record));

        Assert.False(document.RootElement.TryGetProperty("ItemCode", out _));
        Assert.Equal("Bracket", document.RootElement.GetProperty("ItemName").GetString());
    }

    [Fact]
    public void WritesPricesAsTheItemPricesCollection()
    {
        var record = new ItemRecord
        {
            SourceLineNumber = 2,
            ItemCode = "A-1",
            Fields = new Dictionary<string, object?>(),
            Prices = new[]
            {
                new ItemPrice { PriceList = 1, Price = 12.5d, Currency = "USD" },
                new ItemPrice { PriceList = 3 },
            },
        };

        using var document = JsonDocument.Parse(ItemPayloadBuilder.BuildCreatePayload(record));
        var prices = document.RootElement.GetProperty("ItemPrices");

        Assert.Equal(2, prices.GetArrayLength());

        var first = prices[0];
        Assert.Equal(1, first.GetProperty("PriceList").GetInt32());
        Assert.Equal(12.5d, first.GetProperty("Price").GetDouble(), 6);
        Assert.Equal("USD", first.GetProperty("Currency").GetString());

        var second = prices[1];
        Assert.Equal(3, second.GetProperty("PriceList").GetInt32());
        Assert.False(second.TryGetProperty("Price", out _));
        Assert.False(second.TryGetProperty("Currency", out _));
    }

    [Fact]
    public void OmitsTheCollectionEntirelyWhenNoPricesWereSupplied()
    {
        var record = new ItemRecord
        {
            SourceLineNumber = 2,
            ItemCode = "A-1",
            Fields = new Dictionary<string, object?> { ["ItemName"] = "Bracket" },
            Prices = Array.Empty<ItemPrice>(),
        };

        using var document = JsonDocument.Parse(ItemPayloadBuilder.BuildCreatePayload(record));

        Assert.False(document.RootElement.TryGetProperty("ItemPrices", out _));
    }

    [Fact]
    public void EscapesValuesThatWouldOtherwiseBreakTheJsonBody()
    {
        var record = new ItemRecord
        {
            SourceLineNumber = 2,
            ItemCode = "A-1",
            Fields = new Dictionary<string, object?> { ["ItemName"] = "40\" pipe \\ fitting" },
            Prices = Array.Empty<ItemPrice>(),
        };

        using var document = JsonDocument.Parse(ItemPayloadBuilder.BuildCreatePayload(record));

        Assert.Equal("40\" pipe \\ fitting", document.RootElement.GetProperty("ItemName").GetString());
    }
}
