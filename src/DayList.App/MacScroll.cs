using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DayList;

/// <summary>Thin scrollbars that fade out after the view sits idle.</summary>
internal static class MacScroll
{
    public static void AttachTree(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
                Attach(viewer);
            AttachTree(child);
        }
    }

    public static void Attach(ScrollViewer viewer)
    {
        if (viewer.IsLoaded)
            Wire(viewer);
        else
            viewer.Loaded += (_, _) => Wire(viewer);
    }

    private static void Wire(ScrollViewer viewer)
    {
        var bars = new List<ScrollBar>();
        Collect(viewer, bars);
        if (bars.Count == 0)
            return;

        var hovering = new HashSet<ScrollBar>();
        var queue = DispatcherQueue.GetForCurrentThread();
        DispatcherQueueTimer? timer = null;
        if (queue is not null)
        {
            timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1500);
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                foreach (var bar in bars)
                {
                    if (!hovering.Contains(bar))
                        Fade(bar, 0);
                }
            };
        }

        void Show()
        {
            foreach (var bar in bars)
                Fade(bar, 1);
            timer?.Start();
        }

        foreach (var bar in bars)
        {
            bar.Opacity = 0;
            if (bar.Orientation == Orientation.Vertical)
                bar.Width = 6;
            else
                bar.Height = 6;
            bar.PointerEntered += (_, _) =>
            {
                hovering.Add(bar);
                Fade(bar, 1);
                timer?.Stop();
            };
            bar.PointerExited += (_, _) =>
            {
                hovering.Remove(bar);
                timer?.Start();
            };
        }

        viewer.ViewChanged += (_, _) => Show();
    }

    private static void Fade(UIElement element, double to)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(280)),
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var board = new Storyboard();
        board.Children.Add(animation);
        board.Begin();
    }

    private static void Collect(DependencyObject node, List<ScrollBar> bars)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ScrollBar bar)
                bars.Add(bar);
            Collect(child, bars);
        }
    }
}
