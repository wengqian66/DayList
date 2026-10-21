using System.Globalization;
using System.Text.RegularExpressions;

namespace DayList.Core;

/// <summary>
/// One quick-add line after optional date, clock, and #category tokens are lifted out.
/// <see cref="Recognized"/> is false when the line should be stored as a plain title.
/// <see cref="Category"/> is null when the line did not name one; empty means 未分类.
/// </summary>
public sealed record QuickAddParse(string Title, DateOnly? Date, TimeOnly? Reminder, string? Category, bool Recognized)
{
    public static QuickAddParse Plain(string title) => new(title, null, null, null, false);
}

/// <summary>
/// Parses phrases such as 「明天 9 点 写周报 #工作」.
/// Relative dates are resolved from <paramref name="today"/> supplied by the caller.
/// Anything that does not leave a title falls back to the original line.
/// </summary>
public static class QuickAddPhrase
{
    private enum DayPart { None, Morning, Noon, Evening }

    private static readonly (string Token, int Offset, DayPart Part)[] Relative =
    {
        ("大后天", 3, DayPart.None),
        ("后天", 2, DayPart.None),
        ("明天", 1, DayPart.None),
        ("明日", 1, DayPart.None),
        ("今天", 0, DayPart.None),
        ("今日", 0, DayPart.None),
        ("昨天", -1, DayPart.None),
        ("昨日", -1, DayPart.None),
        ("明晚", 1, DayPart.Evening),
        ("明早", 1, DayPart.Morning),
        ("明晨", 1, DayPart.Morning),
        ("今晚", 0, DayPart.Evening)
    };

    private static readonly Regex NextWeek = new(
        @"下(?:周|星期|礼拜)([一二三四五六日天])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThisWeek = new(
        @"(?:本|这)(?:周|星期|礼拜)([一二三四五六日天])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BareWeek = new(
        @"(?<![下本这])(?:周|星期|礼拜)([一二三四五六日天])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IsoDate = new(
        @"(?<!\d)(\d{4})\s*[-/.年]\s*(\d{1,2})\s*[-/.月]\s*(\d{1,2})\s*日?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MonthDay = new(
        @"(?<!\d)(\d{1,2})\s*月\s*(\d{1,2})\s*日",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Clock = new(
        @"(?<![0-9第])(?<lead>凌晨|早上|上午|中午|下午|晚上|傍晚)?\s*(?<h>\d{1,2})\s*(?<sep>[:：点时])\s*(?<m>\d{1,2})?\s*(?<half>半)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new(
        @"(?<![A-Za-z0-9])[#＃](?<name>[^\s#＃]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AtToken = new(
        @"@(?<body>[^\s@#＃]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static QuickAddParse Parse(string? raw, DateOnly today, IEnumerable<string>? knownCategories = null)
    {
        var original = Collapse(raw);
        if (original.Length == 0)
            return QuickAddParse.Plain("");

        var work = original;
        var recognized = false;
        DateOnly? date = null;
        TimeOnly? reminder = null;

        // Explicit "@date/time" marker, e.g. "@明天9点" / "@9点" / "@20:00".
        // A lone "@" not followed by a recognizable date/time is left alone (e.g. @mentions).
        var at = AtToken.Match(work);
        if (at.Success)
        {
            var probe = at.Groups["body"].Value;
            var probePart = DayPart.None;
            var gotDate = false;
            DateOnly? atDate = null;
            if (TryFindDate(probe, today, out var dateHit))
            {
                atDate = dateHit.Date;
                probePart = dateHit.Part;
                probe = Cut(probe, dateHit.Index, dateHit.Length);
                gotDate = true;
            }

            var gotClock = TryFindClock(probe, probePart, out var atClock, out _, out _);
            if (gotDate || gotClock)
            {
                date = atDate;
                reminder = gotClock ? atClock : null;
                work = Cut(work, at.Index, at.Length);
                recognized = true;
            }
        }

        string? category = null;
        var tag = Tag.Match(work);
        if (tag.Success)
        {
            var name = tag.Groups["name"].Value.Trim();
            if (name.Length > 40)
                name = name[..40];
            category = ResolveCategory(name, knownCategories);
            work = Cut(work, tag.Index, tag.Length);
            recognized = true;
        }

        var dayPart = DayPart.None;
        if (TryFindDate(work, today, out var hit))
        {
            date = hit.Date;
            dayPart = hit.Part;
            work = Cut(work, hit.Index, hit.Length);
            recognized = true;
        }

        if (TryFindClock(work, dayPart, out var time, out var clockIndex, out var clockLength))
        {
            reminder = time;
            work = Cut(work, clockIndex, clockLength);
            recognized = true;
        }

        var title = Collapse(work);
        if (!recognized || title.Length == 0)
            return QuickAddParse.Plain(original);

        return new QuickAddParse(title, date, reminder, category, true);
    }

    private static string? ResolveCategory(string name, IEnumerable<string>? knownCategories)
    {
        if (DayTaskCategory.IsUncategorized(name))
            return DayTaskCategory.UncategorizedKey;

        if (knownCategories is not null)
        {
            foreach (var known in knownCategories)
            {
                var normalized = DayTaskCategory.Normalize(known);
                if (normalized.Length == 0)
                    continue;
                if (string.Equals(normalized, name, StringComparison.Ordinal))
                    return normalized;
            }
        }

        return DayTaskCategory.Normalize(name);
    }

    private readonly record struct DateHit(int Index, int Length, DateOnly Date, DayPart Part);

    private static bool TryFindDate(string text, DateOnly today, out DateHit best)
    {
        DateHit? chosen = null;
        void Consider(int index, int length, DateOnly date, DayPart part)
        {
            if (index < 0 || length <= 0)
                return;
            if (chosen is DateHit current
                && (index > current.Index || (index == current.Index && length <= current.Length)))
            {
                return;
            }

            chosen = new DateHit(index, length, date, part);
        }

        foreach (var token in Relative)
        {
            var at = text.IndexOf(token.Token, StringComparison.Ordinal);
            if (at >= 0)
                Consider(at, token.Token.Length, today.AddDays(token.Offset), token.Part);
        }

        ConsiderWeek(NextWeek, text, today, nextWeek: true, Consider);
        ConsiderWeek(ThisWeek, text, today, nextWeek: false, Consider);
        ConsiderBareWeek(text, today, Consider);
        ConsiderIso(text, Consider);
        ConsiderMonthDay(text, today, Consider);

        if (chosen is DateHit hit)
        {
            best = hit;
            return true;
        }

        best = default;
        return false;
    }

    private static void ConsiderWeek(
        Regex pattern,
        string text,
        DateOnly today,
        bool nextWeek,
        Action<int, int, DateOnly, DayPart> consider)
    {
        foreach (Match match in pattern.Matches(text))
        {
            if (!TryWeekday(match.Groups[1].Value[0], out var weekday))
                continue;
            var date = nextWeek ? WeekdayInNextWeek(today, weekday) : WeekdayInThisWeek(today, weekday);
            consider(match.Index, match.Length, date, DayPart.None);
        }
    }

    private static void ConsiderBareWeek(string text, DateOnly today, Action<int, int, DateOnly, DayPart> consider)
    {
        foreach (Match match in BareWeek.Matches(text))
        {
            if (!TryWeekday(match.Groups[1].Value[0], out var weekday))
                continue;
            consider(match.Index, match.Length, NextOrSameWeekday(today, weekday), DayPart.None);
        }
    }

    private static void ConsiderIso(string text, Action<int, int, DateOnly, DayPart> consider)
    {
        foreach (Match match in IsoDate.Matches(text))
        {
            if (!TryDate(
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    out var date))
            {
                continue;
            }

            consider(match.Index, match.Length, date, DayPart.None);
        }
    }

    private static void ConsiderMonthDay(string text, DateOnly today, Action<int, int, DateOnly, DayPart> consider)
    {
        foreach (Match match in MonthDay.Matches(text))
        {
            var month = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var day = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (!TryDate(today.Year, month, day, out var date))
                continue;
            if (date < today && !TryDate(today.Year + 1, month, day, out date))
                continue;
            consider(match.Index, match.Length, date, DayPart.None);
        }
    }

    private static bool TryFindClock(
        string text,
        DayPart inherited,
        out TimeOnly time,
        out int index,
        out int length)
    {
        time = default;
        index = 0;
        length = 0;
        for (var match = Clock.Match(text); match.Success; match = match.NextMatch())
        {
            var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minute = 0;
            if (match.Groups["m"].Success)
                minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (match.Groups["half"].Success)
                minute = 30;

            var part = inherited;
            if (match.Groups["lead"].Success)
                part = PartOf(match.Groups["lead"].Value);

            hour = ApplyPart(hour, part);
            if (hour is < 0 or > 23 || minute is < 0 or > 59)
                continue;

            var end = match.Index + match.Length;
            if (match.Groups["m"].Success && end < text.Length && text[end] == '分')
                end++;

            time = new TimeOnly(hour, minute);
            index = match.Index;
            length = end - match.Index;
            return true;
        }

        return false;
    }

    private static DayPart PartOf(string lead) => lead switch
    {
        "凌晨" or "早上" or "上午" => DayPart.Morning,
        "中午" => DayPart.Noon,
        "下午" or "晚上" or "傍晚" => DayPart.Evening,
        _ => DayPart.None
    };

    private static int ApplyPart(int hour, DayPart part)
    {
        if (hour is < 0 or > 23)
            return -1;
        if (part == DayPart.None || hour > 12)
            return hour;

        return part switch
        {
            DayPart.Morning => hour == 12 ? 0 : hour,
            DayPart.Noon => hour is >= 1 and <= 10 ? hour + 12 : hour,
            DayPart.Evening => hour < 12 ? hour + 12 : hour,
            _ => hour
        };
    }

    private static bool TryWeekday(char ch, out DayOfWeek day)
    {
        switch (ch)
        {
            case '一': day = DayOfWeek.Monday; return true;
            case '二': day = DayOfWeek.Tuesday; return true;
            case '三': day = DayOfWeek.Wednesday; return true;
            case '四': day = DayOfWeek.Thursday; return true;
            case '五': day = DayOfWeek.Friday; return true;
            case '六': day = DayOfWeek.Saturday; return true;
            case '日':
            case '天': day = DayOfWeek.Sunday; return true;
            default: day = default; return false;
        }
    }

    private static DateOnly NextOrSameWeekday(DateOnly today, DayOfWeek target)
    {
        var delta = ((int)target - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(delta);
    }

    private static DateOnly WeekdayInNextWeek(DateOnly today, DayOfWeek target)
    {
        var deltaToMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (deltaToMonday == 0)
            deltaToMonday = 7;
        var nextMonday = today.AddDays(deltaToMonday);
        var offset = ((int)target - (int)DayOfWeek.Monday + 7) % 7;
        return nextMonday.AddDays(offset);
    }

    private static DateOnly WeekdayInThisWeek(DateOnly today, DayOfWeek target)
    {
        var mondayOffset = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var monday = today.AddDays(-mondayOffset);
        var offset = ((int)target - (int)DayOfWeek.Monday + 7) % 7;
        return monday.AddDays(offset);
    }

    private static bool TryDate(int year, int month, int day, out DateOnly date)
    {
        try
        {
            date = new DateOnly(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            date = default;
            return false;
        }
    }

    private static string Cut(string text, int index, int length) =>
        Collapse(text.Remove(index, length));

    private static string Collapse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var normalized = raw.Replace('\u3000', ' ').Replace('\t', ' ');
        return string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
