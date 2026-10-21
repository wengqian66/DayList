using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace DayList;

public sealed partial class MainWindow : Window
{
    private readonly MainPage _page;
    private TrayHost? _tray;
    private bool _exitRequested;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        Title = "DayList";
        _page = (MainPage)Content;
        _page.AttachHost(this);
        WindowChrome.Prepare(this, 1100, 860, 820, 600);

        var settings = SettingsStore.Load();
        WindowChrome.ApplyPlacement(this, settings, 1100, 860);
        WindowChrome.SetAlwaysOnTop(this, settings.AlwaysOnTop);

        _tray = new TrayHost(this, ShowFromTray, FocusQuickAdd, ShowFromTrayAndFocusAdd, ExitFromTray);
        ReminderNotifier.Activated += OnReminderActivated;
        ReminderNotifier.EnsureRegistered();
        var hotkeyError = ApplyHotkey(settings);
        ApplyQuickAddHotkey(settings);
        if (hotkeyError is not null || _tray is { IsTrayVisible: false })
            Activated += ReportHotkeyOnReady;

        AppWindow.Closing += OnClosing;

        if (settings.StartMinimizedToTray && settings.MinimizeToTray)
        {
            Activated += OnFirstActivatedHide;
        }
    }

    public string? ApplyHotkey(AppSettings settings)
    {
        if (_tray is null)
            return "托盘宿主未初始化。";

        var text = settings.Hotkey ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _tray.UnregisterHotkey();
            return null;
        }

        var ok = _tray.RegisterHotkey(HotkeySpec.Parse(text));
        return ok ? null : _tray.LastHotkeyError;
    }

    /// <summary>Apply desktop shell options after Settings are saved. Shows hotkey status only here (not for pin).</summary>
    public void ReloadShellSettings(AppSettings settings)
    {
        WindowChrome.SetAlwaysOnTop(this, settings.AlwaysOnTop);
        var error = ApplyHotkey(settings);
        var quickError = ApplyQuickAddHotkey(settings) ? null : _tray?.LastQuickAddHotkeyError;
        if (error is not null)
            _page.ShowShellStatus(error, isError: true);
        else if (quickError is not null)
            _page.ShowShellStatus(quickError, isError: true);
        else
            _page.ShowShellStatus($"全局热键已生效：{HotkeySpec.Parse(settings.Hotkey).ToDisplay()}", isError: false);
    }

    public void BringToFront() => ShowFromTray();

    public void FocusQuickAdd()
    {
        if (SettingsStore.Load().HasVault)
        {
            QuickAddWindow.Open();
            return;
        }

        ShowFromTray();
        _page.FocusAddBox();
    }

    private void ShowFromTrayAndFocusAdd()
    {
        ShowFromTray();
        _page.FocusAddBox();
    }

    public bool ApplyQuickAddHotkey(AppSettings settings)
    {
        if (_tray is null)
            return false;

        var text = settings.QuickAddHotkey ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _tray.UnregisterQuickAddHotkey();
            return true;
        }

        return _tray.RegisterQuickAddHotkey(HotkeySpec.Parse(text));
    }


    private void ReportHotkeyOnReady(object sender, WindowActivatedEventArgs args)
    {
        Activated -= ReportHotkeyOnReady;
        if (_tray is { IsTrayVisible: false })
            _page.ShowShellStatus("系统托盘图标未能显示，关闭窗口可能无法从托盘恢复。", isError: true);
        else if (_tray is { IsHotkeyRegistered: false })
            _page.ShowShellStatus(_tray.LastHotkeyError ?? "全局热键未生效。", isError: true);
    }
    private void OnFirstActivatedHide(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnFirstActivatedHide;
        if (_exitRequested)
            return;
        HideToTray();
    }

    private void ShowFromTray()
    {
        _tray?.EnsureTrayIcon();
        WindowChrome.ShowAndFocus(this);
    }

    private void ExitFromTray()
    {
        _exitRequested = true;
        PersistPlacement();
        Close();
    }

    private void HideToTray()
    {
        _tray?.EnsureTrayIcon();
        PersistPlacement();
        WindowChrome.HideToTray(this);
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing)
            return;

        var settings = _page.CurrentSettings;
        if (!_exitRequested && settings.MinimizeToTray)
        {
            args.Cancel = true;
            HideToTray();
            return;
        }

        _closing = true;
        PersistPlacement();
        ReminderNotifier.Unregister();
        _tray?.Dispose();
        _tray = null;
    }

    private void OnReminderActivated(string action, string id)
    {
        DispatcherQueue.TryEnqueue(() => _page.HandleReminderAction(action, id));
    }

    private void PersistPlacement()
    {
        try
        {
            var live = _page.CurrentSettings;
            WindowChrome.CapturePlacement(this, live);
            SettingsStore.Save(live);
        }
        catch
        {
            // Placement persistence must not block close.
        }
    }
}