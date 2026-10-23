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
        var sourceDay = new DateOnly(2026, 10, 20);
        var finishedOn = new DateOnly(2026, 10, 21);
        var session = Open("会话知识库接口返回具体的session名称，可参考会话列表名称的获取方式", "工作", sourceDay);
        var batch = Open("批量删除历史会话（http://localhost:3001/api/agent-platform/workspace/files?agent_id=149&session_id=cf9017ff-e9c4-49d8-9faf-d8b290b7c043&path=output%2F_part4.md）支持传多个agent_id和session_id？", "工作", sourceDay);
        var open = new[] { session, batch };

        var (sessionId, sessionLink) = CarryLineage.Stamp(session, finishedOn);
        var (batchId, batchLink) = CarryLineage.Stamp(batch, finishedOn);
        var links = new CarryLinkTable();
        links.Remember(sessionLink);
        links.Remember(batchLink);

        var finished = new[]
        {
            new FinishedTaskRef(finishedOn, "会话知识库接口返回具体的session名称，可参考会话列表名称的获取方式（贾老师那边得新增接口）", "工作", "", sessionId),
            new FinishedTaskRef(finishedOn, "批量删除历史会话文件（http://localhost:3001/api/agent-platform/workspace/files?agent_id=149&session_id=cf9017ff-e9c4-49d8-9faf-d8b290b7c043&path=output%2F_part4.md）是否支持传多个agent_id和session_id？", "工作", "", batchId)
        };

        Assert.Equal(2, CarryOverOffer.StillUnfinished(open, finished).Count);
        Assert.Empty(CarryOverOffer.StillUnfinished(open, finished, links));
    }

    [Fact]
    public void StillUnfinished_KeepsUnrelatedShortTasks()
    {
        var source = new DateOnly(2026, 10, 15);
        var finishedOn = new DateOnly(2026, 10, 16);
        var open = new[]
        {
            Open("买", "生活", source),
            Open("写周报", "工作", source),
            Open("整理会议室", "工作", source),
            Open("买两盒牛奶", "生活", source),
            Open("回邮件", "工作", source)
        };
        var finished = new[]
        {
            new FinishedTaskRef(finishedOn, "卖", "生活"),
            new FinishedTaskRef(finishedOn, "写日报", "工作"),
            new FinishedTaskRef(finishedOn, "整理会议", "工作"),
            new FinishedTaskRef(finishedOn, "买两盒牛乃", "生活"),
            new FinishedTaskRef(finishedOn, "回复邮件", "工作")
        };

        var pending = CarryOverOffer.StillUnfinished(open, finished);

        Assert.Equal(5, pending.Count);
        Assert.Empty(CarryOverOffer.StillUnfinished(
            new[] { Open("买", "生活", source) },
            new[] { new FinishedTaskRef(finishedOn, "买", "生活") }));
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
    public void StillUnfinished_LineageDoesNotRetireADifferentDayOrCategory()
    {
        var sourceDay = new DateOnly(2026, 10, 15);
        var earlierDay = new DateOnly(2026, 10, 14);
        var finishedOn = new DateOnly(2026, 10, 16);
        var source = Open("买菜", "生活", sourceDay);
        var earlierSameWords = Open("买菜", "生活", earlierDay);
        var otherCategory = Open("买菜", "工作", sourceDay);
        var unrelated = Open("洗碗", "生活", sourceDay);
        var open = new[] { source, earlierSameWords, otherCategory, unrelated };

        var (carryId, link) = CarryLineage.Stamp(source, finishedOn);
        var finished = new[]
        {
            new FinishedTaskRef(finishedOn, "去超市买菜", "生活", "", carryId)
        };

        Assert.Equal(4, CarryOverOffer.StillUnfinished(open, finished).Count);

        var links = new CarryLinkTable();
        links.Remember(link);
        var pending = CarryOverOffer.StillUnfinished(open, finished, links);

        Assert.DoesNotContain(pending, item => item.Date == sourceDay && item.Category == "生活" && item.Text == "买菜");
        Assert.Contains(pending, item => item.Date == earlierDay && item.Text == "买菜");
        Assert.Contains(pending, item => item.Category == "工作" && item.Text == "买菜");
        Assert.Contains(pending, item => item.Text == "洗碗");
    }

    [Fact]
    public void StillUnfinished_AttachmentOnly_RetiresThroughCarryId()
    {
        var finishedOn = new DateOnly(2026, 10, 16);
        var image = ImageOnly("only.png");
        var other = ImageOnly("other.png");
        var (carryId, link) = CarryLineage.Stamp(image, finishedOn);
        var links = new CarryLinkTable();
        links.Remember(link);

        var brought = new DayTask
        {
            Text = "补充说明",
            Category = "",
            IsDone = true,
            CarryId = carryId,
            Attachments = image.Attachments.Select(item => item.Copy()).ToList()
        };
        var line = brought.ToMarkdown();
        Assert.EndsWith("^" + carryId, line);
        Assert.True(DayTask.TryParseLine(line, out var parsed));
        Assert.Equal("补充说明", parsed.Text);
        Assert.Equal(carryId, parsed.CarryId);
        Assert.DoesNotContain("^" + carryId, parsed.Text);
        Assert.Single(parsed.Attachments);

        var finished = new FinishedTaskRef(
            finishedOn,
            parsed.Text,
            "",
            OverdueIdentity.AttachmentKey(parsed.Attachments),
            parsed.CarryId);
        Assert.Empty(CarryOverOffer.StillUnfinished(new[] { image }, new[] { finished }, links));
        Assert.Single(CarryOverOffer.StillUnfinished(new[] { other }, new[] { finished }, links));
        Assert.Single(CarryOverOffer.StillUnfinished(new[] { image }, new[] { finished }));

        var today = new TodayTaskRef("补充说明", "", OverdueIdentity.AttachmentKey(parsed.Attachments), carryId);
        Assert.True(CarryOverOffer.AlreadyOnToday(image, new[] { today }, links));
        Assert.False(CarryOverOffer.AlreadyOnToday(image, new[] { today }));
    }

    [Fact]
    public void UndoBring_RemovesTheLink_AndAnEditedCheckOffNoLongerRetiresTheSource()
    {
        var source = Open("买菜", "生活", new DateOnly(2026, 10, 15));
        var broughtTo = new DateOnly(2026, 10, 16);
        var (carryId, link) = CarryLineage.Stamp(source, broughtTo);
        var links = new CarryLinkTable();
        links.Remember(link);

        var edited = new TodayTaskRef("去超市买菜", "生活", "", carryId);
        Assert.True(CarryOverOffer.AlreadyOnToday(source, new[] { edited }, links));
        Assert.Empty(CarryOverOffer.Pending(
            new[] { source },
            new[] { edited },
            new CarryOverMemory(),
            broughtTo,
            links));

        var finished = new[] { new FinishedTaskRef(broughtTo, "去超市买菜", "生活", "", carryId) };
        Assert.Empty(CarryOverOffer.StillUnfinished(new[] { source }, finished, links));

        Assert.True(links.Remove(carryId));
        Assert.False(CarryOverOffer.AlreadyOnToday(source, new[] { edited }, links));
        Assert.Single(CarryOverOffer.StillUnfinished(new[] { source }, finished, links));
        Assert.Single(CarryOverOffer.Pending(
            new[] { source },
            new[] { edited },
            new CarryOverMemory(),
            broughtTo,
            links));
    }

    [Fact]
    public void CarryMarker_StaysOnTheMarkdownLine_AndOutOfTheTitle()
    {
        var task = new DayTask
        {
            Text = "买菜",
            Category = "生活",
            CarryId = "dl-abc12345",
            Attachments = new List<TaskAttachment>
            {
                new()
                {
                    RelativePath = "DayList-attachments/2026-10-16/a.png",
                    DisplayName = "a.png",
                    IsImage = true
                }
            }
        };

        const string line = "- [ ] 买菜 ![[DayList-attachments/2026-10-16/a.png]] ^dl-abc12345";
        Assert.Equal(line, task.ToMarkdown());
        Assert.True(DayTask.TryParseLine(line, out var parsed));
        Assert.Equal("买菜", parsed.Text);
        Assert.Equal("dl-abc12345", parsed.CarryId);
        Assert.Equal(line, parsed.ToMarkdown());
        Assert.Equal("买菜", OverdueIdentity.IdentityText(parsed.Text, parsed.Attachments));
        Assert.Equal(
            "买菜",
            OverdueIdentity.Label(new OverdueTaskItem(Yesterday, "买菜 ^dl-abc12345", "生活", "", '-', 'x')));

        task.Text = "去超市买菜";
        task.IsDone = true;
        Assert.Equal(
            "- [x] 去超市买菜 ![[DayList-attachments/2026-10-16/a.png]] ^dl-abc12345",
            task.ToMarkdown());

        const string userBlock = "- [ ] 见导师 ^todo";
        Assert.True(DayTask.TryParseLine(userBlock, out var user));
        Assert.Equal("见导师 ^todo", user.Text);
        Assert.Equal("", user.CarryId);
        Assert.Equal(userBlock, user.ToMarkdown());

        var without = task.Copy();
        without.CarryId = "";
        Assert.False(DayTask.SameList(new[] { task }, new[] { without }));
    }

    [Fact]
    public void CarryLinkStore_RoundTripsBesideCarryOver()
    {
        Assert.Equal(
            Path.GetDirectoryName(CarryOverStore.DefaultPath),
            Path.GetDirectoryName(CarryLinkStore.DefaultPath));
        Assert.Equal("carry-links.json", Path.GetFileName(CarryLinkStore.DefaultPath));

        var path = Path.Combine(Path.GetTempPath(), "daylist-links-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var source = Open("买菜", "生活");
            var (carryId, link) = CarryLineage.Stamp(source, Today);
            var table = new CarryLinkTable();
            table.Remember(link);
            CarryLinkStore.Save(table, path);

            var json = File.ReadAllText(path);
            Assert.Contains("\"carryId\"", json);
            Assert.Contains("买菜", json);
            Assert.DoesNotContain("\\u", json);

            var loaded = CarryLinkStore.Load(path);
            var found = loaded.Find(carryId);
            Assert.NotNull(found);
            Assert.True(found!.PointsAt(source));
            Assert.True(loaded.Remove(carryId));
            CarryLinkStore.Save(loaded, path);
            Assert.Null(CarryLinkStore.Load(path).Find(carryId));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void CollectOpenBefore_RetiresSource_WhenBroughtCopyWasEditedThenChecked()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var sourceDay = new DateOnly(2026, 10, 15);
        var brought = new DateOnly(2026, 10, 16);
        var today = new DateOnly(2026, 10, 17);
        DailyNoteStore.Write(settings, sourceDay, new[]
        {
            new DayTask { Text = "买菜", Category = "生活" },
            new DayTask { Text = "写信", Category = "生活" }
        });

        var source = Open("买菜", "生活", sourceDay);
        var (carryId, link) = CarryLineage.Stamp(source, brought);
        var links = new CarryLinkTable();
        links.Remember(link);

        var written = DailyNoteStore.Write(settings, brought, new[]
        {
            new DayTask
            {
                Text = "去超市买菜",
                Category = "生活",
                IsDone = true,
                CarryId = carryId
            }
        });
        Assert.Contains("^" + carryId, File.ReadAllText(written.FullPath));

        var readBack = DailyNoteStore.Read(settings, brought);
        Assert.Equal("去超市买菜", Assert.Single(readBack.Tasks).Text);
        Assert.Equal(carryId, readBack.Tasks[0].CarryId);

        var open = OverdueScan.CollectOpenBefore(settings, today, links: links);
        Assert.DoesNotContain(open, item => item.Text == "买菜");
        Assert.Equal("写信", Assert.Single(open).Text);

        var withoutLinks = OverdueScan.CollectOpenBefore(settings, today);
        Assert.Contains(withoutLinks, item => item.Text == "买菜");
        Assert.Contains(withoutLinks, item => item.Text == "写信");
    }

    [Fact]
    public void Pending_ShowsOneRow_WhenTheSameTaskIsOpenOnSeveralEarlierDays()
    {
        // 「带入今日」 leaves the source line unchecked, so after copying on consecutive
        // days the same title sits open on 10/21, 10/22 and 10/23. The banner must offer it
        // once (the newest copy), not once per source day.
        var sourceDay = new DateOnly(2026, 10, 21);
        var copiedDay = new DateOnly(2026, 10, 22);
        var today = new DateOnly(2026, 10, 23);

        var source = Open("买菜", "生活", sourceDay);
        var (_, link) = CarryLineage.Stamp(source, copiedDay);
        var links = new CarryLinkTable();
        links.Remember(link);

        var open = new[]
        {
            source,
            Open("买菜", "生活", copiedDay),
            Open("写周报", "工作", copiedDay)
        };

        var pending = CarryOverOffer.Pending(open, Array.Empty<TodayTaskRef>(), new CarryOverMemory(), today, links);

        Assert.Equal(2, pending.Count);
        Assert.Equal(copiedDay, pending[0].Date);
        Assert.Equal("买菜", pending[0].Text);
        Assert.Equal("写周报", pending[1].Text);
    }

    [Fact]
    public void Pending_KeepsTasksThatOnlyShareATitleAcrossCategories()
    {
        var sourceDay = new DateOnly(2026, 10, 21);
        var copiedDay = new DateOnly(2026, 10, 22);
        var today = new DateOnly(2026, 10, 23);

        var open = new[]
        {
            Open("整理", "生活", sourceDay),
            Open("整理", "工作", copiedDay)
        };

        var pending = CarryOverOffer.Pending(open, Array.Empty<TodayTaskRef>(), new CarryOverMemory(), today, null);

        Assert.Equal(2, pending.Count);
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
