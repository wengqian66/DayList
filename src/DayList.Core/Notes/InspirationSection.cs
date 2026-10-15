using System.Text;
using System.Text.RegularExpressions;

namespace DayList.Core;

/// <summary>
/// Plain-text capture under a sibling heading (default <c>## 灵感</c>).
/// Lines are bullets, never checkbox tasks, and are not part of the DayList section.
/// </summary>
public sealed class InspirationEntry
{
    private static readonly Regex ClockPrefix = new(
        @"^(?<h>\d{1,2}):(?<m>\d{2})[ \t]+(?<text>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string? TimeLabel { get; set; }

    public string Text { get; set; } = "";

    public string? RawLine { get; set; }

    public string ToMarkdown()
    {
        var lines = SplitText(Text);
        if (lines.Count == 0)
            return "";

        if (lines.Count == 1
            && RawLine is not null
            && TryParseBullet(RawLine, out var rawTime, out var rawText)
            && string.Equals(rawTime, TimeLabel, StringComparison.Ordinal)
            && string.Equals(rawText, lines[0], StringComparison.Ordinal))
        {
            return RawLine;
        }

        var first = string.IsNullOrEmpty(TimeLabel) ? "- " + lines[0] : "- " + TimeLabel + " " + lines[0];
        if (lines.Count == 1)
            return first;

        var builder = new StringBuilder(first);
        for (var i = 1; i < lines.Count; i++)
        {
            builder.Append('\n');
            builder.Append("  ");
            builder.Append(lines[i]);
        }

        return builder.ToString();
    }

    /// <summary>One line for the Today glance. Extra lines stay in the note and in the editor.</summary>
    public string GlanceText() => string.Join(" ", SplitText(Text));

    public static bool SameList(IReadOnlyList<InspirationEntry> left, IReadOnlyList<InspirationEntry> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].TimeLabel, right[i].TimeLabel, StringComparison.Ordinal)
                || !string.Equals(NormalizeText(left[i].Text), NormalizeText(right[i].Text), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> SplitText(string? text)
    {
        var lines = new List<string>();
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                lines.Add(trimmed);
        }

        return lines;
    }

    private static string NormalizeText(string? text) => string.Join("\n", SplitText(text));

    internal static bool TryParseBullet(string line, out string? time, out string body)
    {
        time = null;
        body = "";
        if (line.Length == 0 || line[0] is ' ' or '\t')
            return false;
        if (line[0] is not ('-' or '*' or '+'))
            return false;
        if (line.Length < 2 || line[1] is not (' ' or '\t'))
            return false;

        body = line[1..].Trim();
        if (body.Length == 0)
            return false;
        if (IsCheckbox(body))
            return false;

        var clock = ClockPrefix.Match(body);
        if (!clock.Success)
            return true;

        var hour = int.Parse(clock.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minute = int.Parse(clock.Groups["m"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return true;

        var text = clock.Groups["text"].Value.Trim();
        if (text.Length == 0)
            return true;

        time = clock.Groups["h"].Value + ":" + clock.Groups["m"].Value;
        body = text;
        return true;
    }

    private static bool IsCheckbox(string body) =>
        body.Length >= 3
        && body[0] == '['
        && body[2] == ']'
        && body[1] is ' ' or 'x' or 'X';
}

public static class InspirationSection
{
    public const string DefaultHeading = "## 灵感";

    public static bool SameHeading(string? left, string? right)
    {
        var a = HeadingSpec.Parse(string.IsNullOrWhiteSpace(left) ? "## DayList" : left);
        var b = HeadingSpec.Parse(string.IsNullOrWhiteSpace(right) ? DefaultHeading : right);
        return a.Level == b.Level && string.Equals(a.Text, b.Text, StringComparison.Ordinal);
    }

    public static IReadOnlyList<InspirationEntry> Read(string text, string? headingSetting)
    {
        var (level, title) = ParseHeading(headingSetting);
        var (lines, _, _) = Split(text);
        var headingIndex = FindHeading(lines, level, title);
        if (headingIndex < 0)
            return Array.Empty<InspirationEntry>();

        var bodyEnd = FindSectionEnd(lines, headingIndex, level);
        var entries = new List<InspirationEntry>();
        var inFence = false;
        for (var i = headingIndex + 1; i < bodyEnd; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            if (!InspirationEntry.TryParseBullet(lines[i], out var time, out var body))
                continue;

            var entry = new InspirationEntry
            {
                TimeLabel = time,
                Text = body,
                RawLine = lines[i]
            };
            while (i + 1 < bodyEnd && IsContinuation(lines[i + 1]))
            {
                i++;
                var extra = lines[i].Trim();
                if (extra.Length > 0)
                    entry.Text = entry.Text + "\n" + extra;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// Removes the inspiration heading and everything until the next same-or-higher heading.
    /// Used so monthly digests never include capture notes, even when the heading sits inside a wider section.
    /// </summary>
    public static string Strip(string text, string? headingSetting)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? "";

        var (level, title) = ParseHeading(headingSetting);
        var (lines, newline, endsWithNewline) = Split(text);
        var headingIndex = FindHeading(lines, level, title);
        if (headingIndex < 0)
            return text;

        var bodyEnd = FindSectionEnd(lines, headingIndex, level);
        var start = headingIndex;
        if (start > 0 && lines[start - 1].Length == 0)
            start--;

        var output = new List<string>();
        output.AddRange(lines.Take(start));
        if (output.Count > 0 && bodyEnd < lines.Count && output[^1].Length != 0 && lines[bodyEnd].Length != 0)
            output.Add("");
        output.AddRange(lines.Skip(bodyEnd));
        return Join(output, newline, endsWithNewline);
    }

    public static string Render(string text, string? headingSetting, IReadOnlyList<InspirationEntry> entries)
    {
        var (level, title) = ParseHeading(headingSetting);
        var (lines, newline, endsWithNewline) = Split(text ?? "");
        var entryLines = new List<string>();
        foreach (var entry in entries)
        {
            var markdown = entry.ToMarkdown();
            if (markdown.Length == 0)
                continue;
            entryLines.AddRange(markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        }

        var headingIndex = FindHeading(lines, level, title);
        if (headingIndex < 0)
        {
            if (entryLines.Count == 0)
                return text ?? "";

            var block = new StringBuilder();
            block.Append(HeadingSpec.Format(level, title));
            block.Append(newline);
            block.Append(newline);
            foreach (var line in entryLines)
            {
                block.Append(line);
                block.Append(newline);
            }

            if (string.IsNullOrEmpty(text))
                return block.ToString();

            var gap = text!.EndsWith('\n') ? newline : newline + newline;
            return text + gap + block;
        }

        var bodyEnd = FindSectionEnd(lines, headingIndex, level);
        var body = lines.GetRange(headingIndex + 1, bodyEnd - (headingIndex + 1));
        var (intro, outro) = SplitKept(body);
        if (entryLines.Count == 0 && intro.Count == 0 && outro.Count == 0)
        {
            var start = headingIndex;
            if (start > 0 && lines[start - 1].Length == 0)
                start--;

            var removed = new List<string>();
            removed.AddRange(lines.Take(start));
            if (removed.Count > 0 && bodyEnd < lines.Count && removed[^1].Length != 0 && lines[bodyEnd].Length != 0)
                removed.Add("");
            removed.AddRange(lines.Skip(bodyEnd));
            return Join(removed, newline, endsWithNewline);
        }

        var rebuilt = new List<string>();
        rebuilt.AddRange(lines.Take(headingIndex + 1));
        rebuilt.AddRange(ComposeBody(intro, entryLines, outro));
        if (bodyEnd < lines.Count
            && rebuilt.Count > 0
            && rebuilt[^1].Length != 0
            && MarkdownHeading.TryParse(lines[bodyEnd]) is not null)
        {
            rebuilt.Add("");
        }

        rebuilt.AddRange(lines.Skip(bodyEnd));
        return Join(rebuilt, newline, endsWithNewline);
    }

    private static (int Level, string Title) ParseHeading(string? headingSetting)
    {
        var source = string.IsNullOrWhiteSpace(headingSetting) ? DefaultHeading : headingSetting;
        return HeadingSpec.Parse(source);
    }

    private static List<string> ComposeBody(List<string> intro, List<string> entryLines, List<string> outro)
    {
        var output = new List<string>();
        if (intro.Count > 0)
        {
            output.AddRange(intro);
            if (output[^1].Length != 0)
                output.Add("");
        }
        else if (entryLines.Count > 0)
        {
            output.Add("");
        }

        output.AddRange(entryLines);

        if (outro.Count > 0)
        {
            if (output.Count > 0 && output[^1].Length != 0)
                output.Add("");
            output.AddRange(outro);
        }

        return output;
    }

    private static (List<string> Intro, List<string> Outro) SplitKept(List<string> body)
    {
        var (first, last) = FindEntrySpan(body);
        if (first < 0)
            return (TrimTrailing(body), new List<string>());

        var intro = first > 0 ? body.GetRange(0, first) : new List<string>();
        var outro = last + 1 < body.Count ? body.GetRange(last + 1, body.Count - (last + 1)) : new List<string>();
        // Keep a leading blank so "## 灵感" stays separated from the prose above the bullets.
        return (TrimTrailing(intro), TrimEdges(outro));
    }

    private static (int First, int Last) FindEntrySpan(List<string> body)
    {
        var first = -1;
        var last = -1;
        var inFence = false;
        for (var i = 0; i < body.Count; i++)
        {
            if (IsFence(body[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence || !InspirationEntry.TryParseBullet(body[i], out _, out _))
                continue;

            if (first < 0)
                first = i;
            last = i;
            while (last + 1 < body.Count && IsContinuation(body[last + 1]))
                last++;
            i = last;
        }

        return (first, last);
    }

    private static List<string> TrimTrailing(List<string> lines)
    {
        var copy = new List<string>(lines);
        while (copy.Count > 0 && copy[^1].Length == 0)
            copy.RemoveAt(copy.Count - 1);
        return copy;
    }

    private static List<string> TrimEdges(List<string> lines)
    {
        var copy = TrimTrailing(lines);
        while (copy.Count > 0 && copy[0].Length == 0)
            copy.RemoveAt(0);
        return copy;
    }

    private static bool IsContinuation(string line) =>
        line.Length > 0 && (line[0] == '\t' || line.StartsWith("  ", StringComparison.Ordinal));

    private static int FindHeading(List<string> lines, int level, string title)
    {
        var inFence = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            var heading = MarkdownHeading.TryParse(lines[i]);
            if (heading is not null
                && heading.Value.Level == level
                && string.Equals(heading.Value.Text, title, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindSectionEnd(List<string> lines, int headingIndex, int level)
    {
        var inFence = false;
        for (var i = headingIndex + 1; i < lines.Count; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            var heading = MarkdownHeading.TryParse(lines[i]);
            if (heading is not null && heading.Value.Level <= level)
                return i;
        }

        return lines.Count;
    }

    private static bool IsFence(string line) =>
        line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static (List<string> Lines, string Newline, bool EndsWithNewline) Split(string text)
    {
        if (text.Length == 0)
            return (new List<string>(), "\n", false);

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var next = text.IndexOf('\n', index);
            if (next < 0)
            {
                lines.Add(text[index..]);
                return (lines, newline, false);
            }

            var end = next;
            if (end > index && text[end - 1] == '\r')
                end--;

            lines.Add(text[index..end]);
            index = next + 1;
            if (index == text.Length)
                return (lines, newline, true);
        }

        return (lines, newline, true);
    }

    private static string Join(List<string> lines, string newline, bool endsWithNewline)
    {
        var text = string.Join(newline, lines);
        if (endsWithNewline)
            text += newline;
        return text;
    }
}
