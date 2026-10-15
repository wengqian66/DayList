namespace DayList.Core;

/// <summary>
/// Groups task rows into category cards, preserving first-seen order
/// except that uncategorized tasks stay in front.
/// </summary>
public static class CategoryGrouping
{
    public static List<(string Key, List<T> Items)> Group<T>(IEnumerable<T> rows, Func<T, string?> categoryOf)
    {
        var groups = new List<(string Key, List<T> Items)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = DayTaskCategory.Normalize(categoryOf(row));
            if (!index.TryGetValue(key, out var at))
            {
                at = groups.Count;
                index[key] = at;
                groups.Add((key, new List<T>()));
            }

            groups[at].Items.Add(row);
        }

        if (index.TryGetValue(DayTaskCategory.UncategorizedKey, out var unc) && unc != 0)
        {
            var item = groups[unc];
            groups.RemoveAt(unc);
            groups.Insert(0, item);
        }

        return groups;
    }
}
