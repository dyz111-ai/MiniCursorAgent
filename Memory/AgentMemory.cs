using MiniCursorAgent.Models;
using System.IO;

namespace MiniCursorAgent.Memory;

public sealed class AgentMemory
{
    private readonly List<ChatMessage> _conversationHistory = new();

    public string? CurrentFilePath { get; set; }
    public string CurrentCode { get; set; } = string.Empty;
    public string? LastReviewResult { get; set; }
    public string? LastMetricsResult { get; set; }
    public string? LastBuildResult { get; set; }
    public string? LastWritePath { get; set; }
    public bool AllowFileWrite { get; set; }

    public IReadOnlyList<ChatMessage> ConversationHistory => _conversationHistory;

    public void AddConversation(string role, string content)
    {
        _conversationHistory.Add(new ChatMessage(role, content));

        // 简单短期记忆：只保留最近 20 条，避免 prompt 过长。
        if (_conversationHistory.Count > 20)
        {
            _conversationHistory.RemoveAt(0);
        }
    }

    public string BuildMemorySummary()
    {
        return $"""
当前文件路径：{CurrentFilePath ?? "未打开"}
当前文件类型：{GetFileTypeLabel()}
是否允许写入文件：{AllowFileWrite}
最近一次代码审查结果：{TrimForPrompt(LastReviewResult, 1200)}
最近一次代码指标结果：{TrimForPrompt(LastMetricsResult, 800)}
最近一次构建结果：{TrimForPrompt(LastBuildResult, 1200)}
最近一次写入路径：{LastWritePath ?? "无"}
""";
    }

    private static string TrimForPrompt(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "无";
        }

        return text.Length <= maxLength ? text : text[..maxLength] + "...（已截断）";
    }

    private string GetFileTypeLabel()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath))
        {
            return "未知";
        }

        var extension = Path.GetExtension(CurrentFilePath);
        return string.IsNullOrWhiteSpace(extension) ? "未知" : extension;
    }
}
