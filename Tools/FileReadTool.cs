using MiniCursorAgent.Memory;
using MiniCursorAgent.Services;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class FileReadTool : IAgentTool
{
    public string Name => "FileReadTool";

    public string Description => "读取当前文件或指定路径的文本/代码内容。输入：{\"path\":\"可选，不传则读取当前文件\"}";

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var path = ToolJson.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = memory.CurrentFilePath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "FileReadTool 错误：当前没有打开文件，也没有提供 path。";
        }

        if (!File.Exists(path))
        {
            return $"FileReadTool 错误：文件不存在：{path}";
        }

        string code;
        if (string.Equals(path, memory.CurrentFilePath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(memory.CurrentCode))
        {
            code = memory.CurrentCode;
        }
        else
        {
            code = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        }

        memory.CurrentFilePath = path;
        memory.CurrentCode = code;

        var fenceTag = CodeFileHelper.GetCodeFenceTag(path);
        var fenceOpen = string.IsNullOrEmpty(fenceTag) ? "```" : $"```{fenceTag}";

        var result = $"""
FileReadTool 读取成功。
路径：{path}
文件类型：{CodeFileHelper.GetFileTypeLabel(path)}
代码内容：
{fenceOpen}
{code}
```
""";

        return ToolJson.TrimForObservation(result);
    }
}
