namespace DayList.Core;

public sealed record OverdueTaskItem(
    DateOnly Date,
    string Text,
    string Category,
    string Indent,
    char Bullet,
    char DoneMark)
{
    /// <summary>
    /// Files that were split out of the task line. Empty when the note had none.
    /// </summary>
    public IReadOnlyList<TaskAttachment> Attachments { get; init; } = Array.Empty<TaskAttachment>();
}

/// <summary>
/// How an overdue row is matched against today and against a later check-off.
/// Text stays the identity when the line has any. A line that is only attachments
/// uses file names, so copying into today's folder still counts as the same task.
/// </summary>
public static class OverdueIdentity
{
    public static string AttachmentKey(IEnumerable<TaskAttachment>? attachments)
    {
        if (attachments is null)
            return "";

        var names = new List<string>();
        foreach (var attachment in attachments)
        {
            if (attachment is null)
                continue;
            var name = Path.GetFileName((attachment.RelativePath ?? "").Replace('\\', '/').Trim('/'));
            if (name.Length == 0)
                continue;
            names.Add(name.ToLowerInvariant());
        }

        return names.Count == 0 ? "" : "\u001e" + string.Join('\u001f', names);
    }

    public static string IdentityText(string? text, IEnumerable<TaskAttachment>? attachments) =>
        IdentityText(text, AttachmentKey(attachments));

    public static string IdentityText(string? text, string? attachmentKey)
    {
        var trimmed = (text ?? "").Trim();
        if (trimmed.Length > 0)
            return trimmed;
        return attachmentKey ?? "";
    }

    public static string Label(OverdueTaskItem item)
    {
        var text = (item.Text ?? "").Trim();
        if (text.Length > 0)
            return text;

        var names = new List<string>();
        foreach (var attachment in item.Attachments)
        {
            var name = (attachment.DisplayName ?? "").Trim();
            if (name.Length == 0)
                name = Path.GetFileName((attachment.RelativePath ?? "").Replace('\\', '/'));
            if (name.Length > 0)
                names.Add(name);
        }

        return string.Join(" ", names);
    }
}

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
                var text = task.Text.Trim();
                var attachments = task.Attachments.Select(item => item.Copy()).ToArray();
                if (OverdueIdentity.IdentityText(text, attachments).Length == 0)
                    continue;

                var category = DayTaskCategory.Normalize(task.Category);
                if (task.IsDone)
                {
                    finished.Add(new FinishedTaskRef(
                        day,
                        text,
                        category,
                        OverdueIdentity.AttachmentKey(attachments)));
                    continue;
                }

                open.Add(new OverdueTaskItem(
                    day,
                    text,
                    category,
                    task.Indent,
                    task.Bullet,
                    task.DoneMark)
                {
                    Attachments = attachments
                });
            }
        }

        // Source notes stay unchecked after a task is copied forward and ticked on a later day.
        return CarryOverOffer.StillUnfinished(open, finished);
    }
}
