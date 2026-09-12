using System.Text;

namespace SapB1.ItemImport.Core.Csv;

/// <summary>Quotes and neutralises values written to a report file.</summary>
public static class CsvValueEscaper
{
    // Excel and LibreOffice evaluate a cell that starts with one of these, so a SAP error message
    // containing "=..." would execute as a formula when the operator opens the report.
    private static readonly char[] FormulaTriggers = { '=', '+', '-', '@', '\t', '\r' };

    /// <summary>Returns a quoted, formula-safe CSV field.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        builder.Append('"');

        if (Array.IndexOf(FormulaTriggers, value[0]) >= 0)
        {
            builder.Append('\'');
        }

        foreach (var current in value)
        {
            if (current == '"')
            {
                builder.Append("\"\"");
            }
            else if (current is '\r' or '\n')
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(current);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}
