using System.Globalization;

namespace SapB1.ItemImport.Core.Mapping;

/// <summary>Controls how source text is turned into SAP-ready values.</summary>
public sealed class ValueConversionOptions
{
    /// <summary>
    /// Culture used to parse numbers and dates. Invariant by default; set to e.g. <c>de-DE</c> for
    /// exports that use a comma decimal separator.
    /// </summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>
    /// Literal that means "explicitly clear this field in SAP". Without it there is no way to tell
    /// "leave the existing value alone" (empty cell) from "blank it out".
    /// </summary>
    public string NullToken { get; set; } = "<NULL>";
}
