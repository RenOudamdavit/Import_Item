using System.Text;
using SapB1.ItemImport.Core.Csv;

namespace SapB1.ItemImport.Tests;

public sealed class CsvTableReaderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sapb1-import-csv").FullName;

    [Fact]
    public void ReadsValuesByColumnNameIgnoringCase()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,ItemName\nA-1,Bracket\n"));
        var row = reader.ReadRows().Single();

        Assert.Equal("A-1", row["itemcode"]);
        Assert.Equal("Bracket", row["ITEMNAME"]);
        Assert.Null(row["NotAColumn"]);
    }

    [Fact]
    public void SkipsBlankSeparatorLines()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode\nA-1\n\n\nA-2\n"));

        Assert.Equal(new[] { "A-1", "A-2" }, reader.ReadRows().Select(r => r["ItemCode"]));
    }

    [Fact]
    public void ToleratesShortRecordsFromSpreadsheetExports()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,ItemName,BarCode\nA-1,Bracket\n"));
        var row = reader.ReadRows().Single();

        Assert.Equal("Bracket", row["ItemName"]);
        Assert.Null(row["BarCode"]);
    }

    [Fact]
    public void TrimsSurroundingWhitespaceByDefault()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,ItemName\n  A-1 , Bracket \n"));
        var row = reader.ReadRows().Single();

        Assert.Equal("A-1", row["ItemCode"]);
        Assert.Equal("Bracket", row["ItemName"]);
    }

    [Fact]
    public void CanKeepWhitespaceWhenAskedTo()
    {
        using var reader = CsvTableReader.Create(
            new StringReader("ItemCode,ItemName\nA-1, Bracket \n"),
            new CsvReaderOptions { TrimValues = false });

        Assert.Equal(" Bracket ", reader.ReadRows().Single()["ItemName"]);
    }

    [Fact]
    public void RejectsDuplicateColumnNames()
    {
        var exception = Assert.Throws<CsvFormatException>(
            () => CsvTableReader.Create(new StringReader("ItemCode,ItemName,ItemName\n")));

        Assert.Contains("duplicate column name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnEmptyFile()
    {
        var exception = Assert.Throws<CsvFormatException>(() => CsvTableReader.Create(new StringReader(string.Empty)));

        Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DropsTrailingEmptyHeaderColumns()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode,ItemName,,\nA-1,Bracket,,\n"));

        Assert.Equal(new[] { "ItemCode", "ItemName" }, reader.Header.Columns);
    }

    [Fact]
    public void RejectsABlankColumnBetweenNamedOnes()
    {
        var exception = Assert.Throws<CsvFormatException>(
            () => CsvTableReader.Create(new StringReader("ItemCode,,ItemName\n")));

        Assert.Contains("no name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RowsCanOnlyBeEnumeratedOnce()
    {
        using var reader = CsvTableReader.Create(new StringReader("ItemCode\nA-1\n"));

        _ = reader.ReadRows().ToList();

        Assert.Throws<InvalidOperationException>(() => reader.ReadRows());
    }

    [Fact]
    public void StripsAUtf8ByteOrderMarkFromTheFirstColumnName()
    {
        var path = WriteFile("bom.csv", "ItemCode,ItemName\nA-1,Bracket\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        using var reader = CsvTableReader.Open(path);

        Assert.Equal("ItemCode", reader.Header.Columns[0]);
        Assert.Equal("A-1", reader.ReadRows().Single()["ItemCode"]);
    }

    [Fact]
    public void DetectsASemicolonSeparatedFile()
    {
        var path = WriteFile("semicolon.csv", "ItemCode;ItemName\nA-1;Bracket\n", Encoding.UTF8);

        using var reader = CsvTableReader.Open(path);

        Assert.Equal(';', reader.Delimiter);
        Assert.Equal("Bracket", reader.ReadRows().Single()["ItemName"]);
    }

    [Fact]
    public void HonoursAnExplicitDelimiterOverDetection()
    {
        var path = WriteFile("tabs.csv", "ItemCode\tItemName\nA-1\tBracket\n", Encoding.UTF8);

        using var reader = CsvTableReader.Open(path, new CsvReaderOptions { Delimiter = '\t' });

        Assert.Equal("Bracket", reader.ReadRows().Single()["ItemName"]);
    }

    [Fact]
    public void ReadsALegacyLatin1File()
    {
        var path = WriteFile("latin1.csv", "ItemCode,ItemName\nA-1,Stahlwinkel Größe\n", Encoding.Latin1);

        using var reader = CsvTableReader.Open(path, new CsvReaderOptions { Encoding = Encoding.Latin1 });

        Assert.Equal("Stahlwinkel Größe", reader.ReadRows().Single()["ItemName"]);
    }

    private string WriteFile(string name, string content, Encoding encoding)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, encoding);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
