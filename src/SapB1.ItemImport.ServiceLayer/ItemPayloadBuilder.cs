using System.Globalization;
using System.Text;
using System.Text.Json;
using SapB1.ItemImport.Core.Domain;
using SapB1.ItemImport.Core.Mapping;

namespace SapB1.ItemImport.ServiceLayer;

/// <summary>Turns an <see cref="ItemRecord"/> into the JSON body the Service Layer expects.</summary>
/// <remarks>
/// Only supplied fields are written. For <c>PATCH</c> that is what makes an update non-destructive:
/// SAP merges the properties it receives and leaves the rest of the item master alone.
/// </remarks>
public static class ItemPayloadBuilder
{
    /// <summary>Body for <c>POST /Items</c>, including the primary key.</summary>
    public static string BuildCreatePayload(ItemRecord record) => Build(record, includeItemCode: true);

    /// <summary>Body for <c>PATCH /Items('code')</c>, without the primary key.</summary>
    public static string BuildUpdatePayload(ItemRecord record) => Build(record, includeItemCode: false);

    private static string Build(ItemRecord record, bool includeItemCode)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var buffer = new MemoryStream(256);

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            if (includeItemCode)
            {
                writer.WriteString(ItemFieldCatalog.ItemCodeProperty, record.ItemCode);
            }

            foreach (var (property, value) in record.Fields)
            {
                // The key is addressed in the URL (update) or written above (create); never twice.
                if (string.Equals(property, ItemFieldCatalog.ItemCodeProperty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                writer.WritePropertyName(property);
                WriteValue(writer, value);
            }

            if (record.Prices.Count > 0)
            {
                writer.WritePropertyName("ItemPrices");
                writer.WriteStartArray();

                foreach (var price in record.Prices)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("PriceList", price.PriceList);

                    if (price.Price.HasValue)
                    {
                        writer.WriteNumber("Price", price.Price.Value);
                    }

                    if (!string.IsNullOrEmpty(price.Currency))
                    {
                        writer.WriteString("Currency", price.Currency);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case double number:
                writer.WriteNumberValue(number);
                break;
            case decimal number:
                writer.WriteNumberValue(number);
                break;
            case DateTime date:
                writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
