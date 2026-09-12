using System.Globalization;
using SapB1.ItemImport.Core.Mapping;

namespace SapB1.ItemImport.Tests;

public sealed class SapValueConverterTests
{
    private static readonly ValueConversionOptions Invariant = new();

    private static ItemFieldDefinition Field(string name)
    {
        Assert.True(ItemFieldCatalog.TryResolve(name, out var definition));
        return definition!;
    }

    [Theory]
    [InlineData("Y", "tYES")]
    [InlineData("yes", "tYES")]
    [InlineData("TRUE", "tYES")]
    [InlineData("1", "tYES")]
    [InlineData("tYES", "tYES")]
    [InlineData("N", "tNO")]
    [InlineData("false", "tNO")]
    [InlineData("0", "tNO")]
    public void ConvertsYesNoTokens(string raw, string expected)
    {
        Assert.True(SapValueConverter.TryConvert(Field("InventoryItem"), raw, Invariant, out var value, out var error));
        Assert.Null(error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void RejectsUnknownYesNoToken()
    {
        Assert.False(SapValueConverter.TryConvert(Field("SalesItem"), "maybe", Invariant, out _, out var error));
        Assert.Contains("yes/no", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertsIntegerField()
    {
        Assert.True(SapValueConverter.TryConvert(Field("ItemsGroupCode"), "100", Invariant, out var value, out _));
        Assert.Equal(100, value);
    }

    [Fact]
    public void RejectsNonNumericInteger()
    {
        Assert.False(SapValueConverter.TryConvert(Field("ItemsGroupCode"), "one hundred", Invariant, out _, out var error));
        Assert.Contains("whole number", error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConvertsDecimalWithInvariantCulture()
    {
        Assert.True(SapValueConverter.TryConvert(Field("AvgStdPrice"), "1234.56", Invariant, out var value, out _));
        Assert.Equal(1234.56d, Assert.IsType<double>(value), 6);
    }

    [Fact]
    public void ConvertsDecimalWithCommaDecimalSeparator()
    {
        var options = new ValueConversionOptions { Culture = CommaDecimalCulture() };

        Assert.True(SapValueConverter.TryConvert(Field("AvgStdPrice"), "1.234,56", options, out var value, out _));
        Assert.Equal(1234.56d, Assert.IsType<double>(value), 6);
    }

    [Fact]
    public void RejectsTextLongerThanTheSapLimit()
    {
        var tooLong = new string('x', 101);

        Assert.False(SapValueConverter.TryConvert(Field("ItemName"), tooLong, Invariant, out _, out var error));
        Assert.Contains("at most 100", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("item", "itItems")]
    [InlineData("Items", "itItems")]
    [InlineData("labour", "itLabor")]
    [InlineData("itFixedAssets", "itFixedAssets")]
    public void ConvertsItemTypeEnumeration(string raw, string expected)
    {
        Assert.True(SapValueConverter.TryConvert(Field("ItemType"), raw, Invariant, out var value, out _));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ListsAcceptedValuesWhenEnumerationIsWrong()
    {
        Assert.False(SapValueConverter.TryConvert(Field("ItemType"), "widget", Invariant, out _, out var error));
        Assert.Contains("itItems", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void NullTokenProducesAnExplicitNull()
    {
        Assert.True(SapValueConverter.TryConvert(Field("BarCode"), "<NULL>", Invariant, out var value, out var error));
        Assert.Null(error);
        Assert.Null(value);
    }

    [Fact]
    public void RejectsNonFiniteNumbers()
    {
        Assert.False(SapValueConverter.TryConvert(Field("AvgStdPrice"), "NaN", Invariant, out _, out var error));
        Assert.NotNull(error);
    }

    internal static CultureInfo CommaDecimalCulture()
    {
        // Built from the invariant culture rather than looked up by name, so the test does not depend
        // on ICU data being present on the build agent.
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }
}
