using System.Text;

namespace DayList.Core;

public sealed class DailyNoteDocument
{
    private readonly string _original;
    private readonly string _newline;
    private readonly bool _endsWithNewline;
    private readonly bool _found;
    private readonly int _headingIndex;
    private readonly int _bodyEnd;
    private readonly List<string> _lines;
    private readonly int _level;
    private readonly string _title;

    private DailyNoteDocument(
        string original,
        string newline,
        bool endsWithNewline,
        bool found,
        int headingIndex,
        int bodyEnd,
        List<string> lines,
        int level,
        string title,
        IReadOnlyList<DayTask> tasks)
    {
        _original = original;
        _newline = newline;
        _endsWithNewline = endsWithNewline;
        _found = found;
        _headingIndex = headingIndex;
        _bodyEnd = bodyEnd;
        _lines = lines;
        _level = level;
        _title = title;
        Tasks = tasks;
    }

    public bool SectionFound => _found;

    public IReadOnlyList<DayTask> Tasks { get; }

    public static DailyNoteDocument Parse(string text, string? headingSetting)
    {
        var (level, title) = HeadingSpec.Parse(headingSetting);
        var (lines, newline, endsWithNewline) = Split(text);
        var headingIndex = FindHeading(lines, level, title);
        if (headingIndex < 0)
        {
            return new DailyNoteDocument(
                text,
                newline,
                endsWithNewline,
                false,
                -1,
                lines.Count,
                lines,
                level,
                title,
                Array.Empty<DayTask>());
        }

        var bodyEnd = FindSectionEnd(lines, headingIndex, level);
        var tasks = new List<DayTask>();
        var inFence = false;
        var category = DayTaskCategory.UncategorizedKey;
        var categoryLevel = Math.Min(level + 1, 6);
        for (var i = headingIndex + 1; i < bodyEnd; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            var heading = MarkdownHeading.TryParse(lines[i]);
            if (heading is not null && heading.Value.Level == categoryLevel)
            {
                category = DayTaskCategory.Normalize(heading.Value.Text);
                continue;
            }

            if (DayTask.TryParseLine(lines[i], out var task))
            {
                task.OriginIndex = tasks.Count;
                task.Category = category;
                tasks.Add(task);
            }
        }

        return new DailyNoteDocument(
            text,
            newline,
            endsWithNewline,
            true,
            headingIndex,
            bodyEnd,
            lines,
            level,
            title,
            tasks);
    }

    /// <summary>
    /// Renames the configured section heading line in-place, keeping level and body.
    /// </summary>
    public static (bool Changed, string Text) TryRenameSectionTitle(string text, string? oldHeadingSetting, string newTitle)
    {
        var (level, oldTitle) = HeadingSpec.Parse(oldHeadingSetting);
        var trimmed = (newTitle ?? "").Trim();
        if (trimmed.Length == 0)
            trimmed = "DayList";
        if (string.Equals(oldTitle, trimmed, StringComparison.Ordinal))
            return (false, text);

        var (lines, newline, endsWithNewline) = Split(text);
        var headingIndex = FindHeading(lines, level, oldTitle);
        if (headingIndex < 0)
            return (false, text);

        lines[headingIndex] = HeadingSpec.Format(level, trimmed);
        return (true, Join(lines, newline, endsWithNewline));
    }

    public string SectionBody()
    {
        if (!_found)
            return "";

        var count = _bodyEnd - (_headingIndex + 1);
        if (count <= 0)
            return "";

        return string.Join("\n", _lines.GetRange(_headingIndex + 1, count)).Trim('\r', '\n');
    }

    public string Render(IReadOnlyList<DayTask> tasks)
    {
        if (!_found)
        {
            if (tasks.Count == 0)
                return _original;

            var block = BuildNewSection(tasks);
            if (_original.Length == 0)
                return block;

            var gap = _original.EndsWith('\n') ? _newline : _newline + _newline;
            return _original + gap + block;
        }

        var body = _lines.GetRange(_headingIndex + 1, _bodyEnd - (_headingIndex + 1));
        var rebuilt = new List<string>(_lines.Count + tasks.Count + 8);
        rebuilt.AddRange(_lines.Take(_headingIndex + 1));
        rebuilt.AddRange(RenderBody(body, tasks));
        if (_bodyEnd < _lines.Count
            && rebuilt.Count > 0
            && rebuilt[^1].Length != 0
            && MarkdownHeading.TryParse(_lines[_bodyEnd]) is not null)
        {
            rebuilt.Add("");
        }
        rebuilt.AddRange(_lines.Skip(_bodyEnd));
        return Join(rebuilt, _newline, _endsWithNewline);
    }

    private string BuildNewSection(IReadOnlyList<DayTask> tasks)
    {
        var builder = new StringBuilder();
        builder.Append(HeadingSpec.Format(_level, _title));
        builder.Append(_newline);
        foreach (var line in RenderBody(new List<string>(), tasks))
        {
            builder.Append(line);
            builder.Append(_newline);
        }

        return builder.ToString();
    }

    private List<string> RenderBody(List<string> body, IReadOnlyList<DayTask> tasks)
    {
        var categoryLevel = Math.Min(_level + 1, 6);
        var intro = ExtractIntro(body, categoryLevel);
        var outro = ExtractOutro(body, categoryLevel);
        var output = new List<string>();

        if (intro.Count > 0)
        {
            output.AddRange(intro);
            if (output.Count == 0 || output[^1].Length != 0)
                output.Add("");
        }
        else if (tasks.Count > 0)
        {
            output.Add("");
        }

        foreach (var (category, group) in GroupTasks(tasks))
        {
            if (output.Count > 0 && output[^1].Length != 0)
                output.Add("");

            if (!DayTaskCategory.IsUncategorized(category))
                output.Add(HeadingSpec.Format(categoryLevel, category));

            foreach (var task in group)
                output.Add(task.ToMarkdown());
        }

        if (outro.Count > 0)
        {
            if (output.Count > 0 && output[^1].Length != 0)
                output.Add("");
            output.AddRange(outro);
        }

        return output;
    }

    private static List<(string Category, List<DayTask> Tasks)> GroupTasks(IReadOnlyList<DayTask> tasks)
    {
        var groups = new List<(string Category, List<DayTask> Tasks)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            var category = DayTaskCategory.Normalize(task.Category);
            if (!index.TryGetValue(category, out var at))
            {
                at = groups.Count;
                index[category] = at;
                groups.Add((category, new List<DayTask>()));
            }

            groups[at].Tasks.Add(task);
        }

        if (index.TryGetValue(DayTaskCategory.UncategorizedKey, out var uncategorizedAt) && uncategorizedAt != 0)
        {
            var item = groups[uncategorizedAt];
            groups.RemoveAt(uncategorizedAt);
            groups.Insert(0, item);
        }

        return groups;
    }


    private static List<string> ExtractOutro(List<string> body, int categoryLevel)
    {
        var lastContent = -1;
        var inFence = false;
        for (var i = 0; i < body.Count; i++)
        {
            var line = body[i];
            if (IsFence(line))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            if (DayTask.TryParseLine(line, out _))
            {
                lastContent = i;
                continue;
            }

            var heading = MarkdownHeading.TryParse(line);
            if (heading is not null && heading.Value.Level == categoryLevel)
                lastContent = i;
        }

        if (lastContent < 0 || lastContent >= body.Count - 1)
            return new List<string>();

        var outro = body.GetRange(lastContent + 1, body.Count - (lastContent + 1));
        while (outro.Count > 0 && outro[0].Length == 0)
            outro.RemoveAt(0);
        while (outro.Count > 0 && outro[^1].Length == 0)
            outro.RemoveAt(outro.Count - 1);
        return outro;
    }

    private static List<string> ExtractIntro(List<string> body, int categoryLevel)
    {
        var intro = new List<string>();
        var inFence = false;
        for (var i = 0; i < body.Count; i++)
        {
            var line = body[i];
            if (IsFence(line))
            {
                inFence = !inFence;
                intro.Add(line);
                continue;
            }

            if (inFence)
            {
                intro.Add(line);
                continue;
            }

            if (DayTask.TryParseLine(line, out _))
                break;

            var heading = MarkdownHeading.TryParse(line);
            if (heading is not null && heading.Value.Level == categoryLevel)
                break;

            intro.Add(line);
        }

        while (intro.Count > 0 && intro[^1].Length == 0)
            intro.RemoveAt(intro.Count - 1);

        return intro;
    }

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
