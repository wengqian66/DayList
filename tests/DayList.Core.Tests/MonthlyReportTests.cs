using System.Net;
using System.Text;
using DayList.Core;

namespace DayList.Core.Tests;

public class MonthlyReportTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void Endpoint_JoinsChatCompletions()
    {
        Assert.Equal(
            "https://api.openai.com/v1/chat/completions",
            ChatProtocol.Endpoint("https://api.openai.com/v1").AbsoluteUri);
        Assert.Equal(
            "https://api.openai.com/v1/chat/completions",
            ChatProtocol.Endpoint("https://api.openai.com/v1/").AbsoluteUri);
        Assert.Equal(
            "http://127.0.0.1:11434/v1/chat/completions",
            ChatProtocol.Endpoint("http://127.0.0.1:11434/v1/chat/completions").AbsoluteUri);
    }

    [Fact]
    public void ResponseParser_ReadsStringArrayAndError()
    {
        Assert.Equal("你好", ChatProtocol.ReadAssistantContent("""{"choices":[{"message":{"content":"你好"}}]}"""));
        Assert.Equal(
            "甲乙",
            ChatProtocol.ReadAssistantContent("""{"choices":[{"message":{"content":[{"type":"text","text":"甲"},{"text":"乙"}]}}]}"""));
        Assert.Null(ChatProtocol.ReadAssistantContent("not-json"));
        Assert.Equal("nope", ChatProtocol.ReadError("""{"error":{"message":"nope"}}"""));
        Assert.Equal("正文", ChatProtocol.StripFences("```markdown\n正文\n```"));
    }

    [Fact]
    public async Task Generate_WritesMarkdownAndDoesNotLeakTheKey()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        settings.ApiBaseUrl = "http://127.0.0.1:9/v1";
        settings.ApiKey = "secret-key";
        settings.ApiModel = "local-model";
        var (note, _) = VaultLayout.Daily(settings, new DateOnly(2026, 10, 2));
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        await File.WriteAllTextAsync(note, "## DayList\n\n- [ ] 写月报\n");

        var handler = new StubHandler("""{"choices":[{"message":{"content":"```markdown\n## 完成了什么\n写月报还在清单上。\n```"}}]}""");
        var service = new MonthlyReportService(new HttpClient(handler));
        var result = await service.GenerateAsync(settings, Day, CancellationToken.None);

        Assert.Equal(MonthlyReportStatus.Saved, result.Status);
        Assert.Equal("Reports/2026-10.md", result.RelativePath);
        Assert.NotNull(handler.RequestUri);
        Assert.Equal("http://127.0.0.1:9/v1/chat/completions", handler.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-key", handler.AuthorizationParameter);
        Assert.Contains("local-model", handler.Body);
        Assert.Contains("写月报", handler.Body);
        var saved = await File.ReadAllTextAsync(result.FullPath!);
        Assert.Contains("# 2026-10 月报", saved);
        Assert.Contains("写月报还在清单上。", saved);
        Assert.DoesNotContain("secret-key", saved);
        Assert.DoesNotContain("```", saved);
        Assert.False(File.Exists(result.FullPath + ".daylist.tmp"));
    }

    [Fact]
    public async Task Generate_SkipsTheNetworkWhenUnconfiguredOrEmpty()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var handler = new StubHandler("{}");
        var service = new MonthlyReportService(new HttpClient(handler));

        var missing = await service.GenerateAsync(settings, Day, CancellationToken.None);
        Assert.Equal(MonthlyReportStatus.NotConfigured, missing.Status);
        Assert.Equal(0, handler.Calls);

        settings.ApiBaseUrl = "https://example.test/v1";
        settings.ApiKey = "k";
        settings.ApiModel = "m";
        var empty = await service.GenerateAsync(settings, Day, CancellationToken.None);
        Assert.Equal(MonthlyReportStatus.EmptyMonth, empty.Status);
        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "Reports")));
    }

    [Fact]
    public async Task Generate_SurfacesApiErrors()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        settings.ApiBaseUrl = "https://example.test/v1";
        settings.ApiKey = "k";
        settings.ApiModel = "m";
        var (note, _) = VaultLayout.Daily(settings, Day);
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        await File.WriteAllTextAsync(note, "## DayList\n\n- [x] 完成\n");

        var handler = new StubHandler("""{"error":{"message":"额度用完了"}}""", HttpStatusCode.TooManyRequests);
        var result = await new MonthlyReportService(new HttpClient(handler)).GenerateAsync(settings, Day, CancellationToken.None);

        Assert.Equal(MonthlyReportStatus.Failed, result.Status);
        Assert.Equal("额度用完了", result.Message);
        Assert.False(File.Exists(Path.Combine(vault.Root, "Reports", "2026-10.md")));
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

        public int Calls { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
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
