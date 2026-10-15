using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DayList.Core;

public sealed record PlanTaskDraft(DateOnly Date, string Category, string Text);

public enum PlanGenerateStatus
{
    Ok,
    NotConfigured,
    EmptyInput,
    Failed
}

public sealed record PlanGenerateResult(
    PlanGenerateStatus Status,
    string Message,
    IReadOnlyList<PlanTaskDraft> Tasks)
{
    public static PlanGenerateResult NotConfigured() =>
        new(PlanGenerateStatus.NotConfigured, "还没配置 AI 接口。打开设置填写 Base URL、API Key 和模型后，才能用自然语言生成计划。", Array.Empty<PlanTaskDraft>());

    public static PlanGenerateResult EmptyInput() =>
        new(PlanGenerateStatus.EmptyInput, "先写几句想做的事，再生成计划。", Array.Empty<PlanTaskDraft>());

    public static PlanGenerateResult Failed(string message) =>
        new(PlanGenerateStatus.Failed, message, Array.Empty<PlanTaskDraft>());

    public static PlanGenerateResult Ok(IReadOnlyList<PlanTaskDraft> tasks) =>
        new(PlanGenerateStatus.Ok, $"已解析出 {tasks.Count} 条任务。", tasks);
}

/// <summary>Parse and validate AI JSON for natural-language plans (unit-testable without HTTP).</summary>
public static class PlanDraftParser
{
    private static readonly Regex DateIso = new(
        @"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<PlanTaskDraft> Parse(string? raw, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<PlanTaskDraft>();

        var text = ChatProtocol.StripFences(raw);
        // Prefer JSON array; tolerate object wrapper { "tasks": [...] }
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind == JsonValueKind.Object
                 && root.TryGetProperty("tasks", out var tasks)
                 && tasks.ValueKind == JsonValueKind.Array)
        {
            array = tasks;
        }
        else
        {
            throw new FormatException("模型返回的不是任务列表 JSON。");
        }

        var list = new List<PlanTaskDraft>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var title = ReadString(item, "text")
                ?? ReadString(item, "title")
                ?? ReadString(item, "task");
            if (string.IsNullOrWhiteSpace(title))
                continue;

            title = title.Trim();
            if (title.Length == 0)
                continue;

            var category = DayTaskCategory.Normalize(
                ReadString(item, "category") ?? ReadString(item, "标签") ?? "");

            var date = today;
            var dateRaw = ReadString(item, "date") ?? ReadString(item, "day");
            if (!string.IsNullOrWhiteSpace(dateRaw))
                date = ParseDate(dateRaw.Trim(), today);

            list.Add(new PlanTaskDraft(date, category, title));
        }

        return list;
    }

    public static DateOnly ParseDate(string raw, DateOnly today)
    {
        var s = raw.Trim();
        if (s is "今天" or "今日" or "today" or "Today")
            return today;
        if (s is "明天" or "明日" or "tomorrow" or "Tomorrow")
            return today.AddDays(1);
        if (s is "后天")
            return today.AddDays(2);
        if (s is "昨天" or "昨日")
            return today.AddDays(-1);

        var iso = DateIso.Match(s);
        if (iso.Success)
        {
            var y = int.Parse(iso.Groups["y"].Value, CultureInfo.InvariantCulture);
            var m = int.Parse(iso.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(iso.Groups["d"].Value, CultureInfo.InvariantCulture);
            return new DateOnly(y, m, d);
        }

        // M/d or M月d日 relative to today's year
        var md = Regex.Match(s, @"^(?<m>\d{1,2})[/-月](?<d>\d{1,2})日?$");
        if (md.Success)
        {
            var m = int.Parse(md.Groups["m"].Value, CultureInfo.InvariantCulture);
            var d = int.Parse(md.Groups["d"].Value, CultureInfo.InvariantCulture);
            return new DateOnly(today.Year, m, d);
        }

        if (DateOnly.TryParse(s, CultureInfo.GetCultureInfo("zh-CN"), DateTimeStyles.None, out var parsed))
            return parsed;
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return parsed;

        throw new FormatException("无法识别日期：" + raw);
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }
}

public static class PlanDraftApply
{
    /// <summary>Append drafts into daily notes (grouped by date). Returns how many tasks were added.</summary>
    public static int Apply(AppSettings settings, IEnumerable<PlanTaskDraft> drafts)
    {
        var added = 0;
        foreach (var group in drafts.GroupBy(d => d.Date).OrderBy(g => g.Key))
        {
            var snapshot = DailyNoteStore.Read(settings, group.Key);
            var tasks = snapshot.Tasks.Select(t => t.Copy()).ToList();
            foreach (var draft in group)
            {
                tasks.Add(new DayTask
                {
                    Text = draft.Text,
                    Category = draft.Category,
                    IsDone = false
                });
                added++;
            }

            DailyNoteStore.Write(settings, group.Key, tasks);
        }

        return added;
    }
}

public sealed class PlanGenerationService
{
    public static string BuildSystemPrompt(DateOnly today, IEnumerable<string> knownCategories)
    {
        var cats = knownCategories
            .Select(DayTaskCategory.Normalize)
            .Where(c => !DayTaskCategory.IsUncategorized(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.CurrentCulture)
            .ToList();
        if (cats.Count == 0)
            cats.AddRange(new[] { "工作", "学习", "生活" });

        var catList = string.Join("、", cats);
        var example = """{"date":"yyyy-MM-dd","category":"分类名","text":"任务标题"}""";
        return
            "你是待办助手。用户会用中文描述接下来要做的事。请只输出 JSON 数组（不要 Markdown 围栏、不要开场白），元素形如：\n"
            + example
            + "\n规则：\n"
            + $"- 今天是 {today:yyyy-MM-dd}。相对词：今天/今日→当天，明天→+1天，后天→+2天。\n"
            + "- 未写日期时用今天。\n"
            + $"- category 优先从这些里选：{catList}；都不合适再自拟短中文分类；无法归类用空字符串。\n"
            + "- text 要短、可执行，去掉「然后」「另外」等口语。\n"
            + "- 一条事项对应一个对象；不要编造用户没说的事。";
    }

    private readonly HttpClient _http;

    public PlanGenerationService(HttpClient http) => _http = http;

    public async Task<PlanGenerateResult> GenerateAsync(
        AppSettings settings,
        string userText,
        DateOnly today,
        IEnumerable<string> knownCategories,
        CancellationToken cancellationToken)
    {
        settings.Normalize();
        if (!settings.IsApiConfigured)
            return PlanGenerateResult.NotConfigured();

        var input = (userText ?? "").Trim();
        if (input.Length == 0)
            return PlanGenerateResult.EmptyInput();

        try
        {
            var system = BuildSystemPrompt(today, knownCategories);
            using var request = new HttpRequestMessage(HttpMethod.Post, ChatProtocol.Endpoint(settings.ApiBaseUrl));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            request.Headers.TryAddWithoutValidation("User-Agent", "DayList");
            request.Content = new StringContent(
                ChatProtocol.BuildRequest(settings.ApiModel, system, input),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = ChatProtocol.ReadError(body) ?? $"接口返回 {(int)response.StatusCode}。";
                return PlanGenerateResult.Failed(error);
            }

            var content = ChatProtocol.ReadAssistantContent(body);
            if (string.IsNullOrWhiteSpace(content))
                return PlanGenerateResult.Failed("接口没有返回计划内容。");

            IReadOnlyList<PlanTaskDraft> tasks;
            try
            {
                tasks = PlanDraftParser.Parse(content, today);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
            {
                return PlanGenerateResult.Failed("没法解析模型返回的计划：" + ex.Message);
            }

            if (tasks.Count == 0)
                return PlanGenerateResult.Failed("模型没有给出可写入的任务。");

            return PlanGenerateResult.Ok(tasks);
        }
        catch (OperationCanceledException)
        {
            return PlanGenerateResult.Failed("请求超时或已取消。");
        }
        catch (HttpRequestException ex)
        {
            return PlanGenerateResult.Failed("无法连接接口：" + ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return PlanGenerateResult.Failed(ex.Message);
        }
    }
}
