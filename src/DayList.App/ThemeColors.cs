using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DayList;

/// <summary>
/// Theme-aware colors for UI built in code (reminder / focus popups, time wheels).
/// XAML picks these up automatically via ThemeResource; code-created controls read
/// them here so dark mode does not leave light "islands" behind.
/// </summary>
internal static class ThemeColors
{
    public static Color Cream => Get("SoftCreamColor", 0xF7, 0xF2, 0xEA);

    public static Color Deep => Get("SoftCreamDeepColor", 0xF0, 0xE8, 0xDC);

    public static Color Paper => Get("SoftCardColor", 0xFF, 0xFB, 0xF6);

    public static Color Ink => Get("SoftInkColor", 0x3D, 0x34, 0x29);

    public static Color Muted => Get("SoftMutedInkColor", 0x6B, 0x5F, 0x52);

    public static Color Line => Get("SoftLineColor", 0xE4, 0xD8, 0xC8);

    public static SolidColorBrush Brush(Color color) => new(color);

    private static Color Get(string key, byte r, byte g, byte b)
    {
        if (Application.Current is not null
            && Application.Current.Resources.TryGetValue(key, out var value)
            && value is Color color)
        {
            return color;
        }

        return Color.FromArgb(255, r, g, b);
    }
}
