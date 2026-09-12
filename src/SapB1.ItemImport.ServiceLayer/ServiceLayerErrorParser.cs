using System.Text.Json;

namespace SapB1.ItemImport.ServiceLayer;

/// <summary>
/// Extracts the SAP error code and message from a Service Layer error body.
/// </summary>
/// <remarks>
/// Shapes differ between patch levels — <c>message</c> is sometimes an object with a <c>value</c>
/// field and sometimes a bare string, and the code is sometimes numeric and sometimes quoted — so
/// both are handled. The raw body is used as a fallback so an operator never sees an empty reason.
/// </remarks>
public static class ServiceLayerErrorParser
{
    private const int MaxFallbackLength = 500;

    public static (string? Code, string Message) Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, "SAP returned an error with no details.");
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var code = ReadCode(error);
                var message = ReadMessage(error);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return (code, message);
                }

                if (code is not null)
                {
                    return (code, $"SAP rejected the request with error code {code}.");
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (an HTML error page from a reverse proxy, for instance) — fall through.
        }

        return (null, Truncate(body.Trim()));
    }

    private static string? ReadCode(JsonElement error)
    {
        if (!error.TryGetProperty("code", out var code))
        {
            return null;
        }

        return code.ValueKind switch
        {
            JsonValueKind.Number => code.GetRawText(),
            JsonValueKind.String => code.GetString(),
            _ => null,
        };
    }

    private static string? ReadMessage(JsonElement error)
    {
        if (!error.TryGetProperty("message", out var message))
        {
            return null;
        }

        return message.ValueKind switch
        {
            JsonValueKind.String => message.GetString(),
            JsonValueKind.Object when message.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                => value.GetString(),
            _ => null,
        };
    }

    private static string Truncate(string value)
        => value.Length <= MaxFallbackLength ? value : value[..MaxFallbackLength] + "…";
}
