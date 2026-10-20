using System.Text.RegularExpressions;

namespace DayList.Core;

public static class DayTaskCategory
{
    public const string UncategorizedKey = "";
    public const string UncategorizedLabel = "未分类";
    public const string NewCategorySentinel = "＋ 新建分类…";

    public static bool IsUncategorized(string? category)
    {
        var trimmed = (category ?? "").Trim();
        return trimmed.Length == 0
            || string.Equals(trimmed, UncategorizedLabel, StringComparison.Ordinal);
    }

    public static string Normalize(string? category)
    {
        var trimmed = (category ?? "").Trim();
        return IsUncategorized(trimmed) ? UncategorizedKey : trimmed;
    }

    public static string Display(string? category) =>
        IsUncategorized(category) ? UncategorizedLabel : category!.Trim();
}

public sealed class DayTask
{
    private static readonly Regex LineRegex = new(
        @"^(?<indent>[ \t]*)(?<bullet>[-*+])[ \t]+\[(?<mark>[ xX])\](?<gap>[ \t]?)(?<text>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const int IndentStepSpaces = 2;

    public bool IsDone { get; set; }

    public string Text { get; set; } = "";

    public string Category { get; set; } = DayTaskCategory.UncategorizedKey;

    public string Indent { get; set; } = "";

    public char Bullet { get; set; } = '-';

    public char DoneMark { get; set; } = 'x';

    public string? RawLine { get; set; }

    public List<TaskAttachment> Attachments { get; set; } = new();

    public int OriginIndex { get; set; } = -1;

    public int IndentLevel => MeasureIndent(Indent);

    public static int MeasureIndent(string? indent)
    {
        if (string.IsNullOrEmpty(indent))
            return 0;

        var spaces = 0;
        foreach (var ch in indent)
        {
            if (ch == '\t')
                spaces += IndentStepSpaces;
            else if (ch == ' ')
                spaces++;
        }

        return spaces / IndentStepSpaces;
    }

    public static string IndentForLevel(int level)
    {
        if (level <= 0)
            return "";
        return new string(' ', level * IndentStepSpaces);
    }

    public void IndentIn() => Indent = IndentForLevel(IndentLevel + 1);

    public void IndentOut() => Indent = IndentForLevel(Math.Max(0, IndentLevel - 1));

    public static bool TryParseLine(string line, out DayTask task)
    {
        var match = LineRegex.Match(line);
        if (!match.Success)
        {
            task = null!;
            return false;
        }

        var mark = match.Groups["mark"].Value[0];
        var (text, attachments) = TaskAttachmentSyntax.Split(match.Groups["text"].Value);
        task = new DayTask
        {
            Indent = match.Groups["indent"].Value,
            Bullet = match.Groups["bullet"].Value[0],
            IsDone = mark is 'x' or 'X',
            DoneMark = mark == 'X' ? 'X' : 'x',
            Text = text,
            Attachments = attachments,
            RawLine = line
        };
        return true;
    }

    public DayTask Copy() => new()
    {
        IsDone = IsDone,
        Text = Text,
        Category = Category,
        Indent = Indent,
        Bullet = Bullet,
        DoneMark = DoneMark,
        RawLine = RawLine,
        OriginIndex = OriginIndex,
        Attachments = Attachments.Select(item => item.Copy()).ToList()
    };

    public string ToMarkdown()
    {
        if (RawLine is not null
            && TryParseLine(RawLine, out var parsed)
            && parsed.IsDone == IsDone
            && parsed.Text == Text
            && parsed.Indent == Indent
            && parsed.Bullet == Bullet
            && TaskAttachment.SameSequence(parsed.Attachments, Attachments))
        {
            return RawLine;
        }

        var mark = !IsDone ? " " : DoneMark == 'X' ? "X" : "x";
        return $"{Indent}{Bullet} [{mark}] {TaskAttachmentSyntax.Compose(Text, Attachments)}";
    }

    public static bool SameList(IReadOnlyList<DayTask> left, IReadOnlyList<DayTask> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].IsDone != right[i].IsDone
                || left[i].Bullet != right[i].Bullet
                || !string.Equals(left[i].Text, right[i].Text, StringComparison.Ordinal)
                || !string.Equals(left[i].Indent, right[i].Indent, StringComparison.Ordinal)
                || !string.Equals(
                    DayTaskCategory.Normalize(left[i].Category),
                    DayTaskCategory.Normalize(right[i].Category),
                    StringComparison.Ordinal)
                || !TaskAttachment.SameSequence(left[i].Attachments, right[i].Attachments))
            {
                return false;
            }
        }

        return true;
    }
}
