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

        if (NeedsFormulaGuard(value))
        {
            builder.Append('\'');
        }

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];

            if (current == '"')
            {
                builder.Append("\"\"");
            }
            else if (current is '\r' or '\n')
            {
                // A line break inside a field would split one result across two report lines.
                // A run of breaks collapses into a single space, so "a\r\nb" stays "a b".
                builder.Append(' ');

                while (index + 1 < value.Length && value[index + 1] is '\r' or '\n')
                {
                    index++;
                }
            }
            else
            {
                builder.Append(current);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    private static bool NeedsFormulaGuard(string value)
    {
        if (Array.IndexOf(FormulaTriggers, value[0]) < 0)
        {
            return false;
        }

        // SAP error codes are negative integers (-1116) and quantities can be negative too.
        // A bare number is not an executable formula, and prefixing it would hand the operator
        // a text cell they cannot sort or filter on.
        return !IsPlainNumber(value);
    }

    /// <summary>True when the whole value is a signed decimal literal, e.g. "-1116" or "-2.50".</summary>
    private static bool IsPlainNumber(string value)
    {
        var index = value[0] is '+' or '-' ? 1 : 0;
        var digits = 0;
        var separators = 0;

        for (; index < value.Length; index++)
        {
            var current = value[index];

            if (char.IsAsciiDigit(current))
            {
                digits++;
                continue;
            }

            if (current == '.' && separators == 0)
            {
                separators++;
                continue;
            }

            return false;
        }

        return digits > 0;
    }
}
