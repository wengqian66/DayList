using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace DayList;

/// <summary>Determinate ring: thin stroke, round caps, starting at 12 o'clock.</summary>
public sealed class RingProgress : Grid
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RingProgress),
            new PropertyMetadata(0d, (d, _) => ((RingProgress)d).Redraw()));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(RingProgress),
            new PropertyMetadata(null, (d, _) => ((RingProgress)d).Redraw()));

    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingProgress),
            new PropertyMetadata(3.5d, (d, _) => ((RingProgress)d).Redraw()));

    private readonly Ellipse _track;
    private readonly Path _arc;
    private readonly Ellipse _complete;

    public RingProgress()
    {
        IsHitTestVisible = false;
        _track = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromArgb(0x0F, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _arc = new Path
        {
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _complete = new Ellipse
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        Children.Add(_track);
        Children.Add(_arc);
        Children.Add(_complete);
        SizeChanged += (_, _) => Redraw();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    private void Redraw()
    {
        var stroke = Stroke ?? _track.Stroke;
        var thickness = Math.Max(1, Thickness);
        _track.StrokeThickness = thickness;
        _arc.StrokeThickness = thickness;
        _arc.Stroke = stroke;
        _complete.Stroke = stroke;
        _complete.StrokeThickness = thickness;

        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 2)
        {
            _arc.Data = null;
            return;
        }

        var progress = Math.Clamp(Value, 0, 1);
        if (progress >= 0.999)
        {
            _arc.Visibility = Visibility.Collapsed;
            _complete.Visibility = Visibility.Visible;
            return;
        }

        _complete.Visibility = Visibility.Collapsed;
        _arc.Visibility = progress <= 0.0001 ? Visibility.Collapsed : Visibility.Visible;
        if (progress <= 0.0001)
        {
            _arc.Data = null;
            return;
        }

        var radius = (size - thickness) / 2;
        var cx = ActualWidth / 2;
        var cy = ActualHeight / 2;
        var sweep = progress * 360;
        var start = PointAt(cx, cy, radius, 0);
        var end = PointAt(cx, cy, radius, sweep);
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = sweep > 180
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        _arc.Data = geometry;
    }

    /// <summary>0 degrees is 12 o'clock, clockwise.</summary>
    private static Point PointAt(double cx, double cy, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(cx + radius * Math.Sin(radians), cy - radius * Math.Cos(radians));
    }
}
