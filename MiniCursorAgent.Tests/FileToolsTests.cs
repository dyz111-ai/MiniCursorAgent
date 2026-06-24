using MiniCursorAgent.Memory;
using MiniCursorAgent.Tools;
using System.Text.Json;
using Xunit;

namespace MiniCursorAgent.Tests;

public class FileToolsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"mca_test_{Guid.NewGuid():N}");

    public FileToolsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string TempFile(string name, string content = "hello world")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonElement JsonArg(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    // ── FileReadTool ────────────────────────────────────────────────────────

    [Fact]
    public async Task FileRead_NoPathNoMemory_ReturnsError()
    {
        var tool = new FileReadTool();
        var memory = new AgentMemory();
        var result = await tool.ExecuteAsync(default, memory);

        Assert.Contains("错误", result);
    }

    [Fact]
    public async Task FileRead_FromCurrentFilePath_ReadsMemoryCode()
    {
        var path = TempFile("sample.cs", "int x = 1;");
        var tool = new FileReadTool();
        var memory = new AgentMemory { CurrentFilePath = path, CurrentCode = "int x = 1;" };

        var result = await tool.ExecuteAsync(default, memory);

        Assert.Contains("int x = 1;", result);
        Assert.Contains("读取成功", result);
    }

    [Fact]
    public async Task FileRead_ExplicitPath_ReadsDiskFile()
    {
        var path = TempFile("explicit.cs", "var y = 2;");
        var tool = new FileReadTool();
        var memory = new AgentMemory();
        var input = JsonArg($"{{\"path\":\"{path.Replace("\\", "\\\\")}\"}}");

        var result = await tool.ExecuteAsync(input, memory);

        Assert.Contains("var y = 2;", result);
    }

    [Fact]
    public async Task FileRead_NonExistentPath_ReturnsError()
    {
        var tool = new FileReadTool();
        var memory = new AgentMemory();
        var input = JsonArg("{\"path\":\"C:\\\\nonexistent_file_abc.cs\"}");

        var result = await tool.ExecuteAsync(input, memory);

        Assert.Contains("错误", result);
    }

    // ── FileWriteTool ───────────────────────────────────────────────────────

    [Fact]
    public async Task FileWrite_AllowWriteFalse_RefusesExecution()
    {
        var tool = new FileWriteTool();
        var memory = new AgentMemory { AllowFileWrite = false };
        var input = JsonArg("{\"content\":\"something\"}");

        var result = await tool.ExecuteAsync(input, memory);

        Assert.Contains("拒绝", result);
    }

    [Fact]
    public async Task FileWrite_NoContent_ReturnsError()
    {
        var tool = new FileWriteTool();
        var memory = new AgentMemory { AllowFileWrite = true, CurrentFilePath = TempFile("x.cs") };

        var result = await tool.ExecuteAsync(default, memory);

        Assert.Contains("错误", result);
    }

    [Fact]
    public async Task FileWrite_ValidPathAndContent_WritesFileToDisk()
    {
        var path = Path.Combine(_tempDir, "output.cs");
        var tool = new FileWriteTool();
        var memory = new AgentMemory { AllowFileWrite = true };
        var escaped = path.Replace("\\", "\\\\");
        var input = JsonArg($"{{\"path\":\"{escaped}\",\"content\":\"written content\"}}");

        var result = await tool.ExecuteAsync(input, memory);

        Assert.Contains("写入成功", result);
        Assert.Equal("written content", File.ReadAllText(path));
    }

    [Fact]
    public async Task FileWrite_ExistingFile_CreatesBackup()
    {
        var path = TempFile("existing.cs", "original");
        var tool = new FileWriteTool();
        var memory = new AgentMemory { AllowFileWrite = true };
        var escaped = path.Replace("\\", "\\\\");
        var input = JsonArg($"{{\"path\":\"{escaped}\",\"content\":\"updated\"}}");

        await tool.ExecuteAsync(input, memory);

        var backups = Directory.GetFiles(_tempDir, "existing.cs.*.bak");
        Assert.Single(backups);
    }

    [Fact]
    public async Task FileWrite_UpdatesMemoryLastWritePath()
    {
        var path = Path.Combine(_tempDir, "mem.cs");
        var tool = new FileWriteTool();
        var memory = new AgentMemory { AllowFileWrite = true };
        var escaped = path.Replace("\\", "\\\\");
        var input = JsonArg($"{{\"path\":\"{escaped}\",\"content\":\"x\"}}");

        await tool.ExecuteAsync(input, memory);

        Assert.Equal(path, memory.LastWritePath);
    }

    // ── ReplaceTextTool ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceText_AllowWriteFalse_RefusesExecution()
    {
        var tool = new ReplaceTextTool();
        var memory = new AgentMemory { AllowFileWrite = false };

        var result = await tool.ExecuteAsync(default, memory);

        Assert.Contains("拒绝", result);
    }

    [Fact]
    public async Task ReplaceText_OldTextNotFound_ReturnsError()
    {
        var path = TempFile("r.cs", "int x = 1;");
        var tool = new ReplaceTextTool();
        var memory = new AgentMemory { AllowFileWrite = true, CurrentFilePath = path, CurrentCode = "int x = 1;" };
        var input = JsonArg("{\"oldText\":\"NOT PRESENT\",\"newText\":\"replacement\"}");

        var result = await tool.ExecuteAsync(input, memory);

        Assert.Contains("没有找到", result);
    }

    [Fact]
    public async Task ReplaceText_ValidReplacement_ReplacesFirstOccurrence()
    {
        var path = TempFile("rep.cs", "aaa bbb aaa");
        var tool = new ReplaceTextTool();
        var memory = new AgentMemory { AllowFileWrite = true, CurrentFilePath = path, CurrentCode = "aaa bbb aaa" };
        var input = JsonArg("{\"oldText\":\"aaa\",\"newText\":\"zzz\"}");

        await tool.ExecuteAsync(input, memory);

        Assert.Equal("zzz bbb aaa", File.ReadAllText(path));
    }

    [Fact]
    public async Task ReplaceText_ReplaceAll_ReplacesAllOccurrences()
    {
        var path = TempFile("repall.cs", "aaa bbb aaa");
        var tool = new ReplaceTextTool();
        var memory = new AgentMemory { AllowFileWrite = true, CurrentFilePath = path, CurrentCode = "aaa bbb aaa" };
        var input = JsonArg("{\"oldText\":\"aaa\",\"newText\":\"zzz\",\"replaceAll\":true}");

        await tool.ExecuteAsync(input, memory);

        Assert.Equal("zzz bbb zzz", File.ReadAllText(path));
    }

    [Fact]
    public async Task ReplaceText_ValidReplacement_CreatesBackup()
    {
        var path = TempFile("bak.cs", "original text here");
        var tool = new ReplaceTextTool();
        var memory = new AgentMemory { AllowFileWrite = true, CurrentFilePath = path, CurrentCode = "original text here" };
        var input = JsonArg("{\"oldText\":\"original\",\"newText\":\"updated\"}");

        await tool.ExecuteAsync(input, memory);

        var backups = Directory.GetFiles(_tempDir, "bak.cs.*.bak");
        Assert.Single(backups);
    }
}
