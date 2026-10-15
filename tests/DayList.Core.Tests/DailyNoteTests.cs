using DayList.Core;

namespace DayList.Core.Tests;

public class DailyNoteTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void RoundTrip_PreservesSurroundingMarkdownAndOddTaskSyntax()
    {
        const string original = "# 2026-10-15\n\n## DayList\n\n- [ ] 买牛奶\n- [X]  回邮件\n\n## 日志\n\n随便写\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");

        Assert.Equal(2, document.Tasks.Count);
        Assert.False(document.Tasks[0].IsDone);
        Assert.True(document.Tasks[1].IsDone);
        Assert.Equal('X', document.Tasks[1].DoneMark);
        Assert.Equal(original, document.Render(document.Tasks));
        Assert.DoesNotContain("随便", document.SectionBody());
        Assert.Contains("买牛奶", document.SectionBody());
    }

    [Fact]
    public void EditToggleDeleteAndAdd_KeepsIntroAndRewritesTasks()
    {
        const string original = "## DayList\n\n先记一句\n\n- [ ] a\n\n夹在中间\n\n- [X] b\n\n## 下一节\n\n尾巴\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");
        var tasks = document.Tasks.Select(task => task.Copy()).ToList();
        tasks[0].IsDone = true;
        tasks[1].Text = "b2";
        tasks.RemoveAt(0);
        tasks.Add(new DayTask { Text = "散步" });

        var rendered = document.Render(tasks);

        Assert.Equal(
            "## DayList\n\n先记一句\n\n- [X] b2\n- [ ] 散步\n\n## 下一节\n\n尾巴\n",
            rendered);
    }

    [Fact]
    public void Categories_ParseAndRewriteAsSubheadings()
    {
        const string original = "## DayList\n\n### 工作\n- [ ] 周报\n### 生活\n- [ ] 买菜\n- [x] 洗碗\n\n## Next\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");

        Assert.Equal(3, document.Tasks.Count);
        Assert.Equal("工作", document.Tasks[0].Category);
        Assert.Equal("生活", document.Tasks[1].Category);
        Assert.Equal("生活", document.Tasks[2].Category);

        var tasks = document.Tasks.Select(task => task.Copy()).ToList();
        tasks.Add(new DayTask { Text = "散装", Category = "" });
        tasks.Add(new DayTask { Text = "新需求", Category = "工作" });

        var rendered = document.Render(tasks);
        Assert.Equal(
            "## DayList\n\n- [ ] 散装\n\n### 工作\n- [ ] 周报\n- [ ] 新需求\n\n### 生活\n- [ ] 买菜\n- [x] 洗碗\n\n## Next\n",
            rendered);
    }

    [Fact]
    public void FlatTasks_StayUncategorizedWithoutHeaders()
    {
        const string original = "## DayList\n\n- [ ] a\n- [ ] b\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");
        Assert.All(document.Tasks, task => Assert.Equal("", task.Category));
        Assert.Equal(original, document.Render(document.Tasks));
    }

    [Fact]
    public void FencedBlock_DoesNotEndTheSectionOrBecomeTasks()
    {
        const string original = "## DayList\n\n```\n## Other\n- [ ] fake\n```\n\n- [ ] real\n\n## Next\n\ntail\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");

        Assert.Equal(new[] { "real" }, document.Tasks.Select(task => task.Text));
        Assert.Contains("fake", document.SectionBody());
        Assert.DoesNotContain("tail", document.SectionBody());
        Assert.Equal(original, document.Render(document.Tasks));
    }

    [Fact]
    public void DeeperHeading_BecomesCategory()
    {
        const string original = "## DayList\n\n### 细则\n\n- [ ] inside\n\n## End\n\nout\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");

        var task = Assert.Single(document.Tasks);
        Assert.Equal("inside", task.Text);
        Assert.Equal("细则", task.Category);
        Assert.Contains("### 细则", document.SectionBody());
        Assert.DoesNotContain("out", document.SectionBody());
        Assert.Equal("## DayList\n\n### 细则\n- [ ] inside\n\n## End\n\nout\n", document.Render(document.Tasks));
    }

    [Fact]
    public void MissingSection_IsAppendedWithoutTouchingExistingText()
    {
        var document = DailyNoteDocument.Parse("# Title\n\n正文\n", "DayList");
        var rendered = document.Render(new[] { new DayTask { Text = "买牛奶" } });

        Assert.False(document.SectionFound);
        Assert.Equal("# Title\n\n正文\n\n## DayList\n\n- [ ] 买牛奶\n", rendered);
        Assert.Equal("# Title\n\n正文\n", document.Render(Array.Empty<DayTask>()));
    }

    [Fact]
    public void EmptyFile_BecomesASection()
    {
        var rendered = DailyNoteDocument.Parse("", "### Todos").Render(new[] { new DayTask { Text = "a" } });
        Assert.Equal("### Todos\n\n- [ ] a\n", rendered);
    }

    [Fact]
    public void CrlfAndStarBullet_RoundTrip()
    {
        const string original = "## DayList\r\n\r\n* [x] done\r\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");

        Assert.Equal('*', document.Tasks[0].Bullet);
        Assert.True(document.Tasks[0].IsDone);
        Assert.Equal(original, document.Render(document.Tasks));
    }

    [Fact]
    public void HeadingWithoutSpace_IsNotASection()
    {
        const string original = "##DayList\n- [ ] a\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");
        Assert.False(document.SectionFound);
        Assert.Empty(document.Tasks);
    }

    [Fact]
    public void Store_CreatesUtf8FileAndSkipsIdenticalWrite()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var created = DailyNoteStore.Write(settings, Day, new[] { new DayTask { Text = "买牛奶 ??" } });

        Assert.True(created.Wrote);
        Assert.Equal("2026-10-15.md", created.RelativePath);
        Assert.False(File.Exists(created.FullPath + ".daylist.tmp"));
        var bytes = File.ReadAllBytes(created.FullPath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("## DayList\n\n- [ ] 买牛奶 ??\n", File.ReadAllText(created.FullPath));

        var snapshot = DailyNoteStore.Read(settings, Day);
        var again = DailyNoteStore.Write(settings, Day, snapshot.Tasks);
        Assert.False(again.Wrote);
        Assert.Equal(bytes, File.ReadAllBytes(created.FullPath));
    }

    [Fact]
    public void Store_PreservesBomAndProseWhenToggling()
    {
        using var vault = new TempVault();
        var settings = vault.Settings(folder: "Daily", pattern: "yyyy-MM-dd");
        var (fullPath, relative) = VaultLayout.Daily(settings, Day);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var text = "前言\r\n\r\n## DayList\r\n\r\n- [ ] a\r\n\r\n后记\r\n";
        var body = new System.Text.UTF8Encoding(false).GetBytes(text);
        var bytes = new byte[body.Length + 3];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);
        File.WriteAllBytes(fullPath, bytes);

        var snapshot = DailyNoteStore.Read(settings, Day);
        Assert.Equal("Daily/2026-10-15.md", relative);
        snapshot.Tasks[0].IsDone = true;
        var written = DailyNoteStore.Write(settings, Day, snapshot.Tasks);
        var roundTrip = File.ReadAllBytes(written.FullPath);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, roundTrip[..3]);
        Assert.Equal("前言\r\n\r\n## DayList\r\n\r\n- [x] a\r\n\r\n后记\r\n", System.Text.Encoding.UTF8.GetString(roundTrip[3..]));
    }

    [Fact]
    public void Store_DoesNotCreateAFileForAnEmptyDay()
    {
        using var vault = new TempVault();
        var result = DailyNoteStore.Write(vault.Settings(), Day, Array.Empty<DayTask>());
        Assert.False(result.Wrote);
        Assert.False(File.Exists(result.FullPath));
        var snapshot = DailyNoteStore.Read(vault.Settings(), Day);
        Assert.False(snapshot.FileExists);
        Assert.Empty(snapshot.Tasks);
    }

    [Fact]
    public void Store_CreatesNestedFoldersFromThePattern()
    {
        using var vault = new TempVault();
        var settings = vault.Settings(pattern: "Daily/YYYY/MM/YYYY-MM-DD");
        var result = DailyNoteStore.Write(settings, Day, new[] { new DayTask { Text = "a" } });
        Assert.Equal("Daily/2026/10/2026-10-15.md", result.RelativePath);
        Assert.True(File.Exists(result.FullPath));
    }

    [Fact]
    public void Store_WritesCategoryHeadings()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var result = DailyNoteStore.Write(settings, Day, new[]
        {
            new DayTask { Text = "散装" },
            new DayTask { Text = "开会", Category = "工作" },
        });
        Assert.Equal("## DayList\n\n- [ ] 散装\n\n### 工作\n- [ ] 开会\n", File.ReadAllText(result.FullPath));
        var snapshot = DailyNoteStore.Read(settings, Day);
        Assert.Equal("", snapshot.Tasks[0].Category);
        Assert.Equal("工作", snapshot.Tasks[1].Category);
    }

    [Fact]
    public void TryRenameSectionTitle_RewritesHeadingKeepsBody()
    {
        var text = "# Journal\n\n## DayList\n\n- [ ] one\n\n## Other\n";
        var (changed, next) = DailyNoteDocument.TryRenameSectionTitle(text, "## DayList", "待办");
        Assert.True(changed);
        Assert.Contains("## 待办", next);
        Assert.DoesNotContain("## DayList", next);
        Assert.Contains("- [ ] one", next);
        Assert.Contains("## Other", next);

        var doc = DailyNoteDocument.Parse(next, "## 待办");
        Assert.True(doc.SectionFound);
        Assert.Single(doc.Tasks);
    }

    [Fact]
    public void TryRenameSectionTitle_NoOpWhenMissing()
    {
        var text = "## Other\n\n- [ ] x\n";
        var (changed, next) = DailyNoteDocument.TryRenameSectionTitle(text, "## DayList", "待办");
        Assert.False(changed);
        Assert.Equal(text, next);
    }
}
