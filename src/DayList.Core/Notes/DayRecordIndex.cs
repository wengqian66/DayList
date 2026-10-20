namespace DayList.Core;

/// <summary>
/// Days the calendar should mark: a daily note the app already reads as tasks or inspirations.
/// An empty note, or a file with neither, stays unmarked.
/// </summary>
public static class DayRecordIndex
{
    public static IReadOnlySet<DateOnly> WithRecords(AppSettings settings, DateOnly first, DateOnly last)
    {
        var marked = new HashSet<DateOnly>();
        if (!settings.HasVault || last < first)
            return marked;

        for (var day = first; day <= last; day = day.AddDays(1))
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

            if (snapshot.Tasks.Any(task => !string.IsNullOrWhiteSpace(task.Text) || task.Attachments.Count > 0)
                || snapshot.Inspirations.Any(entry => !string.IsNullOrWhiteSpace(entry.Text)))
            {
                marked.Add(day);
            }
        }

        return marked;
    }
}
