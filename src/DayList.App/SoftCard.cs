using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;
using Windows.UI;

namespace DayList;

/// <summary>
/// White card host. The template lifts the face about 4px in 120ms on pointer hover
/// and paints a soft two-layer shadow in the padding gutter.
/// </summary>
public sealed class SoftCard : ContentControl
{
    private const float Corner = 16f;

    private Grid? _shadowHost;
    private FrameworkElement? _face;
    private bool _hover;

    public SoftCard()
    {
        DefaultStyleKey = typeof(SoftCard);
        SizeChanged += (_, _) => RefreshShadow();
        Loaded += (_, _) => RefreshShadow();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadowHost = GetTemplateChild("ShadowHost") as Grid;
        _face = GetTemplateChild("Face") as FrameworkElement;
        if (GetTemplateChild("Root") is FrameworkElement root)
        {
            foreach (var group in VisualStateManager.GetVisualStateGroups(root))
            {
                group.CurrentStateChanged += (_, args) =>
                    SetHover(string.Equals(args.NewState?.Name, "PointerOver", StringComparison.Ordinal));
            }
        }

        RefreshShadow();
    }

    private void SetHover(bool hover)
    {
        if (_hover == hover)
            return;
        _hover = hover;
        RefreshShadow();
    }

    private void RefreshShadow()
    {
        if (_shadowHost is null || _face is null)
            return;
        if (_shadowHost.ActualWidth < 1 || _shadowHost.ActualHeight < 1)
            return;
        if (_face.ActualWidth < 1 || _face.ActualHeight < 1)
            return;

        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        var container = compositor.CreateContainerVisual();
        container.Size = new System.Numerics.Vector2((float)_shadowHost.ActualWidth, (float)_shadowHost.ActualHeight);

        var origin = _face.TransformToVisual(_shadowHost).TransformPoint(new Point(0, 0));
        var size = new System.Numerics.Vector2((float)_face.ActualWidth, (float)_face.ActualHeight);
        if (_hover)
        {
            AddLayer(container, compositor, size, origin, blur: 4, opacity: 0.07f, y: 2);
            AddLayer(container, compositor, size, origin, blur: 22, opacity: 0.12f, y: 10);
        }
        else
        {
            AddLayer(container, compositor, size, origin, blur: 2, opacity: 0.05f, y: 1);
            AddLayer(container, compositor, size, origin, blur: 14, opacity: 0.07f, y: 4);
        }

        ElementCompositionPreview.SetElementChildVisual(_shadowHost, container);
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
        sprite.Brush = compositor.CreateColorBrush(Color.FromArgb(255, 255, 251, 246));
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
        shadow.Color = Color.FromArgb(255, 0x3D, 0x34, 0x29);
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
