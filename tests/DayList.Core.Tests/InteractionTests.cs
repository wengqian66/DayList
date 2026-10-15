using DayList.Core;

namespace DayList.Core.Tests;

public class InteractionTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void Indent_RoundTripsInMarkdown()
    {
        const string original = "## DayList\n\n- [ ] parent\n  - [ ] child\n    - [x] grand\n";
        var document = DailyNoteDocument.Parse(original, "## DayList");
        Assert.Equal(3, document.Tasks.Count);
        Assert.Equal(0, document.Tasks[0].IndentLevel);
        Assert.Equal(1, document.Tasks[1].IndentLevel);
        Assert.Equal(2, document.Tasks[2].IndentLevel);
        Assert.Equal(original, document.Render(document.Tasks));
    }

    [Fact]
    public void IndentInOut_UsesTwoSpaces()
    {
        var task = new DayTask { Text = "a" };
        task.IndentIn();
        Assert.Equal("  ", task.Indent);
        Assert.Equal(1, task.IndentLevel);
        task.IndentIn();
        Assert.Equal("    ", task.Indent);
        task.IndentOut();
        Assert.Equal("  ", task.Indent);
        task.IndentOut();
        Assert.Equal("", task.Indent);
        Assert.Equal("- [ ] a", task.ToMarkdown());
    }

    [Fact]
    public void HotkeySpec_ParsesDefault()
    {
        var hotkey = HotkeySpec.Parse("Ctrl+Shift+D");
        Assert.True(hotkey.Ctrl);
        Assert.True(hotkey.Shift);
        Assert.False(hotkey.Alt);
        Assert.Equal('D', hotkey.VirtualKey);
        Assert.Equal("Ctrl+Shift+D", hotkey.ToDisplay());
    }

    [Fact]
    public void OverdueScan_FindsOpenTasksBeforeToday()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        DailyNoteStore.Write(settings, new DateOnly(2026, 10, 13), new[]
        {
            new DayTask { Text = "old open" },
            new DayTask { Text = "old done", IsDone = true },
        });
        DailyNoteStore.Write(settings, new DateOnly(2026, 10, 14), new[]
        {
            new DayTask { Text = "yesterday", Category = "工作" },
        });
        DailyNoteStore.Write(settings, Day, new[]
        {
            new DayTask { Text = "today open" },
        });

        var items = OverdueScan.CollectOpenBefore(settings, Day);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, item => item.Text == "old open" && item.Date == new DateOnly(2026, 10, 13));
        Assert.Contains(items, item => item.Text == "yesterday" && item.Category == "工作");
        Assert.DoesNotContain(items, item => item.Text == "today open");
        Assert.DoesNotContain(items, item => item.Text == "old done");
    }

    [Fact]
    public void AppSettings_PersistsDesktopFlags()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings
            {
                VaultPath = Path.GetTempPath().TrimEnd('\\', '/'),
                AlwaysOnTop = true,
                MinimizeToTray = false,
                StartMinimizedToTray = true,
                Hotkey = "Ctrl+Alt+L",
                WindowLeft = 12,
                WindowTop = 34,
                WindowWidth = 500,
                WindowHeight = 700
            };
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);
            Assert.True(loaded.AlwaysOnTop);
            Assert.False(loaded.MinimizeToTray);
            Assert.True(loaded.StartMinimizedToTray);
            Assert.Equal("Ctrl+Alt+L", loaded.Hotkey);
            Assert.Equal(12, loaded.WindowLeft);
            Assert.Equal(500, loaded.WindowWidth);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
