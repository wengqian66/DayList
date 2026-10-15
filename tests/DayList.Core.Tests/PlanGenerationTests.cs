using System.Net;
using System.Text;
using DayList.Core;

namespace DayList.Core.Tests;

public class PlanGenerationTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public void Parse_ReadsArrayAndRelativeDates()
    {
        var json =
            """
            [
              {"date":"今天","category":"工作","text":"写周报"},
              {"date":"明天","category":"生活","text":"健身30分钟"},
              {"date":"2026-10-18","category":"","text":"买牛奶"}
            ]
            """;
        var tasks = PlanDraftParser.Parse(json, Today);
        Assert.Equal(3, tasks.Count);
        Assert.Equal(Today, tasks[0].Date);
        Assert.Equal("工作", tasks[0].Category);
        Assert.Equal("写周报", tasks[0].Text);
        Assert.Equal(Today.AddDays(1), tasks[1].Date);
        Assert.Equal("生活", tasks[1].Category);
        Assert.Equal(new DateOnly(2026, 10, 18), tasks[2].Date);
        Assert.Equal(DayTaskCategory.UncategorizedKey, tasks[2].Category);
    }

    [Fact]
    public void Parse_AcceptsFencedTasksWrapper()
    {
        var raw =
            """
            ```json
            {"tasks":[{"date":"后天","category":"学习","text":"读论文"}]}
            ```
            """;
        var tasks = PlanDraftParser.Parse(raw, Today);
        Assert.Single(tasks);
        Assert.Equal(Today.AddDays(2), tasks[0].Date);
        Assert.Equal("学习", tasks[0].Category);
    }

    [Fact]
    public void ParseDate_HandlesMdAndChinese()
    {
        Assert.Equal(Today, PlanDraftParser.ParseDate("今日", Today));
        Assert.Equal(new DateOnly(2026, 10, 20), PlanDraftParser.ParseDate("10月20日", Today));
        Assert.Equal(new DateOnly(2026, 11, 1), PlanDraftParser.ParseDate("11/1", Today));
    }

    [Fact]
    public void BuildSystemPrompt_IncludesTodayAndCategories()
    {
        var prompt = PlanGenerationService.BuildSystemPrompt(Today, new[] { "工作", "生活" });
        Assert.Contains("2026-10-15", prompt);
        Assert.Contains("工作", prompt);
        Assert.Contains("生活", prompt);
        Assert.Contains("JSON", prompt);
    }

    [Fact]
    public async Task Generate_ParsesModelJson_DoesNotLeakKeyIntoMessage()
    {
        var settings = new AppSettings
        {
            VaultPath = Path.GetTempPath(),
            ApiBaseUrl = "http://127.0.0.1:9/v1",
            ApiKey = "secret-plan-key",
            ApiModel = "local-model"
        };
        var handler = new StubHandler(
            """{"choices":[{"message":{"content":"[{\"date\":\"明天\",\"category\":\"工作\",\"text\":\"改设置页\"}]"}}]}""");
        var service = new PlanGenerationService(new HttpClient(handler));
        var result = await service.GenerateAsync(
            settings,
            "明天把设置页改完",
            Today,
            new[] { "工作" },
            CancellationToken.None);

        Assert.Equal(PlanGenerateStatus.Ok, result.Status);
        Assert.Single(result.Tasks);
        Assert.Equal(Today.AddDays(1), result.Tasks[0].Date);
        Assert.Equal("改设置页", result.Tasks[0].Text);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-plan-key", handler.AuthorizationParameter);
        Assert.DoesNotContain("secret-plan-key", result.Message);
        Assert.Contains("设置页", handler.Body);
        Assert.Contains("local-model", handler.Body);
    }

    [Fact]
    public async Task Generate_NotConfigured_WhenMissingKey()
    {
        var settings = new AppSettings { ApiBaseUrl = "https://api.openai.com/v1", ApiKey = "", ApiModel = "x" };
        var service = new PlanGenerationService(new HttpClient());
        var result = await service.GenerateAsync(settings, "写周报", Today, Array.Empty<string>(), CancellationToken.None);
        Assert.Equal(PlanGenerateStatus.NotConfigured, result.Status);
    }

    [Fact]
    public void Apply_AppendsIntoDailyNotesByDate()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var drafts = new[]
        {
            new PlanTaskDraft(Today, "工作", "写周报"),
            new PlanTaskDraft(Today.AddDays(1), "生活", "健身"),
            new PlanTaskDraft(Today, "工作", "开会")
        };
        var added = PlanDraftApply.Apply(settings, drafts);
        Assert.Equal(3, added);

        var todayNote = DailyNoteStore.Read(settings, Today);
        Assert.Equal(2, todayNote.Tasks.Count);
        Assert.Contains(todayNote.Tasks, t => t.Text == "写周报" && t.Category == "工作");
        Assert.Contains(todayNote.Tasks, t => t.Text == "开会");

        var tomorrow = DailyNoteStore.Read(settings, Today.AddDays(1));
        Assert.Single(tomorrow.Tasks);
        Assert.Equal("健身", tomorrow.Tasks[0].Text);
        Assert.Contains("### 生活", File.ReadAllText(tomorrow.FullPath));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _status;

        public StubHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        {
            _response = response;
            _status = status;
        }

        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
