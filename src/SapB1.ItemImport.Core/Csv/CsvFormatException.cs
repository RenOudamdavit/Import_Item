namespace SapB1.ItemImport.Core.Csv;

/// <summary>Raised when the source file cannot be parsed as delimited text.</summary>
public sealed class CsvFormatException : Exception
{
    public CsvFormatException(string message, int lineNumber)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    /// <summary>1-based physical line where the malformed record starts.</summary>
    public int LineNumber { get; }
}
