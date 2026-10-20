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
}
