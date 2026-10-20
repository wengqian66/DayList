using DayList.Core;

namespace DayList.Core.Tests;

public class CategoryCatalogTests
{
    private static readonly DateOnly DayA = new(2026, 10, 15);
    private static readonly DateOnly DayB = new(2026, 10, 16);

    [Fact]
    public void Normalize_DropsBlanksAndDuplicates_KeepsFirstSeenOrder()
    {
        var names = CategoryCatalog.Normalize(new string?[] { " 工作 ", "未分类", "", null, "生活", "工作", "  生活  " });
        Assert.Equal(new[] { "工作", "生活" }, names);
    }

    [Fact]
    public void Remember_RoundTripsThroughSettings_AndStaysAvailableOnALaterDay()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-categories-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings();
            var created = CategoryCatalog.Remember(settings.TaskCategories, settings.RetiredTaskCategories, " 工作 ");
            Assert.True(created.Changed);
            settings.TaskCategories = created.Categories.ToList();
            settings.RetiredTaskCategories = created.Retired.ToList();
            SettingsStore.Save(settings, path);

            var json = File.ReadAllText(path);
            Assert.Contains("taskCategories", json);
            Assert.Contains("工作", json);
            Assert.DoesNotContain("\\u5DE5", json);

            var loaded = SettingsStore.Load(path);
            Assert.Equal(new[] { "工作" }, loaded.TaskCategories);
            Assert.Empty(loaded.RetiredTaskCategories);

            var dayB = CategoryCatalog.Available(loaded.TaskCategories, Array.Empty<string>());
            Assert.Equal(new[] { "工作" }, dayB);
            Assert.Equal(new[] { "工作" }, CategoryCatalog.WithoutTasks(loaded.TaskCategories, Array.Empty<string>()));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void CreateOnDayA_IsAvailableOnDayB_WithoutSeedingAnEmptyNote()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        DailyNoteStore.Write(settings, DayA, new[]
        {
            new DayTask { Text = "周报", Category = "工作" }
        });

        var absorbed = CategoryCatalog.Absorb(
            settings.TaskCategories,
            settings.RetiredTaskCategories,
            new[] { "工作" });
        Assert.True(absorbed.Changed);
        settings.TaskCategories = absorbed.Categories.ToList();
        settings.RetiredTaskCategories = absorbed.Retired.ToList();

        var dayB = CategoryCatalog.Available(settings.TaskCategories, Array.Empty<string>());
        Assert.Equal(new[] { "工作" }, dayB);

        var untouched = DailyNoteStore.Write(settings, DayB, Array.Empty<DayTask>());
        Assert.False(untouched.Wrote);
        Assert.False(File.Exists(untouched.FullPath));
        Assert.DoesNotContain("### 工作", DailyNoteDocument.Parse("", "## DayList").Render(new[]
        {
            new DayTask { Text = "买牛奶", Category = "生活" }
        }));
    }

    [Fact]
    public void Forget_RemovesTheNameForLaterDays_AndLeavesExistingTasks()
    {
        using var vault = new TempVault();
        var settings = vault.Settings();
        var written = DailyNoteStore.Write(settings, DayA, new[]
        {
            new DayTask { Text = "周报", Category = "工作" },
            new DayTask { Text = "买菜", Category = "生活" }
        });
        var before = File.ReadAllBytes(written.FullPath);

        var catalog = CategoryCatalog.RememberAll(
            settings.TaskCategories,
            settings.RetiredTaskCategories,
            new[] { "工作", "生活" });
        var forgotten = CategoryCatalog.Forget(catalog.Categories, catalog.Retired, "工作");
        Assert.True(forgotten.Changed);
        Assert.Equal(new[] { "生活" }, forgotten.Categories);
        Assert.Equal(new[] { "工作" }, forgotten.Retired);
        Assert.Equal(before, File.ReadAllBytes(written.FullPath));

        var snapshot = DailyNoteStore.Read(settings, DayA);
        Assert.Equal(new[] { "工作", "生活" }, snapshot.Tasks.Select(task => task.Category).ToArray());

        var later = CategoryCatalog.Available(forgotten.Categories, Array.Empty<string>());
        Assert.Equal(new[] { "生活" }, later);
        Assert.Equal(new[] { "生活" }, CategoryCatalog.WithoutTasks(forgotten.Categories, Array.Empty<string>()));

        var todayStillShowsIt = CategoryCatalog.Available(
            forgotten.Categories,
            snapshot.Tasks.Select(task => task.Category));
        Assert.Equal(new[] { "生活", "工作" }, todayStillShowsIt);

        var reabsorb = CategoryCatalog.Absorb(
            forgotten.Categories,
            forgotten.Retired,
            snapshot.Tasks.Select(task => task.Category));
        Assert.False(reabsorb.Changed);
        Assert.DoesNotContain("工作", reabsorb.Categories);
        Assert.Contains("工作", reabsorb.Retired);
    }

    [Fact]
    public void Remember_AfterForget_RestoresTheNameForLaterDays()
    {
        var forgotten = CategoryCatalog.Forget(new[] { "工作", "生活" }, Array.Empty<string>(), "工作");
        var restored = CategoryCatalog.Remember(forgotten.Categories, forgotten.Retired, "工作");
        Assert.True(restored.Changed);
        Assert.Equal(new[] { "生活", "工作" }, restored.Categories);
        Assert.Empty(restored.Retired);
        Assert.Equal(new[] { "生活", "工作" }, CategoryCatalog.Available(restored.Categories, Array.Empty<string>()));
    }

    [Fact]
    public void Forget_IgnoresUncategorized_AndSecondForgetIsANoOp()
    {
        var once = CategoryCatalog.Forget(new[] { "工作" }, Array.Empty<string>(), "工作");
        var blank = CategoryCatalog.Forget(once.Categories, once.Retired, "  ");
        Assert.False(blank.Changed);
        Assert.Equal(once.Categories, blank.Categories);
        Assert.Equal(once.Retired, blank.Retired);

        var unlabeled = CategoryCatalog.Forget(once.Categories, once.Retired, "未分类");
        Assert.False(unlabeled.Changed);

        var again = CategoryCatalog.Forget(once.Categories, once.Retired, "工作");
        Assert.False(again.Changed);
        Assert.Equal(new[] { "工作" }, again.Retired);
    }

    [Fact]
    public void Absorb_SkipsRetiredNames_AndAppendsNewOnes()
    {
        var forgotten = CategoryCatalog.Forget(new[] { "工作" }, Array.Empty<string>(), "工作");
        var absorbed = CategoryCatalog.Absorb(
            forgotten.Categories,
            forgotten.Retired,
            new[] { "工作", "运动", "运动", "" });
        Assert.True(absorbed.Changed);
        Assert.Equal(new[] { "运动" }, absorbed.Categories);
        Assert.Equal(new[] { "工作" }, absorbed.Retired);
    }

    [Fact]
    public void MainList_OmitsSavedCategoriesWithNoTasks_PickerStillListsThem()
    {
        var catalog = new[] { "工作", "生活", "运动" };
        var tasks = new List<DayTask>
        {
            new() { Text = "周报", Category = "工作" }
        };

        var sections = CategoryGrouping.ForMainList(tasks, task => task.Category);
        Assert.Equal(new[] { "工作" }, sections.Select(section => section.Key).ToArray());
        Assert.DoesNotContain(sections, section => section.Items.Count == 0);

        var picker = CategoryCatalog.PickerEntries(catalog, tasks.Select(task => task.Category));
        Assert.Contains(picker, entry => entry.Label == "生活" && entry.CanDelete);
        Assert.Contains(picker, entry => entry.Label == "运动" && entry.CanDelete);
        Assert.Contains(picker, entry => entry.Label == "工作" && entry.CanDelete);

        tasks.Add(new DayTask { Text = "跑步", Category = "运动" });
        sections = CategoryGrouping.ForMainList(tasks, task => task.Category);
        Assert.Equal(new[] { "工作", "运动" }, sections.Select(section => section.Key).ToArray());
        Assert.Equal(new[] { "跑步" }, sections.Single(section => section.Key == "运动").Items.Select(task => task.Text).ToArray());
    }

    [Fact]
    public void WithoutTasks_KeepsCatalogOrder_AndSkipsNamesAlreadyUsed()
    {
        var idle = CategoryCatalog.WithoutTasks(
            new[] { "生活", "工作", "运动" },
            new[] { "工作", "" });
        Assert.Equal(new[] { "生活", "运动" }, idle);
    }

    [Fact]
    public void Settings_NormalizeDropsOverlap_AndCopyDoesNotAliasTheLists()
    {
        var settings = new AppSettings
        {
            TaskCategories = new List<string> { "工作", "工作", "未分类", "生活" },
            RetiredTaskCategories = new List<string> { "工作", "旧类", "  " }
        };
        settings.Normalize();
        Assert.Equal(new[] { "工作", "生活" }, settings.TaskCategories);
        Assert.Equal(new[] { "旧类" }, settings.RetiredTaskCategories);

        var copy = settings.Copy();
        copy.TaskCategories.Add("运动");
        copy.RetiredTaskCategories.Clear();
        Assert.Equal(new[] { "工作", "生活" }, settings.TaskCategories);
        Assert.Equal(new[] { "旧类" }, settings.RetiredTaskCategories);
    }

    [Fact]
    public void Settings_MissingCatalogFields_StayEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "daylist-categories-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "vaultPath": "D:\\vault"
                }
                """);
            var loaded = SettingsStore.Load(path);
            Assert.Empty(loaded.TaskCategories);
            Assert.Empty(loaded.RetiredTaskCategories);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void PickerEntries_OfferDeleteOnlyForCatalogNames()
    {
        var entries = CategoryCatalog.PickerEntries(
            new[] { "生活", "工作", "未分类", "  " },
            new[] { "工作", "旧类", "" });

        Assert.Equal(DayTaskCategory.UncategorizedLabel, entries[0].Label);
        Assert.False(entries[0].CanDelete);
        Assert.Equal(DayTaskCategory.NewCategorySentinel, entries[^1].Label);
        Assert.False(entries[^1].CanDelete);

        var byLabel = entries.ToDictionary(entry => entry.Label);
        Assert.True(byLabel["工作"].CanDelete);
        Assert.True(byLabel["生活"].CanDelete);
        Assert.False(byLabel["旧类"].CanDelete);
        Assert.DoesNotContain(entries, entry => entry.Label.Length == 0);
        Assert.Equal(5, entries.Count);
        Assert.Equal(entries.Count, entries.Select(entry => entry.Label).Distinct().Count());
    }

    [Fact]
    public void TryAcceptPickerDelete_KeepsTheComposedCategory_AndRejectsNonCatalogRows()
    {
        Assert.True(CategoryCatalog.TryAcceptPickerDelete(
            "工作",
            canDelete: true,
            composeCategory: "生活",
            out var delete,
            out var kept));
        Assert.Equal("工作", delete);
        Assert.Equal("生活", kept);

        Assert.True(CategoryCatalog.TryAcceptPickerDelete(
            "  工作  ",
            canDelete: true,
            composeCategory: DayTaskCategory.UncategorizedKey,
            out delete,
            out kept));
        Assert.Equal("工作", delete);
        Assert.Equal(DayTaskCategory.UncategorizedKey, kept);

        Assert.False(CategoryCatalog.TryAcceptPickerDelete(
            "工作",
            canDelete: false,
            composeCategory: "生活",
            out delete,
            out kept));
        Assert.Equal("", delete);
        Assert.Equal("生活", kept);

        Assert.False(CategoryCatalog.TryAcceptPickerDelete(
            DayTaskCategory.UncategorizedLabel,
            canDelete: true,
            composeCategory: "生活",
            out _,
            out kept));
        Assert.Equal("生活", kept);

        Assert.False(CategoryCatalog.TryAcceptPickerDelete(
            DayTaskCategory.NewCategorySentinel,
            canDelete: true,
            composeCategory: "生活",
            out _,
            out kept));
        Assert.Equal("生活", kept);

        Assert.False(CategoryCatalog.TryAcceptPickerDelete(
            "  ",
            canDelete: true,
            composeCategory: null,
            out _,
            out kept));
        Assert.Equal("", kept);
    }
}
