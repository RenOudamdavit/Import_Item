using SapB1.ItemImport.Core.Mapping;

namespace SapB1.ItemImport.Tests;

public sealed class ItemFieldCatalogTests
{
    [Theory]
    [InlineData("ItemCode")]
    [InlineData("item code")]
    [InlineData("ITEM_CODE")]
    [InlineData("Item-Code")]
    [InlineData("SKU")]
    [InlineData("Code")]
    public void ResolvesItemCodeSpellings(string column)
    {
        Assert.True(ItemFieldCatalog.TryResolve(column, out var definition));
        Assert.Equal("ItemCode", definition!.SapProperty);
    }

    [Theory]
    [InlineData("Description", "ItemName")]
    [InlineData("Item Group", "ItemsGroupCode")]
    [InlineData("Preferred Vendor", "Mainsupplier")]
    [InlineData("Min Stock", "MinInventory")]
    [InlineData("Warehouse", "DefaultWarehouse")]
    [InlineData("Active", "Valid")]
    [InlineData("Remarks", "User_Text")]
    [InlineData("Sales Tax Code", "SalesVATGroup")]
    public void ResolvesBusinessAliases(string column, string expected)
    {
        Assert.True(ItemFieldCatalog.TryResolve(column, out var definition));
        Assert.Equal(expected, definition!.SapProperty);
    }

    [Fact]
    public void PassesUserDefinedFieldsThroughUnchanged()
    {
        Assert.True(ItemFieldCatalog.TryResolve("U_Category", out var definition));
        Assert.Equal("U_Category", definition!.SapProperty);
        Assert.Equal(SapFieldKind.Text, definition.Kind);
    }

    [Fact]
    public void DoesNotResolveUnknownColumns()
    {
        Assert.False(ItemFieldCatalog.TryResolve("Favourite Colour", out var definition));
        Assert.Null(definition);
    }

    [Fact]
    public void EveryEnumerationDefinitionHasAcceptedValues()
    {
        foreach (var field in ItemFieldCatalog.All.Where(f => f.Kind == SapFieldKind.Enumeration))
        {
            Assert.NotNull(field.EnumerationValues);
            Assert.NotEmpty(field.EnumerationValues!);
        }
    }
}
