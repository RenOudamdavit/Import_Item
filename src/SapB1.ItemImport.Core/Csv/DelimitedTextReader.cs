using System.Text;

namespace SapB1.ItemImport.Core.Csv;

/// <summary>
/// Streaming RFC 4180 delimited-text reader. Handles quoted fields, escaped quotes ("")
/// and newlines embedded inside quoted fields, with CRLF / LF / CR line endings.
/// Streaming matters here: item master exports routinely run to hundreds of thousands of rows
/// and must not be materialised in memory.
/// </summary>
public sealed class DelimitedTextReader
{
    private const int EndOfStream = -1;
    private const int NothingPeeked = -2;

    private readonly TextReader _reader;
    private readonly char _delimiter;
    private readonly char _quote;
    private readonly StringBuilder _field = new();
    private readonly List<string> _fields = new();

    private int _peeked = NothingPeeked;
    private int _line = 1;

    public DelimitedTextReader(TextReader reader, char delimiter = ',', char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (delimiter == quote)
        {
            throw new ArgumentException("Delimiter and quote characters must differ.", nameof(delimiter));
        }

        if (delimiter is '\r' or '\n' || quote is '\r' or '\n')
        {
            throw new ArgumentException("Delimiter and quote characters must not be line breaks.", nameof(delimiter));
        }

        _reader = reader;
        _delimiter = delimiter;
        _quote = quote;
    }

    /// <summary>
    /// Reads the next physical record. Returns <c>false</c> at end of stream.
    /// </summary>
    /// <param name="fields">Raw, untrimmed field values of the record.</param>
    /// <param name="lineNumber">1-based physical line on which the record starts.</param>
    public bool TryReadRecord(out string[] fields, out int lineNumber)
    {
        lineNumber = _line;
        fields = Array.Empty<string>();

        if (Peek() == EndOfStream)
        {
            return false;
        }

        _fields.Clear();
        _field.Clear();

        var insideQuotes = false;
        var fieldWasQuoted = false;
        var quoteOpenedOnLine = _line;

        while (true)
        {
            var read = Read();

            if (read == EndOfStream)
            {
                if (insideQuotes)
                {
                    throw new CsvFormatException(
                        "unterminated quoted field — the file ends before the closing quote.",
                        quoteOpenedOnLine);
                }

                break;
            }

            var current = (char)read;

            if (insideQuotes)
            {
                if (current == _quote)
                {
                    if (Peek() == _quote)
                    {
                        Read();
                        _field.Append(_quote);
                    }
                    else
                    {
                        insideQuotes = false;
                    }
                }
                else
                {
                    if (current == '\n')
                    {
                        _line++;
                    }

                    _field.Append(current);
                }

                continue;
            }

            if (current == _quote && _field.Length == 0 && !fieldWasQuoted)
            {
                insideQuotes = true;
                fieldWasQuoted = true;
                quoteOpenedOnLine = _line;
                continue;
            }

            if (current == _delimiter)
            {
                _fields.Add(_field.ToString());
                _field.Clear();
                fieldWasQuoted = false;
                continue;
            }

            if (current == '\r')
            {
                if (Peek() == '\n')
                {
                    Read();
                }

                _line++;
                break;
            }

            if (current == '\n')
            {
                _line++;
                break;
            }

            _field.Append(current);
        }

        _fields.Add(_field.ToString());
        fields = _fields.ToArray();
        return true;
    }

    private int Read()
    {
        if (_peeked != NothingPeeked)
        {
            var value = _peeked;
            _peeked = NothingPeeked;
            return value;
        }

        return _reader.Read();
    }

    private int Peek()
    {
        if (_peeked == NothingPeeked)
        {
            _peeked = _reader.Read();
        }

        return _peeked;
    }
}
