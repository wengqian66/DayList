using Microsoft.UI.Xaml;

namespace DayList;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        // Light-only. The dark palette was removed; keep the app on the cream theme
        // regardless of the OS setting.
        RequestedTheme = ApplicationTheme.Light;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
