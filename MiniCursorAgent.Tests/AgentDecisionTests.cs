using MiniCursorAgent.Models;
using System.Text.Json;
using Xunit;

namespace MiniCursorAgent.Tests;

public class AgentDecisionTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsCorrectDecision()
    {
        var json = """{"thought":"需要先读取文件","action":"FileReadTool","actionInput":{}}""";
        var decision = AgentDecision.Parse(json);

        Assert.Equal("需要先读取文件", decision.Thought);
        Assert.Equal("FileReadTool", decision.Action);
        Assert.Equal(JsonValueKind.Object, decision.ActionInput.ValueKind);
    }

    [Fact]
    public void Parse_FinalAnswer_ParsesAnswerField()
    {
        var json = """{"thought":"完成","action":"FinalAnswer","actionInput":{"answer":"审查结果如下"}}""";
        var decision = AgentDecision.Parse(json);

        Assert.Equal("FinalAnswer", decision.Action);
        Assert.True(decision.ActionInput.TryGetProperty("answer", out var ans));
        Assert.Equal("审查结果如下", ans.GetString());
    }

    [Fact]
    public void Parse_WrappedInMarkdownCodeBlock_ExtractsJsonCorrectly()
    {
        var wrapped = "```json\n{\"thought\":\"t\",\"action\":\"FinalAnswer\",\"actionInput\":{}}\n```";
        var decision = AgentDecision.Parse(wrapped);

        Assert.Equal("FinalAnswer", decision.Action);
    }

    [Fact]
    public void Parse_UpperCaseFieldNames_ParsesCorrectly()
    {
        var json = """{"Thought":"大写思考","Action":"CodeReviewTool","ActionInput":{}}""";
        var decision = AgentDecision.Parse(json);

        Assert.Equal("大写思考", decision.Thought);
        Assert.Equal("CodeReviewTool", decision.Action);
    }

    [Fact]
    public void Parse_ActionInputWithPath_ReturnsPathProperty()
    {
        var json = """{"thought":"读取","action":"FileReadTool","actionInput":{"path":"test.cs"}}""";
        var decision = AgentDecision.Parse(json);

        Assert.True(decision.ActionInput.TryGetProperty("path", out var path));
        Assert.Equal("test.cs", path.GetString());
    }

    [Fact]
    public void Parse_MissingActionField_ThrowsInvalidOperationException()
    {
        var json = """{"thought":"only thought","actionInput":{}}""";
        Assert.Throws<InvalidOperationException>(() => AgentDecision.Parse(json));
    }

    [Fact]
    public void Parse_PlainText_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => AgentDecision.Parse("这不是 JSON，请只返回 JSON 对象。"));
    }

    [Fact]
    public void Parse_EmptyString_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => AgentDecision.Parse(string.Empty));
    }

    [Fact]
    public void Parse_ActionInputUnderscore_ParsesCorrectly()
    {
        var json = """{"thought":"t","action":"FileReadTool","action_input":{"path":"a.cs"}}""";
        var decision = AgentDecision.Parse(json);
        Assert.Equal("FileReadTool", decision.Action);
    }
}
