using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MiniCursorAgent.Services;

public static class MarkdownLogRenderer
{
    private static readonly Regex NumberedListPattern = new(@"^\d+\.\s+", RegexOptions.Compiled);
    private static readonly Regex InlinePattern = new(@"\*\*(.+?)\*\*|`([^`]+)`", RegexOptions.Compiled);

    public static IEnumerable<Block> CreateBlocks(string markdown, Brush defaultForeground)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                yield return new Paragraph(new Run(" "))
                {
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 4,
                    LineHeight = 4
                };
                continue;
            }

            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                yield return CreateHeadingParagraph(trimmed[4..], 3, defaultForeground);
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                yield return CreateHeadingParagraph(trimmed[3..], 2, defaultForeground);
                continue;
            }

            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                yield return CreateHeadingParagraph(trimmed[2..], 1, defaultForeground);
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                yield return CreateListParagraph(trimmed[2..], defaultForeground);
                continue;
            }

            if (NumberedListPattern.IsMatch(trimmed))
            {
                var content = NumberedListPattern.Replace(trimmed, string.Empty);
                yield return CreateListParagraph(content, defaultForeground);
                continue;
            }

            yield return CreateBodyParagraph(trimmed, defaultForeground);
        }
    }

    private static Paragraph CreateHeadingParagraph(string text, int level, Brush defaultForeground)
    {
        var (fontSize, weight, topMargin, color) = level switch
        {
            1 => (16.5, FontWeights.Bold, 10.0, Color.FromRgb(0x11, 0x11, 0x11)),
            2 => (15.0, FontWeights.Bold, 8.0, Color.FromRgb(0x1A, 0x1A, 0x1A)),
            _ => (14.0, FontWeights.SemiBold, 6.0, Color.FromRgb(0x2A, 0x2A, 0x2A))
        };

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, topMargin, 0, 4),
            FontSize = fontSize,
            FontWeight = weight,
            LineHeight = fontSize + 4
        };

        AddInlineFormattedText(paragraph, text, new SolidColorBrush(color), fontSize, weight);
        return paragraph;
    }

    private static Paragraph CreateListParagraph(string text, Brush defaultForeground)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(16, 1, 0, 3),
            FontSize = 13,
            LineHeight = 20
        };

        paragraph.Inlines.Add(new Run("• ")
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
            FontWeight = FontWeights.Bold
        });

        AddInlineFormattedText(paragraph, text, defaultForeground, 13, FontWeights.Normal);
        return paragraph;
    }

    private static Paragraph CreateBodyParagraph(string text, Brush defaultForeground)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 1, 0, 3),
            FontSize = 13,
            LineHeight = 20
        };

        AddInlineFormattedText(paragraph, text, defaultForeground, 13, FontWeights.Normal);
        return paragraph;
    }

    private static void AddInlineFormattedText(
        Paragraph paragraph,
        string text,
        Brush defaultForeground,
        double fontSize,
        FontWeight defaultWeight)
    {
        var lastIndex = 0;

        foreach (Match match in InlinePattern.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                paragraph.Inlines.Add(CreateRun(text[lastIndex..match.Index], defaultForeground, fontSize, defaultWeight));
            }

            if (match.Groups[1].Success)
            {
                paragraph.Inlines.Add(CreateRun(match.Groups[1].Value, defaultForeground, fontSize, FontWeights.Bold));
            }
            else if (match.Groups[2].Success)
            {
                paragraph.Inlines.Add(new Run(match.Groups[2].Value)
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0x25, 0x4E)),
                    Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = fontSize - 0.5,
                    FontWeight = FontWeights.Normal
                });
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            paragraph.Inlines.Add(CreateRun(text[lastIndex..], defaultForeground, fontSize, defaultWeight));
        }

        if (paragraph.Inlines.Count == 0)
        {
            paragraph.Inlines.Add(CreateRun(text, defaultForeground, fontSize, defaultWeight));
        }
    }

    private static Run CreateRun(string text, Brush foreground, double fontSize, FontWeight weight)
    {
        return new Run(text)
        {
            Foreground = foreground,
            FontSize = fontSize,
            FontWeight = weight
        };
    }
}
