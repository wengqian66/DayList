using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DayList.Core;

/// <summary>
/// One-shot reminder for a task on a calendar day. Stored beside settings, never in the Markdown line.
/// </summary>
public sealed class TaskReminder
{
    public string Id { get; set; } = "";

    public string TaskText { get; set; } = "";

    public DateOnly Date { get; set; }

    /// <summary>Local wall time, <c>H:mm</c> or <c>HH:mm</c>.</summary>
    public string Time { get; set; } = "";

    public bool Fired { get; set; }
}

public sealed record ReminderDueSet(
    IReadOnlyList<TaskReminder> Notify,
    IReadOnlyList<TaskReminder> Expire);

public static class ReminderSchedule
{
    public static readonly TimeSpan DefaultCatchUp = TimeSpan.FromHours(12);

    private static readonly Regex Clock = new(
        @"^(?<h>\d{1,2}):(?<m>\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParseClock(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Clock.Match(text.Trim());
        if (!match.Success)
            return false;

        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return false;

        time = new TimeOnly(hour, minute);
        return true;
    }

    public static string FormatClock(TimeOnly time) =>
        time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static bool TryGetDue(TaskReminder reminder, out DateTime due)
    {
        due = default;
        if (reminder.Date.Year < 2000)
            return false;
        if (!TryParseClock(reminder.Time, out var time))
            return false;

        due = reminder.Date.ToDateTime(time);
        return true;
    }

    /// <summary>
    /// <paramref name="Notify"/> is due and still inside the catch-up window.
    /// <paramref name="Expire"/> was due longer ago and should be marked fired without a popup.
    /// </summary>
    public static ReminderDueSet Partition(IEnumerable<TaskReminder> reminders, DateTime now, TimeSpan? catchUp = null)
    {
        var window = catchUp ?? DefaultCatchUp;
        if (window < TimeSpan.Zero)
            window = TimeSpan.Zero;

        var notify = new List<TaskReminder>();
        var expire = new List<TaskReminder>();
        foreach (var reminder in reminders)
        {
            if (reminder.Fired || !TryGetDue(reminder, out var due) || due > now)
                continue;

            if (now - due <= window)
                notify.Add(reminder);
            else
                expire.Add(reminder);
        }

        return new ReminderDueSet(notify, expire);
    }

    public static void ApplySnooze(TaskReminder reminder, DateTime now, int minutes = 10)
    {
        if (minutes < 1)
            minutes = 1;

        var target = now.AddMinutes(minutes);
        if (target.Second != 0 || target.Millisecond != 0)
        {
            target = new DateTime(target.Year, target.Month, target.Day, target.Hour, target.Minute, 0, target.Kind)
                .AddMinutes(1);
        }

        reminder.Date = DateOnly.FromDateTime(target);
        reminder.Time = FormatClock(TimeOnly.FromDateTime(target));
        reminder.Fired = false;
    }
}

public static class ReminderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DayList", "reminders.json");

    public static IReadOnlyList<TaskReminder> Load(string path)
    {
        if (!File.Exists(path))
            return Array.Empty<TaskReminder>();

        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<ReminderFile>(json, JsonOptions);
            return Normalize(file?.Reminders);
        }
        catch (JsonException)
        {
            return Array.Empty<TaskReminder>();
        }
    }

    public static void Save(string path, IEnumerable<TaskReminder> reminders)
    {
        var kept = PurgeOldFired(Normalize(reminders), DateOnly.FromDateTime(DateTime.Now), keepDays: 7);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(new ReminderFile { Reminders = kept }, JsonOptions);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temp, path, overwrite: true);
    }

    public static TaskReminder SetForTask(string path, string? id, string taskText, DateOnly date, TimeOnly time)
    {
        var text = (taskText ?? "").Trim();
        if (text.Length == 0)
            throw new ArgumentException("提醒需要任务文字。", nameof(taskText));

        var all = Load(path).ToList();
        TaskReminder? existing = null;
        if (!string.IsNullOrWhiteSpace(id))
            existing = all.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        existing ??= all.FirstOrDefault(item =>
            !item.Fired
            && item.Date == date
            && string.Equals(item.TaskText, text, StringComparison.Ordinal));

        if (existing is null)
        {
            existing = new TaskReminder { Id = Guid.NewGuid().ToString("N") };
            all.Add(existing);
        }

        existing.TaskText = text;
        existing.Date = date;
        existing.Time = ReminderSchedule.FormatClock(time);
        existing.Fired = false;
        Save(path, all);
        return Copy(existing);
    }

    public static bool Rename(string path, string id, string taskText)
    {
        var text = (taskText ?? "").Trim();
        if (text.Length == 0 || string.IsNullOrWhiteSpace(id))
            return false;

        var all = Load(path).ToList();
        var existing = all.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is null || string.Equals(existing.TaskText, text, StringComparison.Ordinal))
            return false;

        existing.TaskText = text;
        Save(path, all);
        return true;
    }

    public static bool Remove(string path, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var all = Load(path).ToList();
        var removed = all.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (removed == 0)
            return false;

        Save(path, all);
        return true;
    }

    public static bool MarkFired(string path, string id)
    {
        var all = Load(path).ToList();
        var existing = all.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is null || existing.Fired)
            return false;

        existing.Fired = true;
        Save(path, all);
        return true;
    }

    public static bool Snooze(string path, string id, DateTime now, int minutes = 10)
    {
        var all = Load(path).ToList();
        var existing = all.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (existing is null)
            return false;

        ReminderSchedule.ApplySnooze(existing, now, minutes);
        Save(path, all);
        return true;
    }

    public static TaskReminder? Find(string path, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return Load(path).FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    internal static List<TaskReminder> Normalize(IEnumerable<TaskReminder>? raw)
    {
        var list = new List<TaskReminder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (raw is null)
            return list;

        foreach (var item in raw)
        {
            if (item is null)
                continue;

            var text = (item.TaskText ?? "").Trim();
            if (text.Length == 0 || item.Date.Year < 2000)
                continue;
            if (!ReminderSchedule.TryParseClock(item.Time, out var clock))
                continue;

            var id = (item.Id ?? "").Trim();
            if (id.Length == 0)
                id = Guid.NewGuid().ToString("N");
            if (!seen.Add(id))
                continue;

            list.Add(new TaskReminder
            {
                Id = id,
                TaskText = text,
                Date = item.Date,
                Time = ReminderSchedule.FormatClock(clock),
                Fired = item.Fired
            });
        }

        return list;
    }

    internal static List<TaskReminder> PurgeOldFired(List<TaskReminder> reminders, DateOnly today, int keepDays)
    {
        var cutoff = today.AddDays(-Math.Max(0, keepDays));
        reminders.RemoveAll(item => item.Fired && item.Date < cutoff);
        return reminders;
    }

    private static TaskReminder Copy(TaskReminder reminder) => new()
    {
        Id = reminder.Id,
        TaskText = reminder.TaskText,
        Date = reminder.Date,
        Time = reminder.Time,
        Fired = reminder.Fired
    };

    private sealed class ReminderFile
    {
        public List<TaskReminder> Reminders { get; set; } = new();
    }
}
