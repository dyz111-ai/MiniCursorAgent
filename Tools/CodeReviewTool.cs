using MiniCursorAgent.Memory;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiniCursorAgent.Tools;

public sealed class CodeReviewTool : IAgentTool
{
    private static readonly Regex EmptyCatchPattern = new(@"^\s*catch\s*(\([^)]*\))?\s*\{\s*\}\s*$", RegexOptions.Compiled);
    private static readonly Regex HardcodedSecretPattern = new(@"(password|passwd|api[_-]?key|secret|token)\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => "CodeReviewTool";

    public string Description => "基于通用规则审查当前文件代码，发现常见问题。输入：{\"code\":\"可选，不传则审查当前编辑器内容\"}";

    public Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var code = ToolJson.GetString(input, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = memory.CurrentCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult("CodeReviewTool 错误：没有可审查的内容。请先调用 FileReadTool。");
        }

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var issues = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("FIXME", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("HACK", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"第 {lineNumber} 行：存在 TODO/FIXME/HACK 标记，提交前请确认是否已处理。");
            }

            if (trimmed.Length > 140)
            {
                issues.Add($"第 {lineNumber} 行：单行长度超过 140 个字符，可读性较差，建议拆分。");
            }

            if (EmptyCatchPattern.IsMatch(trimmed))
            {
                issues.Add($"第 {lineNumber} 行：存在空 catch 块，可能吞掉错误。建议至少记录日志或重新抛出。");
            }

            if (HardcodedSecretPattern.IsMatch(trimmed))
            {
                issues.Add($"第 {lineNumber} 行：疑似硬编码密码/密钥/令牌，建议使用环境变量或配置文件。");
            }

            if (trimmed.Contains("eval(", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"第 {lineNumber} 行：使用 eval()，存在安全风险，建议避免。");
            }

            if (trimmed.Contains("debugger", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"第 {lineNumber} 行：疑似调试断点语句，发布前建议移除。");
            }

            if (Regex.IsMatch(trimmed, @"^\s*//.*\?\?\?|\s*#.*\?\?\?"))
            {
                issues.Add($"第 {lineNumber} 行：存在未完成的占位注释（???），可能需要补全。");
            }
        }

        if (code.Contains("http://", StringComparison.OrdinalIgnoreCase) &&
            !code.Contains("https://", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("整体问题：检测到明文 http:// 链接，若涉及网络请求，建议优先使用 HTTPS。");
        }

        var builder = new StringBuilder();
        builder.AppendLine("CodeReviewTool 审查完成。");
        builder.AppendLine($"文件：{memory.CurrentFilePath ?? "当前编辑器内容"}");

        if (issues.Count == 0)
        {
            builder.AppendLine("未发现明显通用规则问题。仍建议结合具体语言与业务逻辑继续人工检查。");
        }
        else
        {
            builder.AppendLine($"发现 {issues.Count} 个潜在问题：");
            foreach (var issue in issues)
            {
                builder.AppendLine("- " + issue);
            }
        }

        var result = builder.ToString();
        memory.LastReviewResult = result;
        return Task.FromResult(result);
    }
}
