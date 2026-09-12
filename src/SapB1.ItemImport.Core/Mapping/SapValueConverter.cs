using System.Globalization;

namespace SapB1.ItemImport.Core.Mapping;

/// <summary>Converts a source cell into the CLR value the SAP Business One JSON payload expects.</summary>
public static class SapValueConverter
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyyMMdd",
    };

    /// <summary>
    /// Attempts to convert <paramref name="rawValue"/> for <paramref name="field"/>.
    /// </summary>
    /// <returns><c>true</c> on success; on failure <paramref name="error"/> holds an operator-readable reason.</returns>
    public static bool TryConvert(
        ItemFieldDefinition field,
        string rawValue,
        ValueConversionOptions options,
        out object? value,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(rawValue);
        ArgumentNullException.ThrowIfNull(options);

        value = null;
        error = null;

        if (string.Equals(rawValue, options.NullToken, StringComparison.OrdinalIgnoreCase))
        {
            // Explicit clear: emit a JSON null.
            return true;
        }

        switch (field.Kind)
        {
            case SapFieldKind.Text:
                if (field.MaxLength.HasValue && rawValue.Length > field.MaxLength.Value)
                {
                    error = $"value is {rawValue.Length} characters but SAP allows at most {field.MaxLength.Value}.";
                    return false;
                }

                value = rawValue;
                return true;

            case SapFieldKind.Integer:
                if (!int.TryParse(rawValue, NumberStyles.Integer | NumberStyles.AllowThousands, options.Culture, out var intValue))
                {
                    error = $"'{rawValue}' is not a whole number.";
                    return false;
                }

                value = intValue;
                return true;

            case SapFieldKind.Decimal:
                if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, options.Culture, out var doubleValue))
                {
                    var cultureName = options.Culture.Name.Length == 0 ? "invariant" : options.Culture.Name;
                    error = $"'{rawValue}' is not a number (culture: {cultureName}).";
                    return false;
                }

                if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue))
                {
                    error = $"'{rawValue}' is not a finite number.";
                    return false;
                }

                value = doubleValue;
                return true;

            case SapFieldKind.YesNo:
                if (!ItemFieldCatalog.BooleanTokens.TryGetValue(SapNameNormalizer.Normalize(rawValue), out var yesNo))
                {
                    error = $"'{rawValue}' is not a yes/no value. Use Y, N, Yes, No, True, False, 1 or 0.";
                    return false;
                }

                value = yesNo;
                return true;

            case SapFieldKind.Enumeration:
                var accepted = field.EnumerationValues!;
                if (!accepted.TryGetValue(SapNameNormalizer.Normalize(rawValue), out var enumValue))
                {
                    var allowed = string.Join(", ", accepted.Values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal));
                    error = $"'{rawValue}' is not valid for {field.SapProperty}. Accepted values: {allowed}.";
                    return false;
                }

                value = enumValue;
                return true;

            case SapFieldKind.Date:
                if (!TryParseDate(rawValue, options.Culture, out var date))
                {
                    error = $"'{rawValue}' is not a date. Use yyyy-MM-dd.";
                    return false;
                }

                value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;

            default:
                error = $"field kind {field.Kind} is not supported.";
                return false;
        }
    }

    private static bool TryParseDate(string rawValue, CultureInfo culture, out DateTime date)
    {
        if (DateTime.TryParseExact(rawValue, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        return DateTime.TryParse(rawValue, culture, DateTimeStyles.None, out date);
    }
}
