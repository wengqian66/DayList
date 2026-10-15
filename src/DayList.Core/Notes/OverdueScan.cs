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

        var items = new List<OverdueTaskItem>();
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
                if (task.IsDone || string.IsNullOrWhiteSpace(task.Text))
                    continue;

                items.Add(new OverdueTaskItem(
                    day,
                    task.Text.Trim(),
                    DayTaskCategory.Normalize(task.Category),
                    task.Indent,
                    task.Bullet,
                    task.DoneMark));
            }
        }

        return items;
    }
}
