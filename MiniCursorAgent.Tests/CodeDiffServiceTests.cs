using MiniCursorAgent.Services;
using Xunit;

namespace MiniCursorAgent.Tests;

public class CodeDiffServiceTests
{
    [Fact]
    public void BuildDisplayLines_IdenticalText_AllLinesUnchanged()
    {
        var text = "line1\nline2\nline3";
        var result = CodeDiffService.BuildDisplayLines(text, text);

        Assert.All(result, line => Assert.Equal(DiffLineKind.Unchanged, line.Kind));
    }

    [Fact]
    public void BuildDisplayLines_AddedLine_DetectsAddedLine()
    {
        var oldText = "line1\nline3";
        var newText = "line1\nline2\nline3";

        var result = CodeDiffService.BuildDisplayLines(oldText, newText);

        Assert.Contains(result, line => line.Kind == DiffLineKind.Added && line.Text == "line2");
    }

    [Fact]
    public void BuildDisplayLines_RemovedLine_DetectsRemovedLine()
    {
        var oldText = "line1\nline2\nline3";
        var newText = "line1\nline3";

        var result = CodeDiffService.BuildDisplayLines(oldText, newText);

        Assert.Contains(result, line => line.Kind == DiffLineKind.Removed && line.Text == "line2");
    }

    [Fact]
    public void BuildDisplayLines_ReplacedLine_DetectsBothAddedAndRemoved()
    {
        var oldText = "hello";
        var newText = "world";

        var result = CodeDiffService.BuildDisplayLines(oldText, newText);

        Assert.Contains(result, line => line.Kind == DiffLineKind.Removed);
        Assert.Contains(result, line => line.Kind == DiffLineKind.Added);
    }

    [Fact]
    public void BuildDisplayLines_CommonLines_PreservedAsUnchanged()
    {
        var oldText = "aaa\nbbb\nccc";
        var newText = "aaa\nxxx\nccc";

        var result = CodeDiffService.BuildDisplayLines(oldText, newText);

        Assert.Contains(result, line => line.Kind == DiffLineKind.Unchanged && line.Text == "aaa");
        Assert.Contains(result, line => line.Kind == DiffLineKind.Unchanged && line.Text == "ccc");
    }

    [Fact]
    public void HasChanges_IdenticalText_ReturnsFalse()
    {
        var text = "no changes here";
        var lines = CodeDiffService.BuildDisplayLines(text, text);

        Assert.False(CodeDiffService.HasChanges(lines));
    }

    [Fact]
    public void HasChanges_DifferentText_ReturnsTrue()
    {
        var lines = CodeDiffService.BuildDisplayLines("old content", "new content");

        Assert.True(CodeDiffService.HasChanges(lines));
    }

    [Fact]
    public void BuildDisplayLines_EmptyOldText_NewLineIsAdded()
    {
        var result = CodeDiffService.BuildDisplayLines(string.Empty, "new line");

        Assert.Contains(result, line => line.Kind == DiffLineKind.Added && line.Text == "new line");
    }

    [Fact]
    public void BuildDisplayLines_EmptyNewText_OldLineIsRemoved()
    {
        var result = CodeDiffService.BuildDisplayLines("old line", string.Empty);

        Assert.Contains(result, line => line.Kind == DiffLineKind.Removed && line.Text == "old line");
    }

    [Fact]
    public void BuildDisplayLines_CrlfNormalized_TreatedSameAsLf()
    {
        var lf = "line1\nline2";
        var crlf = "line1\r\nline2";

        var result = CodeDiffService.BuildDisplayLines(lf, crlf);

        Assert.False(CodeDiffService.HasChanges(result));
    }
}
