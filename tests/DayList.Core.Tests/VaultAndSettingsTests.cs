using DayList.Core;

namespace DayList.Core.Tests;

public class VaultAndSettingsTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);

    [Fact]
    public void Patterns_AcceptObsidianAndDotNetTokens()
    {
        Assert.Equal("yyyy-MM-dd", DatePattern.ToDotNet("YYYY-MM-DD"));
        Assert.Equal("yyyy-MM-dd", DatePattern.ToDotNet("yyyy-MM-dd"));
        Assert.Equal("yyyy-MM-dd dddd", DatePattern.ToDotNet("YYYY-MM-DD dddd"));
        Assert.Equal("2026/10/15", DatePattern.Format(Day, "YYYY/MM/DD"));
        Assert.Equal("15-10-2026", DatePattern.Format(Day, "DD-MM-YYYY"));
    }

    [Fact]
    public void Layout_UsesVaultRootByDefaultAndRejectsEscape()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        Assert.Equal("2026-10-15.md", VaultLayout.DailyRelative(settings, Day));

        settings.DailyFolder = "Daily Notes";
        Assert.Equal("Daily Notes/2026-10-15.md", VaultLayout.DailyRelative(settings, Day));

        settings.DailyFolder = "..";
        Assert.Throws<InvalidOperationException>(() => VaultLayout.Daily(settings, Day));

        settings.DailyFolder = "";
        settings.ReportFolder = ".obsidian";
        Assert.Throws<InvalidOperationException>(() => VaultLayout.ReportRelative(settings, Day));
    }

    [Fact]
    public void InvalidPattern_ExplainsTheFailure()
    {
        using var vault = new TempVault();
        var settings = vault.Settings(pattern: "DDD");
        var error = Assert.Throws<FormatException>(() => VaultLayout.DailyRelative(settings, Day));
        Assert.Contains("DDD", error.Message);
    }

    [Fact]
    public void ObsidianDailyNotes_ReadsFolderAndFormat()
    {
        using var vault = new TempVault();
        Assert.Null(ObsidianDailyNotes.TryRead(vault.Root));

        var directory = Path.Combine(vault.Root, ".obsidian");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "daily-notes.json"), """
            {
              "folder": "00 Daily",
              "format": "YYYY-MM-DD",
              "template": "Templates/Daily"
            }
            """);

        var config = ObsidianDailyNotes.TryRead(vault.Root);
        Assert.NotNull(config);
        Assert.Equal("00 Daily", config.Folder);
        Assert.Equal("YYYY-MM-DD", config.Format);

        File.WriteAllText(Path.Combine(directory, "daily-notes.json"), "{");
        Assert.Null(ObsidianDailyNotes.TryRead(vault.Root));
    }

    [Fact]
    public void Settings_RoundTripUtf8AndIgnoreComputedFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings
            {
                VaultPath = @"C:\库\My Vault",
                DailyFolder = "日记",
                DailyFilePattern = "YYYY-MM-DD",
                SectionHeading = "## 今日",
                ApiBaseUrl = "http://127.0.0.1:11434/v1/",
                ApiKey = "secret-key",
                ApiModel = "llama3.1",
                ReportFolder = "月报",
                ReportFilePattern = "yyyy-MM"
            };
            SettingsStore.Save(settings, path);
            var json = File.ReadAllText(path);
            Assert.Contains("日记", json);
            Assert.DoesNotContain("hasVault", json);
            Assert.Equal("http://127.0.0.1:11434/v1", settings.ApiBaseUrl);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(@"C:\库\My Vault", loaded.VaultPath);
            Assert.Equal("日记", loaded.DailyFolder);
            Assert.Equal("secret-key", loaded.ApiKey);
            Assert.Equal("llama3.1", loaded.ApiModel);
            Assert.True(loaded.IsApiConfigured);

            File.WriteAllText(path, """
                {
                  // comment
                  "vaultPath": "D:\\vault",
                }
                """);
            var partial = SettingsStore.Load(path);
            Assert.Equal(@"D:\vault", partial.VaultPath);
            Assert.Equal("YYYY-MM-DD", partial.DailyFilePattern);
            Assert.Equal("## DayList", partial.SectionHeading);

            File.WriteAllText(path, "not-json");
            Assert.Equal("", SettingsStore.Load(path).VaultPath);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void MonthDigest_ConcatenatesOnlyThisMonthsSections()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        WriteNote(settings, new DateOnly(2026, 10, 1), "## DayList\n\n- [x] 开工\n\n备注\n");
        WriteNote(settings, new DateOnly(2026, 10, 3), "# 三号\n\n没有小节\n");
        WriteNote(settings, new DateOnly(2026, 9, 30), "## DayList\n\n- [ ] 上月\n");
        WriteNote(settings, new DateOnly(2026, 10, 15), "## DayList\n\n\n");

        var digest = MonthDigest.Build(settings, Day);

        Assert.Contains("## 2026-10-01", digest);
        Assert.Contains("- [x] 开工", digest);
        Assert.Contains("备注", digest);
        Assert.DoesNotContain("上月", digest);
        Assert.DoesNotContain("没有小节", digest);
        Assert.DoesNotContain("2026-10-03", digest);
        Assert.DoesNotContain("2026-10-15", digest);
    }

    private static void WriteNote(AppSettings settings, DateOnly day, string content)
    {
        var (fullPath, _) = VaultLayout.Daily(settings, day);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}

public sealed class TempVault : IDisposable
{
    public TempVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "daylist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public AppSettings Settings(string? folder = null, string? pattern = null) => new()
    {
        VaultPath = Root,
        DailyFolder = folder ?? "",
        DailyFilePattern = pattern ?? "YYYY-MM-DD"
    };

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
