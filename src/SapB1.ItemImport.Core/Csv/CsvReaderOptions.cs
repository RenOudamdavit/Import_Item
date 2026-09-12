using System.Text;

namespace SapB1.ItemImport.Core.Csv;

/// <summary>Parsing options for the delimited item source file.</summary>
public sealed class CsvReaderOptions
{
    /// <summary>Field delimiter. <c>null</c> auto-detects from the header line (path-based reads only).</summary>
    public char? Delimiter { get; set; }

    /// <summary>Quote character. Defaults to the RFC 4180 double quote.</summary>
    public char Quote { get; set; } = '"';

    /// <summary>
    /// Fallback encoding when the file has no byte-order mark. Defaults to UTF-8.
    /// Set <see cref="System.Text.Encoding.Latin1"/> for legacy Windows-1252-style exports.
    /// </summary>
    public Encoding Encoding { get; set; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Trim leading/trailing whitespace from every parsed value. SAP rejects padded keys, so this defaults to <c>true</c>.</summary>
    public bool TrimValues { get; set; } = true;
}
