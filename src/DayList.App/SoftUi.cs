using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DayList;

/// <summary>
/// Soft-card chrome for ContentDialogs. WinUI paints the sheet with theme brushes
/// (ContentDialogTopOverlay / ContentDialogBackground), so setting Style.Background
/// alone still looks pure white. These brushes are forced on the dialog instance.
/// </summary>
internal static class SoftUi
{
    private static readonly Color Cream = Color.FromArgb(255, 0xF7, 0xF2, 0xEA);
    private static readonly Color Paper = Color.FromArgb(255, 0xFF, 0xFB, 0xF6);
    private static readonly Color Line = Color.FromArgb(255, 0xE4, 0xD8, 0xC8);
    private static readonly Color Ink = Color.FromArgb(255, 0x3D, 0x34, 0x29);
    private static readonly Color Muted = Color.FromArgb(255, 0x7A, 0x6F, 0x62);
    private static readonly Color Deep = Color.FromArgb(255, 0xF0, 0xE8, 0xDC);

    public static Style? DialogFieldStyle => StyleOf("SoftSettingsFieldStyle");

    public static Style? MenuFlyoutPresenterStyle => StyleOf("SoftMenuFlyoutPresenterStyle");

    public static ContentDialog CreateDialog(
        XamlRoot xamlRoot,
        string title,
        object content,
        string primaryText,
        string closeText = "取消",
        ContentDialogButton defaultButton = ContentDialogButton.Primary)
    {
        StyleContent(content);

        var shell = new Border
        {
            Background = new SolidColorBrush(Cream),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(0),
            Child = content as UIElement
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = shell,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = defaultButton,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Light,
            Background = new SolidColorBrush(Cream),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Foreground = new SolidColorBrush(Ink)
        };

        ForceCreamResources(dialog.Resources);

        if (StyleOf("SoftDialogPrimaryButtonStyle") is Style primary)
            dialog.PrimaryButtonStyle = primary;
        else if (StyleOf("AccentButtonStyle") is Style accent)
            dialog.PrimaryButtonStyle = accent;

        if (StyleOf("SoftDialogCloseButtonStyle") is Style close)
            dialog.CloseButtonStyle = close;

        return dialog;
    }

    public static TextBox CreateDialogTextBox(
        string? placeholder = null,
        bool acceptsReturn = false,
        double minHeight = 36,
        double maxHeight = 200)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder ?? "",
            AcceptsReturn = acceptsReturn,
            TextWrapping = acceptsReturn ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = minHeight,
            MaxHeight = maxHeight,
            FontSize = 14,
            RequestedTheme = ElementTheme.Light,
            Background = new SolidColorBrush(Paper),
            Foreground = new SolidColorBrush(Ink),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8)
        };
        if (DialogFieldStyle is Style style)
            box.Style = style;
        return box;
    }

    public static TextBlock CreateDialogBody(string text, double maxWidth = 400)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = maxWidth,
            FontSize = 13,
            Foreground = new SolidColorBrush(Ink),
            LineHeight = 22
        };
    }


    public static void StyleComboBox(ComboBox combo)
    {
        if (combo is null)
            return;

        combo.RequestedTheme = ElementTheme.Light;
        combo.Background = new SolidColorBrush(Paper);
        combo.Foreground = new SolidColorBrush(Ink);
        combo.BorderBrush = new SolidColorBrush(Line);
        combo.CornerRadius = new CornerRadius(14);

        // Popup reads these ThemeResources from the ComboBox's resource lookup.
        ForceComboDropDownResources(combo.Resources);

        if (StyleOf("SoftComboBoxStyle") is Style style)
            combo.Style = style;
        if (StyleOf("SoftComboBoxItemStyle") is Style itemStyle)
            combo.ItemContainerStyle = itemStyle;
    }

    private static void ForceComboDropDownResources(ResourceDictionary resources)
    {
        void Brush(string key, Color color) => resources[key] = new SolidColorBrush(color);

        Brush("ComboBoxDropDownBackground", Cream);
        Brush("ComboBoxDropDownBackgroundPointerOver", Deep);
        Brush("ComboBoxDropDownBackgroundPointerPressed", Line);
        Brush("ComboBoxDropDownBorderBrush", Line);
        Brush("ComboBoxDropDownForeground", Ink);
        Brush("ComboBoxPopupBackgroundThemeBrush", Cream);
        Brush("ComboBoxPopupBorderThemeBrush", Line);
        Brush("ComboBoxPopupForegroundThemeBrush", Ink);
        Brush("ComboBoxItemBackground", Cream);
        Brush("ComboBoxItemBackgroundPointerOver", Deep);
        Brush("ComboBoxItemBackgroundPressed", Line);
        Brush("ComboBoxItemBackgroundSelected", Deep);
        Brush("ComboBoxItemBackgroundSelectedPointerOver", Deep);
        Brush("ComboBoxItemBackgroundSelectedUnfocused", Deep);
        Brush("ComboBoxItemForeground", Ink);
        Brush("ComboBoxItemForegroundPointerOver", Ink);
        Brush("ComboBoxItemForegroundSelected", Ink);
        Brush("ComboBoxItemForegroundSelectedUnfocused", Ink);
        Brush("ComboBoxItemPillFillBrush", Color.FromArgb(255, 0x5B, 0x9B, 0xD5));
        Brush("AcrylicInAppFillColorDefaultBrush", Cream);
        Brush("AcrylicBackgroundFillColorDefaultBrush", Cream);
        Brush("SolidBackgroundFillColorBaseBrush", Cream);
        Brush("LayerFillColorDefaultBrush", Cream);
        Brush("LayerFillColorAltBrush", Cream);
    }

    public static void StyleMenuFlyout(MenuFlyout flyout)
    {
        if (flyout is null)
            return;

        // MenuFlyout is not a FrameworkElement — style the presenter only.
        // Cream brushes for menu surfaces live in App.xaml theme overrides.
        if (MenuFlyoutPresenterStyle is Style style)
        {
            flyout.MenuFlyoutPresenterStyle = style;
        }
        else
        {
            flyout.MenuFlyoutPresenterStyle = new Style(typeof(MenuFlyoutPresenter))
            {
                Setters =
                {
                    new Setter(Control.BackgroundProperty, new SolidColorBrush(Cream)),
                    new Setter(Control.BorderBrushProperty, new SolidColorBrush(Line)),
                    new Setter(Control.BorderThicknessProperty, new Thickness(1)),
                    new Setter(Control.CornerRadiusProperty, new CornerRadius(12)),
                    new Setter(Control.PaddingProperty, new Thickness(4)),
                    new Setter(FrameworkElement.RequestedThemeProperty, ElementTheme.Light)
                }
            };
        }
    }

    private static void ForceCreamResources(ResourceDictionary resources)
    {
        void Brush(string key, Color color) => resources[key] = new SolidColorBrush(color);

        Brush("ContentDialogBackground", Cream);
        Brush("ContentDialogTopOverlay", Cream);
        Brush("ContentDialogBorderBrush", Line);
        Brush("ContentDialogForeground", Ink);
        Brush("ContentDialogSeparatorBorderBrush", Line);
        Brush("ContentDialogSmokeFill", Color.FromArgb(140, 0x3D, 0x34, 0x29));
        Brush("SolidBackgroundFillColorBaseBrush", Cream);
        Brush("LayerFillColorAltBrush", Cream);
        Brush("CardBackgroundFillColorDefaultBrush", Paper);
        Brush("CardBackgroundFillColorSecondaryBrush", Cream);
        Brush("TextFillColorPrimaryBrush", Ink);
        Brush("TextFillColorSecondaryBrush", Muted);
        Brush("ButtonBackground", Deep);
        Brush("ButtonBackgroundPointerOver", Paper);
        Brush("ButtonBackgroundPressed", Line);
        Brush("ButtonForeground", Ink);
        Brush("ButtonBorderBrush", Line);
        Brush("ControlFillColorDefaultBrush", Paper);
        Brush("ControlFillColorSecondaryBrush", Deep);
        Brush("TextControlBackground", Paper);
        Brush("TextControlBackgroundFocused", Paper);
        Brush("TextControlBorderBrush", Line);
        Brush("TextControlForeground", Ink);
        Brush("TextControlPlaceholderForeground", Muted);
    }

    private static void StyleContent(object content)
    {
        switch (content)
        {
            case TextBox box:
                box.RequestedTheme = ElementTheme.Light;
                box.Background = new SolidColorBrush(Paper);
                box.Foreground = new SolidColorBrush(Ink);
                box.BorderBrush = new SolidColorBrush(Line);
                if (box.Style is null && DialogFieldStyle is Style field)
                    box.Style = field;
                break;
            case TextBlock block:
                block.Foreground = new SolidColorBrush(Ink);
                break;
            case ScrollViewer scroll when scroll.Content is not null:
                scroll.Background = new SolidColorBrush(Cream);
                StyleContent(scroll.Content);
                break;
            case Panel panel:
                panel.Background = new SolidColorBrush(Cream);
                foreach (var child in panel.Children)
                    StyleContent(child);
                break;
        }
    }

    private static Style? StyleOf(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Style : null;
}
