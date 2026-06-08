using MiniCursorAgent.Memory;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class CodeReviewTool : IAgentTool
{
    public string Name => "CodeReviewTool";

    public string Description => "基于固定规则审查 C# 代码，发现常见问题。输入：{\"code\":\"可选，不传则审查当前编辑器代码\"}";

    public Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var code = ToolJson.GetString(input, "code");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = memory.CurrentCode;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult("CodeReviewTool 错误：没有可审查的代码。请先调用 FileReadTool。 ");
        }

        var lines = code.Replace("\r\n", "\n").Split('\n');
        var issues = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Contains("async void", StringComparison.Ordinal))
            {
                issues.Add($"第 {lineNumber} 行：出现 async void。除事件处理器外，建议改为 async Task，便于异常传播和测试。");
            }

            if (trimmed.Contains(".Result", StringComparison.Ordinal) || trimmed.Contains(".Wait()", StringComparison.Ordinal))
            {
                issues.Add($"第 {lineNumber} 行：同步等待异步任务，可能造成死锁或阻塞。建议改用 await。");
            }

            if (trimmed.Contains("File.ReadAllText(", StringComparison.Ordinal))
            {
                issues.Add($"第 {lineNumber} 行：使用同步文件读取。若在 UI 或服务端场景中，建议改为 File.ReadAllTextAsync。");
            }

            if (trimmed.Contains("File.WriteAllText(", StringComparison.Ordinal))
            {
                issues.Add($"第 {lineNumber} 行：使用同步文件写入。建议改为 File.WriteAllTextAsync，并加入异常处理。");
            }

            if (trimmed.StartsWith("catch", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("Exception", StringComparison.Ordinal))
            {
                issues.Add($"第 {lineNumber} 行：捕获 Exception 范围较大。建议记录异常信息，并尽量捕获更具体的异常类型。");
            }

            if (trimmed is "catch" or "catch()" or "catch {" or "catch{}")
            {
                issues.Add($"第 {lineNumber} 行：存在空 catch 或无异常变量的 catch，可能吞掉错误。建议至少记录日志。");
            }

            if (trimmed.Contains("TODO", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("FIXME", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"第 {lineNumber} 行：存在 TODO/FIXME，提交前需要确认是否已处理。");
            }

            if (trimmed.Length > 140)
            {
                issues.Add($"第 {lineNumber} 行：单行长度超过 140 个字符，可读性较差。建议拆分。");
            }
        }

        if (!code.Contains("try", StringComparison.OrdinalIgnoreCase) &&
            (code.Contains("File.", StringComparison.Ordinal) || code.Contains("HttpClient", StringComparison.Ordinal)))
        {
            issues.Add("整体问题：代码涉及文件或网络操作，但没有明显异常处理。建议增加 try-catch，并给用户返回可理解的错误信息。");
        }

        var builder = new StringBuilder();
        builder.AppendLine("CodeReviewTool 审查完成。");
        builder.AppendLine($"文件：{memory.CurrentFilePath ?? "当前编辑器内容"}");

        if (issues.Count == 0)
        {
            builder.AppendLine("未发现明显规则问题。仍建议结合业务逻辑继续人工检查。 ");
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
