namespace SapB1.ItemImport.Core.Csv;

/// <summary>A single data record of the source file, addressable by column name.</summary>
public sealed class CsvRow
{
    private readonly CsvHeader _header;
    private readonly string[] _values;

    public CsvRow(CsvHeader header, string[] values, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(values);

        _header = header;
        _values = values;
        LineNumber = lineNumber;
    }

    /// <summary>1-based physical line of the source file, used verbatim in error messages.</summary>
    public int LineNumber { get; }

    public IReadOnlyList<string> Columns => _header.Columns;

    /// <summary>
    /// Value for a column, or <c>null</c> when the column is absent from the file or the record is
    /// short. A short record is normal for spreadsheet exports that omit trailing empty cells.
    /// </summary>
    public string? this[string column]
    {
        get
        {
            if (!_header.TryGetOrdinal(column, out var ordinal) || ordinal >= _values.Length)
            {
                return null;
            }

            return _values[ordinal];
        }
    }

    /// <summary><c>true</c> when every cell in the record is empty — a blank separator line.</summary>
    public bool IsBlank()
    {
        foreach (var value in _values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
        }

        return true;
    }
}
