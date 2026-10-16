namespace DayList.Core;

public readonly record struct TaskDoneSnapshot(string Category, bool IsDone);

/// <summary>
/// A check just finished a category, the whole day, or neither.
/// When the last open task also finishes its category, only the day is reported
/// so the UI plays one celebration.
/// </summary>
public readonly record struct CompletionMoment(string? CategoryJustCompleted, bool DayJustCompleted)
{
    public bool Any => DayJustCompleted || CategoryJustCompleted is not null;
}

public static class CompletionFeedback
{
    public static CompletionMoment Detect(
        IReadOnlyList<TaskDoneSnapshot> before,
        IReadOnlyList<TaskDoneSnapshot> after)
    {
        var day = AllDone(after) && !AllDone(before);
        if (day)
            return new CompletionMoment(null, true);

        var beforeGroups = Group(before);
        foreach (var group in Group(after))
        {
            if (!AllDone(group.Value))
                continue;
            if (!beforeGroups.TryGetValue(group.Key, out var previous) || AllDone(previous))
                continue;
            return new CompletionMoment(group.Key, false);
        }

        return default;
    }

    private static bool AllDone(IReadOnlyList<TaskDoneSnapshot> tasks) =>
        tasks.Count > 0 && tasks.All(task => task.IsDone);

    private static Dictionary<string, List<TaskDoneSnapshot>> Group(IReadOnlyList<TaskDoneSnapshot> tasks)
    {
        var groups = new Dictionary<string, List<TaskDoneSnapshot>>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            var key = DayTaskCategory.Normalize(task.Category);
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<TaskDoneSnapshot>();
                groups[key] = list;
            }

            list.Add(task);
        }

        return groups;
    }
}
