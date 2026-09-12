namespace SapB1.ItemImport.Core.Csv;

/// <summary>Column names of the source file, with case-insensitive lookup by name.</summary>
public sealed class CsvHeader
{
    private readonly Dictionary<string, int> _ordinalByName;

    private CsvHeader(IReadOnlyList<string> columns, Dictionary<string, int> ordinalByName)
    {
        Columns = columns;
        _ordinalByName = ordinalByName;
    }

    /// <summary>Column names in file order.</summary>
    public IReadOnlyList<string> Columns { get; }

    public int Count => Columns.Count;

    /// <summary>
    /// Builds a header from the raw first record. Trailing empty columns (commonly appended by
    /// spreadsheet exports) are dropped; interior blanks and duplicates are rejected because they
    /// make column-to-field mapping ambiguous.
    /// </summary>
    public static CsvHeader Create(IReadOnlyList<string> rawColumns, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(rawColumns);

        var trimmed = new List<string>(rawColumns.Count);
        foreach (var column in rawColumns)
        {
            trimmed.Add(column.Trim().TrimStart('\uFEFF').Trim());
        }

        while (trimmed.Count > 0 && string.IsNullOrEmpty(trimmed[^1]))
        {
            trimmed.RemoveAt(trimmed.Count - 1);
        }

        if (trimmed.Count == 0)
        {
            throw new CsvFormatException("the header row is empty — expected at least an ItemCode column.", lineNumber);
        }

        var ordinalByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < trimmed.Count; i++)
        {
            var name = trimmed[i];

            if (string.IsNullOrEmpty(name))
            {
                throw new CsvFormatException(
                    $"column {i + 1} in the header row has no name. Remove the blank column or give it a name.",
                    lineNumber);
            }

            if (!ordinalByName.TryAdd(name, i))
            {
                throw new CsvFormatException(
                    $"duplicate column name '{name}'. Column names must be unique.",
                    lineNumber);
            }
        }

        return new CsvHeader(trimmed, ordinalByName);
    }

    public bool TryGetOrdinal(string column, out int ordinal)
        => _ordinalByName.TryGetValue(column, out ordinal);

    public bool Contains(string column) => _ordinalByName.ContainsKey(column);
}
