using System.Text.Json;

namespace MiniCursorAgent.Tools;

internal static class ToolJson
{
    public static string? GetString(JsonElement input, string propertyName)
    {
        if (input.ValueKind == JsonValueKind.Undefined || input.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (input.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        return null;
    }

    public static bool GetBool(JsonElement input, string propertyName, bool defaultValue = false)
    {
        if (input.ValueKind == JsonValueKind.Undefined || input.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (!input.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }

    public static string TrimForObservation(string text, int maxLength = 12000)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "\n...（Observation 过长，已截断）";
    }
}
