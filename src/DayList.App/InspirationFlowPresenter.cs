using DayList.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DayList;

internal static class InspirationFlowPresenter
{
    public static UIElement DayCard(InspirationFlowDay day, bool selectedDay, Action openDay, Action editDay)
    {
        var ink = Brush("SoftInkBrush", 0x3D, 0x34, 0x29);
        var muted = Brush("SoftMutedInkBrush", 0x7A, 0x6F, 0x62);
        var line = Brush("SoftLineBrush", 0xE4, 0xD8, 0xC8);
        var amber = Brush("SoftAmberInkBrush", 0x8A, 0x5A, 0x20);
        var card = Brush("SoftCardBrush", 0xFF, 0xFB, 0xF6);

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = day.Heading,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            VerticalAlignment = VerticalAlignment.Center
        });
        var caption = new TextBlock
        {
            Text = day.Caption,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(caption, 1);
        header.Children.Add(caption);

        if (!selectedDay)
        {
            var open = new Button
            {
                Content = "打开",
                Style = StyleOf("CategoryChipStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(open, "打开 " + day.Caption);
            open.Click += (_, _) => openDay();
            Grid.SetColumn(open, 2);
            header.Children.Add(open);
        }

        var items = new StackPanel { Spacing = 0, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var item in day.Items)
        {
            var body = Row(item, ink, muted, line, amber);
            if (!selectedDay)
            {
                items.Children.Add(body);
                continue;
            }

            var button = new Button
            {
                Content = body,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 1, 0, 1),
                MinWidth = 0,
                MinHeight = 0,
                CornerRadius = new CornerRadius(8)
            };
            AutomationProperties.SetName(button, "编辑灵感");
            ToolTipService.SetToolTip(button, "点开后可以修改或删除");
            button.Click += (_, _) => editDay();
            items.Children.Add(button);
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(items);
        return new Border
        {
            Background = card,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 12, 14, 12),
            Child = stack
        };
    }

    private static Grid Row(InspirationFlowItem item, Brush ink, Brush muted, Brush line, Brush amber)
    {
        var grid = new Grid { MinHeight = 36, ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Fill = line
        });
        grid.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = amber
        });

        var clock = new TextBlock
        {
            Text = item.Clock,
            FontSize = 12,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(clock, 1);
        var text = new TextBlock
        {
            Text = item.Text,
            FontSize = 14,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 2);
        grid.Children.Add(clock);
        grid.Children.Add(text);
        return grid;
    }

    private static Brush Brush(string key, byte r, byte g, byte b)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
            return brush;
        return new SolidColorBrush(Color.FromArgb(255, r, g, b));
    }

    private static Style? StyleOf(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style style
            ? style
            : null;
}
