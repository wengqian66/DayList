namespace DayList.Core;

public sealed record InspirationFlowSource(DateOnly Date, int SourceIndex, string? TimeLabel, string? Text);

public sealed record InspirationFlowItem(
    DateOnly Date,
    int SourceIndex,
    string Clock,
    string Text,
    string Glance,
    bool HasClock);

public sealed record InspirationFlowDay(
    DateOnly Date,
    string Heading,
    string Caption,
    IReadOnlyList<InspirationFlowItem> Items);

/// <summary>
/// Reads the existing inspiration heading from recent daily notes and orders those bullets by clock.
/// It does not write a second store and does not touch the DayList task section.
/// </summary>
public static class InspirationFlow
{
    public const int RecentDays = 7;

    public static IReadOnlyList<InspirationFlowDay> Read(
        AppSettings settings,
        DateOnly anchor,
        DateOnly today,
        int recentDays = RecentDays)
    {
        var span = Math.Clamp(recentDays, 1, 31);
        var start = AddDaysOrMin(anchor, 1 - span);
        var sources = new List<InspirationFlowSource>();
        for (var day = start; day <= anchor; day = day.AddDays(1))
        {
            IReadOnlyList<InspirationEntry> entries;
            try
            {
                entries = DailyNoteStore.Read(settings, day).Inspirations;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                sources.Add(new InspirationFlowSource(day, i, entry.TimeLabel, entry.Text));
            }
        }

        return Arrange(anchor, today, sources, span);
    }

    public static IReadOnlyList<InspirationFlowDay> Arrange(
        DateOnly anchor,
        DateOnly today,
        IReadOnlyList<InspirationFlowSource> sources,
        int recentDays = RecentDays)
    {
        var span = Math.Clamp(recentDays, 1, 31);
        var start = AddDaysOrMin(anchor, 1 - span);
        var prepared = new List<(InspirationFlowSource Source, string Text, int? Minutes)>();
        foreach (var source in sources)
        {
            if (source.Date < start || source.Date > anchor)
                continue;

            var text = Normalize(source.Text);
            if (text.Length == 0)
                continue;

            prepared.Add((source, text, TryMinutes(source.TimeLabel)));
        }

        var days = new List<InspirationFlowDay>();
        foreach (var group in prepared.GroupBy(item => item.Source.Date).OrderByDescending(group => group.Key))
        {
            var items = group
                .OrderBy(item => item.Minutes ?? int.MaxValue)
                .ThenBy(item => item.Source.SourceIndex)
                .Select(item => new InspirationFlowItem(
                    item.Source.Date,
                    item.Source.SourceIndex,
                    Clock(item.Minutes),
                    item.Text,
                    item.Text.Replace('\n', ' '),
                    item.Minutes is not null))
                .ToArray();
            if (items.Length == 0)
                continue;

            var date = group.Key;
            days.Add(new InspirationFlowDay(date, Heading(date, today), Caption(date, today), items));
        }

        return days;
    }

    public static string Heading(DateOnly date, DateOnly today)
    {
        if (date == today)
            return "今天";
        if (TryAddDays(today, -1, out var yesterday) && date == yesterday)
            return "昨天";
        return $"{date.Month}月{date.Day}日";
    }

    public static string Caption(DateOnly date, DateOnly today)
    {
        var weekday = Weekday(date);
        if (date == today || (TryAddDays(today, -1, out var yesterday) && date == yesterday))
            return $"{date.Month}月{date.Day}日 {weekday}";
        return weekday;
    }

    private static string Clock(int? minutes) =>
        minutes is int value ? $"{value / 60:00}:{value % 60:00}" : "·";

    private static int? TryMinutes(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        var text = label.Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon >= text.Length - 1)
            return null;

        var hourText = text[..colon];
        var minuteText = text[(colon + 1)..];
        if (hourText.Length is < 1 or > 2 || minuteText.Length != 2)
            return null;
        if (!int.TryParse(hourText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var hour))
            return null;
        if (!int.TryParse(minuteText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var minute))
            return null;
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return null;

        return hour * 60 + minute;
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                lines.Add(trimmed);
        }

        return string.Join("\n", lines);
    }

    private static DateOnly AddDaysOrMin(DateOnly date, int days) =>
        TryAddDays(date, days, out var result) ? result : DateOnly.MinValue;

    private static bool TryAddDays(DateOnly date, int days, out DateOnly result)
    {
        try
        {
            result = date.AddDays(days);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = date;
            return false;
        }
    }

    private static string Weekday(DateOnly date) => date.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}
