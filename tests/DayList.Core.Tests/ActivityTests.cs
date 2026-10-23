using DayList.Core;

namespace DayList.Core.Tests;

public class ActivityTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Fact]
    public void Collect_CountsDoneAndTotalPerDay_AndSkipsEmptyDays()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var busy = new DateOnly(2026, 10, 20);
        var quiet = new DateOnly(2026, 10, 21);

        DailyNoteStore.Write(settings, busy, new[]
        {
            new DayTask { Text = "写周报", Category = "工作", IsDone = true },
            new DayTask { Text = "回邮件", Category = "工作", IsDone = true },
            new DayTask { Text = "买牛奶", Category = "生活" },
            new DayTask { Text = "" }
        });
        DailyNoteStore.Write(settings, quiet, new[]
        {
            new DayTask { Text = "散步", Category = "生活", IsDone = true }
        });

        var counts = DayActivityScan.Collect(settings, new DateOnly(2026, 10, 19), Today);

        Assert.True(counts.ContainsKey(busy));
        Assert.Equal(3, counts[busy].Total);
        Assert.Equal(2, counts[busy].Done);
        Assert.Equal(1, counts[quiet].Total);
        Assert.Equal(1, counts[quiet].Done);
        Assert.False(counts.ContainsKey(new DateOnly(2026, 10, 19)));
        Assert.False(counts.ContainsKey(new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void Collect_WithoutVault_IsEmpty()
    {
        var counts = DayActivityScan.Collect(new AppSettings(), Today, Today);
        Assert.Empty(counts);
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 1)]
    [InlineData(2, 8, 1)]
    [InlineData(3, 8, 2)]
    [InlineData(4, 8, 2)]
    [InlineData(5, 8, 3)]
    [InlineData(6, 8, 3)]
    [InlineData(7, 8, 4)]
    [InlineData(8, 8, 4)]
    [InlineData(1, 1, 4)]
    [InlineData(1, 3, 2)]
    public void HeatLevel_UsesQuartilesOfTheBusiestDay(int done, int max, int expected)
    {
        Assert.Equal(expected, HeatLevel.Of(done, max));
    }

    [Fact]
    public void ActivitySummary_CountsStreaksAndTotals()
    {
        var days = new Dictionary<DateOnly, DayActivityCount>
        {
            [new DateOnly(2026, 10, 10)] = new(2, 2),
            [new DateOnly(2026, 10, 11)] = new(3, 1),
            [new DateOnly(2026, 10, 12)] = new(1, 0),
            [new DateOnly(2026, 10, 13)] = new(4, 3),
            [new DateOnly(2026, 10, 14)] = new(2, 1),
            [new DateOnly(2026, 10, 15)] = new(5, 2),
            [new DateOnly(2026, 10, 20)] = new(1, 1),
            [new DateOnly(2026, 10, 21)] = new(1, 1)
        };

        var summary = ActivitySummary.From(days, Today);

        Assert.Equal(11, summary.TotalDone);
        Assert.Equal(7, summary.ActiveDays);
        // 10/13–10/15 is the longest run; 10/20–10/21 is the current one (today still empty).
        Assert.Equal(3, summary.LongestStreak);
        Assert.Equal(2, summary.CurrentStreak);
    }

    [Fact]
    public void ActivitySummary_CountsTodayWhenItHasFinishedTasks()
    {
        var days = new Dictionary<DateOnly, DayActivityCount>
        {
            [new DateOnly(2026, 10, 21)] = new(1, 1),
            [Today] = new(2, 2)
        };

        var summary = ActivitySummary.From(days, Today);

        Assert.Equal(2, summary.CurrentStreak);
        Assert.Equal(2, summary.LongestStreak);
    }

    [Fact]
    public void ActivitySummary_EmptyRange_IsAllZero()
    {
        var summary = ActivitySummary.From(new Dictionary<DateOnly, DayActivityCount>(), Today);

        Assert.Equal(0, summary.TotalDone);
        Assert.Equal(0, summary.ActiveDays);
        Assert.Equal(0, summary.CurrentStreak);
        Assert.Equal(0, summary.LongestStreak);
    }
}
