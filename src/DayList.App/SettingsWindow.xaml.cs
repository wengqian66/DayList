using DayList.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace DayList;

public sealed partial class SettingsWindow : Window
{
    private bool _filling;
    private AppSettings _seed;

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Title = "设置";
        WindowChrome.Prepare(this, 560, 860, 480, 620);
        _seed = current.Copy();
        Fill(current.Copy());
    }

    public AppSettings? Result { get; private set; }

    public void ApplyVault(string path)
    {
        VaultBox.Text = path;
        var config = ObsidianDailyNotes.TryRead(path);
        if (config is null)
        {
            HintText.Text = "没有读到 .obsidian/daily-notes.json，请自己填文件夹和格式。";
            UpdatePreview();
            return;
        }

        if (!string.IsNullOrWhiteSpace(config.Folder))
            FolderBox.Text = config.Folder.Trim().Trim('/', '\\');
        if (!string.IsNullOrWhiteSpace(config.Format))
            PatternBox.Text = config.Format;

        HintText.Text = string.IsNullOrWhiteSpace(config.Folder) && string.IsNullOrWhiteSpace(config.Format)
            ? "找到了每日笔记配置，但里面没有文件夹或格式。"
            : "已从库的每日笔记设置带出文件夹和文件名格式。";
        UpdatePreview();
    }

    private void Fill(AppSettings settings)
    {
        _filling = true;
        VaultBox.Text = settings.VaultPath;
        FolderBox.Text = settings.DailyFolder;
        PatternBox.Text = settings.DailyFilePattern;
        var (_, inspirationTitle) = HeadingSpec.Parse(settings.InspirationHeading);
        InspirationBox.Text = inspirationTitle;
        ReportFolderBox.Text = settings.ReportFolder;
        ReportPatternBox.Text = settings.ReportFilePattern;
        ApiUrlBox.Text = settings.ApiBaseUrl;
        ApiKeyBox.Password = settings.ApiKey;
        ModelBox.Text = settings.ApiModel;
        AlwaysOnTopBox.IsChecked = settings.AlwaysOnTop;
        MinimizeToTrayBox.IsChecked = settings.MinimizeToTray;
        StartMinimizedBox.IsChecked = settings.StartMinimizedToTray;
        HotkeyBox.Text = HotkeySpec.Parse(settings.Hotkey).ToDisplay();
        ReminderSoundBox.IsChecked = settings.ReminderSound;
        ReminderToastOnlyBox.IsChecked = settings.ReminderToastOnly;
        _filling = false;
        UpdatePreview();
    }

    private AppSettings ReadDraft()
    {
        var draft = new AppSettings
        {
            VaultPath = VaultBox.Text ?? "",
            DailyFolder = FolderBox.Text ?? "",
            DailyFilePattern = PatternBox.Text ?? "",
            SectionHeading = _seed.SectionHeading,
            InspirationHeading = string.IsNullOrWhiteSpace(InspirationBox.Text) ? "## 灵感" : InspirationBox.Text,
            ReminderSound = ReminderSoundBox.IsChecked == true,
            ReminderToastOnly = ReminderToastOnlyBox.IsChecked == true,
            ReportFolder = ReportFolderBox.Text ?? "",
            ReportFilePattern = ReportPatternBox.Text ?? "",
            ApiBaseUrl = ApiUrlBox.Text ?? "",
            ApiKey = ApiKeyBox.Password ?? "",
            ApiModel = ModelBox.Text ?? "",
            AlwaysOnTop = AlwaysOnTopBox.IsChecked == true,
            MinimizeToTray = MinimizeToTrayBox.IsChecked == true,
            StartMinimizedToTray = StartMinimizedBox.IsChecked == true,
            Hotkey = HotkeyBox.Text ?? "",
            FocusMinutes = _seed.FocusMinutes,
            BreakMinutes = _seed.BreakMinutes,
            WindowLeft = _seed.WindowLeft,
            WindowTop = _seed.WindowTop,
            WindowWidth = _seed.WindowWidth,
            WindowHeight = _seed.WindowHeight,
            TaskCategories = _seed.TaskCategories?.ToList() ?? new List<string>(),
            RetiredTaskCategories = _seed.RetiredTaskCategories?.ToList() ?? new List<string>()
        };
        draft.Hotkey = HotkeySpec.Parse(draft.Hotkey).ToDisplay();
        draft.Normalize();
        return draft;
    }

    private void FieldChanged(object sender, TextChangedEventArgs e)
    {
        if (!_filling)
            UpdatePreview();
    }

    private void PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_filling)
            UpdatePreview();
    }

    private void UpdatePreview()
    {
        ErrorText.Text = "";
        try
        {
            var draft = ReadDraft();
            if (string.IsNullOrWhiteSpace(draft.VaultPath))
            {
                PreviewText.Text = "选择库文件夹后，这里会预览今天的笔记会写到哪里。";
                return;
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var daily = VaultLayout.DailyRelative(draft, today);
            var report = VaultLayout.ReportRelative(draft, today);
            var (level, title) = HeadingSpec.Parse(draft.SectionHeading);
            var (_, inspiration) = HeadingSpec.Parse(draft.InspirationHeading);
            var clash = InspirationSection.SameHeading(draft.SectionHeading, draft.InspirationHeading)
                ? " 灵感小节和待办小节同名，记灵感前需要改开。"
                : "";
            PreviewText.Text =
                $"今天写入 {daily} 的小节「{title}」（笔记里是 {HeadingSpec.Format(level, title)}）。灵感写入「{inspiration}」。AI 月报写入 {report}。热键 {draft.Hotkey}。{clash}";
        }
        catch (Exception ex)
        {
            PreviewText.Text = ex.Message;
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var pick = await FolderBrowser.PickAsync(this);
        if (pick.Error is not null)
        {
            ErrorText.Text = pick.Error;
            return;
        }

        if (pick.Path is not null)
            ApplyVault(pick.Path);
    }

    private void ReadObsidian_Click(object sender, RoutedEventArgs e)
    {
        var path = (VaultBox.Text ?? "").Trim();
        if (path.Length == 0 || !Directory.Exists(path))
        {
            ErrorText.Text = "先选一个存在的库文件夹。";
            return;
        }

        ApplyVault(path);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppSettings draft;
        try
        {
            draft = ReadDraft();
            if (!Directory.Exists(draft.VaultPath))
            {
                ErrorText.Text = "找不到这个库文件夹。";
                return;
            }

            _ = VaultLayout.Daily(draft, DateOnly.FromDateTime(DateTime.Now));
            _ = VaultLayout.Report(draft, DateOnly.FromDateTime(DateTime.Now));
            if (!string.IsNullOrWhiteSpace(draft.ApiBaseUrl))
            {
                if (!Uri.TryCreate(draft.ApiBaseUrl, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    ErrorText.Text = "Base URL 需要是完整的 http 或 https 地址。";
                    return;
                }

                if (string.IsNullOrWhiteSpace(draft.ApiKey))
                {
                    ErrorText.Text = "填了 Base URL 的话，还需要 API Key。";
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        Result = draft;
        Close();
    }

    private void HotkeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;
        var key = e.Key;
        if (key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows
            or VirtualKey.CapitalLock or VirtualKey.Escape or VirtualKey.Tab)
        {
            return;
        }

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        var alt = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);

        int vk = (int)key;
        bool supported =
            (vk is >= 0x41 and <= 0x5A) // A-Z
            || (vk is >= 0x30 and <= 0x39) // 0-9
            || (vk is >= 0x70 and <= 0x7B); // F1-F12

        if (!supported)
        {
            ErrorText.Text = "请使用 Ctrl/Shift/Alt + 字母、数字或 F1–F12。";
            return;
        }

        if (!ctrl && !shift && !alt)
        {
            ErrorText.Text = "请至少配合 Ctrl、Shift 或 Alt 其中一个修饰键。";
            return;
        }

        var spec = new HotkeySpec(ctrl, shift, alt, vk);
        _filling = true;
        HotkeyBox.Text = spec.ToDisplay();
        _filling = false;
        ErrorText.Text = "";
        UpdatePreview();
    }


    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
