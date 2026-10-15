using DayList.Core;

namespace DayList.Core.Tests;

public class ReminderStoreTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void Clock_AcceptsHourMinuteAndRejectsJunk()
    {
        Assert.True(ReminderSchedule.TryParseClock("9:05", out var morning));
        Assert.Equal(new TimeOnly(9, 5), morning);
        Assert.Equal("09:05", ReminderSchedule.FormatClock(morning));
        Assert.True(ReminderSchedule.TryParseClock("23:59", out _));
        Assert.False(ReminderSchedule.TryParseClock("24:00", out _));
        Assert.False(ReminderSchedule.TryParseClock("14:60", out _));
        Assert.False(ReminderSchedule.TryParseClock("2pm", out _));
        Assert.False(ReminderSchedule.TryParseClock("", out _));
    }

    [Fact]
    public void Store_RoundTripsWithoutTouchingMarkdown()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daylist-reminders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "reminders.json");
        var note = Path.Combine(directory, "2026-10-15.md");
        File.WriteAllText(note, "## DayList\n\n- [ ] 买牛奶\n");
        var before = File.ReadAllBytes(note);

        try
        {
            var saved = ReminderStore.SetForTask(path, null, "买牛奶", Day, new TimeOnly(14, 30));
            Assert.False(string.IsNullOrWhiteSpace(saved.Id));
            Assert.Equal("14:30", saved.Time);
            Assert.False(saved.Fired);

            var json = File.ReadAllText(path);
            Assert.Contains("买牛奶", json);
            Assert.Contains("2026-10-15", json);
            Assert.DoesNotContain("- [ ]", json);
            Assert.DoesNotContain("##", json);
            Assert.Equal(before, File.ReadAllBytes(note));
            Assert.False(File.Exists(path + ".tmp"));

            var again = ReminderStore.SetForTask(path, saved.Id, "买牛奶", Day, new TimeOnly(15, 0));
            Assert.Equal(saved.Id, again.Id);
            Assert.Equal("15:00", ReminderStore.Find(path, saved.Id)!.Time);
            Assert.Single(ReminderStore.Load(path));

            Assert.True(ReminderStore.Rename(path, saved.Id, "买燕麦奶"));
            Assert.Equal("买燕麦奶", ReminderStore.Find(path, saved.Id)!.TaskText);
            Assert.Equal(before, File.ReadAllBytes(note));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_SkipsBrokenRowsAndKeepsComments()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-reminders-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  // local reminders
                  "reminders": [
                    { "id": "ok", "taskText": "喝水", "date": "2026-10-15", "time": "8:00" },
                    { "id": "bad-time", "taskText": "无效", "date": "2026-10-15", "time": "noon" },
                    { "taskText": "", "date": "2026-10-15", "time": "09:00" }
                  ],
                }
                """);

            var loaded = ReminderStore.Load(path);
            var only = Assert.Single(loaded);
            Assert.Equal("ok", only.Id);
            Assert.Equal("08:00", only.Time);
            Assert.Equal(new DateOnly(2026, 10, 15), only.Date);

            File.WriteAllText(path, "not-json");
            Assert.Empty(ReminderStore.Load(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Partition_FiresInsideCatchUpAndExpiresOlderOnes()
    {
        var now = new DateTime(2026, 10, 15, 15, 0, 0);
        var reminders = new[]
        {
            new TaskReminder { Id = "due", TaskText = "到点", Date = Day, Time = "15:00" },
            new TaskReminder { Id = "soon", TaskText = "还早", Date = Day, Time = "15:01" },
            new TaskReminder { Id = "late", TaskText = "刚过", Date = Day, Time = "14:00" },
            new TaskReminder { Id = "stale", TaskText = "昨天", Date = Day.AddDays(-1), Time = "09:00" },
            new TaskReminder { Id = "done", TaskText = "已响", Date = Day, Time = "08:00", Fired = true }
        };

        var due = ReminderSchedule.Partition(reminders, now, TimeSpan.FromHours(12));
        Assert.Equal(new[] { "due", "late" }, due.Notify.Select(item => item.Id));
        Assert.Equal(new[] { "stale" }, due.Expire.Select(item => item.Id));

        var early = ReminderSchedule.Partition(reminders, now.AddSeconds(-1), TimeSpan.FromHours(12));
        Assert.DoesNotContain(early.Notify, item => item.Id == "due");
    }

    [Fact]
    public void Snooze_MovesTenMinutesAndClearsFired()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-reminders-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var saved = ReminderStore.SetForTask(path, null, "拉伸", Day, new TimeOnly(23, 55));
            Assert.True(ReminderStore.MarkFired(path, saved.Id));
            Assert.True(ReminderStore.Find(path, saved.Id)!.Fired);

            var now = new DateTime(2026, 10, 15, 23, 55, 0);
            Assert.True(ReminderStore.Snooze(path, saved.Id, now, 10));
            var snoozed = ReminderStore.Find(path, saved.Id);
            Assert.NotNull(snoozed);
            Assert.False(snoozed.Fired);
            Assert.Equal(new DateOnly(2026, 10, 16), snoozed.Date);
            Assert.Equal("00:05", snoozed.Time);

            Assert.True(ReminderStore.Remove(path, saved.Id));
            Assert.Null(ReminderStore.Find(path, saved.Id));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Save_DropsFiredRemindersOlderThanAWeek()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-reminders-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var stale = new TaskReminder
            {
                Id = "old",
                TaskText = "上周",
                Date = DateOnly.FromDateTime(DateTime.Now).AddDays(-8),
                Time = "09:00",
                Fired = true
            };
            var keep = new TaskReminder
            {
                Id = "open",
                TaskText = "仍要提醒",
                Date = DateOnly.FromDateTime(DateTime.Now).AddDays(-8),
                Time = "09:00",
                Fired = false
            };
            ReminderStore.Save(path, new[] { stale, keep });
            var loaded = ReminderStore.Load(path);
            Assert.Equal("open", Assert.Single(loaded).Id);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
