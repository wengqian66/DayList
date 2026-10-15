using System.Net;
using System.Text;
using DayList.Core;

namespace DayList.Core.Tests;

public class InspirationTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void ReadAndRender_KeepsDayListAndPlainBullets()
    {
        const string original = """
            # 2026-10-15

            ## DayList

            - [ ] 买牛奶
            - [x] 回邮件

            ## 灵感

            - 14:05 给妈妈打电话
            * 9:05 原样保留

            ## 日志

            尾巴
            """;

        var document = DailyNoteDocument.Parse(original, "## DayList");
        Assert.Equal(new[] { "买牛奶", "回邮件" }, document.Tasks.Select(task => task.Text));
        Assert.DoesNotContain("打电话", document.SectionBody());

        var entries = InspirationSection.Read(original, "## 灵感");
        Assert.Equal(2, entries.Count);
        Assert.Equal("14:05", entries[0].TimeLabel);
        Assert.Equal("给妈妈打电话", entries[0].Text);
        Assert.Equal("9:05", entries[1].TimeLabel);
        Assert.Equal("原样保留", entries[1].Text);

        Assert.Equal(original, document.Render(document.Tasks));
        Assert.Equal(original, InspirationSection.Render(original, "## 灵感", entries));
    }

    [Fact]
    public void GlanceText_JoinsExtraLinesForTheTodayCard()
    {
        var entry = new InspirationEntry
        {
            TimeLabel = "14:05",
            Text = "给妈妈打电话\n  第二行\n"
        };
        Assert.Equal("给妈妈打电话 第二行", entry.GlanceText());
        Assert.Equal("", new InspirationEntry { Text = " \n\t " }.GlanceText());
    }

    [Fact]
    public void CheckboxAndFences_AreNotInspirations()
    {
        const string original = """
            ## 灵感

            - [ ] 这不是灵感
            - [x] 完成了也不算

            ```
            - 围栏里的假灵感
            ```

            - 真的灵感
              第二行
            """;

        var entries = InspirationSection.Read(original, "## 灵感");
        var entry = Assert.Single(entries);
        Assert.Equal("真的灵感\n第二行", entry.Text);
        Assert.Null(entry.TimeLabel);

        var rendered = InspirationSection.Render(original, "## 灵感", entries);
        Assert.Contains("- [ ] 这不是灵感", rendered);
        Assert.Contains("围栏里的假灵感", rendered);
        Assert.Contains("  第二行", rendered);
        Assert.DoesNotContain("- [ ] 真的灵感", rendered);
    }

    [Fact]
    public void Write_AppendsSectionAndLeavesTasksUntouched()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        DailyNoteStore.Write(settings, Day, new[] { new DayTask { Text = "买牛奶", Category = "生活" } });

        var written = DailyNoteStore.WriteInspirations(settings, Day, new[]
        {
            new InspirationEntry { TimeLabel = "08:30", Text = "忽然想听那张旧唱片" }
        });

        Assert.True(written.Wrote);
        var text = File.ReadAllText(written.FullPath);
        Assert.Contains("### 生活", text);
        Assert.Contains("- [ ] 买牛奶", text);
        Assert.Contains("## 灵感", text);
        Assert.Contains("- 08:30 忽然想听那张旧唱片", text);

        var snapshot = DailyNoteStore.Read(settings, Day);
        Assert.Equal("买牛奶", Assert.Single(snapshot.Tasks).Text);
        Assert.Equal("忽然想听那张旧唱片", Assert.Single(snapshot.Inspirations).Text);

        var cleared = DailyNoteStore.WriteInspirations(settings, Day, Array.Empty<InspirationEntry>());
        Assert.True(cleared.Wrote);
        var after = File.ReadAllText(cleared.FullPath);
        Assert.DoesNotContain("灵感", after);
        Assert.Contains("- [ ] 买牛奶", after);
        Assert.Empty(DailyNoteStore.Read(settings, Day).Inspirations);
    }

    [Fact]
    public void Edit_PreservesSurroundingProseAndCrlf()
    {
        const string original = "## 灵感\r\n\r\n先留一句\r\n\r\n- 10:00 旧的\r\n\r\n后记\r\n";
        var entries = InspirationSection.Read(original, "## 灵感").Select(entry => new InspirationEntry
        {
            TimeLabel = entry.TimeLabel,
            Text = "改过",
            RawLine = entry.RawLine
        }).ToArray();

        var rendered = InspirationSection.Render(original, "## 灵感", entries);
        Assert.Equal("## 灵感\r\n\r\n先留一句\r\n\r\n- 10:00 改过\r\n\r\n后记\r\n", rendered);
    }

    [Fact]
    public void SameHeading_RefusesToOverwriteTheTaskSection()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        settings.InspirationHeading = "## DayList";
        settings.Normalize();
        DailyNoteStore.Write(settings, Day, new[] { new DayTask { Text = "别覆盖" } });

        var error = Assert.Throws<InvalidOperationException>(() =>
            DailyNoteStore.WriteInspirations(settings, Day, new[]
            {
                new InspirationEntry { Text = "不行" }
            }));
        Assert.Contains("同一个标题", error.Message);
        Assert.Equal("## DayList\n\n- [ ] 别覆盖\n", File.ReadAllText(VaultLayout.Daily(settings, Day).FullPath));
        Assert.Empty(DailyNoteStore.Read(settings, Day).Inspirations);
    }

    [Fact]
    public void MonthDigest_IgnoresInspirationEvenWhenNested()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        Write(settings, Day, """
            ## DayList

            - [ ] 写周报

            ## 灵感

            - 14:00 不想进月报
            """);

        var digest = MonthDigest.Build(settings, Day);
        Assert.Contains("写周报", digest);
        Assert.DoesNotContain("不想进月报", digest);
        Assert.DoesNotContain("灵感", digest);

        settings.SectionHeading = "# 日记";
        settings.Normalize();
        Write(settings, new DateOnly(2026, 10, 2), """
            # 日记

            - [x] 开工

            ## 灵感

            - 私密想法

            ## 其它

            还在小节里
            """);

        var nested = MonthDigest.Build(settings, Day);
        Assert.Contains("开工", nested);
        Assert.Contains("还在小节里", nested);
        Assert.DoesNotContain("私密想法", nested);
    }

    [Fact]
    public async Task MonthlyReport_DoesNotSendInspirationToTheModel()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        settings.ApiBaseUrl = "http://127.0.0.1:9/v1";
        settings.ApiKey = "secret-key";
        settings.ApiModel = "local-model";
        Write(settings, Day, """
            ## DayList

            - [ ] 只这一条该进月报

            ## 灵感

            - 22:01 月报里不该出现的念头
            """);

        var handler = new StubHandler("""{"choices":[{"message":{"content":"## 完成了什么\n还在写。"}}]}""");
        var service = new MonthlyReportService(new HttpClient(handler));
        var result = await service.GenerateAsync(settings, Day, CancellationToken.None);

        Assert.Equal(MonthlyReportStatus.Saved, result.Status);
        Assert.Contains("只这一条该进月报", handler.Body);
        Assert.DoesNotContain("月报里不该出现的念头", handler.Body);
        Assert.DoesNotContain("## 灵感", handler.Body);
    }

    [Fact]
    public void PlanApply_DoesNotTouchInspiration()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        Write(settings, Day, """
            ## DayList

            - [ ] 原任务

            ## 灵感

            - 07:15 保持不动
            """);

        var added = PlanDraftApply.Apply(settings, new[]
        {
            new PlanTaskDraft(Day, "生活", "散步")
        });

        Assert.Equal(1, added);
        var text = File.ReadAllText(VaultLayout.Daily(settings, Day).FullPath);
        Assert.Contains("- [ ] 原任务", text);
        Assert.Contains("- [ ] 散步", text);
        Assert.Contains("## 灵感", text);
        Assert.Contains("- 07:15 保持不动", text);
        Assert.Equal("保持不动", Assert.Single(DailyNoteStore.Read(settings, Day).Inspirations).Text);
    }

    [Fact]
    public void Settings_NormalizesInspirationHeading()
    {
        var settings = new AppSettings();
        settings.Normalize();
        Assert.Equal("## 灵感", settings.InspirationHeading);
        Assert.True(settings.ReminderSound);
        Assert.False(settings.ReminderToastOnly);

        settings.InspirationHeading = "火花";
        settings.Normalize();
        Assert.Equal("## 火花", settings.InspirationHeading);
        Assert.False(InspirationSection.SameHeading(settings.SectionHeading, settings.InspirationHeading));
    }

    [Fact]
    public void Write_CanCreateANoteThatOnlyHasInspiration()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var written = DailyNoteStore.WriteInspirations(settings, Day, new[]
        {
            new InspirationEntry { TimeLabel = "21:04", Text = "只是一句" }
        });

        Assert.True(written.Wrote);
        Assert.Equal("## 灵感\n\n- 21:04 只是一句\n", File.ReadAllText(written.FullPath));
        var snapshot = DailyNoteStore.Read(settings, Day);
        Assert.Empty(snapshot.Tasks);
        Assert.Equal("只是一句", Assert.Single(snapshot.Inspirations).Text);
    }

    [Fact]
    public void EmptyCapture_DoesNotCreateAFile()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var result = DailyNoteStore.WriteInspirations(settings, Day, Array.Empty<InspirationEntry>());
        Assert.False(result.Wrote);
        Assert.False(File.Exists(result.FullPath));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;

        public StubHandler(string response) => _response = response;

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }

    private static void Write(AppSettings settings, DateOnly day, string content)
    {
        var (fullPath, _) = VaultLayout.Daily(settings, day);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
