namespace DayList.Core;

public sealed class DayTemplate
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Blurb { get; init; }

    public required IReadOnlyList<DayTemplateTask> Tasks { get; init; }

    public IReadOnlyList<DayTask> CreateTasks() =>
        Tasks.Select(task => new DayTask
        {
            Text = task.Text,
            Category = DayTaskCategory.Normalize(task.Category),
            IsDone = false
        }).ToArray();
}

public readonly record struct DayTemplateTask(string Text, string Category);

/// <summary>
/// One-tap starters for an empty daily note. These are real tasks in the DayList section.
/// </summary>
public static class DayTemplates
{
    public static IReadOnlyList<DayTemplate> Starters { get; } = new DayTemplate[]
    {
        new()
        {
            Id = "morning",
            Title = "晨间三件事",
            Blurb = "先定今天最值得做的三件",
            Tasks = new DayTemplateTask[]
            {
                new("今天最重要的一件事", "工作"),
                new("接着推进的一件事", "工作"),
                new("照顾自己的一件小事", "生活")
            }
        },
        new()
        {
            Id = "shutdown",
            Title = "下班清扫",
            Blurb = "收尾、记下明天、再离开",
            Tasks = new DayTemplateTask[]
            {
                new("把收件箱清到能离开", "工作"),
                new("写下明天的第一件事", "工作"),
                new("合上今天没收尾的杂事", "生活")
            }
        },
        new()
        {
            Id = "weekly",
            Title = "周复盘",
            Blurb = "看这一周，再排下一周",
            Tasks = new DayTemplateTask[]
            {
                new("这周真正完成了什么", "工作"),
                new("哪里卡住了", "工作"),
                new("下周最值得推进的三件事", "工作")
            }
        }
    };

    public static DayTemplate? Find(string? id) =>
        Starters.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.Ordinal));
}
