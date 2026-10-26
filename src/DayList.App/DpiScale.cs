using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace DayList;

/// <summary>
/// Converts the app's layout sizes (DIPs) into window pixels.
/// <para>
/// XAML lays out in device-independent pixels, but <c>AppWindow.Resize</c> and
/// <c>MoveAndResize</c> take physical pixels. Feeding DIP constants straight into them made
/// every window smaller on a scaled display: at 150% the capture bar was 640 physical px,
/// i.e. only 427 DIPs, and its date/time/category row did not fit.
/// </para>
/// </summary>
internal static class DpiScale
{
    /// <summary>Display scale for this window; 1.0 when the query fails.</summary>
    public static double Of(Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>DIPs to whole pixels.</summary>
    public static int Px(Window window, double dips) =>
        (int)Math.Round(dips * Of(window), MidpointRounding.AwayFromZero);

    /// <summary>DIPs to whole pixels, skipping the DPI query when the scale is already known.</summary>
    public static int Px(double scale, double dips) =>
        (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    /// <summary>A rect that keeps the pixel origin but takes DIP dimensions.</summary>
    public static RectInt32 Place(Window window, int x, int y, double widthDips, double heightDips)
    {
        var scale = Of(window);
        return new RectInt32(x, y, Px(scale, widthDips), Px(scale, heightDips));
    }

    /// <summary>Pixels to DIPs, for storing a size that restores the same on any monitor.</summary>
    public static double Dips(Window window, int pixels)
    {
        var scale = Of(window);
        return scale <= 0 ? pixels : pixels / scale;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
