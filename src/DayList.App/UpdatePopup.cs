using System.Net.Http;
using System.Runtime.InteropServices;
using DayList.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace DayList;

/// <summary>
/// Small bottom-right notice shown when a newer release is available. The download runs
/// inside this window (with progress), so it is independent of the main window's state.
/// </summary>
internal sealed class UpdatePopup : Window
{
    private const int Width = 380;
    private const int Height = 206;

    private readonly UpdateInfo _info;
    private readonly Action<string> _onIgnore;
    private readonly TextBlock _status;
    private readonly Button _updateButton;
    private readonly Button _laterButton;
    private readonly Button _ignoreButton;
    private bool _downloading;

    public UpdatePopup(UpdateInfo info, Action<string> onIgnore)
    {
        _info = info;
        _onIgnore = onIgnore;
        Title = "发现新版本";
        SystemBackdrop = null;

        var ink = ThemeColors.Brush(ThemeColors.Ink);
        var muted = ThemeColors.Brush(ThemeColors.Muted);
        var cream = ThemeColors.Brush(ThemeColors.Cream);
        var line = ThemeColors.Brush(ThemeColors.Line);

        _status = new TextBlock
        {
            FontSize = 12,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var buttons = new Grid { ColumnSpacing = 8 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Warm buttons: the default AccentButtonStyle is a saturated system blue that
        // clashes with the cream theme, so the primary action uses the app's amber ink.
        var closedStyle = Application.Current.Resources.TryGetValue("SoftDialogCloseButtonStyle", out var closeValue)
            ? closeValue as Style
            : null;
        var warm = Application.Current.Resources.TryGetValue("SoftAmberInkBrush", out var warmValue)
            ? warmValue as Brush
            : null;

        _ignoreButton = new Button
        {
            Content = "忽略此次更新",
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = muted,
            FontSize = 12,
            Padding = new Thickness(6, 6, 6, 6),
            CornerRadius = new CornerRadius(8)
        };
        _ignoreButton.Click += (_, _) =>
        {
            _onIgnore(_info.Version);
            Close();
        };

        _laterButton = new Button { Content = "稍后", Style = closedStyle };
        _laterButton.Click += (_, _) => Close();

        _updateButton = new Button
        {
            Content = "立即更新",
            Style = closedStyle,
            Background = warm,
            Foreground = ThemeColors.Brush(ThemeColors.Cream),
            BorderThickness = new Thickness(0)
        };
        _updateButton.Click += async (_, _) => await DownloadAndRunAsync();

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_laterButton);
        right.Children.Add(_updateButton);
        Grid.SetColumn(right, 1);

        buttons.Children.Add(_ignoreButton);
        buttons.Children.Add(right);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(new TextBlock { Text = "发现新版本", FontSize = 12, Foreground = muted });
        stack.Children.Add(new TextBlock
        {
            Text = "v" + info.Version,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ink
        });
        stack.Children.Add(new TextBlock
        {
            Text = "点「立即更新」下载并安装；不想现在更新可以选「稍后」或「忽略此次更新」。",
            FontSize = 12,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(_status);
        stack.Children.Add(buttons);

        var shell = new Border
        {
            Background = cream,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 12, 18, 12),
            Child = stack
        };
        shell.RequestedTheme = ElementTheme.Light;
        Content = shell;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true;

        try
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
            TryRoundCorners(WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // Older hosts keep the default chrome.
        }

        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            // Width/Height above are DIPs; the work area and the window are pixels.
            var pixelWidth = DpiScale.Px(this, Width);
            var pixelHeight = DpiScale.Px(this, Height);
            var margin = DpiScale.Px(this, 20);
            var x = work.X + work.Width - pixelWidth - margin;
            var y = work.Y + work.Height - pixelHeight - margin;
            AppWindow.MoveAndResize(new RectInt32(x, y, pixelWidth, pixelHeight));
        }
        catch
        {
            // Placement is best-effort.
        }
    }

    private async Task DownloadAndRunAsync()
    {
        if (_downloading)
            return;

        _downloading = true;
        _updateButton.IsEnabled = false;
        _laterButton.IsEnabled = false;
        _ignoreButton.IsEnabled = false;
        _status.Visibility = Visibility.Visible;
        _status.Text = "正在准备下载…";

        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"DayList-Setup-{_info.Version}.exe");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            using var response = await client.GetAsync(_info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(path))
            {
                var buffer = new byte[81920];
                long received = 0;
                var lastReport = Environment.TickCount64;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    received += read;
                    var now = Environment.TickCount64;
                    if (now - lastReport < 250)
                        continue;

                    lastReport = now;
                    var pct = total > 0 ? received * 100 / total : 0;
                    _status.Text = total > 0
                        ? $"正在下载… {pct}%（{ToMb(received)} / {ToMb(total)} MB）"
                        : $"正在下载… {ToMb(received)} MB";
                }
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            await Task.Delay(800);
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            _status.Text = "下载失败：" + ex.Message;
            _updateButton.IsEnabled = true;
            _laterButton.IsEnabled = true;
            _ignoreButton.IsEnabled = true;
            _downloading = false;
        }
    }

    private static string ToMb(long bytes) =>
        (bytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void TryRoundCorners(IntPtr hwnd)
    {
        try
        {
            var preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch
        {
            // Rounded corners are cosmetic.
        }
    }
}
