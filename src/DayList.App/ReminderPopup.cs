using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace DayList;

/// <summary>
/// Compact bottom-right reminder. It does not activate or show the main window.
/// </summary>
internal sealed class ReminderPopup : Window
{
    private const int CompactWidth = 380;
    private const int CompactHeight = 196;
    private const int ExpandedWidth = 420;
    private const int ExpandedHeight = 520;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private static readonly List<ReminderPopup> Open = new();

    private readonly TaskReminder _reminder;
    private readonly bool _playSound;
    private readonly Action _onOpen;
    private readonly Action _onSnooze;
    private readonly Action _onRetimed;
    private readonly Action _onDismiss;
    private readonly StackPanel _body;
    private readonly TextBlock _error;
    private bool _expanded;
    private bool _closed;
    private bool _dismissed;

    private ReminderPopup(
        TaskReminder reminder,
        bool playSound,
        Action onOpen,
        Action onSnooze,
        Action onRetimed,
        Action onDismiss)
    {
        _reminder = reminder;
        _playSound = playSound;
        _onOpen = onOpen;
        _onSnooze = onSnooze;
        _onRetimed = onRetimed;
        _onDismiss = onDismiss;

        Title = "到点了";
        SystemBackdrop = null;

        var ink = Brush(0xFF, 0x3D, 0x34, 0x29);
        var muted = Brush(0xFF, 0x7A, 0x6F, 0x62);
        var cream = Brush(0xFF, 0xF7, 0xF2, 0xEA);
        var line = Brush(0xFF, 0xE4, 0xD8, 0xC8);

        _error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush(0xFF, 0xB3, 0x3A, 0x3A),
            Visibility = Visibility.Collapsed
        };

        _body = new StackPanel { Spacing = 8 };
        BuildActions();

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(reminder.Time) ? "到点了" : reminder.Time + "  到点了",
            FontSize = 12,
            Foreground = muted
        };
        var task = new TextBlock
        {
            Text = reminder.TaskText,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(title);
        stack.Children.Add(task);
        stack.Children.Add(_body);
        stack.Children.Add(_error);

        var shell = new Border
        {
            Background = cream,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 14, 16, 14),
            Child = stack
        };
        shell.RequestedTheme = ElementTheme.Light;
        Content = shell;

        Closed += (_, _) => Release(dismissedByUser: true);
    }

    public static void Show(
        TaskReminder reminder,
        bool playSound,
        Action onOpen,
        Action onSnooze,
        Action onRetimed,
        Action onDismiss)
    {
        var popup = new ReminderPopup(reminder, playSound, onOpen, onSnooze, onRetimed, onDismiss);
        Open.Add(popup);
        try
        {
            popup.ApplyChrome();
            popup.MoveToStack(Open.Count - 1, popup._expanded);
            popup.ShowNoActivate();
        }
        catch
        {
            Open.Remove(popup);
            try
            {
                popup.Close();
            }
            catch
            {
                // The window never became visible.
            }

            throw;
        }
    }

    private void BuildActions()
    {
        _body.Children.Clear();
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var open = ActionButton("打开", primary: true);
        open.Click += (_, _) => Finish(_onOpen);
        var snooze = ActionButton("推迟 10 分", primary: false);
        snooze.Click += (_, _) => Finish(_onSnooze);
        var edit = ActionButton("改时间", primary: false);
        edit.Click += (_, _) => ShowWheels();
        var close = ActionButton("关闭", primary: false);
        close.Click += (_, _) => Close();

        Grid.SetColumn(open, 0);
        Grid.SetRow(open, 0);
        Grid.SetColumn(snooze, 1);
        Grid.SetRow(snooze, 0);
        Grid.SetColumn(edit, 0);
        Grid.SetRow(edit, 1);
        Grid.SetColumn(close, 1);
        Grid.SetRow(close, 1);
        grid.Children.Add(open);
        grid.Children.Add(snooze);
        grid.Children.Add(edit);
        grid.Children.Add(close);
        _body.Children.Add(grid);
    }

    private void ShowWheels()
    {
        if (_expanded || _closed)
            return;

        _expanded = true;
        _error.Text = "";
        _error.Visibility = Visibility.Collapsed;
        _body.Children.Clear();

        TimeOnly initial;
        try
        {
            initial = ReminderSchedule.SuggestTime(_reminder.Date, DateTime.Now, _reminder.Time);
        }
        catch
        {
            initial = new TimeOnly(9, 0);
        }

        var wheels = new ReminderTimeWheels(initial);
        var save = ActionButton("保存", primary: true);
        var back = ActionButton("返回", primary: false);
        save.Click += (_, _) => SaveTime(wheels.Time);
        back.Click += (_, _) =>
        {
            _expanded = false;
            _error.Visibility = Visibility.Collapsed;
            BuildActions();
            MoveToStack(IndexOfThis(), expanded: false);
        };

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(save, 0);
        Grid.SetColumn(back, 1);
        row.Children.Add(save);
        row.Children.Add(back);

        _body.Children.Add(wheels);
        _body.Children.Add(row);
        MoveToStack(IndexOfThis(), expanded: true);
        DispatcherQueue.TryEnqueue(() => wheels.FocusHour());
    }

    private void SaveTime(TimeOnly time)
    {
        if (_reminder.Date.ToDateTime(time) <= DateTime.Now)
        {
            _error.Text = "这个时间已经过了。";
            _error.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            ReminderStore.SetForTask(
                ReminderStore.DefaultPath,
                _reminder.Id,
                _reminder.TaskText,
                _reminder.Date,
                time);
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
            _error.Visibility = Visibility.Visible;
            return;
        }

        _onRetimed();
        Close();
    }

    private void Finish(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // The page reports its own errors. Still close the card.
        }

        Close();
    }

    private void ApplyChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            try
            {
                AppWindow.IsShownInSwitchers = false;
            }
            catch (Exception)
            {
                // Switcher visibility is optional.
            }

            try
            {
                presenter.SetBorderAndTitleBar(false, false);
            }
            catch (Exception)
            {
                // A thin system frame is still a corner card, not the main window.
            }
        }
    }

    private void ShowNoActivate()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var previous = GetForegroundWindow();
        try
        {
            AppWindow.Show(false);
        }
        catch (Exception)
        {
            AppWindow.Show();
        }

        ShowWindow(hwnd, SwShowNoActivate);
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpShowWindow | 0x0002 | 0x0001);
        TryRoundCorners(hwnd);

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed)
                return;
            MoveToStack(IndexOfThis(), _expanded);
            var now = GetForegroundWindow();
            if (now == hwnd && previous != IntPtr.Zero && previous != hwnd)
                SetForegroundWindow(previous);
            if (_playSound)
            {
                try
                {
                    ElementSoundPlayer.Play(ElementSoundKind.Invoke);
                }
                catch
                {
                    // Sound is optional.
                }
            }
        });
    }

    private void MoveToStack(int stackIndex, bool expanded)
    {
        var width = expanded ? ExpandedWidth : CompactWidth;
        var height = expanded ? ExpandedHeight : CompactHeight;
        var work = WorkArea();
        var place = ReminderCorner.Place(
            new PixelRect(work.X, work.Y, work.Width, work.Height),
            width,
            height,
            stackIndex);
        AppWindow.MoveAndResize(new RectInt32(place.X, place.Y, width, height));
    }

    private RectInt32 WorkArea()
    {
        try
        {
            if (GetCursorPos(out var point))
            {
                var area = DisplayArea.GetFromPoint(new PointInt32(point.X, point.Y), DisplayAreaFallback.Primary);
                if (area is not null)
                    return area.WorkArea;
            }
        }
        catch
        {
            // Fall through to the primary monitor.
        }

        return DisplayArea.Primary.WorkArea;
    }

    private int IndexOfThis()
    {
        var index = Open.IndexOf(this);
        return index < 0 ? 0 : index;
    }

    private void Release(bool dismissedByUser)
    {
        if (_closed)
            return;
        _closed = true;
        Open.Remove(this);
        for (var i = 0; i < Open.Count; i++)
            Open[i].MoveToStack(i, Open[i]._expanded);
        if (dismissedByUser && !_dismissed)
        {
            _dismissed = true;
            try
            {
                _onDismiss();
            }
            catch
            {
                // The page may already be unloading.
            }
        }
    }

    private static Button ActionButton(string label, bool primary)
    {
        var button = new Button
        {
            Content = label,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34,
            FontSize = 13,
            CornerRadius = new CornerRadius(10)
        };
        if (Application.Current.Resources.TryGetValue(
                primary ? "SoftDialogPrimaryButtonStyle" : "SoftDialogCloseButtonStyle",
                out var value)
            && value is Style style)
        {
            button.Style = style;
        }

        return button;
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    private static void TryRoundCorners(IntPtr hwnd)
    {
        try
        {
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Square corners still read as a card.
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativeShell.Point point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
