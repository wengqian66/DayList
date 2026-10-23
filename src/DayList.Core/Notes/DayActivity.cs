namespace DayList.Core;

/// <summary>How many tasks a daily note held, and how many were checked off there.</summary>
public sealed record DayActivityCount(int Total, int Done);

/// <summary>
/// Per-day task counts for the activity heatmap. The calendar only needs "was there a
/// record"; the heatmap needs numbers, so this walks the range separately.
/// </summary>
public static class DayActivityScan
{
    public static IReadOnlyDictionary<DateOnly, DayActivityCount> Collect(
        AppSettings settings,
        DateOnly first,
        DateOnly last)
    {
        var counts = new Dictionary<DateOnly, DayActivityCount>();
        if (!settings.HasVault || last < first)
            return counts;

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            DailyNoteSnapshot snapshot;
            try
            {
                snapshot = DailyNoteStore.Read(settings, day);
            }
            catch
            {
                // A malformed or locked note must not blank the whole heatmap.
                continue;
            }

            var total = 0;
            var done = 0;
            foreach (var task in snapshot.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Text) && task.Attachments.Count == 0)
                    continue;

                total++;
                if (task.IsDone)
                    done++;
            }

            if (total > 0)
                counts[day] = new DayActivityCount(total, done);
        }

        return counts;
    }
}

/// <summary>Maps a day's finished count onto one of the five heatmap shades.</summary>
public static class HeatLevel
{
    /// <summary>
    /// 0 means nothing finished. The other four shades are quartiles of the busiest day in
    /// the range, so a light week and a heavy week both use the full palette instead of a
    /// fixed cut that would saturate at one task.
    /// </summary>
    public static int Of(int done, int max)
    {
        if (done <= 0)
            return 0;
        if (max <= 1)
            return 4;

        var bucket = max / 4.0;
        var level = (int)Math.Ceiling(done / bucket);
        return Math.Clamp(level, 1, 4);
    }
}

/// <summary>Headline numbers for the activity view.</summary>
public sealed record ActivitySummary(int TotalDone, int ActiveDays, int CurrentStreak, int LongestStreak)
{
    public static ActivitySummary From(
        IReadOnlyDictionary<DateOnly, DayActivityCount> days,
        DateOnly today)
    {
        var active = new HashSet<DateOnly>();
        var totalDone = 0;
        foreach (var pair in days)
        {
            if (pair.Value.Done <= 0)
                continue;

            totalDone += pair.Value.Done;
            active.Add(pair.Key);
        }

        var longest = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in active.OrderBy(day => day))
        {
            run = previous is DateOnly prior && day == prior.AddDays(1) ? run + 1 : 1;
            if (run > longest)
                longest = run;
            previous = day;
        }

        // Today is often still empty in the morning; count the streak from yesterday then.
        var cursor = today;
        if (!active.Contains(cursor))
            cursor = cursor.AddDays(-1);

        var current = 0;
        while (active.Contains(cursor))
        {
            current++;
            cursor = cursor.AddDays(-1);
        }

        return new ActivitySummary(totalDone, active.Count, current, longest);
    }
}
