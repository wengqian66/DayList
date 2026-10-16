using DayList.Core;

namespace DayList.Core.Tests;

public class QuickAddAndReorderTests
{
    private static readonly DateOnly Friday = new(2026, 10, 16);

    [Fact]
    public void Parse_LiftsTomorrowTimeAndCategory()
    {
        var parsed = QuickAddPhrase.Parse("明天 9 点 写周报 #工作", Friday, new[] { "工作", "生活" });

        Assert.True(parsed.Recognized);
        Assert.Equal("写周报", parsed.Title);
        Assert.Equal(new DateOnly(2026, 10, 17), parsed.Date);
        Assert.Equal(new TimeOnly(9, 0), parsed.Reminder);
        Assert.Equal("工作", parsed.Category);
    }

    [Fact]
    public void Parse_PlainTitleWhenNothingMatches()
    {
        var parsed = QuickAddPhrase.Parse("  写周报  ", Friday);

        Assert.False(parsed.Recognized);
        Assert.Equal("写周报", parsed.Title);
        Assert.Null(parsed.Date);
        Assert.Null(parsed.Reminder);
        Assert.Null(parsed.Category);
    }

    [Fact]
    public void Parse_FallsBackWhenTheLineWouldHaveNoTitle()
    {
        var parsed = QuickAddPhrase.Parse("明天", Friday);

        Assert.False(parsed.Recognized);
        Assert.Equal("明天", parsed.Title);
    }

    [Fact]
    public void Parse_AfternoonHalfHourAndGluedTag()
    {
        var half = QuickAddPhrase.Parse("下午 3 点半 开会", Friday);
        Assert.Equal("开会", half.Title);
        Assert.Equal(new TimeOnly(15, 30), half.Reminder);
        Assert.Null(half.Date);

        var glued = QuickAddPhrase.Parse("写周报#工作", Friday);
        Assert.Equal("写周报", glued.Title);
        Assert.Equal("工作", glued.Category);
        Assert.Null(glued.Reminder);
    }

    [Fact]
    public void Parse_DoesNotEatCSharp()
    {
        var parsed = QuickAddPhrase.Parse("复习 C# 笔记", Friday);

        Assert.False(parsed.Recognized);
        Assert.Equal("复习 C# 笔记", parsed.Title);
    }

    [Fact]
    public void Parse_RelativeWeekdaysAndEveningShortcut()
    {
        var next = QuickAddPhrase.Parse("下周一 交报告", Friday);
        Assert.Equal(new DateOnly(2026, 10, 19), next.Date);
        Assert.Equal("交报告", next.Title);

        var tonight = QuickAddPhrase.Parse("今晚8点看电影", Friday);
        Assert.Equal(Friday, tonight.Date);
        Assert.Equal(new TimeOnly(20, 0), tonight.Reminder);
        Assert.Equal("看电影", tonight.Title);

        var morning = QuickAddPhrase.Parse("明早9点写周报", Friday);
        Assert.Equal(new DateOnly(2026, 10, 17), morning.Date);
        Assert.Equal(new TimeOnly(9, 0), morning.Reminder);
        Assert.Equal("写周报", morning.Title);
    }

    [Fact]
    public void Parse_MonthDayRollsForwardAndColonTime()
    {
        var future = QuickAddPhrase.Parse("10月20日 14:30 体检", Friday);
        Assert.Equal(new DateOnly(2026, 10, 20), future.Date);
        Assert.Equal(new TimeOnly(14, 30), future.Reminder);
        Assert.Equal("体检", future.Title);

        var past = QuickAddPhrase.Parse("10月1日 买礼物", Friday);
        Assert.Equal(new DateOnly(2027, 10, 1), past.Date);
        Assert.Equal("买礼物", past.Title);
    }

    [Theory]
    [InlineData(0, 2, new[] { "b", "a", "c" })]
    [InlineData(2, 0, new[] { "c", "a", "b" })]
    [InlineData(1, 1, new[] { "a", "b", "c" })]
    public void MoveBefore_ReordersFlatItems(int from, int before, string[] expected)
    {
        var tasks = Texts("a", "b", "c");
        var moved = TaskReorder.MoveBefore(tasks, from, before);

        Assert.Equal(expected, moved.Tasks.Select(task => task.Text).ToArray());
        Assert.Equal(expected.Length, moved.SourceIndex.Count);
    }

    [Fact]
    public void MoveBefore_SameCardKeepsOtherCategoriesInPlace()
    {
        var tasks = new[]
        {
            Task("a", "工作"),
            Task("b", "生活"),
            Task("c", "工作")
        };

        var moved = TaskReorder.MoveBefore(tasks, 2, 0, "工作");

        Assert.Equal(new[] { "c", "b", "a" }, moved.Tasks.Select(task => task.Text).ToArray());
        Assert.Equal(new[] { "工作", "生活", "工作" }, moved.Tasks.Select(task => task.Category).ToArray());
        Assert.Equal(new[] { 2, 1, 0 }, moved.SourceIndex.ToArray());
    }

    [Fact]
    public void MoveBefore_CrossCategoryInsertsAheadOfTheTarget()
    {
        var tasks = new[]
        {
            Task("a", "工作"),
            Task("b", "生活"),
            Task("c", "工作")
        };

        var moved = TaskReorder.MoveBefore(tasks, 1, 2, "工作");

        Assert.Equal(new[] { "a", "b", "c" }, moved.Tasks.Select(task => task.Text).ToArray());
        Assert.All(moved.Tasks, task => Assert.Equal("工作", task.Category));
    }

    [Fact]
    public void MoveToCategoryEnd_AppendsWithoutPullingTheCardForward()
    {
        var tasks = new[]
        {
            Task("a", "工作"),
            Task("b", "生活"),
            Task("c", "工作")
        };

        var within = TaskReorder.MoveToCategoryEnd(tasks, 0, "工作");
        Assert.Equal(new[] { "c", "b", "a" }, within.Tasks.Select(task => task.Text).ToArray());
        Assert.Equal("生活", within.Tasks[1].Category);

        var across = TaskReorder.MoveToCategoryEnd(tasks, 1, "工作");
        Assert.Equal(new[] { "a", "c", "b" }, across.Tasks.Select(task => task.Text).ToArray());
        Assert.Equal("工作", across.Tasks[2].Category);
    }

    [Fact]
    public void Reorder_RoundTripsCategoryOrderInMarkdown()
    {
        var tasks = new[]
        {
            Task("收集", "工作"),
            Task("买牛奶", "生活"),
            Task("写周报", "工作")
        };
        var moved = TaskReorder.MoveBefore(tasks, 2, 0, "工作");
        var rendered = DailyNoteDocument.Parse("", "## DayList").Render(moved.Tasks);

        Assert.Contains("### 工作\n- [ ] 写周报\n- [ ] 收集", rendered.Replace("\r\n", "\n"));
        Assert.Contains("### 生活\n- [ ] 买牛奶", rendered.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ExtractInspiration_LeavesTheTaskOutOfTheMonthDigest()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var day = new DateOnly(2026, 10, 16);
        var tasks = new[]
        {
            Task("写周报", "工作"),
            Task("忽然想学陶艺", "生活")
        };

        var (remaining, entry) = TaskReorder.ExtractInspiration(tasks, 1, "09:15");
        DailyNoteStore.Write(settings, day, remaining);
        DailyNoteStore.WriteInspirations(settings, day, new[] { entry });

        var snapshot = DailyNoteStore.Read(settings, day);
        Assert.Equal("写周报", Assert.Single(snapshot.Tasks).Text);
        Assert.Equal("忽然想学陶艺", Assert.Single(snapshot.Inspirations).Text);
        Assert.Equal("09:15", snapshot.Inspirations[0].TimeLabel);

        var digest = MonthDigest.Build(settings, day);
        Assert.Contains("写周报", digest);
        Assert.DoesNotContain("忽然想学陶艺", digest);
        Assert.DoesNotContain("灵感", digest);
    }

    [Fact]
    public void MoveToOtherDay_AppendsOnTheTargetAndDropsTheSource()
    {
        var source = new[] { Task("今天的", "工作"), Task("要挪走", "生活") };
        var target = new[] { Task("已有", "生活") };

        var move = TaskReorder.MoveToOtherDay(source, 1, target);

        Assert.Equal("今天的", Assert.Single(move.Source).Text);
        Assert.Equal(new[] { "已有", "要挪走" }, move.Target.Select(task => task.Text).ToArray());
        Assert.Equal("生活", move.Target[1].Category);
    }

    [Fact]
    public void Corner_PlacesTheFirstCardBottomRightAndStacksUpward()
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var first = ReminderCorner.Place(work, 360, 180, 0);
        var second = ReminderCorner.Place(work, 360, 180, 1);

        Assert.Equal(1920 - 360 - ReminderCorner.Margin, first.X);
        Assert.Equal(1080 - 180 - ReminderCorner.Margin, first.Y);
        Assert.Equal(first.Y - 180 - ReminderCorner.StackGap, second.Y);
        Assert.Equal(first.X, second.X);
    }

    [Fact]
    public void Corner_ClampsWhenTheCardIsTallerThanTheWorkArea()
    {
        var work = new PixelRect(10, 20, 400, 300);
        var placed = ReminderCorner.Place(work, 400, 500, 3);

        Assert.Equal(20, placed.Y);
        Assert.Equal(10, placed.X);
    }

    private static DayTask[] Texts(params string[] titles) =>
        titles.Select(title => Task(title, "")).ToArray();

    private static DayTask Task(string text, string category) => new()
    {
        Text = text,
        Category = category
    };
}
