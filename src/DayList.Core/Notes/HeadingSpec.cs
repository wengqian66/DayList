using System.Text.RegularExpressions;

namespace DayList.Core;

public readonly record struct MarkdownHeading(int Level, string Text)
{
    private static readonly Regex TrailingHashes = new(@"\s+#+\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static MarkdownHeading? TryParse(string line)
    {
        var trimmed = line.Trim();
        var level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
            level++;

        if (level is < 1 or > 6)
            return null;
        if (level >= trimmed.Length || trimmed[level] != ' ')
            return null;

        var text = TrailingHashes.Replace(trimmed[(level + 1)..], "").Trim();
        if (text.Length == 0)
            return null;

        return new MarkdownHeading(level, text);
    }
}

public static class HeadingSpec
{
    public static (int Level, string Text) Parse(string? setting)
    {
        var trimmed = (setting ?? "").Trim();
        if (trimmed.Length == 0)
            return (2, "DayList");

        var heading = MarkdownHeading.TryParse(trimmed);
        return heading is null ? (2, trimmed) : (heading.Value.Level, heading.Value.Text);
    }

    public static string Format(int level, string text) =>
        new string('#', Math.Clamp(level, 1, 6)) + " " + text;
}
