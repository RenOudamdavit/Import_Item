using System.Text;

namespace SapB1.ItemImport.Core.Mapping;

/// <summary>
/// Normalises column and property names so that "Item Code", "item_code", "ITEM-CODE" and
/// "ItemCode" all resolve to the same SAP property without needing an alias entry for each spelling.
/// </summary>
public static class SapNameNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var current in name)
        {
            if (char.IsLetterOrDigit(current))
            {
                builder.Append(char.ToLowerInvariant(current));
            }
        }

        return builder.ToString();
    }
}
