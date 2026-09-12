using SapB1.ItemImport.Core.Csv;

namespace SapB1.ItemImport.Tests;

public sealed class DelimitedTextReaderTests
{
    [Fact]
    public void ReadsPlainRecords()
    {
        var reader = new DelimitedTextReader(new StringReader("a,b,c\r\n1,2,3\n"));

        Assert.True(reader.TryReadRecord(out var header, out var headerLine));
        Assert.Equal(new[] { "a", "b", "c" }, header);
        Assert.Equal(1, headerLine);

        Assert.True(reader.TryReadRecord(out var row, out var rowLine));
        Assert.Equal(new[] { "1", "2", "3" }, row);
        Assert.Equal(2, rowLine);

        Assert.False(reader.TryReadRecord(out _, out _));
    }

    [Fact]
    public void KeepsDelimiterInsideQuotedField()
    {
        var reader = new DelimitedTextReader(new StringReader("\"Bracket, 40mm\",100"));

        Assert.True(reader.TryReadRecord(out var fields, out _));
        Assert.Equal(new[] { "Bracket, 40mm", "100" }, fields);
    }

    [Fact]
    public void UnescapesDoubledQuotes()
    {
        var reader = new DelimitedTextReader(new StringReader("\"40\"\" pipe\",x"));

        Assert.True(reader.TryReadRecord(out var fields, out _));
        Assert.Equal(new[] { "40\" pipe", "x" }, fields);
    }

    [Fact]
    public void KeepsNewlineInsideQuotedFieldAndCountsLines()
    {
        var reader = new DelimitedTextReader(new StringReader("\"line1\nline2\",x\nnext,y"));

        Assert.True(reader.TryReadRecord(out var first, out var firstLine));
        Assert.Equal(new[] { "line1\nline2", "x" }, first);
        Assert.Equal(1, firstLine);

        Assert.True(reader.TryReadRecord(out var second, out var secondLine));
        Assert.Equal(new[] { "next", "y" }, second);
        Assert.Equal(3, secondLine);
    }

    [Fact]
    public void ReadsFinalRecordWithoutTrailingNewline()
    {
        var reader = new DelimitedTextReader(new StringReader("a,b\n1,2"));

        Assert.True(reader.TryReadRecord(out _, out _));
        Assert.True(reader.TryReadRecord(out var last, out _));
        Assert.Equal(new[] { "1", "2" }, last);
        Assert.False(reader.TryReadRecord(out _, out _));
    }

    [Fact]
    public void EmitsEmptyFieldsForConsecutiveDelimiters()
    {
        var reader = new DelimitedTextReader(new StringReader("a,,c,"));

        Assert.True(reader.TryReadRecord(out var fields, out _));
        Assert.Equal(new[] { "a", string.Empty, "c", string.Empty }, fields);
    }

    [Fact]
    public void SupportsAlternativeDelimiter()
    {
        var reader = new DelimitedTextReader(new StringReader("a;b;c"), delimiter: ';');

        Assert.True(reader.TryReadRecord(out var fields, out _));
        Assert.Equal(new[] { "a", "b", "c" }, fields);
    }

    [Fact]
    public void ThrowsWithLineNumberOnUnterminatedQuote()
    {
        var reader = new DelimitedTextReader(new StringReader("a,b\n\"oops,c"));

        Assert.True(reader.TryReadRecord(out _, out _));

        var exception = Assert.Throws<CsvFormatException>(() => reader.TryReadRecord(out _, out _));
        Assert.Equal(2, exception.LineNumber);
        Assert.Contains("unterminated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a,b,c", ',')]
    [InlineData("a;b;c", ';')]
    [InlineData("a\tb\tc", '\t')]
    [InlineData("a|b|c", '|')]
    [InlineData("single", ',')]
    [InlineData("\"a;b\",c", ',')]
    public void DetectsDelimiterFromHeader(string header, char expected)
        => Assert.Equal(expected, DelimiterDetector.Detect(header));
}
