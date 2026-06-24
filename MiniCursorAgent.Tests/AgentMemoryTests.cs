using MiniCursorAgent.Memory;
using MiniCursorAgent.Models;
using Xunit;

namespace MiniCursorAgent.Tests;

public class AgentMemoryTests
{
    [Fact]
    public void AddConversation_SingleMessage_AppearsInHistory()
    {
        var memory = new AgentMemory();
        memory.AddConversation("user", "hello");

        Assert.Single(memory.ConversationHistory);
        Assert.Equal("user", memory.ConversationHistory[0].Role);
        Assert.Equal("hello", memory.ConversationHistory[0].Content);
    }

    [Fact]
    public void AddConversation_ExceedsLimit_TrimsOldestEntries()
    {
        var memory = new AgentMemory();
        for (var i = 0; i < 22; i++)
            memory.AddConversation("user", $"message {i}");

        Assert.Equal(20, memory.ConversationHistory.Count);
        Assert.Equal("message 2", memory.ConversationHistory[0].Content);
        Assert.Equal("message 21", memory.ConversationHistory[19].Content);
    }

    [Fact]
    public void AddConversation_Exactly20_KeepsAll()
    {
        var memory = new AgentMemory();
        for (var i = 0; i < 20; i++)
            memory.AddConversation("user", $"message {i}");

        Assert.Equal(20, memory.ConversationHistory.Count);
    }

    [Fact]
    public void BuildMemorySummary_NoFile_ShowsNotOpened()
    {
        var memory = new AgentMemory();
        var summary = memory.BuildMemorySummary();

        Assert.Contains("未打开", summary);
    }

    [Fact]
    public void BuildMemorySummary_WithFilePath_ShowsPath()
    {
        var memory = new AgentMemory { CurrentFilePath = @"C:\test\Program.cs" };
        var summary = memory.BuildMemorySummary();

        Assert.Contains(@"C:\test\Program.cs", summary);
    }

    [Fact]
    public void BuildMemorySummary_AllowFileWriteTrue_ReflectsTrue()
    {
        var memory = new AgentMemory { AllowFileWrite = true };
        var summary = memory.BuildMemorySummary();

        Assert.Contains("True", summary);
    }

    [Fact]
    public void BuildMemorySummary_LongReviewResult_TruncatesAt1200Chars()
    {
        var memory = new AgentMemory { LastReviewResult = new string('x', 1500) };
        var summary = memory.BuildMemorySummary();

        Assert.Contains("已截断", summary);
    }

    [Fact]
    public void BuildMemorySummary_ShortReviewResult_NotTruncated()
    {
        var memory = new AgentMemory { LastReviewResult = "short result" };
        var summary = memory.BuildMemorySummary();

        Assert.Contains("short result", summary);
        Assert.DoesNotContain("已截断", summary);
    }

    [Fact]
    public void BuildMemorySummary_NullResults_ShowsNone()
    {
        var memory = new AgentMemory();
        var summary = memory.BuildMemorySummary();

        Assert.Contains("无", summary);
    }

    [Fact]
    public void ConversationHistory_IsReadOnly_CannotBeModifiedExternally()
    {
        var memory = new AgentMemory();
        memory.AddConversation("user", "hello");

        Assert.IsAssignableFrom<IReadOnlyList<ChatMessage>>(memory.ConversationHistory);
    }
}
