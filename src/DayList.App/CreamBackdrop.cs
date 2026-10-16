using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DayList;

/// <summary>
/// Cream-gray desktop blur. Acrylic on Windows 11 22H2+, mica on earlier Windows 11.
/// Windows 10 keeps the solid page background.
/// </summary>
internal sealed class CreamBackdrop : SystemBackdrop
{
    private static readonly Color Tint = Color.FromArgb(255, 0xF5, 0xF5, 0xF7);

    private DesktopAcrylicController? _acrylic;
    private MicaController? _mica;
    private SystemBackdropConfiguration? _config;

    public static bool IsSupported =>
        DesktopAcrylicController.IsSupported() || MicaController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _config = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);

        if (DesktopAcrylicController.IsSupported())
        {
            _acrylic = new DesktopAcrylicController
            {
                Kind = DesktopAcrylicKind.Thin,
                TintColor = Tint,
                TintOpacity = 0.76f,
                LuminosityOpacity = 0.9f,
                FallbackColor = Tint
            };
            _acrylic.AddSystemBackdropTarget(connectedTarget);
            _acrylic.SetSystemBackdropConfiguration(_config);
            return;
        }

        if (MicaController.IsSupported())
        {
            _mica = new MicaController
            {
                Kind = MicaKind.Base,
                TintColor = Tint,
                TintOpacity = 0.72f,
                FallbackColor = Tint
            };
            _mica.AddSystemBackdropTarget(connectedTarget);
            _mica.SetSystemBackdropConfiguration(_config);
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_acrylic is not null)
        {
            _acrylic.RemoveSystemBackdropTarget(disconnectedTarget);
            _acrylic.Dispose();
            _acrylic = null;
        }

        if (_mica is not null)
        {
            _mica.RemoveSystemBackdropTarget(disconnectedTarget);
            _mica.Dispose();
            _mica = null;
        }

        _config = null;
    }
}
