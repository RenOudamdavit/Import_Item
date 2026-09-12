using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;
using SapB1.ItemImport.Core.Mapping;
using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Tests;

public sealed class ItemImportServiceTests
{
    [Fact]
    public async Task CreatesMissingItemsAndUpdatesExistingOnes()
    {
        var gateway = new FakeItemGateway();
        gateway.Existing.Add("A-2");

        var (summary, sink) = await RunAsync(gateway, "ItemCode,ItemName\nA-1,One\nA-2,Two\n");

        Assert.Equal(2, summary.TotalRows);
        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.Updated);
        Assert.Equal(0, summary.Failed);
        Assert.True(summary.IsSuccess);

        Assert.Equal("A-1", Assert.Single(gateway.Created).ItemCode);
        Assert.Equal("A-2", Assert.Single(gateway.Updated).ItemCode);
        Assert.Equal(2, sink.Results.Count);
    }

    [Fact]
    public async Task CreateOnlySkipsItemsThatAlreadyExist()
    {
        var gateway = new FakeItemGateway();
        gateway.Existing.Add("A-1");

        var (summary, _) = await RunAsync(
            gateway,
            "ItemCode,ItemName\nA-1,One\n",
            new ImportOptions { Mode = ImportMode.CreateOnly });

        Assert.Equal(1, summary.Skipped);
        Assert.Equal(0, summary.Created);
        Assert.Empty(gateway.Updated);
        Assert.True(summary.IsSuccess);
    }

    [Fact]
    public async Task UpdateOnlyReportsUnknownItemsInsteadOfCreatingThem()
    {
        var gateway = new FakeItemGateway();

        var (summary, sink) = await RunAsync(
            gateway,
            "ItemCode,ItemName\nA-1,One\n",
            new ImportOptions { Mode = ImportMode.UpdateOnly });

        Assert.Equal(1, summary.NotFound);
        Assert.Empty(gateway.Created);
        Assert.Equal(RowStatus.NotFound, Assert.Single(sink.Results).Status);
        Assert.False(summary.IsSuccess);
    }

    [Fact]
    public async Task DryRunWritesNothingButStillClassifiesEveryRow()
    {
        var gateway = new FakeItemGateway();
        gateway.Existing.Add("A-2");

        var (summary, _) = await RunAsync(
            gateway,
            "ItemCode,ItemName\nA-1,One\nA-2,Two\n",
            new ImportOptions { DryRun = true });

        Assert.True(summary.DryRun);
        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.Updated);
        Assert.Empty(gateway.Created);
        Assert.Empty(gateway.Updated);
    }

    [Fact]
    public async Task OneRejectedItemDoesNotStopTheRest()
    {
        var gateway = new FakeItemGateway
        {
            FailWith = record => record.ItemCode == "A-2"
                ? new SapItemRejectedException("Item group does not exist", "-1116")
                : null,
        };

        var (summary, sink) = await RunAsync(gateway, "ItemCode,ItemName\nA-1,One\nA-2,Two\nA-3,Three\n");

        Assert.Equal(2, summary.Created);
        Assert.Equal(1, summary.SapRejected);
        Assert.False(summary.IsSuccess);
        Assert.False(summary.Aborted);

        var rejected = Assert.Single(sink.Results.Where(r => r.Status == RowStatus.SapRejected));
        Assert.Equal("A-2", rejected.ItemCode);
        Assert.Equal("-1116", rejected.SapErrorCode);
        Assert.Contains("Item group does not exist", rejected.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AConnectionFailureAbortsTheRunAndKeepsTheResultsSoFar()
    {
        var gateway = new FakeItemGateway
        {
            FailWith = record => record.ItemCode == "A-2"
                ? new SapConnectionException("the Service Layer went away")
                : null,
        };

        var (summary, sink) = await RunAsync(gateway, "ItemCode,ItemName\nA-1,One\nA-2,Two\nA-3,Three\n");

        Assert.True(summary.Aborted);
        Assert.Contains("went away", summary.AbortReason!, StringComparison.Ordinal);
        Assert.Equal(1, summary.Created);
        Assert.Single(sink.Results);
    }

    [Fact]
    public async Task StopsOnceTheErrorLimitIsReached()
    {
        var gateway = new FakeItemGateway
        {
            FailWith = _ => new SapItemRejectedException("nope"),
        };

        var (summary, _) = await RunAsync(
            gateway,
            "ItemCode,ItemName\nA-1,One\nA-2,Two\nA-3,Three\n",
            new ImportOptions { MaxErrors = 2, ExistenceProbeBatchSize = 1 });

        Assert.True(summary.Aborted);
        Assert.Equal(2, summary.Failed);
        Assert.Contains("error limit of 2", summary.AbortReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RowsThatFailValidationNeverReachSap()
    {
        var gateway = new FakeItemGateway();

        var (summary, sink) = await RunAsync(gateway, "ItemCode,ItemsGroupCode\nA-1,100\nA-2,not-a-number\n");

        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.ValidationFailed);
        Assert.Single(gateway.Created);

        var failed = Assert.Single(sink.Results.Where(r => r.Status == RowStatus.ValidationFailed));
        Assert.Equal("A-2", failed.ItemCode);
        Assert.Contains("whole number", failed.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ADuplicateItemCodeInTheFileIsReportedRatherThanSilentlyWinning()
    {
        var gateway = new FakeItemGateway();

        var (summary, sink) = await RunAsync(gateway, "ItemCode,ItemName\nA-1,First\nA-1,Second\n");

        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.ValidationFailed);
        Assert.Contains(
            sink.Results,
            r => r.Status == RowStatus.ValidationFailed && r.Detail.Contains("duplicate item code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DuplicatesCanBeAllowedExplicitly()
    {
        var gateway = new FakeItemGateway();

        var (summary, _) = await RunAsync(
            gateway,
            "ItemCode,ItemName\nA-1,First\nA-1,Second\n",
            new ImportOptions { AllowDuplicateItemCodes = true, ExistenceProbeBatchSize = 1 });

        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, summary.Created);
        Assert.Equal(1, summary.Updated);
    }

    [Fact]
    public async Task ProbesExistenceInBatchesInsteadOfPerRow()
    {
        var gateway = new FakeItemGateway();
        var csv = "ItemCode,ItemName\n" + string.Join(
            string.Empty,
            Enumerable.Range(1, 10).Select(i => $"A-{i},Item {i}\n"));

        var (summary, _) = await RunAsync(gateway, csv, new ImportOptions { ExistenceProbeBatchSize = 4 });

        Assert.Equal(10, summary.Created);
        Assert.Equal(3, gateway.ProbeCalls);
    }

    [Fact]
    public async Task WarningsOnSuccessfulRowsAreKeptInTheReport()
    {
        var gateway = new FakeItemGateway();

        var (_, sink) = await RunAsync(gateway, "ItemCode,ItemName\nA-1,\n");

        var result = Assert.Single(sink.Results);
        Assert.Equal(RowStatus.Created, result.Status);
        Assert.Contains(result.Messages, m => m.Severity == ValidationSeverity.Warning);
    }

    private static async Task<(ImportSummary Summary, RecordingSink Sink)> RunAsync(
        FakeItemGateway gateway,
        string csv,
        ImportOptions? options = null)
    {
        using var reader = CsvTableReader.Create(new StringReader(csv));
        var builder = new ItemRecordBuilder(reader.Header);

        Assert.True(builder.IsHeaderValid);

        var sink = new RecordingSink();
        var service = new ItemImportService(gateway, options ?? new ImportOptions());
        var summary = await service.ImportAsync(reader.ReadRows().Select(builder.Build), sink);

        return (summary, sink);
    }
}
