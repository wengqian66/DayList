using System.Globalization;
using DayList.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace DayList;

/// <summary>
/// Hour and minute columns. Scrolling, the mouse wheel, or the arrow keys
/// move the value sitting in the center band. There is no text field.
/// </summary>
internal sealed class ReminderTimeWheels : StackPanel
{
    private const double HeaderSlot = 22;
    private const double WellWidth = 112;

    private static readonly SolidColorBrush Ink = Brush(0xFF, 0x3D, 0x34, 0x29);
    private static readonly SolidColorBrush Muted = Brush(0xFF, 0x7A, 0x6F, 0x62);
    private static readonly SolidColorBrush Paper = Brush(0xFF, 0xFF, 0xFB, 0xF6);
    private static readonly SolidColorBrush Line = Brush(0xFF, 0xE4, 0xD8, 0xC8);
    private static readonly SolidColorBrush Deep = Brush(0xFF, 0xF0, 0xE8, 0xDC);
    private static readonly SolidColorBrush Clear = Brush(0x00, 0x00, 0x00, 0x00);

    private readonly WheelColumn _hours;
    private readonly WheelColumn _minutes;
    private readonly TextBlock _readout;

    public ReminderTimeWheels(TimeOnly initial)
    {
        Spacing = 10;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        var hour = Math.Clamp(initial.Hour, 0, 23);
        var minute = Math.Clamp(initial.Minute, 0, 59);
        _hours = new WheelColumn("时", "小时", 24, hour);
        _minutes = new WheelColumn("分", "分钟", 60, minute);
        _hours.ValueChanged += (_, _) => RefreshReadout();
        _minutes.ValueChanged += (_, _) => RefreshReadout();

        var row = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var colonHost = new StackPanel { Width = 16 };
        colonHost.Children.Add(new Border { Height = HeaderSlot, IsHitTestVisible = false });
        var colon = new TextBlock
        {
            Text = ":",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ink,
            IsHitTestVisible = false
        };
        colonHost.Children.Add(new Grid
        {
            Height = ReminderWheelLayout.BandHeight,
            Children = { colon }
        });

        Grid.SetColumn(_hours, 0);
        Grid.SetColumn(colonHost, 1);
        Grid.SetColumn(_minutes, 2);
        row.Children.Add(_hours);
        row.Children.Add(colonHost);
        row.Children.Add(_minutes);

        _readout = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink
        };
        AutomationProperties.SetName(_readout, "已选时间");
        RefreshReadout();

        Children.Add(row);
        Children.Add(_readout);

        Loaded += (_, _) =>
        {
            _readout.Foreground = Ink;
            colon.Foreground = Ink;
        };
    }

    public TimeOnly Time => new(_hours.Value, _minutes.Value);

    public void FocusHour() => _hours.FocusWheel();

    private void RefreshReadout() =>
        _readout.Text = Time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Color.FromArgb(a, r, g, b));

    private sealed class WheelColumn : StackPanel
    {
        private readonly int _count;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _items;
        private readonly TextBlock _header;
        private readonly Border _well;
        private readonly List<TextBlock> _labels = new();
        private int _value;
        private int _wheelAccum;
        private bool _ready;
        private bool _suppress;

        public WheelColumn(string caption, string automationName, int count, int initial)
        {
            _count = count;
            _value = Math.Clamp(initial, 0, count - 1);
            Spacing = 0;

            _header = new TextBlock
            {
                Text = caption,
                FontSize = 12,
                Height = 16,
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Muted,
                IsHitTestVisible = false
            };

            _items = new StackPanel { Background = Clear };
            var total = ReminderWheelLayout.SlotCount(count);
            for (var i = 0; i < total; i++)
            {
                var number = i - ReminderWheelLayout.SpacerRows;
                int? value = number >= 0 && number < count ? number : null;
                var label = new TextBlock
                {
                    Text = value is int n ? n.ToString("00", CultureInfo.InvariantCulture) : "",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    Foreground = Muted,
                    IsHitTestVisible = false
                };
                var chrome = new Border
                {
                    Margin = new Thickness(6, 2, 6, 2),
                    CornerRadius = new CornerRadius(8),
                    Background = Clear,
                    Child = label
                };
                var row = new Border
                {
                    Height = ReminderWheelLayout.ItemHeight,
                    Background = Clear,
                    Tag = value,
                    Child = chrome
                };
                row.Tapped += (_, args) =>
                {
                    if (row.Tag is int picked)
                    {
                        Center(picked, animate: true);
                        args.Handled = true;
                    }
                };
                _labels.Add(label);
                _items.Children.Add(row);
            }

            _scroll = new ScrollViewer
            {
                Height = ReminderWheelLayout.BandHeight,
                Content = _items,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ZoomMode = ZoomMode.Disabled,
                Padding = new Thickness(0),
                Background = Clear,
                IsTabStop = true,
                BringIntoViewOnFocusChange = false,
                RequestedTheme = ElementTheme.Light
            };
            AutomationProperties.SetName(_scroll, automationName);
            _scroll.ViewChanged += OnViewChanged;
            _scroll.PointerWheelChanged += OnPointerWheel;
            _scroll.PreviewKeyDown += OnPreviewKeyDown;
            _scroll.SizeChanged += (_, _) => TryPlace();
            _scroll.RegisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty, (_, _) => TryPlace());
            _scroll.RegisterPropertyChangedCallback(ScrollViewer.ViewportHeightProperty, (_, _) => TryPlace());

            _well = new Border
            {
                Width = WellWidth,
                Background = Paper,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = _scroll
            };

            Children.Add(_header);
            Children.Add(_well);
            Loaded += (_, _) => ApplyChrome();
        }

        public int Value => _value;

        public event EventHandler? ValueChanged;

        public void FocusWheel() => _scroll.Focus(FocusState.Programmatic);

        private void ApplyChrome()
        {
            _header.Foreground = Muted;
            _well.Background = Paper;
            _well.BorderBrush = Line;
            _scroll.Background = Paper;
            _scroll.Resources["ScrollViewerBackground"] = Paper;
            _items.Background = Clear;
            Paint();
            TryPlace();
        }

        private void TryPlace()
        {
            if (_ready || _scroll.ViewportHeight <= 1 || _scroll.ExtentHeight <= 1)
                return;

            _ready = true;
            Center(_value, animate: false);
        }

        private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_suppress)
            {
                if (!e.IsIntermediate)
                {
                    _suppress = false;
                    Paint();
                }

                return;
            }

            var picked = ReminderWheelLayout.ValueAtOffset(
                _scroll.VerticalOffset,
                _scroll.ViewportHeight,
                _count);
            if (picked is int value)
                Commit(value);
            Paint();

            if (!e.IsIntermediate && picked is int settle)
                Center(settle, animate: true);
        }

        private void OnPointerWheel(object? sender, PointerRoutedEventArgs e)
        {
            var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
            e.Handled = true;
            if (delta == 0)
                return;

            _wheelAccum += delta;
            var steps = 0;
            while (_wheelAccum >= 80)
            {
                steps--;
                _wheelAccum -= 80;
            }

            while (_wheelAccum <= -80)
            {
                steps++;
                _wheelAccum += 80;
            }

            if (steps != 0)
                Step(steps);
        }

        private void OnPreviewKeyDown(object? sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Up)
            {
                Step(-1);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Down)
            {
                Step(1);
                e.Handled = true;
            }
        }

        private void Step(int direction) => Center(_value + direction, animate: true);

        private void Center(int value, bool animate)
        {
            Commit(value);
            if (_scroll.ViewportHeight <= 1 || _scroll.ExtentHeight <= 1)
            {
                Paint();
                return;
            }

            var target = ReminderWheelLayout.OffsetForValue(
                _value,
                _scroll.ViewportHeight,
                _scroll.ExtentHeight,
                _count);
            if (Math.Abs(_scroll.VerticalOffset - target) < 0.5)
            {
                _suppress = false;
                Paint();
                return;
            }

            _suppress = true;
            var started = _scroll.ChangeView(null, target, null, !animate);
            if (!started)
            {
                _suppress = false;
                Paint();
            }
        }

        private void Commit(int value)
        {
            var next = Math.Clamp(value, 0, _count - 1);
            if (next == _value)
                return;

            _value = next;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Paint()
        {
            var raw = (_scroll.VerticalOffset + Math.Max(_scroll.ViewportHeight, ReminderWheelLayout.BandHeight) / 2)
                / ReminderWheelLayout.ItemHeight - 0.5;
            var centerIndex = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            for (var i = 0; i < _labels.Count; i++)
            {
                var label = _labels[i];
                if (_items.Children[i] is not Border row)
                {
                    label.Opacity = 0;
                    continue;
                }

                if (row.Tag is not int)
                {
                    label.Opacity = 0;
                    if (row.Child is Border spacer)
                        spacer.Background = Clear;
                    continue;
                }

                var distance = Math.Abs(i - centerIndex);
                var selected = distance == 0;
                if (row.Child is Border chrome)
                    chrome.Background = selected ? Deep : Clear;
                label.Opacity = selected ? 1 : distance == 1 ? 0.78 : 0.4;
                label.FontSize = selected ? 18 : 14;
                label.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                label.Foreground = selected ? Ink : Muted;
            }
        }
    }
}
