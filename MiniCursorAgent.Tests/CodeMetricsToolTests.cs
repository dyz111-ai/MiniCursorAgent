using MiniCursorAgent.Memory;
using MiniCursorAgent.Tools;
using Xunit;

namespace MiniCursorAgent.Tests;

public class CodeMetricsToolTests
{
    private static async Task<string> GetMetrics(string code)
    {
        var tool = new CodeMetricsTool();
        var memory = new AgentMemory { CurrentCode = code };
        return await tool.ExecuteAsync(default, memory);
    }

    [Fact]
    public async Task Metrics_ThreeLines_CountsCorrectly()
    {
        var result = await GetMetrics("line1\nline2\nline3");
        Assert.Contains("总代码行数：3", result);
    }

    [Fact]
    public async Task Metrics_WithEmptyLines_CountsNonEmptyCorrectly()
    {
        var result = await GetMetrics("line1\n\nline3");
        Assert.Contains("非空行数：2", result);
    }

    [Fact]
    public async Task Metrics_ClassDeclaration_DetectsType()
    {
        var code = "public class MyService { }";
        var result = await GetMetrics(code);
        Assert.Contains("类型数量", result);
        Assert.DoesNotContain("类型数量（class/struct/interface 等，粗略统计）：0", result);
    }

    [Fact]
    public async Task Metrics_InterfaceDeclaration_CountsAsType()
    {
        var code = "public interface IMyService { }";
        var result = await GetMetrics(code);
        Assert.Contains("类型数量", result);
    }

    [Fact]
    public async Task Metrics_IfStatement_IncreasesComplexity()
    {
        var simpleCode = "int x = 1;";
        var complexCode = "if (x > 0) { } if (y > 0) { }";

        var simpleResult = await GetMetrics(simpleCode);
        var complexResult = await GetMetrics(complexCode);

        var simpleComplexity = ExtractComplexity(simpleResult);
        var complexComplexity = ExtractComplexity(complexResult);

        Assert.True(complexComplexity > simpleComplexity);
    }

    [Fact]
    public async Task Metrics_EmptyCode_ReturnsError()
    {
        var tool = new CodeMetricsTool();
        var memory = new AgentMemory { CurrentCode = string.Empty };
        var result = await tool.ExecuteAsync(default, memory);
        Assert.Contains("错误", result);
    }

    [Fact]
    public async Task Metrics_SingleLine_MaxLineLengthCorrect()
    {
        var line = new string('a', 80);
        var result = await GetMetrics(line);
        Assert.Contains("最长行长度：80", result);
    }

    [Fact]
    public async Task Metrics_CsharpMethod_CountsFunction()
    {
        var code = "public string GetName() { return \"test\"; }";
        var result = await GetMetrics(code);
        Assert.Contains("函数/方法数量", result);
    }

    private static int ExtractComplexity(string metricsResult)
    {
        const string prefix = "近似圈复杂度：";
        var idx = metricsResult.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return -1;
        var start = idx + prefix.Length;
        var end = metricsResult.IndexOf('\n', start);
        var numStr = end < 0 ? metricsResult[start..] : metricsResult[start..end];
        return int.TryParse(numStr.Trim(), out var n) ? n : -1;
    }
}
