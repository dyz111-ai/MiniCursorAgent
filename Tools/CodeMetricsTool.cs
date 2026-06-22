using MiniCursorAgent.Memory;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniCursorAgent.Tools;

public sealed class CodeMetricsTool : IAgentTool
{
    private static readonly Regex TypePattern = new(
        @"\b(class|struct|interface|record|enum|trait|object)\s+[A-Za-z_]\w*",
        RegexOptions.Compiled);

    private static readonly Regex FunctionPattern = new(
        @"\b(def|function|fn|func)\s+[A-Za-z_]\w*|\b(public|private|protected|internal)\s+(static\s+)?(async\s+)?[A-Za-z0-9_<>,\[\]\?\.]+\s+[A-Za-z_]\w*\s*\(",
        RegexOptions.Compiled);

    public string Name => "CodeMetricsTool";

    public string Description => "统计当前文件的行数、类型/函数数量和简单复杂度指标。输入：{\"code\":\"可选，不传则分析当前编辑器内容\"}";

    public Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var code = ToolJson.GetString(input, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = memory.CurrentCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult("CodeMetricsTool 错误：没有可分析的内容。请先调用 FileReadTool。");
        }

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var totalLines = lines.Length;
        var nonEmptyLines = lines.Count(line => !string.IsNullOrWhiteSpace(line));
        var typeCount = TypePattern.Matches(code).Count;
        var functionCount = FunctionPattern.Matches(code).Count;
        var maxLineLength = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
        var decisionPoints = Regex.Matches(code, @"\b(if|for|foreach|while|case|catch|switch|elif|else\s+if)\b|&&|\|\|").Count;
        var approximateComplexity = 1 + decisionPoints;

        var result = $"""
CodeMetricsTool 分析完成。
文件：{memory.CurrentFilePath ?? "当前编辑器内容"}
总代码行数：{totalLines}
非空行数：{nonEmptyLines}
类型数量（class/struct/interface 等，粗略统计）：{typeCount}
函数/方法数量（粗略统计）：{functionCount}
最长行长度：{maxLineLength}
近似圈复杂度：{approximateComplexity}
说明：这些是轻量级文本指标，用于辅助审查，不等同于语言专用静态分析器。
""";

        memory.LastMetricsResult = result;
        return Task.FromResult(result);
    }
}
