namespace MiniCursorAgent.Services;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed
}

public sealed record DiffDisplayLine(DiffLineKind Kind, string Text);

public static class CodeDiffService
{
    public static List<DiffDisplayLine> BuildDisplayLines(string oldText, string newText)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        if (oldLines.SequenceEqual(newLines))
        {
            return oldLines.Select(line => new DiffDisplayLine(DiffLineKind.Unchanged, line)).ToList();
        }

        var lcs = BuildLcsTable(oldLines, newLines);
        var result = new List<DiffDisplayLine>();
        var i = oldLines.Length;
        var j = newLines.Length;

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && oldLines[i - 1] == newLines[j - 1])
            {
                result.Add(new DiffDisplayLine(DiffLineKind.Unchanged, oldLines[i - 1]));
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                result.Add(new DiffDisplayLine(DiffLineKind.Added, newLines[j - 1]));
                j--;
            }
            else if (i > 0)
            {
                result.Add(new DiffDisplayLine(DiffLineKind.Removed, oldLines[i - 1]));
                i--;
            }
        }

        result.Reverse();
        return result;
    }

    public static bool HasChanges(IReadOnlyList<DiffDisplayLine> lines)
    {
        return lines.Any(line => line.Kind is DiffLineKind.Added or DiffLineKind.Removed);
    }

    private static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    private static int[,] BuildLcsTable(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        var table = new int[oldLines.Count + 1, newLines.Count + 1];

        for (var i = 1; i <= oldLines.Count; i++)
        {
            for (var j = 1; j <= newLines.Count; j++)
            {
                table[i, j] = oldLines[i - 1] == newLines[j - 1]
                    ? table[i - 1, j - 1] + 1
                    : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table;
    }
}
