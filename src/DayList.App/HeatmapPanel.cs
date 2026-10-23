using DayList.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DayList;

/// <summary>
/// Builds the GitHub-style activity grid: 7 rows (Monday first) by N week columns, with
/// month labels along the top and weekday labels down the left.
/// </summary>
internal static class HeatmapPanel
{
    private const double Cell = 10;
    private const double Gap = 2;
    private const double Step = Cell + Gap;
    private const double LeftGutter = 24;
    private const double TopGutter = 18;
    private const double TextHeight = 15;
    private const int Weeks = 53;

    /// <summary>Monday of the column that starts <paramref name="weeks"/> weeks before today's week.</summary>
    public static DateOnly FirstColumn(DateOnly today, int weeks = Weeks)
    {
        var monday = StartOfWeek(today);
        return monday.AddDays(-7 * (weeks - 1));
    }

    public static DateOnly LastColumnEnd(DateOnly today) => StartOfWeek(today).AddDays(6);

    public static void Build(
        Grid host,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, DayActivityCount> counts,
        Action<DateOnly> onPick)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();

        var first = FirstColumn(today);
        var lastDay = LastColumnEnd(today);

        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftGutter) });
        for (var week = 0; week < Weeks; week++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Step) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopGutter) });
        for (var row = 0; row < 7; row++)
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Step) });

        var maxDone = counts.Count == 0 ? 0 : counts.Values.Max(count => count.Done);
        var months = new List<(int Column, int Month)>();

        for (var week = 0; week < Weeks; week++)
        {
            for (var row = 0; row < 7; row++)
            {
                var day = first.AddDays(week * 7 + row);
                if (day > lastDay)
                    continue;

                // A month gets one label, put on the column where it first appears.
                if (row == 0 && (months.Count == 0 || months[^1].Month != day.Month))
                    months.Add((week, day.Month));

                var cell = BuildCell(day, today, counts, maxDone, onPick);
                Grid.SetColumn(cell, week + 1);
                Grid.SetRow(cell, row + 1);
                host.Children.Add(cell);
            }
        }

        AddMonthLabels(host, months);
        AddWeekdayLabels(host);
    }

    private static Border BuildCell(
        DateOnly day,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, DayActivityCount> counts,
        int maxDone,
        Action<DateOnly> onPick)
    {
        counts.TryGetValue(day, out var count);
        var future = day > today;
        var level = future ? -1 : HeatLevel.Of(count?.Done ?? 0, maxDone);

        var cell = new Border
        {
            Width = Cell,
            Height = Cell,
            CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = future ? Transparent() : HeatBrush(level),
            BorderBrush = future ? Transparent() : LineBrush(),
            BorderThickness = future ? new Thickness(0) : new Thickness(1)
        };

        if (future)
            return cell;

        // Later dates are skipped entirely, so only real days answer the pointer.
        var label = day.Month + "月" + day.Day + "日 " + WeekdayName(day) +
                    (day == today ? "（今天）" : "");
        var detail = count is null || count.Total == 0
            ? "没有任务"
            : count.Done == 0
                ? $"完成 0 / 共 {count.Total} 项"
                : $"完成 {count.Done} / 共 {count.Total} 项";
        ToolTipService.SetToolTip(cell, label + " · " + detail);
        cell.Tag = day;
        cell.Tapped += (_, e) =>
        {
            e.Handled = true;
            onPick(day);
        };

        return cell;
    }

    private static void AddMonthLabels(Grid host, List<(int Column, int Month)> months)
    {
        var lastColumn = -4;
        foreach (var (column, month) in months)
        {
            // The grid columns are 12px wide and the label is ~28px, so drop a label that
            // would sit on top of the previous one instead of letting them collide.
            if (column - lastColumn < 4)
                continue;

            var text = new TextBlock
            {
                Text = month + "月",
                FontSize = 10,
                Foreground = MutedBrush(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(text, column + 1);
            Grid.SetColumnSpan(text, 4);
            Grid.SetRow(text, 0);
            host.Children.Add(text);
            lastColumn = column;
        }
    }

    /// <summary>
    /// Monday, Wednesday, Friday. The grid starts on Monday, so their day rows are 0, 2 and 4.
    /// A day row is only 12px tall, which clipped the top strokes of 三 and 五, so each label
    /// spans three rows: the cell is 36px, and centring in it puts the text exactly on the
    /// middle row without any clipping.
    /// </summary>
    private static void AddWeekdayLabels(Grid host)
    {
        var rows = new (int Row, string Text)[] { (0, "一"), (2, "三"), (4, "五") };
        foreach (var (row, text) in rows)
        {
            var block = new TextBlock
            {
                Text = text,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 10,
                Foreground = MutedBrush(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, 0);
            Grid.SetRow(block, row);
            Grid.SetRowSpan(block, 3);
            host.Children.Add(block);
        }
    }

    private static DateOnly StartOfWeek(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return day.AddDays(-offset);
    }

    private static string WeekdayName(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    private static Brush HeatBrush(int level) => level switch
    {
        1 => Resource("SoftHeat1Brush", 0xFF, 0xEB, 0xDC, 0xBE),
        2 => Resource("SoftHeat2Brush", 0xFF, 0xDF, 0xC0, 0x8D),
        3 => Resource("SoftHeat3Brush", 0xFF, 0xC8, 0x95, 0x52),
        4 => Resource("SoftHeat4Brush", 0xFF, 0x8A, 0x5A, 0x20),
        _ => Resource("SoftHeat0Brush", 0xFF, 0xF0, 0xE8, 0xDC)
    };

    private static Brush LineBrush() => Resource("SoftHeatLineBrush", 0x55, 0xE4, 0xD8, 0xC8);

    private static Brush MutedBrush() => Resource("SoftMutedInkBrush", 0xFF, 0x6B, 0x5F, 0x52);

    private static Brush Transparent() => new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

    private static Brush Resource(string key, byte a, byte r, byte g, byte b)
    {
        if (Application.Current is not null
            && Application.Current.Resources.TryGetValue(key, out var value)
            && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}
