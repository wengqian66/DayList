namespace DayList.Core;

public sealed record OverdueTaskItem(
    DateOnly Date,
    string Text,
    string Category,
    string Indent,
    char Bullet,
    char DoneMark);

public static class OverdueScan
{
    public static IReadOnlyList<OverdueTaskItem> CollectOpenBefore(
        AppSettings settings,
        DateOnly today,
        int lookbackDays = 31)
    {
        if (!settings.HasVault)
            return Array.Empty<OverdueTaskItem>();

        var start = today.AddDays(-Math.Max(1, lookbackDays));
        if (start.Year != today.Year || start.Month != today.Month)
            start = new DateOnly(today.Year, today.Month, 1);

        var open = new List<OverdueTaskItem>();
        var finished = new List<FinishedTaskRef>();
        for (var day = start; day < today; day = day.AddDays(1))
        {
            DailyNoteSnapshot snapshot;
            try
            {
                snapshot = DailyNoteStore.Read(settings, day);
            }
            catch
            {
                continue;
            }

            foreach (var task in snapshot.Tasks)
            {
                if (string.IsNullOrWhiteSpace(task.Text))
                    continue;

                var text = task.Text.Trim();
                var category = DayTaskCategory.Normalize(task.Category);
                if (task.IsDone)
                {
                    finished.Add(new FinishedTaskRef(day, text, category));
                    continue;
                }

                open.Add(new OverdueTaskItem(
                    day,
                    text,
                    category,
                    task.Indent,
                    task.Bullet,
                    task.DoneMark));
            }
        }

        // Source notes stay unchecked after a task is copied forward and ticked on a later day.
        return CarryOverOffer.StillUnfinished(open, finished);
    }
}
