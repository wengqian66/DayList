using System.Diagnostics;
using System.Numerics;
using DayList.Core;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace DayList;

/// <summary>
/// Paints the day-complete sprinkle as composition sprites on an overlay that
/// stays in the tree. No storyboard and no <see cref="Microsoft.UI.Xaml.Controls.ProgressRing"/>
/// transform — those were the 1.1.8 fail-fast (0x800F1000 / 0xC000027B).
/// </summary>
internal sealed class ConfettiOverlay
{
    private DispatcherQueueTimer? _timer;
    private UIElement? _host;

    public void Play(FrameworkElement host, IReadOnlyList<ConfettiPiece> pieces)
    {
        Clear();
        if (!host.IsLoaded || pieces.Count == 0)
            return;

        try
        {
            host.UpdateLayout();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        var width = (float)host.ActualWidth;
        var height = (float)host.ActualHeight;
        if (width < 32 || height < 32)
            return;

        try
        {
            Attach(host, pieces, width, height);
            Arm(host.DispatcherQueue);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Clear();
        }
    }

    public void Clear()
    {
        _timer?.Stop();
        if (_host is null)
            return;

        var host = _host;
        _host = null;
        try
        {
            ElementCompositionPreview.SetElementChildVisual(host, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void Attach(FrameworkElement host, IReadOnlyList<ConfettiPiece> pieces, float width, float height)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var container = compositor.CreateContainerVisual();
        container.Size = new Vector2(width, height);
        container.Clip = compositor.CreateInsetClip();

        var settle = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.16f, 0.45f),
            new Vector2(0.22f, 1f));
        var motion = TimeSpan.FromMilliseconds(ConfettiChoreography.MotionMs);
        var scraps = new List<(SpriteVisual Visual, ConfettiPiece Piece)>(pieces.Count);

        foreach (var piece in pieces)
        {
            var scrap = compositor.CreateSpriteVisual();
            scrap.Size = new Vector2(piece.Width, piece.Height);
            scrap.CenterPoint = new Vector3(piece.Width / 2f, piece.Height / 2f, 0);
            scrap.Brush = compositor.CreateColorBrush(Color.FromArgb(255, piece.R, piece.G, piece.B));
            scrap.Opacity = 0;
            scrap.Offset = new Vector3(piece.X * width, piece.Y * height, 0);
            scrap.RotationAngleInDegrees = piece.StartDegrees;
            container.Children.InsertAtTop(scrap);
            scraps.Add((scrap, piece));
        }

        // Parent first so the animation clock starts on a live visual.
        ElementCompositionPreview.SetElementChildVisual(host, container);
        _host = host;

        foreach (var (scrap, piece) in scraps)
        {
            var delay = TimeSpan.FromMilliseconds(piece.DelayMs);
            var start = scrap.Offset;

            var fall = compositor.CreateVector3KeyFrameAnimation();
            fall.Duration = motion;
            fall.DelayTime = delay;
            fall.InsertKeyFrame(0f, start);
            fall.InsertKeyFrame(
                1f,
                new Vector3((piece.X + piece.Drift) * width, (piece.Y + piece.Fall) * height, 0),
                settle);

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = motion;
            fade.DelayTime = delay;
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(0.12f, 0.9f);
            fade.InsertKeyFrame(0.7f, 0.82f);
            fade.InsertKeyFrame(1f, 0f);

            var spin = compositor.CreateScalarKeyFrameAnimation();
            spin.Duration = motion;
            spin.DelayTime = delay;
            spin.InsertKeyFrame(0f, piece.StartDegrees);
            spin.InsertKeyFrame(1f, piece.EndDegrees);

            scrap.StartAnimation("Offset", fall);
            scrap.StartAnimation("Opacity", fade);
            scrap.StartAnimation("RotationAngleInDegrees", spin);
        }
    }

    private void Arm(DispatcherQueue queue)
    {
        if (_timer is null)
        {
            _timer = queue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Tick += (_, _) => Clear();
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(ConfettiChoreography.LifetimeMs);
        _timer.Start();
    }
}
