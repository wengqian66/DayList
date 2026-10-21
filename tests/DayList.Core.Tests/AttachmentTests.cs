using DayList.Core;

namespace DayList.Core.Tests;

public class AttachmentTests
{
    private static readonly DateOnly Day = new(2026, 10, 20);

    [Fact]
    public void Parse_SplitsImageAndDocxAndLeavesOtherWikiLinks()
    {
        const string line =
            "- [ ] 买牛奶 [[某笔记]] ![[DayList-attachments/2026-10-20/photo.png|120]] [[DayList-attachments/2026-10-20/notes.docx|周报]]";
        Assert.True(DayTask.TryParseLine(line, out var task));

        Assert.Equal("买牛奶 [[某笔记]]", task.Text);
        Assert.Equal(2, task.Attachments.Count);
        Assert.True(task.Attachments[0].IsImage);
        Assert.Equal("DayList-attachments/2026-10-20/photo.png", task.Attachments[0].RelativePath);
        Assert.Equal("photo.png", task.Attachments[0].DisplayName);
        Assert.False(task.Attachments[1].IsImage);
        Assert.Equal("周报", task.Attachments[1].DisplayName);
        Assert.Equal("DayList-attachments/2026-10-20/notes.docx", task.Attachments[1].RelativePath);
        Assert.Equal(line, task.ToMarkdown());
    }

    [Fact]
    public void Parse_ReadsMarkdownLinksAndRelativeParents()
    {
        const string line =
            "- [ ] 看图 ![封面](../DayList-attachments/2026-10-20/cover%20a.webp) [说明](<DayList-attachments/2026-10-20/readme.md>)";
        Assert.True(DayTask.TryParseLine(line, out var task));

        Assert.Equal("看图", task.Text);
        Assert.Equal(2, task.Attachments.Count);
        Assert.True(task.Attachments[0].IsImage);
        Assert.Equal("DayList-attachments/2026-10-20/cover a.webp", task.Attachments[0].RelativePath);
        Assert.Equal("封面", task.Attachments[0].DisplayName);
        Assert.False(task.Attachments[1].IsImage);
        Assert.Equal("说明", task.Attachments[1].DisplayName);
        Assert.Equal("DayList-attachments/2026-10-20/readme.md", task.Attachments[1].RelativePath);
        Assert.Equal(line, task.ToMarkdown());
    }

    [Fact]
    public void Edit_RewritesTheLineButKeepsOriginalLinkTokens()
    {
        const string original =
            "## DayList\n\n- [ ] 买牛奶 ![图](DayList-attachments/2026-10-20/a.png) [[DayList-attachments/2026-10-20/b.docx]]\n\n## 日志\n\n尾巴\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");
        Assert.Equal(original, document.Render(document.Tasks));

        var tasks = document.Tasks.Select(task => task.Copy()).ToList();
        tasks[0].Text = "买面包";
        tasks[0].IsDone = true;

        var rendered = document.Render(tasks);
        Assert.Equal(
            "## DayList\n\n- [x] 买面包 ![图](DayList-attachments/2026-10-20/a.png) [[DayList-attachments/2026-10-20/b.docx]]\n\n## 日志\n\n尾巴\n",
            rendered);

        var again = DailyNoteDocument.Parse(rendered, "## DayList");
        Assert.Equal("买面包", again.Tasks[0].Text);
        Assert.Equal(2, again.Tasks[0].Attachments.Count);
        Assert.True(again.Tasks[0].Attachments[0].IsImage);
        Assert.Equal("DayList-attachments/2026-10-20/b.docx", again.Tasks[0].Attachments[1].RelativePath);
        Assert.DoesNotContain("尾巴", again.SectionBody());
    }

    [Fact]
    public void SameList_NoticesAnAttachmentAddedOutsideTheApp()
    {
        const string before = "## DayList\n\n- [ ] 买牛奶\n";
        const string after = "## DayList\n\n- [ ] 买牛奶 ![[DayList-attachments/2026-10-20/a.png]]\n";
        var left = DailyNoteDocument.Parse(before, "## DayList").Tasks;
        var right = DailyNoteDocument.Parse(after, "## DayList").Tasks;
        Assert.False(DayTask.SameList(left, right));
        Assert.Equal(before, DailyNoteDocument.Parse(before, "## DayList").Render(left));
    }

    [Fact]
    public void Import_CopiesIntoTheDatedVaultFolderAndReusesIdenticalBytes()
    {
        using var vault = new TempVault();
        var sourceDir = Path.Combine(vault.Root, "incoming");
        Directory.CreateDirectory(sourceDir);
        var png = Path.Combine(sourceDir, "Shot|1.PNG");
        var docx = Path.Combine(sourceDir, "周报.docx");
        File.WriteAllBytes(png, new byte[] { 1, 2, 3, 4 });
        File.WriteAllText(docx, "hello");

        var image = AttachmentVault.Import(vault.Root, Day, png);
        var document = AttachmentVault.Import(vault.Root, Day, docx);
        var again = AttachmentVault.Import(vault.Root, Day, png);

        Assert.Equal("DayList-attachments/2026-10-20/Shot_1.png", image.RelativePath);
        Assert.True(image.IsImage);
        Assert.Equal("![[DayList-attachments/2026-10-20/Shot_1.png]]", image.OriginalToken);
        Assert.Equal("DayList-attachments/2026-10-20/周报.docx", document.RelativePath);
        Assert.False(document.IsImage);
        Assert.Equal(image.RelativePath, again.RelativePath);
        Assert.True(File.Exists(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "Shot_1.png")));
        Assert.True(File.Exists(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "周报.docx")));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "Shot_1.png")));
    }

    [Fact]
    public void Import_RejectsOversizeAndUnsupportedFiles()
    {
        using var vault = new TempVault();
        var big = Path.Combine(vault.Root, "big.png");
        using (var stream = new FileStream(big, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength(AttachmentKinds.MaxBytes + 1);
        var exe = Path.Combine(vault.Root, "run.exe");
        File.WriteAllBytes(exe, new byte[] { 1 });

        var oversize = Assert.Throws<InvalidOperationException>(() => AttachmentVault.Import(vault.Root, Day, big));
        Assert.Equal("附件超过 20MB，没有添加。", oversize.Message);
        var blocked = Assert.Throws<InvalidOperationException>(() => AttachmentVault.Import(vault.Root, Day, exe));
        Assert.Equal("不支持这种附件（.exe）。", blocked.Message);
        Assert.False(Directory.Exists(Path.Combine(vault.Root, "DayList-attachments")));
    }

    [Fact]
    public void Import_DifferentBytesGetASuffix()
    {
        using var vault = new TempVault();
        var source = Path.Combine(vault.Root, "keep.png");
        File.WriteAllBytes(source, new byte[] { 1 });
        var imported = AttachmentVault.Import(vault.Root, Day, source);
        File.WriteAllBytes(source, new byte[] { 9 });
        var next = AttachmentVault.Import(vault.Root, Day, source);

        Assert.Equal("DayList-attachments/2026-10-20/keep.png", imported.RelativePath);
        Assert.Equal("DayList-attachments/2026-10-20/keep-2.png", next.RelativePath);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "keep.png")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "keep-2.png")));
    }

    [Fact]
    public void Store_RoundTripsAttachmentLines()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var task = new DayTask
        {
            Text = "买牛奶",
            Attachments = new List<TaskAttachment>
            {
                new()
                {
                    RelativePath = "DayList-attachments/2026-10-20/photo.png",
                    DisplayName = "photo.png",
                    IsImage = true,
                    OriginalToken = "![[DayList-attachments/2026-10-20/photo.png]]"
                },
                new()
                {
                    RelativePath = "DayList-attachments/2026-10-20/notes.docx",
                    DisplayName = "notes.docx",
                    IsImage = false,
                    OriginalToken = "[[DayList-attachments/2026-10-20/notes.docx]]"
                }
            }
        };

        DailyNoteStore.Write(settings, Day, new[] { task });
        var snapshot = DailyNoteStore.Read(settings, Day);
        var loaded = Assert.Single(snapshot.Tasks);
        Assert.Equal("买牛奶", loaded.Text);
        Assert.Equal(2, loaded.Attachments.Count);
        Assert.Equal("DayList-attachments/2026-10-20/photo.png", loaded.Attachments[0].RelativePath);
        Assert.Equal("DayList-attachments/2026-10-20/notes.docx", loaded.Attachments[1].RelativePath);
        Assert.Contains("![[DayList-attachments/2026-10-20/photo.png]]", File.ReadAllText(snapshot.FullPath));
        Assert.Contains("[[DayList-attachments/2026-10-20/notes.docx]]", File.ReadAllText(snapshot.FullPath));
    }

    [Fact]
    public void Delete_RemovesTheFileOnlyWhenNothingElseMentionsIt()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var relative = "DayList-attachments/2026-10-20/photo.png";
        var full = VaultLayout.Combine(vault.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[] { 7 });

        var note = "## DayList\n\n- [ ] 买牛奶 ![[" + relative + "]]\n\n## 日志\n\n还提到 photo.png\n";
        var (notePath, _) = VaultLayout.Daily(settings, Day);
        File.WriteAllText(notePath, note);
        Assert.False(AttachmentVault.TryDeleteIfUnreferenced(vault.Root, relative));
        Assert.True(File.Exists(full));

        File.WriteAllText(notePath, "## DayList\n\n- [ ] 买牛奶\n\n## 日志\n\n干净\n");
        var other = Path.Combine(vault.Root, "elsewhere.md");
        File.WriteAllText(other, "最短链接 ![[photo.png]]");
        Assert.False(AttachmentVault.TryDeleteIfUnreferenced(vault.Root, relative));
        Assert.True(File.Exists(full));

        File.Delete(other);
        Assert.True(AttachmentVault.TryDeleteIfUnreferenced(vault.Root, relative));
        Assert.False(File.Exists(full));
        Assert.False(Directory.Exists(Path.GetDirectoryName(full)!));
    }

    [Fact]
    public void Delete_DoesNotTouchFilesOutsideTheAttachmentFolder()
    {
        using var vault = new TempVault();
        var userFile = Path.Combine(vault.Root, "Photos", "keep.png");
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        File.WriteAllBytes(userFile, new byte[] { 1 });
        Assert.False(AttachmentVault.TryDeleteIfUnreferenced(vault.Root, "Photos/keep.png"));
        Assert.True(File.Exists(userFile));
    }

    [Fact]
    public void Delete_LeavesTheFileWhenAFolderCannotBeScanned()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var vault = new TempVault();
        var relative = "DayList-attachments/2026-10-20/photo.png";
        var full = VaultLayout.Combine(vault.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[] { 3 });

        var blocked = Path.Combine(vault.Root, "secret");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "note.md"), "no link");
        try
        {
            File.SetUnixFileMode(blocked, UnixFileMode.None);
            var readable = true;
            try
            {
                _ = Directory.EnumerateFileSystemEntries(blocked).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                readable = false;
            }

            if (readable)
                return;

            Assert.False(AttachmentVault.TryDeleteIfUnreferenced(vault.Root, relative));
            Assert.True(File.Exists(full));
        }
        finally
        {
            try
            {
                File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
                // TempVault still tries to delete the tree.
            }
        }
    }

    [Fact]
    public void ImportMany_CopiesPngAndDocxAndKeepsGoingAfterAReject()
    {
        using var vault = new TempVault();
        var incoming = Path.Combine(vault.Root, "incoming");
        Directory.CreateDirectory(incoming);
        var png = Path.Combine(incoming, "shot.png");
        var docx = Path.Combine(incoming, "notes.docx");
        var exe = Path.Combine(incoming, "run.exe");
        File.WriteAllBytes(png, new byte[] { 1, 2, 3 });
        File.WriteAllText(docx, "hello");
        File.WriteAllBytes(exe, new byte[] { 9 });

        var batch = AttachmentVault.ImportMany(vault.Root, Day, new[] { png, exe, docx });

        Assert.Equal(2, batch.Added.Count);
        Assert.True(batch.Added[0].IsImage);
        Assert.Equal("DayList-attachments/2026-10-20/shot.png", batch.Added[0].RelativePath);
        Assert.Equal("![[DayList-attachments/2026-10-20/shot.png]]", batch.Added[0].OriginalToken);
        Assert.False(batch.Added[1].IsImage);
        Assert.Equal("DayList-attachments/2026-10-20/notes.docx", batch.Added[1].RelativePath);
        Assert.Equal("[[DayList-attachments/2026-10-20/notes.docx]]", batch.Added[1].OriginalToken);
        Assert.Equal("不支持这种附件（.exe）。", batch.Error);

        var task = new DayTask { Text = "买牛奶", Attachments = batch.Added.ToList() };
        var markdown = task.ToMarkdown();
        Assert.Contains("![[DayList-attachments/2026-10-20/shot.png]]", markdown);
        Assert.Contains("[[DayList-attachments/2026-10-20/notes.docx]]", markdown);
        Assert.True(DayTask.TryParseLine(markdown, out var parsed));
        Assert.Equal("买牛奶", parsed.Text);
        Assert.Equal(2, parsed.Attachments.Count);
        Assert.True(parsed.Attachments[0].IsImage);
        Assert.False(parsed.Attachments[1].IsImage);
    }

    [Fact]
    public void ImportMany_SkipsAFolderAndAFileAlreadyOnTheTask()
    {
        using var vault = new TempVault();
        var incoming = Path.Combine(vault.Root, "incoming");
        Directory.CreateDirectory(incoming);
        var png = Path.Combine(incoming, "shot.png");
        File.WriteAllBytes(png, new byte[] { 4, 5, 6 });

        var folderOnly = AttachmentVault.ImportMany(vault.Root, Day, new[] { incoming });
        Assert.Empty(folderOnly.Added);
        Assert.Equal("只能附加文件。", folderOnly.Error);

        var first = AttachmentVault.Import(vault.Root, Day, png);
        var again = AttachmentVault.ImportMany(
            vault.Root,
            Day,
            new[] { incoming, png },
            new[] { first.RelativePath });

        Assert.Empty(again.Added);
        Assert.Equal("这个文件已经附在这条任务上。", again.Error);
        Assert.True(File.Exists(Path.Combine(vault.Root, "DayList-attachments", "2026-10-20", "shot.png")));
    }

    [Fact]
    public void ImportMany_ReportsAMissingVaultWithoutThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "daylist-missing-" + Guid.NewGuid().ToString("N"));
        var batch = AttachmentVault.ImportMany(missing, Day, new[] { Path.Combine(missing, "a.png") });
        Assert.Empty(batch.Added);
        Assert.Equal("还没有选择 Obsidian 库。", batch.Error);
    }

    [Fact]
    public void BringForward_CopiesAttachmentsIntoTodayAndRewritesLinks()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var sourceDay = new DateOnly(2026, 10, 15);
        var today = new DateOnly(2026, 10, 16);
        var photo = WithToken(
            AttachmentVault.Import(vault.Root, sourceDay, WriteIncoming(vault, "photo.png", new byte[] { 1, 2, 3, 4 })),
            "![[DayList-attachments/2026-10-15/photo.png|120]]");
        var report = WithToken(
            AttachmentVault.Import(vault.Root, sourceDay, WriteIncoming(vault, "周报.docx", "hello"u8.ToArray())),
            "[[DayList-attachments/2026-10-15/周报.docx|周报]]",
            "周报");
        var cover = WithToken(
            AttachmentVault.Import(vault.Root, sourceDay, WriteIncoming(vault, "cover a.webp", new byte[] { 9, 8 })),
            "![封面](../DayList-attachments/2026-10-15/cover%20a.webp)",
            "封面");

        DailyNoteStore.Write(settings, sourceDay, new[]
        {
            new DayTask
            {
                Text = "买牛奶 [[某笔记]]",
                Category = "生活",
                Indent = "  ",
                Bullet = '*',
                Attachments = new List<TaskAttachment> { photo, report, cover }
            },
            new DayTask
            {
                Text = "已完成",
                IsDone = true,
                Attachments = new List<TaskAttachment> { photo.Copy() }
            }
        });

        var yesterdayPath = VaultLayout.Daily(settings, sourceDay).FullPath;
        var before = File.ReadAllBytes(yesterdayPath);
        var item = Assert.Single(OverdueScan.CollectOpenBefore(settings, today));
        Assert.Equal("买牛奶 [[某笔记]]", item.Text);
        Assert.Equal("生活", item.Category);
        Assert.Equal("  ", item.Indent);
        Assert.Equal('*', item.Bullet);
        Assert.Equal(3, item.Attachments.Count);
        Assert.Equal("DayList-attachments/2026-10-15/photo.png", item.Attachments[0].RelativePath);
        Assert.True(item.Attachments[0].IsImage);
        Assert.Equal("周报", item.Attachments[1].DisplayName);
        Assert.False(item.Attachments[1].IsImage);
        Assert.Equal("封面", item.Attachments[2].DisplayName);

        var copied = AttachmentVault.CopyIntoDay(vault.Root, today, item.Attachments);

        Assert.Equal(before, File.ReadAllBytes(yesterdayPath));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(DayFile(vault, sourceDay, "photo.png")));
        Assert.Equal("hello", File.ReadAllText(DayFile(vault, sourceDay, "周报.docx")));
        Assert.Contains("2026-10-15/photo.png", File.ReadAllText(yesterdayPath));
        Assert.DoesNotContain("2026-10-16", File.ReadAllText(yesterdayPath));

        Assert.Equal("DayList-attachments/2026-10-16/photo.png", copied[0].RelativePath);
        Assert.True(copied[0].IsImage);
        Assert.Equal("photo.png", copied[0].DisplayName);
        Assert.Equal("![[DayList-attachments/2026-10-16/photo.png|120]]", copied[0].OriginalToken);
        Assert.Equal("DayList-attachments/2026-10-16/周报.docx", copied[1].RelativePath);
        Assert.False(copied[1].IsImage);
        Assert.Equal("周报", copied[1].DisplayName);
        Assert.Equal("[[DayList-attachments/2026-10-16/周报.docx|周报]]", copied[1].OriginalToken);
        Assert.Equal("DayList-attachments/2026-10-16/cover a.webp", copied[2].RelativePath);
        Assert.True(copied[2].IsImage);
        Assert.Equal("封面", copied[2].DisplayName);
        Assert.Equal("![封面](../DayList-attachments/2026-10-16/cover%20a.webp)", copied[2].OriginalToken);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(DayFile(vault, today, "photo.png")));
        Assert.Equal("hello", File.ReadAllText(DayFile(vault, today, "周报.docx")));
        Assert.Equal(new byte[] { 9, 8 }, File.ReadAllBytes(DayFile(vault, today, "cover a.webp")));

        DailyNoteStore.Write(settings, today, new[]
        {
            new DayTask
            {
                Text = item.Text,
                Category = item.Category,
                Indent = item.Indent,
                Bullet = item.Bullet,
                DoneMark = item.DoneMark,
                Attachments = copied.ToList()
            }
        });
        var todayNote = File.ReadAllText(VaultLayout.Daily(settings, today).FullPath);
        Assert.Contains("![[DayList-attachments/2026-10-16/photo.png|120]]", todayNote);
        Assert.Contains("[[DayList-attachments/2026-10-16/周报.docx|周报]]", todayNote);
        Assert.Contains("![封面](../DayList-attachments/2026-10-16/cover%20a.webp)", todayNote);
        Assert.DoesNotContain("2026-10-15", todayNote);

        var again = Assert.Single(DailyNoteStore.Read(settings, today).Tasks);
        Assert.Equal("买牛奶 [[某笔记]]", again.Text);
        Assert.Equal("生活", again.Category);
        Assert.Equal(3, again.Attachments.Count);
        Assert.Equal(copied[0].RelativePath, again.Attachments[0].RelativePath);
        Assert.True(again.Attachments[0].IsImage);
        Assert.Equal("周报", again.Attachments[1].DisplayName);
        Assert.Equal(copied[2].RelativePath, again.Attachments[2].RelativePath);
        Assert.True(File.Exists(VaultLayout.Combine(vault.Root, again.Attachments[0].RelativePath)));
        Assert.True(File.Exists(VaultLayout.Combine(vault.Root, again.Attachments[2].RelativePath)));
    }

    [Fact]
    public void BringForward_KeepsAnImageOnlyTaskAndMatchesItAfterTheCopy()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var sourceDay = new DateOnly(2026, 10, 15);
        var today = new DateOnly(2026, 10, 16);
        var image = AttachmentVault.Import(vault.Root, sourceDay, WriteIncoming(vault, "only.png", new byte[] { 7 }));
        DailyNoteStore.Write(settings, sourceDay, new[]
        {
            new DayTask { Attachments = new List<TaskAttachment> { image } }
        });

        var item = Assert.Single(OverdueScan.CollectOpenBefore(settings, today));
        Assert.Equal("", item.Text);
        Assert.Equal("only.png", OverdueIdentity.Label(item));
        var copied = Assert.Single(AttachmentVault.CopyIntoDay(vault.Root, today, item.Attachments));
        Assert.Equal("DayList-attachments/2026-10-16/only.png", copied.RelativePath);
        Assert.True(copied.IsImage);
        Assert.Equal("![[DayList-attachments/2026-10-16/only.png]]", copied.OriginalToken);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(DayFile(vault, sourceDay, "only.png")));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(DayFile(vault, today, "only.png")));
        Assert.Equal(
            OverdueIdentity.AttachmentKey(item.Attachments),
            OverdueIdentity.AttachmentKey(new[] { copied }));

        var onToday = new TodayTaskRef("", item.Category, OverdueIdentity.AttachmentKey(new[] { copied }));
        Assert.True(CarryOverOffer.AlreadyOnToday(item, new[] { onToday }));
        Assert.Equal("", Assert.Single(CarryOverOffer.Pending(
            new[] { item },
            Array.Empty<TodayTaskRef>(),
            new CarryOverMemory(),
            today)).Text);
        Assert.Empty(CarryOverOffer.Pending(new[] { item }, new[] { onToday }, new CarryOverMemory(), today));
    }

    [Fact]
    public void CopyIntoDay_ReusesIdenticalBytesAndDoesNotClobberADifferentFile()
    {
        using var vault = new TempVault();
        var sourceDay = new DateOnly(2026, 10, 15);
        var today = new DateOnly(2026, 10, 16);
        var incoming = WriteIncoming(vault, "photo.png", new byte[] { 1, 2, 3 });
        var yesterday = AttachmentVault.Import(vault.Root, sourceDay, incoming);
        var sameToday = AttachmentVault.Import(vault.Root, today, incoming);
        var reused = Assert.Single(AttachmentVault.CopyIntoDay(vault.Root, today, new[] { yesterday }));
        Assert.Equal(sameToday.RelativePath, reused.RelativePath);
        Assert.Equal("![[DayList-attachments/2026-10-16/photo.png]]", reused.OriginalToken);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(vault.Root, "DayList-attachments", "2026-10-16")));

        File.WriteAllBytes(DayFile(vault, sourceDay, "photo.png"), new byte[] { 9 });
        var suffixed = Assert.Single(AttachmentVault.CopyIntoDay(vault.Root, today, new[] { yesterday }));
        Assert.Equal("DayList-attachments/2026-10-16/photo-2.png", suffixed.RelativePath);
        Assert.Equal("![[DayList-attachments/2026-10-16/photo-2.png]]", suffixed.OriginalToken);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(DayFile(vault, today, "photo.png")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(DayFile(vault, today, "photo-2.png")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(DayFile(vault, sourceDay, "photo.png")));
    }

    [Fact]
    public void CopyIntoDay_KeepsAMissingFileAndIgnoresAnEmptyList()
    {
        using var vault = new TempVault();
        var today = new DateOnly(2026, 10, 16);
        Assert.Empty(AttachmentVault.CopyIntoDay(vault.Root, today, null));
        Assert.Empty(AttachmentVault.CopyIntoDay(vault.Root, today, Array.Empty<TaskAttachment>()));

        var missing = new TaskAttachment
        {
            RelativePath = "DayList-attachments/2026-10-15/gone.png",
            DisplayName = "gone.png",
            IsImage = true,
            OriginalToken = "![[DayList-attachments/2026-10-15/gone.png]]"
        };
        var kept = Assert.Single(AttachmentVault.CopyIntoDay(vault.Root, today, new[] { missing }));
        Assert.Equal(missing.RelativePath, kept.RelativePath);
        Assert.Equal(missing.OriginalToken, kept.OriginalToken);
        Assert.False(File.Exists(DayFile(vault, today, "gone.png")));
    }

    private static TaskAttachment WithToken(TaskAttachment source, string token, string? display = null) => new()
    {
        RelativePath = source.RelativePath,
        DisplayName = display ?? source.DisplayName,
        IsImage = source.IsImage,
        OriginalToken = token
    };

    private static string WriteIncoming(TempVault vault, string name, byte[] bytes)
    {
        var dir = Path.Combine(vault.Root, "incoming");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string DayFile(TempVault vault, DateOnly day, string name) =>
        Path.Combine(vault.Root, "DayList-attachments", day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), name);
}
