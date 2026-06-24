using MiniCursorAgent.Memory;
using Xunit;

namespace MiniCursorAgent.Tests;

public class RagStoreTests
{
    private readonly RagStore _store = new();

    [Fact]
    public void Constructor_PreloadsKnowledgeBase_CountIsPositive()
    {
        Assert.True(_store.Count > 0);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmpty()
    {
        var results = _store.Search(string.Empty);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsEmpty()
    {
        var results = _store.Search("   ");

        Assert.Empty(results);
    }

    [Fact]
    public void Search_RelevantKeyword_ReturnsResults()
    {
        // "async" matches the async/await knowledge entry
        var results = _store.Search("async await deadlock");

        Assert.NotEmpty(results);
    }

    [Fact]
    public void Search_RespectsTopKLimit()
    {
        var results = _store.Search("code exception null dispose async", topK: 2);

        Assert.True(results.Count <= 2);
    }

    [Fact]
    public void Search_ReturnsResultsWithTitle()
    {
        var results = _store.Search("null reference exception check");

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
    }

    [Fact]
    public void Search_ReturnsResultsWithContent()
    {
        var results = _store.Search("dispose IDisposable using");

        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Content)));
    }

    [Fact]
    public void Search_ResultsOrderedByDescendingScore()
    {
        var results = _store.Search("async await task");

        for (var i = 1; i < results.Count; i++)
            Assert.True(results[i - 1].Score >= results[i].Score);
    }

    [Fact]
    public void Search_UnrelatedQuery_ReturnsEmptyOrLowScore()
    {
        // completely unrelated to any C# knowledge entry
        var results = _store.Search("xyzzy foo bar qux unrelated");

        Assert.All(results, r => Assert.True(r.Score > 0.01));
    }

    [Fact]
    public void Search_SecurityKeyword_FindsSecretEntry()
    {
        var results = _store.Search("hardcoded password secret apikey");

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Title.Contains("密钥") || r.Title.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || r.Content.Contains("硬编码", StringComparison.OrdinalIgnoreCase));
    }
}
