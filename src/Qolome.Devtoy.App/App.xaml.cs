using Microsoft.UI.Xaml;
using Qolome_Devtoy_App.Helpers;

namespace Qolome_Devtoy_App;

public partial class App : Application
{
    private Window? _window;

    internal static Window? TryGetWindow() => ((App)Current)._window;

    internal static Window CurrentWindow =>
        TryGetWindow() ?? throw new InvalidOperationException("窗口未就绪");

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UnhandledException: " + e.Message, e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var main = new MainWindow();
            _window = main;

            UiGlyphResources.Apply();
            AppServices.IconTheme.Attach(main);
            AppServices.AppTheme.Attach(main);
            AppServices.IconTheme.ApplyFromSettings();
            main.Activate();
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnLaunched failed", ex);
            throw;
        }
    }
}