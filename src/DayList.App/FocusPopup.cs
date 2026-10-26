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

internal readonly record struct FocusPrompt(string Label, bool Primary, Action? Act);

/// <summary>
/// Soundless bottom-left card. It does not register with reminders or play a sound.
/// </summary>
internal sealed class FocusPopup : Window
{
    private const int Width = 360;
    private const int Height = 188;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private static FocusPopup? _current;

    private bool _closed;

    private FocusPopup(string title, string message, IReadOnlyList<FocusPrompt> prompts)
    {
        Title = title;
        SystemBackdrop = null;

        var ink = ThemeColors.Brush(ThemeColors.Ink);
        var muted = ThemeColors.Brush(ThemeColors.Muted);
        var cream = ThemeColors.Brush(ThemeColors.Cream);
        var line = ThemeColors.Brush(ThemeColors.Line);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ink
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var prompt in prompts)
        {
            var captured = prompt;
            var button = ActionButton(captured.Label, captured.Primary);
            button.Click += (_, _) =>
            {
                try
                {
                    captured.Act?.Invoke();
                }
                catch
                {
                    // The page reports its own errors.
                }

                CloseFromUser();
            };
            actions.Children.Add(button);
        }

        stack.Children.Add(actions);

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
        Closed += (_, _) =>
        {
            _closed = true;
            if (ReferenceEquals(_current, this))
                _current = null;
        };
    }

    public static void Show(string title, string message, params FocusPrompt[] prompts)
    {
        Dismiss();
        var popup = new FocusPopup(title, message, prompts);
        _current = popup;
        try
        {
            popup.ApplyChrome();
            popup.MoveToCorner();
            popup.ShowNoActivate();
        }
        catch
        {
            if (ReferenceEquals(_current, popup))
                _current = null;
            try
            {
                popup.CloseFromUser();
            }
            catch
            {
                // The window never became visible.
            }
        }
    }

    public static void Dismiss()
    {
        var current = _current;
        if (current is null)
            return;
        current.CloseFromUser();
    }

    private void CloseFromUser()
    {
        if (_closed)
            return;
        try
        {
            Close();
        }
        catch
        {
            _closed = true;
            if (ReferenceEquals(_current, this))
                _current = null;
        }
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
                // A thin system frame is still a corner card.
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
            MoveToCorner();
            var now = GetForegroundWindow();
            if (now == hwnd && previous != IntPtr.Zero && previous != hwnd)
                SetForegroundWindow(previous);
        });
    }

    private void MoveToCorner()
    {
        var work = WorkArea();
        // Width/Height are DIPs; the corner maths works in pixels.
        var pixelWidth = DpiScale.Px(this, Width);
        var pixelHeight = DpiScale.Px(this, Height);
        var place = FocusCorner.Place(new PixelRect(work.X, work.Y, work.Width, work.Height), pixelWidth, pixelHeight);
        AppWindow.MoveAndResize(new RectInt32(place.X, place.Y, pixelWidth, pixelHeight));
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

    private static Button ActionButton(string label, bool primary)
    {
        var button = new Button
        {
            Content = label,
            MinHeight = 34,
            FontSize = 13,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 6, 12, 6)
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
            var preference = 2;
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
