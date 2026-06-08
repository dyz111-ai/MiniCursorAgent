using MiniCursorAgent.Memory;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniCursorAgent.Tools;

public sealed class CodeMetricsTool : IAgentTool
{
    public string Name => "CodeMetricsTool";

    public string Description => "统计当前 C# 代码的行数、类数量、方法数量和简单复杂度指标。输入：{}";

    public Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var code = ToolJson.GetString(input, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = memory.CurrentCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult("CodeMetricsTool 错误：没有可分析的代码。请先调用 FileReadTool。");
        }

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var totalLines = lines.Length;
        var nonEmptyLines = lines.Count(line => !string.IsNullOrWhiteSpace(line));
        var classCount = Regex.Matches(code, @"\b(class|record|struct|interface)\s+[A-Za-z_][A-Za-z0-9_]*").Count;
        var methodCount = Regex.Matches(code, @"\b(public|private|protected|internal)\s+(static\s+)?(async\s+)?[A-Za-z0-9_<>,\[\]\?]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\(").Count;
        var maxLineLength = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
        var decisionPoints = Regex.Matches(code, @"\b(if|for|foreach|while|case|catch|switch)\b|&&|\|\|").Count;
        var approximateComplexity = 1 + decisionPoints;

        var result = $"""
CodeMetricsTool 分析完成。
文件：{memory.CurrentFilePath ?? "当前编辑器内容"}
总代码行数：{totalLines}
非空行数：{nonEmptyLines}
类型数量（class/record/struct/interface）：{classCount}
方法数量（粗略统计）：{methodCount}
最长行长度：{maxLineLength}
近似圈复杂度：{approximateComplexity}
说明：这些是轻量级静态指标，用于辅助审查，不等同于 Roslyn 编译级分析。
""";

        memory.LastMetricsResult = result;
        return Task.FromResult(result);
    }
}
