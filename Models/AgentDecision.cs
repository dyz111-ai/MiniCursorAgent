using System.Text.Json;

namespace MiniCursorAgent.Models;

public sealed class AgentDecision
{
    public string Thought { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public JsonElement ActionInput { get; init; }

    public static AgentDecision Parse(string rawText)
    {
        var json = ExtractJson(rawText);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var thought = ReadString(root, "thought") ?? ReadString(root, "Thought") ?? string.Empty;
        var action = ReadString(root, "action") ?? ReadString(root, "Action") ?? string.Empty;

        JsonElement input = default;
        if (root.TryGetProperty("actionInput", out var actionInput) ||
            root.TryGetProperty("action_input", out actionInput) ||
            root.TryGetProperty("ActionInput", out actionInput))
        {
            input = actionInput.Clone();
        }
        else
        {
            using var emptyDocument = JsonDocument.Parse("{}");
            input = emptyDocument.RootElement.Clone();
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new InvalidOperationException("Agent 回复中没有 action 字段。需要返回 JSON：{\"thought\":\"...\",\"action\":\"...\",\"actionInput\":{...}}");
        }

        return new AgentDecision
        {
            Thought = thought,
            Action = action,
            ActionInput = input
        };
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException("Agent 回复不是合法 JSON。原始回复：" + text);
        }

        return trimmed[start..(end + 1)];
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}
