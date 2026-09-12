using System.Globalization;
using System.Text;
using SapB1.ItemImport.Core.Csv;
using SapB1.ItemImport.Core.Import;

namespace SapB1.ItemImport.Core.Reporting;

/// <summary>
/// Writes one CSV line per processed row. This file is the audit trail of the run: it is what an
/// operator hands back to the business when asked which items landed and which did not.
/// </summary>
public sealed class CsvImportReportWriter : IImportResultSink
{
    private readonly StreamWriter _writer;
    private readonly bool _failuresOnly;

    private CsvImportReportWriter(StreamWriter writer, bool failuresOnly)
    {
        _writer = writer;
        _failuresOnly = failuresOnly;
    }

    /// <summary>Creates (or overwrites) the report at <paramref name="path"/>.</summary>
    /// <param name="failuresOnly">Write only rows that failed, for very large imports.</param>
    public static CsvImportReportWriter Create(string path, bool failuresOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // UTF-8 with BOM so Excel shows non-ASCII item names correctly.
        var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = false,
        };

        try
        {
            writer.WriteLine("Line,ItemCode,Status,SapErrorCode,Detail");
        }
        catch
        {
            writer.Dispose();
            throw;
        }

        return new CsvImportReportWriter(writer, failuresOnly);
    }

    public async Task WriteAsync(ItemImportRowResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (_failuresOnly && !result.IsFailure)
        {
            return;
        }

        var line = string.Join(
            ',',
            result.SourceLineNumber.ToString(CultureInfo.InvariantCulture),
            CsvValueEscaper.Escape(result.ItemCode),
            CsvValueEscaper.Escape(result.Status.ToString()),
            CsvValueEscaper.Escape(result.SapErrorCode),
            CsvValueEscaper.Escape(result.Detail));

        await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
