using System.Text.Json.Serialization;

namespace DayList.Core;

public sealed class AppSettings
{
    public string VaultPath { get; set; } = "";

    public string DailyFolder { get; set; } = "";

    public string DailyFilePattern { get; set; } = "YYYY-MM-DD";

    public string SectionHeading { get; set; } = "## DayList";

    public string InspirationHeading { get; set; } = "## 灵感";

    public bool ReminderSound { get; set; } = true;

    public bool ReminderToastOnly { get; set; }

    public bool ConfettiEnabled { get; set; } = true;

    public string ApiBaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string ApiModel { get; set; } = "gpt-4o-mini";

    public string ReportFolder { get; set; } = "Reports";

    public string ReportFilePattern { get; set; } = "yyyy-MM";

    public bool AlwaysOnTop { get; set; }

    public bool MinimizeToTray { get; set; } = true;

    public bool StartMinimizedToTray { get; set; }

    public string Hotkey { get; set; } = "Ctrl+Shift+D";

    /// <summary>Optional global hotkey that opens the quick-add capture bar. Empty = off.</summary>
    public string QuickAddHotkey { get; set; } = "";

    /// <summary>A version the user chose to ignore; the update banner stays quiet for it.</summary>
    public string IgnoredUpdateVersion { get; set; } = "";

    public int FocusMinutes { get; set; } = 25;

    public int BreakMinutes { get; set; } = 5;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    /// <summary>
    /// Task categories kept across days, in the order they were first saved.
    /// </summary>
    public List<string> TaskCategories { get; set; } = new();

    /// <summary>
    /// Names the user removed from <see cref="TaskCategories"/>. Opening an older
    /// note will not put them back. Creating the same name again clears this.
    /// </summary>
    public List<string> RetiredTaskCategories { get; set; } = new();

    [JsonIgnore]
    public bool HasVault =>
        !string.IsNullOrWhiteSpace(VaultPath) && Directory.Exists(VaultPath);

    [JsonIgnore]
    public bool IsApiConfigured =>
        Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ApiModel);

    public void Normalize()
    {
        VaultPath = (VaultPath ?? "").Trim().TrimEnd('/', '\\');
        DailyFolder = (DailyFolder ?? "").Replace('\\', '/').Trim().Trim('/');
        DailyFilePattern = string.IsNullOrWhiteSpace(DailyFilePattern) ? "YYYY-MM-DD" : DailyFilePattern.Trim();
        SectionHeading = string.IsNullOrWhiteSpace(SectionHeading) ? "## DayList" : SectionHeading.Trim();
        InspirationHeading = NormalizeHeading(InspirationHeading, "## 灵感");
        ApiBaseUrl = (ApiBaseUrl ?? "").Trim().TrimEnd('/');
        ApiKey = (ApiKey ?? "").Trim();
        ApiModel = string.IsNullOrWhiteSpace(ApiModel) ? "gpt-4o-mini" : ApiModel.Trim();
        ReportFolder = string.IsNullOrWhiteSpace(ReportFolder)
            ? "Reports"
            : ReportFolder.Replace('\\', '/').Trim().Trim('/');
        ReportFilePattern = string.IsNullOrWhiteSpace(ReportFilePattern) ? "yyyy-MM" : ReportFilePattern.Trim();
        Hotkey = (Hotkey ?? "").Trim();
        QuickAddHotkey = (QuickAddHotkey ?? "").Trim();
        var focus = FocusDuration.Normalize(FocusMinutes, BreakMinutes);
        FocusMinutes = focus.FocusMinutes;
        BreakMinutes = focus.BreakMinutes;
        if (WindowWidth is <= 0)
            WindowWidth = null;
        if (WindowHeight is <= 0)
            WindowHeight = null;
        TaskCategories = CategoryCatalog.Normalize(TaskCategories).ToList();
        var active = new HashSet<string>(TaskCategories, StringComparer.Ordinal);
        RetiredTaskCategories = CategoryCatalog.Normalize(RetiredTaskCategories)
            .Where(name => !active.Contains(name))
            .ToList();
    }

    private static string NormalizeHeading(string? heading, string fallback)
    {
        var (level, title) = HeadingSpec.Parse(string.IsNullOrWhiteSpace(heading) ? fallback : heading);
        if (string.IsNullOrWhiteSpace(title))
            (level, title) = HeadingSpec.Parse(fallback);
        return HeadingSpec.Format(level, title);
    }

    public AppSettings Copy()
    {
        var copy = new AppSettings
        {
            VaultPath = VaultPath,
            DailyFolder = DailyFolder,
            DailyFilePattern = DailyFilePattern,
            SectionHeading = SectionHeading,
            InspirationHeading = InspirationHeading,
            ReminderSound = ReminderSound,
            ReminderToastOnly = ReminderToastOnly,
            ConfettiEnabled = ConfettiEnabled,
            ApiBaseUrl = ApiBaseUrl,
            ApiKey = ApiKey,
            ApiModel = ApiModel,
            ReportFolder = ReportFolder,
            ReportFilePattern = ReportFilePattern,
            AlwaysOnTop = AlwaysOnTop,
            MinimizeToTray = MinimizeToTray,
            StartMinimizedToTray = StartMinimizedToTray,
            Hotkey = Hotkey,
            QuickAddHotkey = QuickAddHotkey,
            IgnoredUpdateVersion = IgnoredUpdateVersion,
            FocusMinutes = FocusMinutes,
            BreakMinutes = BreakMinutes,
            WindowLeft = WindowLeft,
            WindowTop = WindowTop,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            TaskCategories = TaskCategories?.ToList() ?? new List<string>(),
            RetiredTaskCategories = RetiredTaskCategories?.ToList() ?? new List<string>()
        };
        copy.Normalize();
        return copy;
    }
}
