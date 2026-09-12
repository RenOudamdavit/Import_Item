using System.Text;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;
using SapB1.ItemImport.Core.Reporting;
using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Tests;

public sealed class CsvValueEscaperTests
{
    [Fact]
    public void QuotesValuesAndDoublesEmbeddedQuotes()
        => Assert.Equal("\"40\"\" pipe\"", CsvValueEscaper.Escape("40\" pipe"));

    [Fact]
    public void ReplacesNewlinesSoOneRowStaysOneLine()
        => Assert.Equal("\"a b\"", CsvValueEscaper.Escape("a\r\nb"));

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1)")]
    [InlineData("-2+3")]
    [InlineData("@cmd")]
    public void NeutralisesSpreadsheetFormulas(string dangerous)
    {
        // A SAP error message can start with any of these; the report must not execute when opened.
        var escaped = CsvValueEscaper.Escape(dangerous);

        Assert.StartsWith("\"'", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesOrdinaryTextAlone()
        => Assert.Equal("\"Steel bracket\"", CsvValueEscaper.Escape("Steel bracket"));

    [Fact]
    public void RendersNullAndEmptyAsAnEmptyField()
    {
        Assert.Equal(string.Empty, CsvValueEscaper.Escape(null));
        Assert.Equal(string.Empty, CsvValueEscaper.Escape(string.Empty));
    }
}

public sealed class CsvImportReportWriterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("sapb1-import-tests").FullName;

    [Fact]
    public async Task WritesAHeaderAndOneLinePerResult()
    {
        var path = Path.Combine(_directory, "report.csv");

        await using (var writer = CsvImportReportWriter.Create(path))
        {
            await writer.WriteAsync(
                new ItemImportRowResult { SourceLineNumber = 2, ItemCode = "A-1", Status = RowStatus.Created },
                CancellationToken.None);

            await writer.WriteAsync(
                new ItemImportRowResult
                {
                    SourceLineNumber = 3,
                    ItemCode = "A-2",
                    Status = RowStatus.SapRejected,
                    SapErrorCode = "-1116",
                    Message = "Item group does not exist",
                    Messages = new[] { ValidationMessage.Warning("BarCode", "looks unusual") },
                },
                CancellationToken.None);
        }

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);

        Assert.Equal("Line,ItemCode,Status,SapErrorCode,Detail", lines[0]);
        Assert.Equal("2,\"A-1\",\"Created\",,", lines[1]);
        Assert.Contains("\"-1116\"", lines[2], StringComparison.Ordinal);
        Assert.Contains("Item group does not exist", lines[2], StringComparison.Ordinal);
        Assert.Contains("BarCode: looks unusual", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailuresOnlySkipsSuccessfulRows()
    {
        var path = Path.Combine(_directory, "failures.csv");

        await using (var writer = CsvImportReportWriter.Create(path, failuresOnly: true))
        {
            await writer.WriteAsync(
                new ItemImportRowResult { SourceLineNumber = 2, ItemCode = "A-1", Status = RowStatus.Created },
                CancellationToken.None);

            await writer.WriteAsync(
                new ItemImportRowResult { SourceLineNumber = 3, ItemCode = "A-2", Status = RowStatus.ValidationFailed },
                CancellationToken.None);
        }

        var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8);

        Assert.Equal(2, lines.Length);
        Assert.Contains("A-2", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatesTheReportDirectoryIfItIsMissing()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "report.csv");

        await using (var writer = CsvImportReportWriter.Create(path))
        {
            await writer.WriteAsync(
                new ItemImportRowResult { SourceLineNumber = 2, ItemCode = "A-1", Status = RowStatus.Created },
                CancellationToken.None);
        }

        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
