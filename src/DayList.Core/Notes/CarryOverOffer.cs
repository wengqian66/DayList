using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DayList.Core;

/// <summary>
/// One unfinished task from a previous day, already remembered as handled for a calendar day.
/// </summary>
public sealed record CarryOverHandled(DateOnly Date, string Category, string Text)
{
    public static CarryOverHandled From(OverdueTaskItem item) =>
        new(
            item.Date,
            DayTaskCategory.Normalize(item.Category),
            OverdueIdentity.IdentityText(item.Text, item.Attachments));
}

/// <summary>
/// What was already offered or absorbed for one calendar day.
/// A different <see cref="Date"/> means a new day, so the old keys do not suppress a new yesterday.
/// </summary>
public sealed class CarryOverMemory
{
    public DateOnly? Date { get; set; }

    public List<CarryOverHandled> Handled { get; set; } = new();

    public bool Covers(DateOnly today, OverdueTaskItem item)
    {
        if (Date != today)
            return false;

        var key = CarryOverHandled.From(item);
        return Handled.Any(entry => entry == key);
    }

    public bool SameAs(CarryOverMemory other)
    {
        if (Date != other.Date || Handled.Count != other.Handled.Count)
            return false;

        for (var i = 0; i < Handled.Count; i++)
        {
            if (Handled[i] != other.Handled[i])
                return false;
        }

        return true;
    }
}

public readonly record struct TodayTaskRef(string Text, string Category, string AttachmentKey = "");

/// <summary>A task checked off on a daily note. A later date retires the same task from earlier notes.</summary>
public readonly record struct FinishedTaskRef(
    DateOnly Date,
    string Text,
    string Category,
    string AttachmentKey = "");

/// <summary>
/// Decides which earlier open tasks are still worth offering on Today.
/// Showing the banner used to rescan every open line, so a successful bring-into-today
/// came back on the next visit. Handled keys and tasks already on today both dismiss it.
/// A line left unchecked in an older note is also finished once the same task is checked on a later day.
/// </summary>
public static class CarryOverOffer
{
    public const string AllLabel = "全部";
    public const string YesterdayLabel = "昨天未完成";
    public const string EarlierLabel = "更早未完成";

    /// <summary>
    /// Drops an open line when the same task was checked off on a strictly later day.
    /// Text and category identify a normal line. A line that is only attachments matches
    /// on those file names, because bringing it forward copies the files onto the later day.
    /// 「带入今日」 copies forward and does not tick the source note, so that later check is the completion.
    /// </summary>
    public static IReadOnlyList<OverdueTaskItem> StillUnfinished(
        IReadOnlyList<OverdueTaskItem> open,
        IEnumerable<FinishedTaskRef> finished)
    {
        var latest = new Dictionary<(string Category, string Text), DateOnly>();
        foreach (var done in finished)
        {
            var text = OverdueIdentity.IdentityText(done.Text, done.AttachmentKey);
            if (text.Length == 0)
                continue;
            var key = (DayTaskCategory.Normalize(done.Category), text);
            if (!latest.TryGetValue(key, out var existing) || done.Date > existing)
                latest[key] = done.Date;
        }

        if (latest.Count == 0)
            return open.Where(item => OverdueIdentity.IdentityText(item.Text, item.Attachments).Length > 0).ToList();

        var pending = new List<OverdueTaskItem>(open.Count);
        foreach (var item in open)
        {
            var text = OverdueIdentity.IdentityText(item.Text, item.Attachments);
            if (text.Length == 0)
                continue;
            var key = (DayTaskCategory.Normalize(item.Category), text);
            if (latest.TryGetValue(key, out var doneOn) && doneOn > item.Date)
                continue;
            pending.Add(item);
        }

        return pending;
    }

    /// <summary>
    /// 「昨天未完成」 only when every row is the previous calendar day.
    /// Older leftovers stay visible, but the title says 「更早未完成」 so it matches the row date.
    /// </summary>
    public static string BannerTitle(DateOnly today, IReadOnlyList<DateOnly> itemDates)
    {
        if (itemDates.Count == 0)
            return YesterdayLabel;

        var yesterday = today.AddDays(-1);
        var label = itemDates.All(date => date == yesterday) ? YesterdayLabel : EarlierLabel;
        return $"{label} {itemDates.Count} 项";
    }

    public static bool AlreadyOnToday(OverdueTaskItem item, IEnumerable<TodayTaskRef> todayTasks)
    {
        var text = (item.Text ?? "").Trim();
        var category = DayTaskCategory.Normalize(item.Category);
        if (text.Length > 0)
        {
            return todayTasks.Any(task =>
                string.Equals((task.Text ?? "").Trim(), text, StringComparison.Ordinal)
                && DayTaskCategory.Normalize(task.Category) == category);
        }

        var attachmentKey = OverdueIdentity.AttachmentKey(item.Attachments);
        if (attachmentKey.Length == 0)
            return false;

        return todayTasks.Any(task =>
            string.IsNullOrWhiteSpace(task.Text)
            && DayTaskCategory.Normalize(task.Category) == category
            && string.Equals(task.AttachmentKey, attachmentKey, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<OverdueTaskItem> Pending(
        IReadOnlyList<OverdueTaskItem> openBeforeToday,
        IEnumerable<TodayTaskRef> todayTasks,
        CarryOverMemory memory,
        DateOnly today)
    {
        var todayList = todayTasks as IReadOnlyList<TodayTaskRef> ?? todayTasks.ToList();
        var pending = new List<OverdueTaskItem>();
        foreach (var item in openBeforeToday)
        {
            if (OverdueIdentity.IdentityText(item.Text, item.Attachments).Length == 0)
                continue;
            if (AlreadyOnToday(item, todayList))
                continue;
            if (memory.Covers(today, item))
                continue;
            pending.Add(item);
        }

        return pending;
    }

    public static CarryOverMemory MarkHandled(
        CarryOverMemory memory,
        DateOnly today,
        IEnumerable<OverdueTaskItem> items)
    {
        var kept = memory.Date == today
            ? memory.Handled.ToList()
            : new List<CarryOverHandled>();

        foreach (var item in items)
        {
            if (OverdueIdentity.IdentityText(item.Text, item.Attachments).Length == 0)
                continue;
            var key = CarryOverHandled.From(item);
            if (!kept.Contains(key))
                kept.Add(key);
        }

        return new CarryOverMemory
        {
            Date = today,
            Handled = kept
        };
    }

    public static IReadOnlyList<OverdueTaskItem> Filter(
        IReadOnlyList<OverdueTaskItem> items,
        string? categoryOrAll)
    {
        if (string.IsNullOrWhiteSpace(categoryOrAll)
            || string.Equals(categoryOrAll.Trim(), AllLabel, StringComparison.Ordinal))
        {
            return items.ToList();
        }

        var key = DayTaskCategory.Normalize(categoryOrAll);
        return items.Where(item => DayTaskCategory.Normalize(item.Category) == key).ToList();
    }
}

public static class CarryOverStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayList", "carry-over.json");

    public static CarryOverMemory Load() => Load(DefaultPath);

    public static CarryOverMemory Load(string path)
    {
        if (!File.Exists(path))
            return new CarryOverMemory();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CarryOverMemory>(json, JsonOptions) ?? new CarryOverMemory();
        }
        catch (JsonException)
        {
            return new CarryOverMemory();
        }
    }

    public static void Save(CarryOverMemory memory) => Save(memory, DefaultPath);

    public static void Save(CarryOverMemory memory, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(memory, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }
}
