using MiniCursorAgent.Memory;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class ReplaceTextTool : IAgentTool
{
    public string Name => "ReplaceTextTool";

    public string Description => "在当前文件中替换一段文本，适合小范围修改。输入：{\"path\":\"可选\",\"oldText\":\"旧文本\",\"newText\":\"新文本\",\"replaceAll\":false}。写入前会自动备份。";

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        if (!memory.AllowFileWrite)
        {
            return "ReplaceTextTool 拒绝执行：当前界面未勾选“允许 Agent 写入文件”。";
        }

        var path = ToolJson.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = memory.CurrentFilePath;
        }

        var oldText = ToolJson.GetString(input, "oldText");
        var newText = ToolJson.GetString(input, "newText");
        var replaceAll = ToolJson.GetBool(input, "replaceAll");

        if (string.IsNullOrWhiteSpace(path))
        {
            return "ReplaceTextTool 错误：没有目标路径。";
        }

        if (oldText is null)
        {
            return "ReplaceTextTool 错误：缺少 oldText。";
        }

        if (newText is null)
        {
            return "ReplaceTextTool 错误：缺少 newText。";
        }

        var code = string.Equals(path, memory.CurrentFilePath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(memory.CurrentCode)
            ? memory.CurrentCode
            : await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);

        if (!code.Contains(oldText, StringComparison.Ordinal))
        {
            return "ReplaceTextTool 错误：当前文件中没有找到 oldText，未执行修改。请先调用 FileReadTool 确认原文。";
        }

        var updated = replaceAll
            ? code.Replace(oldText, newText, StringComparison.Ordinal)
            : ReplaceFirst(code, oldText, newText);

        if (File.Exists(path))
        {
            var backupPath = path + $".{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: false);
        }

        await File.WriteAllTextAsync(path, updated, Encoding.UTF8, cancellationToken);
        memory.CurrentFilePath = path;
        memory.CurrentCode = updated;
        memory.LastWritePath = path;

        return $"ReplaceTextTool 修改成功：{path}\n替换模式：{(replaceAll ? "全部替换" : "只替换第一次出现")}\n已自动备份旧文件。";
    }

    private static string ReplaceFirst(string source, string oldText, string newText)
    {
        var index = source.IndexOf(oldText, StringComparison.Ordinal);
        return index < 0 ? source : source[..index] + newText + source[(index + oldText.Length)..];
    }
}
