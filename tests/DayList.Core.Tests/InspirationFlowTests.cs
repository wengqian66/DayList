using DayList.Core;

namespace DayList.Core.Tests;

public class InspirationFlowTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void Arrange_OrdersByClockAndKeepsUntimedAfter()
    {
        var sources = new[]
        {
            new InspirationFlowSource(Today, 0, "21:04", "先写但更晚"),
            new InspirationFlowSource(Today, 1, "9:10", "早晨"),
            new InspirationFlowSource(Today, 2, null, "没写时间"),
            new InspirationFlowSource(Today, 3, "09:10", "同一分钟的后一句"),
            new InspirationFlowSource(Today, 4, "24:00", "钟点无效"),
            new InspirationFlowSource(Today, 5, "  ", "   "),
            new InspirationFlowSource(Today.AddDays(-1), 0, "8:05", "昨天早晨\n第二行"),
            new InspirationFlowSource(Today.AddDays(-7), 0, "12:00", "窗口外")
        };

        var days = InspirationFlow.Arrange(Today, Today, sources);

        Assert.Equal(2, days.Count);
        Assert.Equal("今天", days[0].Heading);
        Assert.Equal("10月16日 周五", days[0].Caption);
        Assert.Equal(new[] { "早晨", "同一分钟的后一句", "先写但更晚", "没写时间", "钟点无效" }, days[0].Items.Select(item => item.Text));
        Assert.Equal(new[] { "09:10", "09:10", "21:04", "·", "·" }, days[0].Items.Select(item => item.Clock));
        Assert.False(days[0].Items[3].HasClock);

        Assert.Equal("昨天", days[1].Heading);
        Assert.Equal("10月15日 周四", days[1].Caption);
        var yesterday = Assert.Single(days[1].Items);
        Assert.Equal("昨天早晨\n第二行", yesterday.Text);
        Assert.Equal("昨天早晨 第二行", yesterday.Glance);
        Assert.Equal("08:05", yesterday.Clock);
    }

    [Fact]
    public void Arrange_UsesTheAnchorNotTheWallClockForTheWindow()
    {
        var anchor = new DateOnly(2026, 10, 10);
        var sources = new[]
        {
            new InspirationFlowSource(anchor, 0, "11:00", "这一天"),
            new InspirationFlowSource(Today, 0, "12:00", "更晚的今天"),
            new InspirationFlowSource(anchor.AddDays(-6), 0, "07:00", "窗口内最早")
        };

        var days = InspirationFlow.Arrange(anchor, Today, sources);

        Assert.Equal(new[] { anchor, anchor.AddDays(-6) }, days.Select(day => day.Date));
        Assert.Equal("10月10日", days[0].Heading);
        Assert.Equal("周六", days[0].Caption);
        Assert.Equal("10月4日", days[1].Heading);
    }

    [Fact]
    public void Read_LoadsRecentNotesWithoutWritingOrEnteringTheMonthDigest()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var early = Today.AddDays(-8);
        DailyNoteStore.Write(settings, Today, new[] { new DayTask { Text = "买牛奶", Category = "生活" } });
        DailyNoteStore.WriteInspirations(settings, Today, new[]
        {
            new InspirationEntry { TimeLabel = "18:00", Text = "晚上的一句" },
            new InspirationEntry { TimeLabel = "07:30", Text = "先记下的早晨" }
        });
        DailyNoteStore.WriteInspirations(settings, Today.AddDays(-1), new[]
        {
            new InspirationEntry { TimeLabel = "22:10", Text = "昨晚" }
        });
        DailyNoteStore.WriteInspirations(settings, early, new[]
        {
            new InspirationEntry { TimeLabel = "12:00", Text = "八天前" }
        });

        var todayPath = DailyNoteStore.Read(settings, Today).FullPath;
        var before = File.ReadAllBytes(todayPath);

        var days = InspirationFlow.Read(settings, Today, Today);

        Assert.Equal(before, File.ReadAllBytes(todayPath));
        Assert.Equal(2, days.Count);
        Assert.Equal(new[] { "先记下的早晨", "晚上的一句" }, days[0].Items.Select(item => item.Text));
        Assert.Equal("昨晚", Assert.Single(days[1].Items).Text);
        Assert.DoesNotContain(days, day => day.Items.Any(item => item.Text == "八天前"));

        var digest = MonthDigest.Build(settings, Today);
        Assert.Contains("买牛奶", digest);
        Assert.DoesNotContain("晚上的一句", digest);
        Assert.DoesNotContain("先记下的早晨", digest);
        Assert.DoesNotContain("昨晚", digest);
        Assert.DoesNotContain("灵感", digest);
    }
}
