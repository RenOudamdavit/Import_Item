using System.Text;

namespace SapB1.ItemImport.Core.Csv;

/// <summary>
/// Reads a delimited item file as a sequence of named rows. Owns the underlying reader.
/// </summary>
public sealed class CsvTableReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly bool _ownsReader;
    private readonly DelimitedTextReader _parser;
    private readonly bool _trimValues;
    private bool _rowsEnumerated;

    private CsvTableReader(TextReader reader, bool ownsReader, CsvReaderOptions options, char delimiter)
    {
        _reader = reader;
        _ownsReader = ownsReader;
        _trimValues = options.TrimValues;
        _parser = new DelimitedTextReader(reader, delimiter, options.Quote);
        Delimiter = delimiter;

        if (!_parser.TryReadRecord(out var headerFields, out var headerLine))
        {
            throw new CsvFormatException("the file is empty — expected a header row.", 1);
        }

        Header = CsvHeader.Create(NormalizeValues(headerFields), headerLine);
    }

    /// <summary>Column names of the file.</summary>
    public CsvHeader Header { get; }

    /// <summary>Delimiter actually used, after auto-detection.</summary>
    public char Delimiter { get; }

    /// <summary>Opens a file from disk, honouring its byte-order mark and auto-detecting the delimiter when not configured.</summary>
    public static CsvTableReader Open(string path, CsvReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new CsvReaderOptions();

        var delimiter = options.Delimiter ?? DetectDelimiter(path, options);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: false);
        StreamReader reader;
        try
        {
            reader = new StreamReader(stream, options.Encoding, detectEncodingFromByteOrderMarks: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        try
        {
            return new CsvTableReader(reader, ownsReader: true, options, delimiter);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Wraps an existing reader. The caller keeps ownership of <paramref name="reader"/>.
    /// Auto-detection is unavailable here because it would consume the header, so the delimiter
    /// falls back to <see cref="DelimiterDetector.Fallback"/> when not configured.
    /// </summary>
    public static CsvTableReader Create(TextReader reader, CsvReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new CsvReaderOptions();
        return new CsvTableReader(reader, ownsReader: false, options, options.Delimiter ?? DelimiterDetector.Fallback);
    }

    /// <summary>
    /// Streams the data rows, skipping blank separator lines. Can only be enumerated once because
    /// the source is a forward-only stream.
    /// </summary>
    public IEnumerable<CsvRow> ReadRows()
    {
        if (_rowsEnumerated)
        {
            throw new InvalidOperationException("The rows of a CsvTableReader can only be enumerated once.");
        }

        _rowsEnumerated = true;
        return ReadRowsCore();
    }

    private IEnumerable<CsvRow> ReadRowsCore()
    {
        while (_parser.TryReadRecord(out var fields, out var lineNumber))
        {
            var row = new CsvRow(Header, NormalizeValues(fields), lineNumber);

            if (row.IsBlank())
            {
                continue;
            }

            yield return row;
        }
    }

    private static char DetectDelimiter(string path, CsvReaderOptions options)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, options.Encoding, detectEncodingFromByteOrderMarks: true);
        return DelimiterDetector.Detect(reader.ReadLine(), options.Quote);
    }

    private string[] NormalizeValues(string[] fields)
    {
        if (!_trimValues)
        {
            return fields;
        }

        for (var i = 0; i < fields.Length; i++)
        {
            fields[i] = fields[i].Trim();
        }

        return fields;
    }

    public void Dispose()
    {
        if (_ownsReader)
        {
            _reader.Dispose();
        }
    }
}
