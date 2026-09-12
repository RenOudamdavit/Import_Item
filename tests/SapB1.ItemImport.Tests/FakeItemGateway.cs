using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Import;

namespace SapB1.ItemImport.Tests;

/// <summary>In-memory <see cref="IItemGateway"/> so the orchestration can be tested without SAP.</summary>
internal sealed class FakeItemGateway : IItemGateway
{
    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ItemRecord> Created { get; } = new();

    public List<ItemRecord> Updated { get; } = new();

    public int ProbeCalls { get; private set; }

    /// <summary>Returns an exception to throw for a given item code, or <c>null</c> to succeed.</summary>
    public Func<ItemRecord, Exception?>? FailWith { get; set; }

    public string Description => "fake gateway";

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<HashSet<string>> GetExistingItemCodesAsync(
        IReadOnlyCollection<string> itemCodes,
        CancellationToken cancellationToken)
    {
        ProbeCalls++;

        var found = new HashSet<string>(
            itemCodes.Where(Existing.Contains),
            StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(found);
    }

    public Task CreateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        var failure = FailWith?.Invoke(record);
        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        Created.Add(record);
        Existing.Add(record.ItemCode);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ItemRecord record, CancellationToken cancellationToken)
    {
        var failure = FailWith?.Invoke(record);
        if (failure is not null)
        {
            return Task.FromException(failure);
        }

        Updated.Add(record);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Collects every row result the pipeline emits.</summary>
internal sealed class RecordingSink : IImportResultSink
{
    public List<ItemImportRowResult> Results { get; } = new();

    public Task WriteAsync(ItemImportRowResult result, CancellationToken cancellationToken)
    {
        Results.Add(result);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
