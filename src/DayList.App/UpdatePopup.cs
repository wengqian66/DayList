using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DayList;

/// <summary>
/// Small bottom-right notice shown when a newer release is available. It is a separate
/// always-on-top window, so the prompt is visible even when the main window is hidden.
/// </summary>
internal sealed class UpdatePopup : Window
{
    private const int Width = 360;
    private const int Height = 146;

    public UpdatePopup(UpdateInfo info, Action onUpdate)
    {
        Title = "发现新版本";
        SystemBackdrop = null;

        var ink = ThemeColors.Brush(ThemeColors.Ink);
        var muted = ThemeColors.Brush(ThemeColors.Muted);
        var cream = ThemeColors.Brush(ThemeColors.Cream);
        var line = ThemeColors.Brush(ThemeColors.Line);

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = "发现新版本",
            FontSize = 12,
            Foreground = muted
        });
        stack.Children.Add(new TextBlock
        {
            Text = "v" + info.Version,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ink
        });
        stack.Children.Add(new TextBlock
        {
            Text = "点「立即更新」下载并安装，稍后也可以在设置里更新。",
            FontSize = 12,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var later = new Button { Content = "稍后" };
        later.Click += (_, _) => Close();
        var update = new Button { Content = "立即更新" };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style accentStyle)
            update.Style = accentStyle;
        update.Click += (_, _) =>
        {
            Close();
            onUpdate();
        };
        buttons.Children.Add(later);
        buttons.Children.Add(update);
        stack.Children.Add(buttons);

        var shell = new Border
        {
            Background = cream,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 14, 18, 14),
            Child = stack
        };
        shell.RequestedTheme = ElementTheme.Light;
        Content = shell;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;

        try
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            TryRoundCorners(WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // Older hosts keep the default chrome.
        }

        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var x = work.X + work.Width - Width - 20;
            var y = work.Y + work.Height - Height - 20;
            AppWindow.MoveAndResize(new RectInt32(x, y, Width, Height));
        }
        catch
        {
            // Placement is best-effort.
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
