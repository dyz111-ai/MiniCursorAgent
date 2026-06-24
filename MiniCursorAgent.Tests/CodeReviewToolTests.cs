using MiniCursorAgent.Memory;
using MiniCursorAgent.Tools;
using System.Text.Json;
using Xunit;

namespace MiniCursorAgent.Tests;

public class CodeReviewToolTests
{
    private static async Task<string> ReviewCode(string code)
    {
        var tool = new CodeReviewTool();
        var memory = new AgentMemory { CurrentCode = code };
        return await tool.ExecuteAsync(default, memory);
    }

    [Fact]
    public async Task Review_CleanCode_ReportsNoIssues()
    {
        var code = """
            public class Greeter
            {
                public string Greet(string name) => $"Hello, {name}!";
            }
            """;

        var result = await ReviewCode(code);

        Assert.Contains("未发现明显通用规则问题", result);
    }

    [Fact]
    public async Task Review_TodoComment_DetectsIssue()
    {
        var result = await ReviewCode("// TODO: 实现这个方法");
        Assert.Contains("TODO/FIXME/HACK", result);
    }

    [Fact]
    public async Task Review_FixmeComment_DetectsIssue()
    {
        var result = await ReviewCode("// FIXME: 有 bug");
        Assert.Contains("TODO/FIXME/HACK", result);
    }

    [Fact]
    public async Task Review_EmptyCatch_DetectsIssue()
    {
        var code = "catch { }";
        var result = await ReviewCode(code);
        Assert.Contains("空 catch 块", result);
    }

    [Fact]
    public async Task Review_EmptyCatchWithType_DetectsIssue()
    {
        var code = "catch (Exception) { }";
        var result = await ReviewCode(code);
        Assert.Contains("空 catch 块", result);
    }

    [Fact]
    public async Task Review_HardcodedApiKey_DetectsIssue()
    {
        var code = """var apiKey = "sk-abcdef1234567890";""";
        var result = await ReviewCode(code);
        Assert.Contains("硬编码密码/密钥/令牌", result);
    }

    [Fact]
    public async Task Review_HardcodedPassword_DetectsIssue()
    {
        var code = """string password = "MyS3cr3t!";""";
        var result = await ReviewCode(code);
        Assert.Contains("硬编码密码/密钥/令牌", result);
    }

    [Fact]
    public async Task Review_LineTooLong_DetectsIssue()
    {
        var longLine = new string('x', 141);
        var result = await ReviewCode(longLine);
        Assert.Contains("超过 140 个字符", result);
    }

    [Fact]
    public async Task Review_LineExactly140Chars_NoIssue()
    {
        var line = new string('x', 140);
        var result = await ReviewCode(line);
        Assert.DoesNotContain("超过 140 个字符", result);
    }

    [Fact]
    public async Task Review_NoCode_ReturnsError()
    {
        var tool = new CodeReviewTool();
        var memory = new AgentMemory { CurrentCode = "" };
        var result = await tool.ExecuteAsync(default, memory);
        Assert.Contains("错误", result);
    }

    [Fact]
    public async Task Review_MultipleIssues_CountsCorrectly()
    {
        var code = """
            // TODO: fix this
            catch { }
            """;

        var result = await ReviewCode(code);
        Assert.Contains("发现", result);
        Assert.DoesNotContain("未发现", result);
    }
}
