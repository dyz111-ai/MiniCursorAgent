using MiniCursorAgent.Memory;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class FileWriteTool : IAgentTool
{
    public string Name => "FileWriteTool";

    public string Description => "将完整代码内容写入当前文件或指定路径。输入：{\"path\":\"可选\",\"content\":\"完整新代码\"}。写入前会自动生成 .bak 备份。";

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        if (!memory.AllowFileWrite)
        {
            return "FileWriteTool 拒绝执行：当前界面未勾选“允许 Agent 写入文件”。";
        }

        var path = ToolJson.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = memory.CurrentFilePath;
        }

        var content = ToolJson.GetString(input, "content") ?? ToolJson.GetString(input, "code");

        if (string.IsNullOrWhiteSpace(path))
        {
            return "FileWriteTool 错误：没有目标路径。";
        }

        if (content is null)
        {
            return "FileWriteTool 错误：没有 content 字段，不能写入空内容。";
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            return $"FileWriteTool 错误：目录不存在：{directory}";
        }

        if (File.Exists(path))
        {
            var backupPath = path + $".{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: false);
        }

        await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
        memory.CurrentFilePath = path;
        memory.CurrentCode = content;
        memory.LastWritePath = path;

        return $"FileWriteTool 写入成功：{path}\n已自动备份旧文件（如果旧文件存在）。";
    }
}
