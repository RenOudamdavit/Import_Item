using System.Diagnostics;
using SapB1.ItemImport.Core.Abstractions;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Logging;
using SapB1.ItemImport.Core.Validation;

namespace SapB1.ItemImport.Core.Import;

/// <summary>
/// Drives the import: decide create vs. update per row, isolate per-row failures, stream results.
/// </summary>
/// <remarks>
/// The service is deliberately unaware of both the source format and the SAP transport. It consumes
/// already-parsed rows and talks to an <see cref="IItemGateway"/>, which is what lets the same
/// orchestration and the same reports serve a CSV-over-Service-Layer run and a DI API run.
/// </remarks>
public sealed class ItemImportService
{
    private const int ProgressLogInterval = 500;

    private readonly IItemGateway _gateway;
    private readonly ImportOptions _options;
    private readonly IImportLogger _logger;

    public ItemImportService(IItemGateway gateway, ImportOptions? options = null, IImportLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        _gateway = gateway;
        _options = options ?? new ImportOptions();
        _options.Validate();
        _logger = logger ?? NullImportLogger.Instance;
    }

    /// <summary>Runs the import. Never throws for a row-level problem; those land in the report.</summary>
    public async Task<ImportSummary> ImportAsync(
        IEnumerable<ItemRecordParseResult> rows,
        IImportResultSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sink);

        var state = new RunState();
        var stopwatch = Stopwatch.StartNew();
        var buffer = new List<ItemRecordParseResult>(_options.ExistenceProbeBatchSize);
        var firstSeenLineByItemCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        _logger.Info(
            $"Starting item import: mode={_options.Mode}, dryRun={_options.DryRun}, target={_gateway.Description}.");

        try
        {
            foreach (var parsed in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                state.TotalRows++;

                if (!parsed.IsValid)
                {
                    await RecordAsync(
                        sink,
                        state,
                        new ItemImportRowResult
                        {
                            SourceLineNumber = parsed.SourceLineNumber,
                            ItemCode = parsed.ItemCode,
                            Status = RowStatus.ValidationFailed,
                            Message = "row failed validation and was not sent to SAP.",
                            Messages = parsed.Messages,
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (state.ShouldAbort(_options.MaxErrors))
                    {
                        break;
                    }

                    continue;
                }

                var record = parsed.Record!;

                if (!_options.AllowDuplicateItemCodes
                    && firstSeenLineByItemCode.TryGetValue(record.ItemCode, out var firstLine))
                {
                    await RecordAsync(
                        sink,
                        state,
                        new ItemImportRowResult
                        {
                            SourceLineNumber = record.SourceLineNumber,
                            ItemCode = record.ItemCode,
                            Status = RowStatus.ValidationFailed,
                            Message = $"duplicate item code in the source file (first seen on line {firstLine}).",
                            Messages = parsed.Messages,
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (state.ShouldAbort(_options.MaxErrors))
                    {
                        break;
                    }

                    continue;
                }

                firstSeenLineByItemCode[record.ItemCode] = record.SourceLineNumber;
                buffer.Add(parsed);

                if (buffer.Count >= _options.ExistenceProbeBatchSize)
                {
                    await ProcessBatchAsync(buffer, sink, state, cancellationToken).ConfigureAwait(false);
                    buffer.Clear();

                    if (state.Aborted)
                    {
                        break;
                    }
                }
            }

            if (!state.Aborted && buffer.Count > 0)
            {
                await ProcessBatchAsync(buffer, sink, state, cancellationToken).ConfigureAwait(false);
                buffer.Clear();
            }
        }
        catch (OperationCanceledException)
        {
            state.Abort("the import was cancelled.");
            _logger.Warn("Import cancelled — the report contains every row processed so far.");
        }
        catch (SapConnectionException ex)
        {
            state.Abort($"connection to SAP failed: {ex.Message}");
            _logger.Error("Import aborted: the connection to SAP failed.", ex);
        }

        stopwatch.Stop();

        var summary = state.ToSummary(_options.DryRun, stopwatch.Elapsed);
        _logger.Info(
            $"Import finished in {summary.Duration.TotalSeconds:F1}s: {summary.TotalRows} rows, "
            + $"{summary.Created} created, {summary.Updated} updated, {summary.Skipped} skipped, {summary.Failed} failed.");

        return summary;
    }

    private async Task ProcessBatchAsync(
        List<ItemRecordParseResult> buffer,
        IImportResultSink sink,
        RunState state,
        CancellationToken cancellationToken)
    {
        var itemCodes = new List<string>(buffer.Count);
        foreach (var parsed in buffer)
        {
            itemCodes.Add(parsed.Record!.ItemCode);
        }

        var existing = await _gateway.GetExistingItemCodesAsync(itemCodes, cancellationToken).ConfigureAwait(false);

        foreach (var parsed in buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = parsed.Record!;
            var exists = existing.Contains(record.ItemCode);
            ItemImportRowResult result;

            if (exists && _options.Mode == ImportMode.CreateOnly)
            {
                result = new ItemImportRowResult
                {
                    SourceLineNumber = record.SourceLineNumber,
                    ItemCode = record.ItemCode,
                    Status = RowStatus.Skipped,
                    Message = "item already exists and the run is create-only.",
                    Messages = parsed.Messages,
                };
            }
            else if (!exists && _options.Mode == ImportMode.UpdateOnly)
            {
                result = new ItemImportRowResult
                {
                    SourceLineNumber = record.SourceLineNumber,
                    ItemCode = record.ItemCode,
                    Status = RowStatus.NotFound,
                    Message = "item does not exist in SAP and the run is update-only.",
                    Messages = parsed.Messages,
                };
            }
            else
            {
                result = await WriteAsync(record, exists, parsed.Messages, cancellationToken).ConfigureAwait(false);
            }

            await RecordAsync(sink, state, result, cancellationToken).ConfigureAwait(false);

            if (state.ShouldAbort(_options.MaxErrors))
            {
                return;
            }

            if (state.TotalProcessed % ProgressLogInterval == 0)
            {
                _logger.Info($"Processed {state.TotalProcessed} rows ({state.Failed} failed so far).");
            }
        }
    }

    private async Task<ItemImportRowResult> WriteAsync(
        ItemRecord record,
        bool exists,
        IReadOnlyList<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        var status = exists ? RowStatus.Updated : RowStatus.Created;

        if (_options.DryRun)
        {
            return new ItemImportRowResult
            {
                SourceLineNumber = record.SourceLineNumber,
                ItemCode = record.ItemCode,
                Status = status,
                Message = exists ? "dry run: would update." : "dry run: would create.",
                Messages = messages,
            };
        }

        try
        {
            if (exists)
            {
                await _gateway.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _gateway.CreateAsync(record, cancellationToken).ConfigureAwait(false);
            }

            return new ItemImportRowResult
            {
                SourceLineNumber = record.SourceLineNumber,
                ItemCode = record.ItemCode,
                Status = status,
                Messages = messages,
            };
        }
        catch (SapItemRejectedException ex)
        {
            _logger.Warn($"Line {record.SourceLineNumber} ({record.ItemCode}): SAP rejected the item — {ex.Message}");

            return new ItemImportRowResult
            {
                SourceLineNumber = record.SourceLineNumber,
                ItemCode = record.ItemCode,
                Status = RowStatus.SapRejected,
                Message = ex.Message,
                SapErrorCode = ex.SapErrorCode,
                Messages = messages,
            };
        }
    }

    private static async Task RecordAsync(
        IImportResultSink sink,
        RunState state,
        ItemImportRowResult result,
        CancellationToken cancellationToken)
    {
        state.Count(result);
        await sink.WriteAsync(result, cancellationToken).ConfigureAwait(false);
    }

    private sealed class RunState
    {
        private const int MaxSampleFailures = 20;

        private readonly List<ItemImportRowResult> _sampleFailures = new();

        public int TotalRows { get; set; }

        public int Created { get; private set; }

        public int Updated { get; private set; }

        public int Skipped { get; private set; }

        public int ValidationFailed { get; private set; }

        public int SapRejected { get; private set; }

        public int NotFound { get; private set; }

        public bool Aborted { get; private set; }

        public string? AbortReason { get; private set; }

        public int Failed => ValidationFailed + SapRejected + NotFound;

        public int TotalProcessed => Created + Updated + Skipped + Failed;

        public void Count(ItemImportRowResult result)
        {
            switch (result.Status)
            {
                case RowStatus.Created:
                    Created++;
                    break;
                case RowStatus.Updated:
                    Updated++;
                    break;
                case RowStatus.Skipped:
                    Skipped++;
                    break;
                case RowStatus.ValidationFailed:
                    ValidationFailed++;
                    break;
                case RowStatus.SapRejected:
                    SapRejected++;
                    break;
                case RowStatus.NotFound:
                    NotFound++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unhandled row status.");
            }

            if (result.IsFailure && _sampleFailures.Count < MaxSampleFailures)
            {
                _sampleFailures.Add(result);
            }
        }

        public bool ShouldAbort(int maxErrors)
        {
            if (Aborted)
            {
                return true;
            }

            if (maxErrors > 0 && Failed >= maxErrors)
            {
                Abort($"the error limit of {maxErrors} was reached.");
                return true;
            }

            return false;
        }

        public void Abort(string reason)
        {
            if (Aborted)
            {
                return;
            }

            Aborted = true;
            AbortReason = reason;
        }

        public ImportSummary ToSummary(bool dryRun, TimeSpan duration) => new()
        {
            DryRun = dryRun,
            TotalRows = TotalRows,
            Created = Created,
            Updated = Updated,
            Skipped = Skipped,
            ValidationFailed = ValidationFailed,
            SapRejected = SapRejected,
            NotFound = NotFound,
            Duration = duration,
            Aborted = Aborted,
            AbortReason = AbortReason,
            SampleFailures = _sampleFailures,
        };
    }
}
