namespace DayList.Core;

/// <summary>
/// Result of adding or removing a name in the global category catalog.
/// </summary>
public readonly record struct CategoryCatalogUpdate(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Retired,
    bool Changed);

/// <summary>
/// One row in the add-task category picker. <see cref="CanDelete"/> is true
/// only for a catalog name, never for 未分类 or 「＋ 新建分类…」.
/// </summary>
public readonly record struct CategoryPickerEntry(string Label, bool CanDelete);

/// <summary>
/// Task categories kept in app settings, not in a single day's note.
/// Creating a name once makes it available on later days. Deleting removes it
/// from the catalog and retires it so opening an older note does not put it back.
/// Notes are never rewritten here: tasks already filed under that heading stay.
/// Blank headings are not seeded into markdown; the app shows the catalog itself.
/// </summary>
public static class CategoryCatalog
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string?>? names)
    {
        var result = new List<string>();
        if (names is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var normalized = DayTaskCategory.Normalize(name);
            if (DayTaskCategory.IsUncategorized(normalized))
                continue;
            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// Catalog order, then any extra names already used on this day.
    /// </summary>
    public static IReadOnlyList<string> Available(IEnumerable<string?>? categories, IEnumerable<string?>? alsoUsed)
    {
        var names = Normalize(categories).ToList();
        var seen = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var name in Normalize(alsoUsed))
        {
            if (seen.Add(name))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Catalog names that have no task on the day being shown.
    /// </summary>
    public static IReadOnlyList<string> WithoutTasks(IEnumerable<string?>? categories, IEnumerable<string?>? used)
    {
        var usedSet = new HashSet<string>(Normalize(used), StringComparer.Ordinal);
        return Normalize(categories).Where(name => !usedSet.Contains(name)).ToList();
    }

    public static CategoryCatalogUpdate Remember(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? retired,
        string? name) =>
        RememberAll(categories, retired, new[] { name });

    /// <summary>
    /// Explicit create or reuse. A retired name is restored.
    /// </summary>
    public static CategoryCatalogUpdate RememberAll(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? retired,
        IEnumerable<string?>? names)
    {
        var (active, gone) = Split(categories, retired);
        var changed = false;
        var activeSet = new HashSet<string>(active, StringComparer.Ordinal);
        var goneSet = new HashSet<string>(gone, StringComparer.Ordinal);
        foreach (var name in Normalize(names))
        {
            if (goneSet.Remove(name))
            {
                gone.RemoveAll(item => string.Equals(item, name, StringComparison.Ordinal));
                changed = true;
            }

            if (activeSet.Add(name))
            {
                active.Add(name);
                changed = true;
            }
        }

        return new CategoryCatalogUpdate(active, gone, changed);
    }

    /// <summary>
    /// Pull names already written in a note into the catalog. Retired names stay retired.
    /// </summary>
    public static CategoryCatalogUpdate Absorb(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? retired,
        IEnumerable<string?>? seen)
    {
        var (active, gone) = Split(categories, retired);
        var changed = false;
        var activeSet = new HashSet<string>(active, StringComparer.Ordinal);
        var goneSet = new HashSet<string>(gone, StringComparer.Ordinal);
        foreach (var name in Normalize(seen))
        {
            if (goneSet.Contains(name) || !activeSet.Add(name))
                continue;
            active.Add(name);
            changed = true;
        }

        return new CategoryCatalogUpdate(active, gone, changed);
    }

    /// <summary>
    /// Rows for the add-task category picker. Delete is offered only for names
    /// still in the catalog. The uncategorized row, the new-category row, and
    /// headings that merely appear on today's note are not deletable here.
    /// </summary>
    public static IReadOnlyList<CategoryPickerEntry> PickerEntries(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? alsoUsed)
    {
        var catalog = new HashSet<string>(Normalize(categories), StringComparer.Ordinal);
        var entries = new List<CategoryPickerEntry>
        {
            new(DayTaskCategory.UncategorizedLabel, false)
        };

        foreach (var name in Available(categories, alsoUsed)
                     .OrderBy(name => name, StringComparer.CurrentCulture))
        {
            entries.Add(new CategoryPickerEntry(name, catalog.Contains(name)));
        }

        entries.Add(new CategoryPickerEntry(DayTaskCategory.NewCategorySentinel, false));
        return entries;
    }

    /// <summary>
    /// Drop a name from the catalog and remember that the user removed it.
    /// Does not describe a note edit.
    /// </summary>
    public static CategoryCatalogUpdate Forget(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? retired,
        string? name)
    {
        var normalized = DayTaskCategory.Normalize(name);
        var (active, gone) = Split(categories, retired);
        if (DayTaskCategory.IsUncategorized(normalized))
            return new CategoryCatalogUpdate(active, gone, false);

        var changed = active.RemoveAll(item => string.Equals(item, normalized, StringComparison.Ordinal)) > 0;
        if (!gone.Contains(normalized, StringComparer.Ordinal))
        {
            gone.Add(normalized);
            changed = true;
        }

        return new CategoryCatalogUpdate(active, gone, changed);
    }

    private static (List<string> Categories, List<string> Retired) Split(
        IEnumerable<string?>? categories,
        IEnumerable<string?>? retired)
    {
        var active = Normalize(categories).ToList();
        var activeSet = new HashSet<string>(active, StringComparer.Ordinal);
        var gone = Normalize(retired).Where(name => !activeSet.Contains(name)).ToList();
        return (active, gone);
    }
}
