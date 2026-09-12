using SapB1.ItemImport.Core.Domain;

namespace SapB1.ItemImport.Core.Abstractions;

/// <summary>
/// Everything the import pipeline needs from SAP Business One. Implemented over the Service Layer
/// (REST) and, on Windows, over the DI API — the orchestration above it never changes.
/// </summary>
public interface IItemGateway : IAsyncDisposable
{
    /// <summary>Name of the backend, for logs and reports (e.g. "Service Layer v1").</summary>
    string Description { get; }

    /// <summary>Opens the connection / logs in. Throws <see cref="SapConnectionException"/> on failure.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the subset of <paramref name="itemCodes"/> that already exist. Probing in batches keeps
    /// an upsert from costing one extra round trip per row.
    /// </summary>
    Task<HashSet<string>> GetExistingItemCodesAsync(
        IReadOnlyCollection<string> itemCodes,
        CancellationToken cancellationToken);

    /// <summary>Creates a new item. Throws <see cref="SapItemRejectedException"/> when SAP refuses it.</summary>
    Task CreateAsync(ItemRecord record, CancellationToken cancellationToken);

    /// <summary>Updates only the properties present in <see cref="ItemRecord.Fields"/>.</summary>
    Task UpdateAsync(ItemRecord record, CancellationToken cancellationToken);
}
