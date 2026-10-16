using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace DayList;

internal static class WindowChrome
{
    public const double CornerRadiusDip = 12;

    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCornerRound = 2;
    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    private static readonly Dictionary<Window, Shell> Shells = new();

    public static bool BlurActive { get; private set; }

    public static void Prepare(Window window, int width, int height, double minWidth, double minHeight)
    {
        RemoveNativeFrame(window);
        if (TryBackdrop(window))
            BlurActive = true;

        try
        {
            window.AppWindow.Resize(new SizeInt32(width, height));
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)minWidth;
                presenter.PreferredMinimumHeight = (int)minHeight;
                presenter.IsResizable = true;
                presenter.IsMinimizable = true;
                presenter.IsMaximizable = true;
            }
        }
        catch (Exception)
        {
            // Preferred minimum size is not available on every OS build.
        }

        TrySetIcon(window);
        ApplyRoundPreference(window);
        UpdateRegion(window);
        window.AppWindow.Changed += (_, args) =>
        {
            if (!args.DidSizeChange && !args.DidPresenterChange)
                return;
            UpdateRegion(window);
            window.DispatcherQueue.TryEnqueue(() => SyncShell(window));
        };
    }

    public static void Attach(
        Window window,
        Grid root,
        FrameworkElement titleBar,
        Border? outline,
        FrameworkElement? lights,
        Action<bool>? zoomChanged)
    {
        if (!Shells.TryGetValue(window, out var shell))
        {
            shell = new Shell(window);
            Shells[window] = shell;
            window.Closed += (_, _) => Shells.Remove(window);
        }

        shell.Root = root;
        shell.Outline = outline;
        shell.Lights = lights;
        shell.ZoomChanged = zoomChanged;
        if (!shell.DragHooked)
        {
            shell.DragHooked = true;
            titleBar.PointerPressed += (_, e) => BeginDrag(window, e);
            window.Activated += (_, e) =>
            {
                if (shell.Lights is null)
                    return;
                var active = e.WindowActivationState != WindowActivationState.Deactivated;
                shell.Lights.Opacity = active ? 1 : 0.45;
            };
        }

        if (shell.Grips.Count == 0)
            AddGrips(shell);

        SyncShell(window);
        UpdateRegion(window);
    }

    public static void ApplyBlurSurface(FrameworkElement page, Border? sidebar)
    {
        if (!BlurActive)
            return;

        page.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0xF5, 0xF5, 0xF7));
        if (sidebar is null)
            return;

        sidebar.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xC4, 0xFF, 0xFF, 0xFF));
        sidebar.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0, 0, 0));
    }

    public static void Minimize(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
                presenter.Minimize();
        }
        catch (Exception)
        {
            NativeShell.ShowWindow(WindowNative.GetWindowHandle(window), 6);
        }
    }

    public static void ToggleZoom(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
                return;
            if (presenter.State == OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();
        }
        catch (Exception)
        {
            // Zoom is best-effort when the presenter rejects it.
        }
    }

    public static bool IsMaximized(Window window)
    {
        try
        {
            return window.AppWindow.Presenter is OverlappedPresenter presenter
                && presenter.State == OverlappedPresenterState.Maximized;
        }
        catch (Exception)
        {
            return false;
        }
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

            if (settings.WindowLeft is double left && settings.WindowTop is double top)
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
            settings.WindowLeft = area.X;
            settings.WindowTop = area.Y;
            settings.WindowWidth = size.Width;
            settings.WindowHeight = size.Height;
        }
        catch (Exception)
        {
            // Ignore if the window is closing.
        }
    }

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

    private static void RemoveNativeFrame(Window window)
    {
        try
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = true;
                presenter.IsMinimizable = true;
                presenter.IsMaximizable = true;
            }
        }
        catch (Exception)
        {
            try
            {
                window.ExtendsContentIntoTitleBar = true;
            }
            catch (Exception)
            {
                // Keep the system frame if the presenter rejects both options.
            }
        }
    }

    private static bool TryBackdrop(Window window)
    {
        try
        {
            if (new AccessibilitySettings().HighContrast)
                return false;
        }
        catch (Exception)
        {
            // High contrast is optional.
        }

        try
        {
            if (!CreamBackdrop.IsSupported)
                return false;
            window.SystemBackdrop = new CreamBackdrop();
            return true;
        }
        catch (Exception)
        {
            try
            {
                window.SystemBackdrop = new MicaBackdrop();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static void ApplyRoundPreference(Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var preference = DwmCornerRound;
            DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref preference, sizeof(int));
            // #F5F5F7 as COLORREF 0x00BBGGRR.
            var border = 0x00F7F5F5;
            DwmSetWindowAttribute(hwnd, DwmBorderColor, ref border, sizeof(int));
        }
        catch (Exception)
        {
            // DWM corner preference is Windows 11 only.
        }
    }

    private static void UpdateRegion(Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            if (window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                if (presenter.State == OverlappedPresenterState.Minimized)
                    return;
                if (presenter.State == OverlappedPresenterState.Maximized)
                {
                    SetWindowRgn(hwnd, IntPtr.Zero, true);
                    return;
                }
            }

            var size = window.AppWindow.Size;
            if (size.Width < 48 || size.Height < 48)
                return;

            var dpi = GetDpiForWindow(hwnd);
            if (dpi == 0)
                dpi = 96;
            var diameter = Math.Max(8, (int)Math.Round(CornerRadiusDip * 2 * dpi / 96.0));
            var region = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, diameter, diameter);
            if (region == IntPtr.Zero)
                return;
            if (SetWindowRgn(hwnd, region, true) == 0)
                DeleteObject(region);
        }
        catch (Exception)
        {
            // Rounded clipping is best-effort.
        }
    }

    private static void SyncShell(Window window)
    {
        if (!Shells.TryGetValue(window, out var shell))
            return;

        var maximized = IsMaximized(window);
        if (shell.Outline is not null)
        {
            shell.Outline.CornerRadius = new CornerRadius(maximized ? 0 : CornerRadiusDip);
            shell.Outline.BorderThickness = new Thickness(maximized ? 0 : 1);
        }

        foreach (var grip in shell.Grips)
            grip.IsHitTestVisible = !maximized;

        shell.ZoomChanged?.Invoke(maximized);
    }

    private static void BeginDrag(Window window, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is not UIElement source)
            return;
        if (IsInteractive(source))
            return;
        if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed)
            return;

        var hwnd = WindowNative.GetWindowHandle(window);
        ReleaseCapture();
        SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        e.Handled = true;
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TextBox or ComboBox or CheckBox or Slider)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static void AddGrips(Shell shell)
    {
        var window = shell.Window;
        var span = Math.Max(1, shell.Root.RowDefinitions.Count);
        EdgeGrip Make(int hit, InputSystemCursorShape cursor, HorizontalAlignment h, VerticalAlignment v, double w, double hgt)
        {
            var grip = new EdgeGrip(hit, cursor)
            {
                HorizontalAlignment = h,
                VerticalAlignment = v
            };
            if (!double.IsNaN(w))
                grip.Width = w;
            if (!double.IsNaN(hgt))
                grip.Height = hgt;
            Grid.SetRowSpan(grip, span);
            grip.PointerPressed += (_, e) =>
            {
                if (IsMaximized(window))
                    return;
                if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
                    return;
                var hwnd = WindowNative.GetWindowHandle(window);
                ReleaseCapture();
                SendMessage(hwnd, WmNcLButtonDown, (IntPtr)hit, IntPtr.Zero);
                e.Handled = true;
            };
            shell.Root.Children.Add(grip);
            shell.Grips.Add(grip);
            return grip;
        }

        Make(HtLeft, InputSystemCursorShape.SizeWestEast, HorizontalAlignment.Left, VerticalAlignment.Stretch, 6, double.NaN)
            .Margin = new Thickness(0, 12, 0, 12);
        Make(HtRight, InputSystemCursorShape.SizeWestEast, HorizontalAlignment.Right, VerticalAlignment.Stretch, 6, double.NaN)
            .Margin = new Thickness(0, 12, 0, 12);
        Make(HtTop, InputSystemCursorShape.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 6)
            .Margin = new Thickness(12, 0, 12, 0);
        Make(HtBottom, InputSystemCursorShape.SizeNorthSouth, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 6)
            .Margin = new Thickness(12, 0, 12, 0);
        Make(HtTopLeft, InputSystemCursorShape.SizeNorthwestSoutheast, HorizontalAlignment.Left, VerticalAlignment.Top, 12, 12);
        Make(HtTopRight, InputSystemCursorShape.SizeNortheastSouthwest, HorizontalAlignment.Right, VerticalAlignment.Top, 12, 12);
        Make(HtBottomLeft, InputSystemCursorShape.SizeNortheastSouthwest, HorizontalAlignment.Left, VerticalAlignment.Bottom, 12, 12);
        Make(HtBottomRight, InputSystemCursorShape.SizeNorthwestSoutheast, HorizontalAlignment.Right, VerticalAlignment.Bottom, 12, 12);
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    private sealed class Shell
    {
        public Shell(Window window) => Window = window;

        public Window Window { get; }
        public Grid Root { get; set; } = null!;
        public Border? Outline { get; set; }
        public FrameworkElement? Lights { get; set; }
        public Action<bool>? ZoomChanged { get; set; }
        public bool DragHooked { get; set; }
        public List<EdgeGrip> Grips { get; } = new();
    }

    private sealed class EdgeGrip : Border
    {
        public EdgeGrip(int hit, InputSystemCursorShape cursor)
        {
            Hit = hit;
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
            ProtectedCursor = InputSystemCursor.Create(cursor);
        }

        public int Hit { get; }
    }
}
