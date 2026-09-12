namespace SapB1.ItemImport.Core.Import;

/// <summary>
/// Receives per-row outcomes as they happen. Streaming them keeps memory flat on large files and
/// means a run that is interrupted still leaves a usable report on disk.
/// </summary>
public interface IImportResultSink : IAsyncDisposable
{
    Task WriteAsync(ItemImportRowResult result, CancellationToken cancellationToken);
}

/// <summary>Sink that discards results.</summary>
public sealed class NullImportResultSink : IImportResultSink
{
    public static readonly NullImportResultSink Instance = new();

    private NullImportResultSink()
    {
    }

    public Task WriteAsync(ItemImportRowResult result, CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
