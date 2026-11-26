using Microsoft.UI.Xaml;
using Qolome.Devtoy.Core.Services;
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
        // 须在任何读/写 AppData 之前：打包态 LocalApplicationData 逻辑路径会虚拟化
        ConfigurePackagedAppDataRoot();
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UnhandledException: " + e.Message, e.Exception);
            e.Handled = true;
        };
    }

    /// <summary>
    /// MSIX 下把数据根指到真实 <c>LocalFolder\Qolome\Devtoy</c>，
    /// 避免界面显示 <c>%LocalAppData%\Qolome\Devtoy</c> 而资源管理器找不到。
    /// </summary>
    private static void ConfigurePackagedAppDataRoot()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            DevtoyPaths.UseAppDataRoot(Path.Combine(localFolder, "Qolome", "Devtoy"));
        }
        catch (InvalidOperationException)
        {
            // 非打包运行：沿用 %LocalAppData%\Qolome\Devtoy
        }
        catch (Exception ex)
        {
            CrashLog.Write("ConfigurePackagedAppDataRoot skipped", ex);
        }
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
            AppServices.Performance.Attach(main);
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