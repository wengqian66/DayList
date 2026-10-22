using System.Net.Http;
using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;
using VirtualKey = Windows.System.VirtualKey;

namespace DayList;

/// <summary>
/// Always-on-top capture bar opened from the tray「快速添加」. The input holds only the
/// task text; date / time / category come from the explicit buttons, and「AI 生成计划」
/// switches to a mode that asks the model to split the text into tasks.
/// </summary>
public sealed partial class QuickAddWindow : Window
{
    private const int BaseHeight = 220;
    private const int TimePaneHeight = 280;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static QuickAddWindow? _open;

    private readonly AppSettings _settings;
    private readonly PlanGenerationService _plans;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _heightTimer;
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Now);
    private TimeOnly? _time;
    private long _animStart;
    private int _animFrom;
    private int _animTo;
    private int _animCenterY;
    private bool _activated;
    private bool _aiMode;
    private bool _busy;

    private QuickAddWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _plans = new PlanGenerationService(Http);
        Title = "快速添加";

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;

        // Borderless rounded card.
        try
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            TryRoundCorners(WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // Older hosts keep the default chrome; the window still works.
        }

        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var wa = area.WorkArea;
            const int width = 640;
            var x = wa.X + (wa.Width - width) / 2;
            var y = wa.Y + (wa.Height - BaseHeight) / 2;
            AppWindow.MoveAndResize(new RectInt32(x, y, width, BaseHeight));
        }
        catch
        {
            // Placement is best-effort.
        }

        PopulateCategories();
        PopulateTimePickers();
        UpdateDateLabel();
        UpdateTimeLabel();
        FadeIn(Content as FrameworkElement);
        // Activation can fire before the text box is in the visual tree; focus on Loaded too.
        Input.Loaded += (_, _) => FocusInput();

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                if (_activated)
                    Close();
                return;
            }

            _activated = true;
            FocusInput();
        };
    }

    public static void Open()
    {
        if (_open is not null)
        {
            _open.Activate();
            _open.ForceForeground();
            _open.FocusInput();
            return;
        }

        _open = new QuickAddWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Activate();
        _open.ForceForeground();
        _open.FocusInput();
    }

    /// <summary>Activation alone can leave the window behind the foreground app, which
    /// keeps the text box from receiving keyboard input.</summary>
    private void ForceForeground()
    {
        try
        {
            NativeShell.SetForegroundWindow(WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // Foreground is best-effort.
        }
    }

    private void FocusInput()
    {
        Input.Focus(FocusState.Programmatic);
        Input.SelectAll();

        // One dispatcher tick later the window is usually active for real.
        DispatcherQueue.TryEnqueue(() =>
        {
            Input.Focus(FocusState.Programmatic);
            Input.SelectAll();
        });

        // And once more after the fade-in settles, in case activation stole it back.
        DispatcherQueue.TryEnqueue(() =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Input.Focus(FocusState.Programmatic);
                Input.SelectAll();
            });
        });
    }

    private static void FadeIn(FrameworkElement? root)
    {
        if (root is null)
            return;

        root.Opacity = 0;
        root.Loaded += (_, _) =>
        {
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, root);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Begin();
        };
    }

    private void PopulateCategories()
    {
        CategoryPicker.Items.Add(DayTaskCategory.UncategorizedLabel);
        foreach (var name in _settings.TaskCategories)
        {
            var display = DayTaskCategory.Display(name);
            if (!DayTaskCategory.IsUncategorized(name) && !CategoryPicker.Items.Contains(display))
                CategoryPicker.Items.Add(display);
        }

        CategoryPicker.SelectedIndex = 0;
    }

    private void UpdateDateLabel()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        DateLabel.Text = _date == today
            ? "今天"
            : _date == today.AddDays(1)
                ? "明天"
                : _date == today.AddDays(2)
                    ? "后天"
                    : _date.ToString("M月d日");
    }

    private void UpdateTimeLabel() =>
        TimeLabel.Text = _time is TimeOnly clock ? clock.ToString("HH:mm") : "时间";

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            if (TimePane.Visibility == Visibility.Visible)
            {
                HideTimePicker();
                return;
            }

            if (_aiMode)
            {
                ExitAiMode();
                return;
            }

            Close();
            return;
        }

        if (e.Key != VirtualKey.Enter || _busy)
            return;

        e.Handled = true;
        if (_aiMode)
        {
            await RunAiPlanAsync();
            return;
        }

        Save();
    }

    private void DateButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();
        SoftUi.StyleMenuFlyout(flyout);
        var today = DateOnly.FromDateTime(DateTime.Now);

        void Add(string label, DateOnly date)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) =>
            {
                _date = date;
                UpdateDateLabel();
            };
            flyout.Items.Add(item);
        }

        Add("今天", today);
        Add("明天", today.AddDays(1));
        Add("后天", today.AddDays(2));

        flyout.ShowAt(DateButton, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Bottom });
    }

    private void TimeButton_Click(object sender, RoutedEventArgs e) => ShowTimePicker();

    private void AiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_aiMode)
            ExitAiMode();
        else
            EnterAiMode();
    }

    private void EnterAiMode()
    {
        _aiMode = true;
        ModeChip.Visibility = Visibility.Visible;
        ModeChipText.Text = "AI 生成计划";
        Input.Text = "";
        Input.PlaceholderText = "描述你想做的事，AI 会拆成带日期和分类的任务…";
        HintText.Text = "AI 生成计划模式：写一段自然语言，回车后让 AI 拆成任务并写进笔记。";
        SetButtonsEnabled(false);
        Input.Focus(FocusState.Programmatic);
    }

    private void ExitAiMode_Click(object sender, RoutedEventArgs e) => ExitAiMode();

    private void ExitAiMode()
    {
        _aiMode = false;
        ModeChip.Visibility = Visibility.Collapsed;
        Input.Text = "";
        Input.PlaceholderText = "写点什么…";
        HintText.Text = "回车保存；日期、时间、分类用上面的按钮设置。";
        SetButtonsEnabled(true);
        Input.Focus(FocusState.Programmatic);
    }

    private void SetButtonsEnabled(bool enabled)
    {
        DateButton.IsEnabled = enabled;
        TimeButton.IsEnabled = enabled;
        CategoryPicker.IsEnabled = enabled;
    }

    private void PopulateTimePickers()
    {
        for (var i = 0; i < 24; i++)
            HourPicker.Items.Add(i.ToString("00"));
        for (var i = 0; i < 60; i++)
            MinutePicker.Items.Add(i.ToString("00"));
    }

    private void ShowTimePicker()
    {
        var initial = _time ?? new TimeOnly(9, 0);
        HourPicker.SelectedIndex = initial.Hour;
        MinutePicker.SelectedIndex = initial.Minute;
        TimePane.Visibility = Visibility.Visible;
        AnimateHeight(TimePaneHeight);
        HourPicker.Focus(FocusState.Programmatic);
    }

    private void TimeOk_Click(object sender, RoutedEventArgs e)
    {
        var hour = HourPicker.SelectedIndex < 0 ? 9 : HourPicker.SelectedIndex;
        var minute = MinutePicker.SelectedIndex < 0 ? 0 : MinutePicker.SelectedIndex;
        _time = new TimeOnly(hour, minute);
        UpdateTimeLabel();
        HideTimePicker();
    }

    private void TimeClear_Click(object sender, RoutedEventArgs e)
    {
        _time = null;
        UpdateTimeLabel();
        HideTimePicker();
    }

    private void TimeCancel_Click(object sender, RoutedEventArgs e) => HideTimePicker();

    private void HideTimePicker()
    {
        TimePane.Visibility = Visibility.Collapsed;
        AnimateHeight(BaseHeight);
        Input.Focus(FocusState.Programmatic);
    }

    private void AnimateHeight(int target)
    {
        _animFrom = AppWindow.Size.Height;
        _animTo = target;
        _animCenterY = AppWindow.Position.Y + _animFrom / 2;
        _animStart = Environment.TickCount64;

        if (_heightTimer is null)
        {
            _heightTimer = DispatcherQueue.CreateTimer();
            _heightTimer.Interval = TimeSpan.FromMilliseconds(15);
            _heightTimer.IsRepeating = true;
            _heightTimer.Tick += (_, _) => TickHeight();
        }

        _heightTimer.Start();
    }

    private void TickHeight()
    {
        var elapsed = Environment.TickCount64 - _animStart;
        var t = Math.Clamp(elapsed / 170.0, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3);
        var height = (int)Math.Round(_animFrom + (_animTo - _animFrom) * eased);
        try
        {
            AppWindow.MoveAndResize(new RectInt32(AppWindow.Position.X, _animCenterY - height / 2, AppWindow.Size.Width, height));
        }
        catch
        {
            // Resizing is best-effort.
        }

        if (t >= 1)
            _heightTimer?.Stop();
    }

    private async Task RunAiPlanAsync()
    {
        var text = (Input.Text ?? "").Trim();
        if (text.Length == 0)
        {
            ShowStatus("先写几句想做的事，再生成计划。", isError: true);
            return;
        }

        if (!_settings.IsApiConfigured)
        {
            ShowStatus("还没配置 AI 接口，请先到设置里填写 Base URL、API Key 和模型。", isError: true);
            return;
        }

        if (_busy)
            return;

        _busy = true;
        ShowStatus("正在用 AI 拆解计划…", isError: false);
        try
        {
            var result = await _plans.GenerateAsync(
                _settings,
                text,
                DateOnly.FromDateTime(DateTime.Now),
                _settings.TaskCategories,
                CancellationToken.None);

            if (result.Status == PlanGenerateStatus.Ok)
            {
                PlanDraftApply.Apply(_settings, result.Tasks);
                Close();
                return;
            }

            ShowStatus(result.Message, isError: true);
        }
        catch (Exception ex)
        {
            ShowStatus("生成失败：" + ex.Message, isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xB3, 0x3A, 0x3A))
            : ThemeColors.Brush(ThemeColors.Muted);
    }

    private void Save()
    {
        var text = (Input.Text ?? "").Trim();
        if (text.Length == 0 || !_settings.HasVault)
        {
            Close();
            return;
        }

        try
        {
            var picked = CategoryPicker.SelectedItem?.ToString() ?? DayTaskCategory.UncategorizedLabel;
            var category = DayTaskCategory.Normalize(picked);

            var snapshot = DailyNoteStore.Read(_settings, _date);
            var tasks = snapshot.Tasks.ToList();
            tasks.Add(new DayTask { Text = text, Category = category });
            DailyNoteStore.Write(_settings, _date, tasks);

            if (_time is TimeOnly clock)
                ReminderStore.SetForTask(ReminderStore.DefaultPath, null, text, _date, clock);
        }
        catch
        {
            // Best-effort. The main window surfaces write errors on its own.
        }
        finally
        {
            Close();
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void TryRoundCorners(IntPtr hwnd)
    {
        try
        {
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Rounded corners are cosmetic.
        }
    }
}
