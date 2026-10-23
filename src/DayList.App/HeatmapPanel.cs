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
    private const double LeftGutter = 24;
    private const double TopGutter = 18;

    /// <summary>Column counts offered by the range switcher.</summary>
    public const int WeeksYear = 53;
    public const int WeeksHalf = 26;
    public const int WeeksQuarter = 13;

    /// <summary>Smallest and largest square the grid will draw.</summary>
    private const double MinStep = 10;
    private const double MaxStep = 34;

    /// <summary>Monday of the column that starts <paramref name="weeks"/> weeks before today's week.</summary>
    public static DateOnly FirstColumn(DateOnly today, int weeks = WeeksYear)
    {
        var monday = StartOfWeek(today);
        return monday.AddDays(-7 * (Math.Max(1, weeks) - 1));
    }

    public static DateOnly LastColumnEnd(DateOnly today) => StartOfWeek(today).AddDays(6);

    /// <summary>
    /// Column pitch that fills <paramref name="availableWidth"/>. The grid has a fixed number
    /// of columns, so a wider window — or a shorter range — simply gets bigger squares instead
    /// of empty card space.
    /// </summary>
    public static double StepFor(double availableWidth, int weeks = WeeksYear)
    {
        if (availableWidth <= 0 || double.IsNaN(availableWidth))
            return 12;

        var step = (availableWidth - LeftGutter) / Math.Max(1, weeks);
        return Math.Floor(Math.Clamp(step, MinStep, MaxStep));
    }

    public static void Build(
        Grid host,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, DayActivityCount> counts,
        Action<DateOnly> onPick,
        double availableWidth = 0,
        int weeks = WeeksYear)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        host.RowDefinitions.Clear();

        weeks = Math.Max(1, weeks);
        var step = StepFor(availableWidth, weeks);
        var gap = step >= 16 ? 3 : 2;
        var cell = step - gap;

        var first = FirstColumn(today, weeks);
        var lastDay = LastColumnEnd(today);

        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LeftGutter) });
        for (var week = 0; week < weeks; week++)
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(step) });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TopGutter) });
        for (var row = 0; row < 7; row++)
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(step) });

        var maxDone = counts.Count == 0 ? 0 : counts.Values.Max(count => count.Done);
        var months = new List<(int Column, int Month)>();

        for (var week = 0; week < weeks; week++)
        {
            for (var row = 0; row < 7; row++)
            {
                var day = first.AddDays(week * 7 + row);
                if (day > lastDay)
                    continue;

                // A month gets one label, put on the column where it first appears.
                if (row == 0 && (months.Count == 0 || months[^1].Month != day.Month))
                    months.Add((week, day.Month));

                var cellBorder = BuildCell(day, today, counts, maxDone, onPick, cell);
                Grid.SetColumn(cellBorder, week + 1);
                Grid.SetRow(cellBorder, row + 1);
                host.Children.Add(cellBorder);
            }
        }

        AddMonthLabels(host, months, step);
        AddWeekdayLabels(host);
    }

    private static Border BuildCell(
        DateOnly day,
        DateOnly today,
        IReadOnlyDictionary<DateOnly, DayActivityCount> counts,
        int maxDone,
        Action<DateOnly> onPick,
        double size)
    {
        counts.TryGetValue(day, out var count);
        var future = day > today;
        var level = future ? -1 : HeatLevel.Of(count?.Done ?? 0, maxDone);

        var cell = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(2, size * 0.22)),
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

    private static void AddMonthLabels(Grid host, List<(int Column, int Month)> months, double step)
    {
        // Wide cells can afford a label every other column; narrow ones need more room.
        var minGap = step >= 20 ? 2 : 3;
        var fontSize = step >= 16 ? 11 : 10;
        var placed = new List<(int Column, TextBlock Block)>();

        foreach (var (column, month) in months)
        {
            // A label on the last column would be cut off by the grid edge.
            if (column + 1 >= host.ColumnDefinitions.Count)
                break;

            // Two months can start one column apart (the range begins mid-month). Rather than
            // shoving the newer label to the right — which made those gaps look uneven — drop
            // the older, shorter partial month and keep every remaining label on its own column.
            while (placed.Count > 0 && column - placed[^1].Column < minGap)
            {
                host.Children.Remove(placed[^1].Block);
                placed.RemoveAt(placed.Count - 1);
            }

            var text = new TextBlock
            {
                Text = month + "月",
                FontSize = fontSize,
                Foreground = MutedBrush(),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(text, column + 1);
            Grid.SetColumnSpan(text, Math.Max(2, minGap));
            Grid.SetRow(text, 0);
            host.Children.Add(text);
            placed.Add((column, text));
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
