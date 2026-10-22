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
    public void StillUnfinished_DropsLinesCheckedOffOnALaterDay()
    {
        var source = new DateOnly(2026, 10, 15);
        var finishedOn = new DateOnly(2026, 10, 16);
        var open = new[]
        {
            Open("买菜", "生活", source),
            Open("洗碗", "生活", source),
            Open("写周报", "工作", source),
            Open("回邮件", "工作", source)
        };
        var finished = open.Select(item => new FinishedTaskRef(finishedOn, item.Text, item.Category));

        Assert.Empty(CarryOverOffer.StillUnfinished(open, finished));
    }

    [Fact]
    public void StillUnfinished_KeepsANewerOpenCopy_AndDoesNotCrossCategories()
    {
        var older = Open("买菜", "生活", new DateOnly(2026, 10, 15));
        var newer = Open("买菜", "生活", new DateOnly(2026, 10, 19));
        var otherCategory = Open("买菜", "工作", new DateOnly(2026, 10, 15));
        var finished = new[] { new FinishedTaskRef(new DateOnly(2026, 10, 16), "  买菜  ", "生活") };

        var pending = CarryOverOffer.StillUnfinished(new[] { older, newer, otherCategory }, finished);

        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, item => item.Date == newer.Date && item.Category == "生活");
        Assert.Contains(pending, item => item.Category == "工作");
        Assert.DoesNotContain(pending, item => item.Date == older.Date && item.Category == "生活");
    }

    [Fact]
    public void October20_DoesNotReviveOctober15_AfterTheyWereCheckedOnOctober16()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var source = new DateOnly(2026, 10, 15);
        var checkedOn = new DateOnly(2026, 10, 16);
        var laterWrite = new DateOnly(2026, 10, 19);
        var today = new DateOnly(2026, 10, 20);
        var tasks = new[]
        {
            new DayTask { Text = "买菜", Category = "生活" },
            new DayTask { Text = "洗碗", Category = "生活" },
            new DayTask { Text = "写周报", Category = "工作" },
            new DayTask { Text = "回邮件", Category = "工作" }
        };

        DailyNoteStore.Write(settings, source, tasks);
        DailyNoteStore.Write(settings, checkedOn, tasks.Select(task =>
        {
            var copy = task.Copy();
            copy.IsDone = true;
            return copy;
        }).ToArray());
        DailyNoteStore.Write(settings, laterWrite, new[]
        {
            new DayTask { Text = "别的事", Category = "生活", IsDone = true },
            new DayTask { Text = "还开着", Category = "工作" }
        });

        var open = OverdueScan.CollectOpenBefore(settings, today);
        Assert.DoesNotContain(open, item => item.Date == source);
        var only = Assert.Single(open);
        Assert.Equal("还开着", only.Text);
        Assert.Equal(laterWrite, only.Date);
        Assert.Equal("昨天未完成 1 项", CarryOverOffer.BannerTitle(today, open.Select(item => item.Date).ToList()));
        var pending = CarryOverOffer.Pending(open, Array.Empty<TodayTaskRef>(), new CarryOverMemory(), today);
        Assert.Equal("还开着", Assert.Single(pending).Text);

        var stillOpen = Open("还没做", "生活", source);
        var dismissed = CarryOverOffer.MarkHandled(new CarryOverMemory(), today, new[] { stillOpen });
        Assert.Empty(CarryOverOffer.Pending(new[] { stillOpen }, Array.Empty<TodayTaskRef>(), dismissed, today));
        Assert.Single(CarryOverOffer.Pending(
            new[] { stillOpen },
            Array.Empty<TodayTaskRef>(),
            dismissed,
            today.AddDays(1)));
    }

    [Fact]
    public void StillUnfinished_TextMatchIgnoresAttachments_AndImageOnlyUsesFileNames()
    {
        var source = new DateOnly(2026, 10, 15);
        var finishedOn = new DateOnly(2026, 10, 16);
        var withFile = Open("买菜", "生活", source) with
        {
            Attachments = new[]
            {
                new TaskAttachment
                {
                    RelativePath = "DayList-attachments/2026-10-15/a.png",
                    DisplayName = "a.png",
                    IsImage = true
                }
            }
        };
        Assert.Empty(CarryOverOffer.StillUnfinished(
            new[] { withFile },
            new[] { new FinishedTaskRef(finishedOn, "买菜", "生活") }));

        var image = ImageOnly("only.png");
        var other = ImageOnly("other.png");
        var sameKey = OverdueIdentity.AttachmentKey(image.Attachments);
        Assert.Empty(CarryOverOffer.StillUnfinished(
            new[] { image },
            new[] { new FinishedTaskRef(finishedOn, "", "", sameKey) }));
        Assert.Single(CarryOverOffer.StillUnfinished(
            new[] { image },
            new[] { new FinishedTaskRef(finishedOn, "", "", OverdueIdentity.AttachmentKey(other.Attachments)) }));
        Assert.Single(CarryOverOffer.StillUnfinished(
            new[] { image },
            new[] { new FinishedTaskRef(finishedOn, "only.png", "") }));

        var otherCategory = ImageOnly("only.png", "工作");
        Assert.Single(CarryOverOffer.StillUnfinished(
            new[] { otherCategory },
            new[] { new FinishedTaskRef(finishedOn, "", "", sameKey) }));

        Assert.True(CarryOverOffer.AlreadyOnToday(
            withFile,
            new[] { new TodayTaskRef("买菜", "生活") }));
        Assert.True(CarryOverOffer.AlreadyOnToday(
            image,
            new[] { new TodayTaskRef("", "", sameKey) }));
        Assert.False(CarryOverOffer.AlreadyOnToday(
            image,
            new[] { new TodayTaskRef("only.png", "") }));

        var pending = CarryOverOffer.Pending(new[] { image }, Array.Empty<TodayTaskRef>(), new CarryOverMemory(), Today);
        Assert.Equal("only.png", OverdueIdentity.Label(Assert.Single(pending)));
        var dismissed = CarryOverOffer.MarkHandled(new CarryOverMemory(), Today, pending);
        Assert.Empty(CarryOverOffer.Pending(pending, Array.Empty<TodayTaskRef>(), dismissed, Today));
    }

    [Fact]
    public void StillUnfinished_DropsAnEditedCopyCheckedOffOnALaterDay()
    {
        var source = new DateOnly(2026, 10, 20);
        var finishedOn = new DateOnly(2026, 10, 21);

        var open = new[]
        {
            Open("会话知识库接口返回具体的session名称，可参考会话列表名称的获取方式", "工作", source),
            Open("批量删除历史会话（http://localhost:3001/api/agent-platform/workspace/files?agent_id=149&session_id=cf9017ff-e9c4-49d8-9faf-d8b290b7c043&path=output%2F_part4.md）支持传多个agent_id和session_id？", "工作", source)
        };
        var finished = new[]
        {
            new FinishedTaskRef(finishedOn, "会话知识库接口返回具体的session名称，可参考会话列表名称的获取方式（贾老师那边得新增接口）", "工作"),
            new FinishedTaskRef(finishedOn, "批量删除历史会话文件（http://localhost:3001/api/agent-platform/workspace/files?agent_id=149&session_id=cf9017ff-e9c4-49d8-9faf-d8b290b7c043&path=output%2F_part4.md）是否支持传多个agent_id和session_id？", "工作")
        };

        Assert.Empty(CarryOverOffer.StillUnfinished(open, finished));
    }

    [Fact]
    public void StillUnfinished_KeepsUnrelatedShortTasks()
    {
        var source = new DateOnly(2026, 10, 20);
        var finishedOn = new DateOnly(2026, 10, 21);

        var open = new[] { Open("写周报", "工作", source) };
        var finished = new[] { new FinishedTaskRef(finishedOn, "写日报", "工作") };

        Assert.Single(CarryOverOffer.StillUnfinished(open, finished));
    }

    private static OverdueTaskItem ImageOnly(string file, string category = "") =>
        new(Yesterday, "", category, "", '-', 'x')
        {
            Attachments = new[]
            {
                new TaskAttachment
                {
                    RelativePath = "DayList-attachments/2026-10-15/" + file,
                    DisplayName = file,
                    IsImage = true,
                    OriginalToken = "![[DayList-attachments/2026-10-15/" + file + "]]"
                }
            }
        };

    [Fact]
    public void BannerTitle_SaysYesterdayOnlyWhenEveryItemIsThePreviousDay()
    {
        var today = new DateOnly(2026, 10, 20);
        var yesterday = today.AddDays(-1);

        Assert.Equal("昨天未完成", CarryOverOffer.BannerTitle(today, Array.Empty<DateOnly>()));
        Assert.Equal(
            "昨天未完成 2 项",
            CarryOverOffer.BannerTitle(today, new[] { yesterday, yesterday }));
        Assert.Equal(
            "更早未完成 4 项",
            CarryOverOffer.BannerTitle(today, new[]
            {
                new DateOnly(2026, 10, 15),
                new DateOnly(2026, 10, 15),
                new DateOnly(2026, 10, 15),
                new DateOnly(2026, 10, 15)
            }));
        Assert.Equal(
            "更早未完成 2 项",
            CarryOverOffer.BannerTitle(today, new[] { yesterday, new DateOnly(2026, 10, 15) }));
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
    public void Completion_LastOpenTask_IsTheDay_CategoryStaysSeparate()
    {
        var only = CompletionFeedback.Detect(
            new[] { new TaskDoneSnapshot("工作", false) },
            new[] { new TaskDoneSnapshot("工作", true) });
        Assert.True(only.DayJustCompleted);
        Assert.Null(only.CategoryJustCompleted);
        Assert.True(only.Any);

        var category = CompletionFeedback.Detect(
            new[]
            {
                new TaskDoneSnapshot("工作", false),
                new TaskDoneSnapshot("生活", false)
            },
            new[]
            {
                new TaskDoneSnapshot("工作", true),
                new TaskDoneSnapshot("生活", false)
            });
        Assert.Equal("工作", category.CategoryJustCompleted);
        Assert.False(category.DayJustCompleted);
        Assert.True(category.Any);

        Assert.False(CompletionFeedback.Detect([], []).Any);
        Assert.False(CompletionFeedback.Detect(
            new[] { new TaskDoneSnapshot("工作", false) },
            new[] { new TaskDoneSnapshot("工作", false) }).Any);
    }

    [Fact]
    public void SyncStatus_UsesHumanRelativeTime()
    {
        var now = new DateTimeOffset(2026, 10, 16, 15, 0, 0, TimeSpan.FromHours(8));
        Assert.Equal("已连接笔记库", SyncStatusText.Format(SyncPhase.Idle, null, now));
        Assert.Equal("写入未成功", SyncStatusText.Format(SyncPhase.Error, now, now));
        Assert.Equal(
            "已写入笔记库 · 刚刚",
            SyncStatusText.Format(SyncPhase.Saved, now.AddSeconds(-10), now));
        Assert.Equal(
            "已写入笔记库 · 1 分钟前",
            SyncStatusText.Format(SyncPhase.Saved, now.AddSeconds(-45), now));
        Assert.Equal(
            "已写入笔记库 · 2 分钟前",
            SyncStatusText.Format(SyncPhase.Saved, now.AddMinutes(-2), now));
        Assert.Equal(
            "已写入笔记库 · 14:00",
            SyncStatusText.Format(SyncPhase.Saved, now.AddHours(-1), now));
        Assert.Equal(
            "已写入笔记库 · 10月15日 15:00",
            SyncStatusText.Format(SyncPhase.Saved, now.AddDays(-1), now));
    }
}
