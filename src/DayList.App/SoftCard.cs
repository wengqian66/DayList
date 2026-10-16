using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;
using Windows.UI;

namespace DayList;

/// <summary>
/// White card host. Padding is the shadow gutter; the first XAML child is the card face.
/// </summary>
public sealed class SoftCard : Grid
{
    private const float Corner = 12f;
    private Grid? _host;
    private bool _hover;

    public SoftCard()
    {
        Padding = new Thickness(8, 2, 8, 14);
        Loaded += OnLoaded;
        SizeChanged += (_, _) => Refresh();
        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            _host = new Grid { IsHitTestVisible = false };
            Children.Insert(0, _host);
        }

        _host.Margin = new Thickness(-Padding.Left, -Padding.Top, -Padding.Right, -Padding.Bottom);
        Refresh();
    }

    private void SetHover(bool hover)
    {
        if (_hover == hover)
            return;
        _hover = hover;
        Refresh();
    }

    private void Refresh()
    {
        if (_host is null || _host.ActualWidth < 1 || _host.ActualHeight < 1)
            return;

        FrameworkElement? face = null;
        foreach (var child in Children)
        {
            if (child is FrameworkElement element && !ReferenceEquals(element, _host))
            {
                face = element;
                break;
            }
        }

        if (face is null || face.ActualWidth < 1 || face.ActualHeight < 1)
            return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var container = compositor.CreateContainerVisual();
        container.Size = new System.Numerics.Vector2((float)_host.ActualWidth, (float)_host.ActualHeight);

        var origin = face.TransformToVisual(_host).TransformPoint(new Point(0, 0));
        var size = new System.Numerics.Vector2((float)face.ActualWidth, (float)face.ActualHeight);
        if (_hover)
        {
            AddLayer(container, compositor, size, origin, blur: 4, opacity: 0.06f, y: 2);
            AddLayer(container, compositor, size, origin, blur: 24, opacity: 0.10f, y: 8);
        }
        else
        {
            AddLayer(container, compositor, size, origin, blur: 2, opacity: 0.04f, y: 1);
            AddLayer(container, compositor, size, origin, blur: 12, opacity: 0.06f, y: 4);
        }

        ElementCompositionPreview.SetElementChildVisual(_host, container);
    }

    private static void AddLayer(
        ContainerVisual container,
        Compositor compositor,
        System.Numerics.Vector2 size,
        Point origin,
        float blur,
        float opacity,
        float y)
    {
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = size;
        geometry.CornerRadius = new System.Numerics.Vector2(Corner, Corner);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Size = size;
        sprite.Offset = new System.Numerics.Vector3((float)origin.X, (float)origin.Y, 0);
        sprite.Brush = compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        try
        {
            sprite.Clip = compositor.CreateGeometricClip(geometry);
        }
        catch (Exception)
        {
            // The card face still covers a square shadow caster.
        }

        var shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = blur;
        shadow.Opacity = opacity;
        shadow.Offset = new System.Numerics.Vector3(0, y, 0);
        shadow.Color = Color.FromArgb(255, 0, 0, 0);
        try
        {
            shadow.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        }
        catch (Exception)
        {
            // Older builds derive the shadow from the sprite brush instead.
        }

        sprite.Shadow = shadow;
        container.Children.InsertAtTop(sprite);
    }
}
