using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DayList.Core;

public enum MonthlyReportStatus
{
    Saved,
    NotConfigured,
    EmptyMonth,
    Failed
}

public sealed record MonthlyReportResult(
    MonthlyReportStatus Status,
    string Message,
    string? FullPath,
    string? RelativePath)
{
    public static MonthlyReportResult NotConfigured() =>
        new(MonthlyReportStatus.NotConfigured, "还没有配置 OpenAI 兼容接口。", null, null);

    public static MonthlyReportResult EmptyMonth() =>
        new(MonthlyReportStatus.EmptyMonth, "这个月的每日笔记里还没有 DayList 内容。", null, null);

    public static MonthlyReportResult Failed(string message) =>
        new(MonthlyReportStatus.Failed, message, null, null);

    public static MonthlyReportResult Saved(string fullPath, string relativePath) =>
        new(MonthlyReportStatus.Saved, "已写入 " + relativePath, fullPath, relativePath);
}

public sealed class MonthlyReportService
{
    public const string SystemPrompt =
        """
        你是用户的月报编辑。下面是某个月每日笔记里「DayList」小节的原文。请用简体中文写一份 Markdown 月报，只根据这些内容，不要编造没出现的事项。
        结构：
        ## 完成了什么
        ## 还没收尾
        ## 一个月的样子
        语气平静、具体、简短。不要用代码围栏，不要写开场白。
        """;

    private readonly HttpClient _http;

    public MonthlyReportService(HttpClient http) => _http = http;

    public async Task<MonthlyReportResult> GenerateAsync(AppSettings settings, DateOnly dayInMonth, CancellationToken cancellationToken)
    {
        settings.Normalize();
        if (!settings.IsApiConfigured)
            return MonthlyReportResult.NotConfigured();

        try
        {
            var digest = await Task.Run(() => MonthDigest.Build(settings, dayInMonth), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(digest))
                return MonthlyReportResult.EmptyMonth();

            using var request = new HttpRequestMessage(HttpMethod.Post, ChatProtocol.Endpoint(settings.ApiBaseUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.TryAddWithoutValidation("User-Agent", "DayList");
            request.Content = new StringContent(
                ChatProtocol.BuildRequest(settings.ApiModel, SystemPrompt, digest),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = ChatProtocol.ReadError(body) ?? $"接口返回 {(int)response.StatusCode}。";
                return MonthlyReportResult.Failed(error);
            }

            var content = ChatProtocol.ReadAssistantContent(body);
            if (string.IsNullOrWhiteSpace(content))
                return MonthlyReportResult.Failed("接口没有返回月报正文。");

            var (fullPath, relative) = VaultLayout.Report(settings, dayInMonth);
            var markdown = MonthDigest.WrapReport(dayInMonth, content, DateTimeOffset.Now);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temp = fullPath + ".daylist.tmp";
            try
            {
                await File.WriteAllTextAsync(temp, markdown, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                File.Move(temp, fullPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch (IOException)
                    {
                        // The replace already consumed the temp file.
                    }
                }
            }

            return MonthlyReportResult.Saved(fullPath, relative);
        }
        catch (OperationCanceledException)
        {
            return MonthlyReportResult.Failed("请求超时或已取消。");
        }
        catch (HttpRequestException ex)
        {
            return MonthlyReportResult.Failed("无法连接接口：" + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return MonthlyReportResult.Failed(ex.Message);
        }
    }
}
