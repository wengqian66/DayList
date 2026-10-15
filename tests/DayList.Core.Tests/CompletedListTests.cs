using DayList.Core;

namespace DayList.Core.Tests;

public class CompletedListTests
{
    [Fact]
    public void Completed_groups_one_card_and_each_done_task_once()
    {
        // Same shape as 2026-10-15.md: six tasks, two of them done, both under 生活.
        var rows = new[]
        {
            Row("生活", done: true, "PegLine右键菜单会被图片挡住、超过最大上限后是否有替换逻辑？"),
            Row("生活", done: true, "开发DayList"),
            Row("工作", done: false, "未完成一"),
            Row("工作", done: false, "未完成二"),
            Row("生活", done: false, "未完成三"),
            Row("生活", done: false, "未完成四"),
        };

        var groups = CategoryGrouping.Group(rows.Where(row => row.Done), row => row.Category);

        var card = Assert.Single(groups);
        Assert.Equal("生活", card.Key);
        Assert.Equal(
            new[]
            {
                "PegLine右键菜单会被图片挡住、超过最大上限后是否有替换逻辑？",
                "开发DayList",
            },
            card.Items.Select(item => item.Text).ToArray());
    }

    [Fact]
    public void Opening_completed_does_not_rebuild_once_per_already_done_task()
    {
        // Navigation rebuilds the card list once. Each done checkbox then raises
        // Checked as x:Bind materializes it. Counting those as edits used to
        // rebuild again per done task (2 done → 3 paints of the same card).
        const int doneTasks = 2;
        var rebuilds = 1;
        for (var i = 0; i < doneTasks; i++)
        {
            if (TaskCheckInteraction.DoneFlagChanged(wasDone: true, isChecked: true))
                rebuilds++;
        }

        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public void Unchecking_a_completed_task_still_counts_as_an_edit()
    {
        Assert.True(TaskCheckInteraction.DoneFlagChanged(wasDone: true, isChecked: false));
        Assert.True(TaskCheckInteraction.DoneFlagChanged(wasDone: false, isChecked: true));
        Assert.False(TaskCheckInteraction.DoneFlagChanged(wasDone: false, isChecked: false));
    }

    [Fact]
    public void Uncategorized_group_stays_first_without_duplicating_rows()
    {
        var rows = new[]
        {
            Row("生活", true, "a"),
            Row("", true, "loose"),
            Row("生活", true, "b"),
        };

        var groups = CategoryGrouping.Group(rows, row => row.Category);

        Assert.Equal(new[] { "", "生活" }, groups.Select(group => group.Key).ToArray());
        Assert.Equal(new[] { "loose" }, groups[0].Items.Select(item => item.Text).ToArray());
        Assert.Equal(new[] { "a", "b" }, groups[1].Items.Select(item => item.Text).ToArray());
    }

    private static (string Category, bool Done, string Text) Row(string category, bool done, string text) =>
        (category, done, text);
}
