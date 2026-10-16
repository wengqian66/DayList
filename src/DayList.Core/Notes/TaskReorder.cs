namespace DayList.Core;

public readonly record struct ReorderResult(IReadOnlyList<DayTask> Tasks, IReadOnlyList<int> SourceIndex);

public readonly record struct DayMove(IReadOnlyList<DayTask> Source, IReadOnlyList<DayTask> Target);

/// <summary>
/// Pure list edits for drag-reorder, moving a task onto another day, and turning one into an inspiration.
/// Same-card reorders keep other categories in their slots so card order does not jump.
/// </summary>
public static class TaskReorder
{
    public static ReorderResult MoveBefore(IReadOnlyList<DayTask> tasks, int from, int before, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        EnsureIndex(tasks.Count, from, nameof(from));
        EnsureIndex(tasks.Count, before, nameof(before));

        var targetCategory = category is null
            ? DayTaskCategory.Normalize(tasks[from].Category)
            : DayTaskCategory.Normalize(category);
        var sourceCategory = DayTaskCategory.Normalize(tasks[from].Category);
        var beforeCategory = DayTaskCategory.Normalize(tasks[before].Category);
        var sameCard = sourceCategory == targetCategory && beforeCategory == targetCategory;

        IReadOnlyList<int> order;
        if (from == before)
        {
            order = Range(tasks.Count);
        }
        else if (sameCard)
        {
            order = PermuteCategory(tasks, from, before, sourceCategory);
        }
        else
        {
            var flat = Range(tasks.Count).ToList();
            flat.RemoveAt(from);
            var insertAt = from < before ? before - 1 : before;
            flat.Insert(insertAt, from);
            order = flat;
        }

        return Build(tasks, order, from, targetCategory);
    }

    public static ReorderResult MoveToCategoryEnd(IReadOnlyList<DayTask> tasks, int from, string category)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        EnsureIndex(tasks.Count, from, nameof(from));

        var key = DayTaskCategory.Normalize(category);
        var sourceKey = DayTaskCategory.Normalize(tasks[from].Category);
        if (sourceKey == key)
        {
            var slots = CategorySlots(tasks, key);
            var block = slots.Where(index => index != from).ToList();
            block.Add(from);
            var order = Range(tasks.Count).ToList();
            for (var i = 0; i < slots.Count; i++)
                order[slots[i]] = block[i];
            return Build(tasks, order, from, key);
        }

        var flat = Range(tasks.Count).Where(index => index != from).ToList();
        var insertAt = flat.Count;
        for (var i = flat.Count - 1; i >= 0; i--)
        {
            if (DayTaskCategory.Normalize(tasks[flat[i]].Category) == key)
            {
                insertAt = i + 1;
                break;
            }
        }

        flat.Insert(insertAt, from);
        return Build(tasks, flat, from, key);
    }

    public static DayMove MoveToOtherDay(IReadOnlyList<DayTask> source, int index, IReadOnlyList<DayTask> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        EnsureIndex(source.Count, index, nameof(index));

        var from = source.Select(task => task.Copy()).ToList();
        var to = target.Select(task => task.Copy()).ToList();
        var moving = from[index];
        from.RemoveAt(index);
        moving.RawLine = null;
        to.Add(moving);
        return new DayMove(from, to);
    }

    public static (IReadOnlyList<DayTask> Tasks, InspirationEntry Entry) ExtractInspiration(
        IReadOnlyList<DayTask> tasks,
        int index,
        string? timeLabel)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        EnsureIndex(tasks.Count, index, nameof(index));

        var list = tasks.Select(task => task.Copy()).ToList();
        var moving = list[index];
        list.RemoveAt(index);
        var entry = new InspirationEntry
        {
            TimeLabel = string.IsNullOrWhiteSpace(timeLabel) ? null : timeLabel.Trim(),
            Text = (moving.Text ?? "").Trim()
        };
        return (list, entry);
    }

    private static List<int> PermuteCategory(IReadOnlyList<DayTask> tasks, int from, int before, string category)
    {
        var slots = CategorySlots(tasks, category);
        var block = slots.ToList();
        var fromIn = block.IndexOf(from);
        var beforeIn = block.IndexOf(before);
        block.RemoveAt(fromIn);
        var insert = fromIn < beforeIn ? beforeIn - 1 : beforeIn;
        block.Insert(insert, from);

        var order = Range(tasks.Count).ToList();
        for (var i = 0; i < slots.Count; i++)
            order[slots[i]] = block[i];
        return order;
    }

    private static List<int> CategorySlots(IReadOnlyList<DayTask> tasks, string category)
    {
        var slots = new List<int>();
        for (var i = 0; i < tasks.Count; i++)
        {
            if (DayTaskCategory.Normalize(tasks[i].Category) == category)
                slots.Add(i);
        }

        return slots;
    }

    private static ReorderResult Build(IReadOnlyList<DayTask> tasks, IReadOnlyList<int> order, int from, string category)
    {
        var list = new List<DayTask>(order.Count);
        foreach (var index in order)
            list.Add(tasks[index].Copy());

        var at = -1;
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i] == from)
            {
                at = i;
                break;
            }
        }

        if (at >= 0)
            list[at].Category = category;

        return new ReorderResult(list, order.ToList());
    }

    private static List<int> Range(int count)
    {
        var list = new List<int>(count);
        for (var i = 0; i < count; i++)
            list.Add(i);
        return list;
    }

    private static void EnsureIndex(int count, int index, string name)
    {
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(name);
    }
}
