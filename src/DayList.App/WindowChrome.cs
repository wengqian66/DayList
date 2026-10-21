using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DayList;

internal static class WindowChrome
{
    public static void Prepare(Window window, int width, int height, double minWidth, double minHeight)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception)
        {
            // Windows 10 keeps the default backdrop when Mica is unavailable.
        }

        try
        {
            window.AppWindow.Resize(new SizeInt32(width, height));
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)minWidth;
                presenter.PreferredMinimumHeight = (int)minHeight;
            }
        }
        catch (Exception)
        {
            // Preferred minimum size is not available on every OS build.
        }

        TrySetIcon(window);
    }

    public static void ApplyPlacement(Window window, AppSettings settings, int fallbackWidth, int fallbackHeight)
    {
        try
        {
            var width = (int)(settings.WindowWidth ?? fallbackWidth);
            var height = (int)(settings.WindowHeight ?? fallbackHeight);

            // Soft-card layout needs more horizontal room than older Fluent defaults (480–920).
            // One-shot migrate: bump clearly-narrow remembered widths to the new default.
            if (settings.WindowWidth is double savedWidth && savedWidth < 1000)
                width = fallbackWidth;

            width = Math.Clamp(width, Math.Min(820, fallbackWidth), 4000);
            height = Math.Clamp(height, 480, 4000);
            window.AppWindow.Resize(new SizeInt32(width, height));

            if (settings.WindowLeft is double left && settings.WindowTop is double top
                && IsVisibleOnScreen((int)left, (int)top, width, height))
            {
                window.AppWindow.Move(new PointInt32((int)left, (int)top));
            }
        }
        catch (Exception)
        {
            // Placement is best-effort across displays.
        }
    }

    public static void CapturePlacement(Window window, AppSettings settings)
    {
        try
        {
            var area = window.AppWindow.Position;
            var size = window.AppWindow.Size;
            // Don't persist the minimized/hidden sentinel (-32000, -32000) or a
            // position that would restore off-screen; keep the last good value.
            if (IsVisibleOnScreen(area.X, area.Y, size.Width, size.Height))
            {
                settings.WindowLeft = area.X;
                settings.WindowTop = area.Y;
            }

            settings.WindowWidth = size.Width;
            settings.WindowHeight = size.Height;
        }
        catch (Exception)
        {
            // Ignore if the window is closing.
        }
    }

    private static bool IsVisibleOnScreen(int left, int top, int width, int height)
    {
        // Windows reports minimized/hidden windows at (-32000, -32000); reject that
        // sentinel and any other far-off coordinate so we never restore off-display.
        if (left <= -16384 || top <= -16384 || left >= 16384 || top >= 16384)
            return false;

        int vx = GetSystemMetrics(SmXVirtualScreen);
        int vy = GetSystemMetrics(SmYVirtualScreen);
        int vw = GetSystemMetrics(SmCxVirtualScreen);
        int vh = GetSystemMetrics(SmCyVirtualScreen);
        if (vw <= 0 || vh <= 0)
            return true; // Virtual-screen query failed; trust the saved value.

        // At least part of the window must land inside the union of all displays.
        return left < vx + vw && top < vy + vh && left + width > vx && top + height > vy;
    }

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static void SetAlwaysOnTop(Window window, bool enabled)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.IsAlwaysOnTop = enabled;
        }
        catch (Exception)
        {
            // Older builds may not expose IsAlwaysOnTop.
        }
    }

    public static void ShowAndFocus(Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (NativeShell.IsIconic(hwnd))
                NativeShell.ShowWindow(hwnd, 9); // SW_RESTORE
            window.AppWindow.Show();
            window.Activate();
            NativeShell.SetForegroundWindow(hwnd);
        }
        catch (Exception)
        {
            try
            {
                window.Activate();
            }
            catch
            {
                // Ignore focus races during shutdown.
            }
        }
    }

    public static void HideToTray(Window window)
    {
        try
        {
            window.AppWindow.Hide();
        }
        catch (Exception)
        {
            // Hide may fail if already closing.
        }
    }

    private static void TrySetIcon(Window window)
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "DayList.ico"),
                Path.Combine(AppContext.BaseDirectory, "DayList.ico")
            };
            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;
                window.AppWindow.SetIcon(path);
                return;
            }
        }
        catch (Exception)
        {
            // Icon is optional on hosts that reject SetIcon.
        }
    }
}
