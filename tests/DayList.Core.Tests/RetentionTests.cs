using DayList.Core;

namespace DayList.Core.Tests;

public class RetentionTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateOnly Yesterday = new(2026, 10, 15);

    private static OverdueTaskItem Open(string text, string category = "", DateOnly? date = null) =>
        new(date ?? Yesterday, text, category, "", '-', 'x');

    [Fact]
    public void Pending_HidesTasksAlreadyOnToday()
    {
        var open = new[]
        {
            Open("写周报", "工作"),
            Open("买牛奶", "生活")
        };
        var today = new[] { new TodayTaskRef("写周报", "工作") };

        var pending = CarryOverOffer.Pending(open, today, new CarryOverMemory(), Today);

        Assert.Single(pending);
        Assert.Equal("买牛奶", pending[0].Text);
    }

    [Fact]
    public void Pending_EmptyRelativeToToday_DoesNotNag()
    {
        var open = new[] { Open("写周报", "工作"), Open("买牛奶", "生活") };
        var today = new[]
        {
            new TodayTaskRef("写周报", "工作"),
            new TodayTaskRef("  买牛奶  ", "生活")
        };

        var pending = CarryOverOffer.Pending(open, today, new CarryOverMemory(), Today);

        Assert.Empty(pending);
    }

    [Fact]
    public void MarkHandled_DismissesForThatDay_UntilANewLeftoverAppears()
    {
        var offered = new[] { Open("写周报", "工作"), Open("买牛奶", "生活") };
        var memory = CarryOverOffer.MarkHandled(new CarryOverMemory(), Today, offered);

        Assert.Empty(CarryOverOffer.Pending(offered, Array.Empty<TodayTaskRef>(), memory, Today));

        var withNew = offered.Append(Open("新出现的", "工作")).ToArray();
        var pending = CarryOverOffer.Pending(withNew, Array.Empty<TodayTaskRef>(), memory, Today);
        Assert.Single(pending);
        Assert.Equal("新出现的", pending[0].Text);
    }

    [Fact]
    public void HandledYesterday_DoesNotSuppressANewCalendarDay()
    {
        var item = Open("写周报", "工作");
        var memory = CarryOverOffer.MarkHandled(new CarryOverMemory(), Today, new[] { item });
        var nextDay = Today.AddDays(1);

        var pending = CarryOverOffer.Pending(new[] { item }, Array.Empty<TodayTaskRef>(), memory, nextDay);

        Assert.Single(pending);
    }

    [Fact]
    public void AbsorbedOntoToday_StaysDismissedEvenIfRemovedFromToday()
    {
        var open = new[] { Open("写周报", "工作") };
        var today = new[] { new TodayTaskRef("写周报", "工作") };
        var absorbed = open.Where(item => CarryOverOffer.AlreadyOnToday(item, today)).ToArray();
        var memory = CarryOverOffer.MarkHandled(new CarryOverMemory(), Today, absorbed);

        var pending = CarryOverOffer.Pending(open, Array.Empty<TodayTaskRef>(), memory, Today);

        Assert.Empty(pending);
    }

    [Fact]
    public void BringIntoToday_ThenRescan_StaysDismissed()
    {
        var item = Open("写周报", "工作");
        var open = new[] { item, Open("买牛奶", "生活") };
        var brought = CarryOverOffer.MarkHandled(new CarryOverMemory(), Today, new[] { item });
        var today = new[] { new TodayTaskRef(item.Text, item.Category) };

        var pending = CarryOverOffer.Pending(open, today, brought, Today);

        Assert.Single(pending);
        Assert.Equal("买牛奶", pending[0].Text);

        var allBrought = CarryOverOffer.MarkHandled(brought, Today, pending);
        var todayAfter = today.Append(new TodayTaskRef("买牛奶", "生活")).ToArray();
        Assert.Empty(CarryOverOffer.Pending(open, todayAfter, allBrought, Today));
    }

    [Fact]
    public void Filter_AllLabel_KeepsEveryItem_AndCategoryNarrows()
    {
        var open = new[] { Open("写周报", "工作"), Open("买牛奶", "生活"), Open("散步", "") };

        Assert.Equal(3, CarryOverOffer.Filter(open, CarryOverOffer.AllLabel).Count);
        Assert.Equal(3, CarryOverOffer.Filter(open, "  全部 ").Count);
        var work = CarryOverOffer.Filter(open, "工作");
        Assert.Single(work);
        Assert.Equal("写周报", work[0].Text);
        Assert.Equal("散步", Assert.Single(CarryOverOffer.Filter(open, "未分类")).Text);
    }

    [Fact]
    public void Store_RoundTripsHandledKeys_ForTheCalendarDay()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-carry-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var memory = CarryOverOffer.MarkHandled(
                new CarryOverMemory(),
                Today,
                new[] { Open("写周报", "工作") });
            CarryOverStore.Save(memory, path);
            var loaded = CarryOverStore.Load(path);

            Assert.True(loaded.SameAs(memory));
            Assert.Empty(CarryOverOffer.Pending(
                new[] { Open("写周报", "工作") },
                Array.Empty<TodayTaskRef>(),
                loaded,
                Today));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Templates_OfferThreeStarters_ThatBecomeRealTasks()
    {
        Assert.Equal(new[] { "morning", "shutdown", "weekly" }, DayTemplates.Starters.Select(item => item.Id));
        Assert.Contains(DayTemplates.Starters, item => item.Title == "晨间三件事");
        Assert.Contains(DayTemplates.Starters, item => item.Title == "下班清扫");
        Assert.Contains(DayTemplates.Starters, item => item.Title == "周复盘");

        foreach (var template in DayTemplates.Starters)
        {
            var tasks = template.CreateTasks();
            Assert.InRange(tasks.Count, 3, 3);
            Assert.All(tasks, task =>
            {
                Assert.False(task.IsDone);
                Assert.False(string.IsNullOrWhiteSpace(task.Text));
            });
        }

        var morning = DayTemplates.Find("morning")!;
        Assert.Contains(morning.CreateTasks(), task => task.Category == "生活");
        Assert.Null(DayTemplates.Find("missing"));
    }

    [Fact]
    public void Completion_CategoryFiresOnce_DaySuppressesTheCategoryToast()
    {
        var before = new[]
        {
            new TaskDoneSnapshot("工作", false),
            new TaskDoneSnapshot("工作", true),
            new TaskDoneSnapshot("生活", false)
        };
        var categoryDone = new[]
        {
            new TaskDoneSnapshot("工作", true),
            new TaskDoneSnapshot("工作", true),
            new TaskDoneSnapshot("生活", false)
        };
        var moment = CompletionFeedback.Detect(before, categoryDone);
        Assert.Equal("工作", moment.CategoryJustCompleted);
        Assert.False(moment.DayJustCompleted);

        var dayBefore = new[]
        {
            new TaskDoneSnapshot("工作", true),
            new TaskDoneSnapshot("生活", false)
        };
        var dayAfter = new[]
        {
            new TaskDoneSnapshot("工作", true),
            new TaskDoneSnapshot("生活", true)
        };
        var day = CompletionFeedback.Detect(dayBefore, dayAfter);
        Assert.True(day.DayJustCompleted);
        Assert.Null(day.CategoryJustCompleted);

        var again = CompletionFeedback.Detect(dayAfter, dayAfter);
        Assert.False(again.Any);

        var undone = CompletionFeedback.Detect(dayAfter, dayBefore);
        Assert.False(undone.Any);
    }

    [Fact]
    public void SyncStatus_UsesHumanRelativeTime()
    {
        var now = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal("已连接 Obsidian", SyncStatusText.Format(SyncPhase.Idle, null, now));
        Assert.Equal("写入未成功", SyncStatusText.Format(SyncPhase.Error, now, now));
        Assert.Equal(
            "已写入 Obsidian · 刚刚",
            SyncStatusText.Format(SyncPhase.Saved, now.AddSeconds(-10), now));
        Assert.Equal(
            "已写入 Obsidian · 1 分钟前",
            SyncStatusText.Format(SyncPhase.Saved, now.AddSeconds(-45), now));
        Assert.Equal(
            "已写入 Obsidian · 2 分钟前",
            SyncStatusText.Format(SyncPhase.Saved, now.AddMinutes(-2), now));
        Assert.Equal(
            "已写入 Obsidian · 14:00",
            SyncStatusText.Format(SyncPhase.Saved, now.AddHours(-1), now));
        Assert.Equal(
            "已写入 Obsidian · 10月15日 15:00",
            SyncStatusText.Format(SyncPhase.Saved, now.AddDays(-1), now));
    }
}
