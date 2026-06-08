using MiniCursorAgent.Memory;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MiniCursorAgent.Tools;

public sealed class BuildTool : IAgentTool
{
    public string Name => "BuildTool";

    public string Description => "尝试在当前文件所在目录或其父目录中寻找 .csproj，然后执行 dotnet build。输入：{\"path\":\"可选，文件/目录/csproj 路径\"}";

    public async Task<string> ExecuteAsync(JsonElement input, AgentMemory memory, CancellationToken cancellationToken = default)
    {
        var path = ToolJson.GetString(input, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = memory.CurrentFilePath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return "BuildTool 错误：没有可用于寻找项目的路径。";
        }

        var projectPath = ResolveProjectPath(path);
        if (projectPath is null)
        {
            return $"BuildTool 未执行：从路径 {path} 向上未找到 .csproj 文件。单文件可以继续审查，但无法执行 dotnet build。";
        }

        var output = new StringBuilder();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" --nologo",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                output.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeoutCts.Token);

            var result = $"""
BuildTool 执行完成。
项目：{projectPath}
退出码：{process.ExitCode}
输出：
{ToolJson.TrimForObservation(output.ToString(), 10000)}
""";
            memory.LastBuildResult = result;
            return result;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore
            }

            var result = "BuildTool 超时：dotnet build 超过 60 秒未结束。";
            memory.LastBuildResult = result;
            return result;
        }
        catch (Exception ex)
        {
            var result = $"BuildTool 执行失败：{ex.Message}\n请确认本机已安装 .NET SDK，并且 dotnet 命令可用。";
            memory.LastBuildResult = result;
            return result;
        }
    }

    private static string? ResolveProjectPath(string path)
    {
        if (File.Exists(path) && string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(path);
        }

        string? directory;
        if (Directory.Exists(path))
        {
            directory = Path.GetFullPath(path);
        }
        else if (File.Exists(path))
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }
        else
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(path));
        }

        while (!string.IsNullOrWhiteSpace(directory))
        {
            var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                return csprojFiles[0];
            }

            directory = Directory.GetParent(directory)?.FullName;
        }

        return null;
    }
}
