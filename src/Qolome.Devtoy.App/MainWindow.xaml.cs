using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Data;
using Qolome_Devtoy_App.Helpers;

namespace Qolome_Devtoy_App;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        NetsRuntime.GetRemoteDataSource = () => AppServices.Session.Settings.DataSource;
        _ = InitializeNetsAsync();
        RootFrame.Navigate(typeof(MainPage));
        TitleBarChrome.Apply(this);
        TitleBarChrome.ListenToThemeChanges(this, RootFrame);
        Activated += (_, _) => TitleBarChrome.Apply(this);
        Closed += (_, _) => AppServices.Session.FlushSettings();
    }

    private static async Task InitializeNetsAsync()
    {
        try
        {
            Qolome.Nets.App.AppServices.LoadScanSettings();
            await AppDatabase.InitializeAsync().ConfigureAwait(true);
            var warn = AppDatabase.StartupWarning;
            if (!string.IsNullOrWhiteSpace(warn))
            {
                AppServices.Session.LogWarn(warn);
            }

            AppServices.Session.Log($"配置库（SQLite）：{DevtoyPaths.LocalDbPath}");
            AppServices.Session.Log($"业务库：{AppDatabase.DescribeConnection()}");
        }
        catch (Exception ex)
        {
            AppServices.Session.LogWarn("网络工具初始化失败：" + ex.Message);
            CrashLog.Write("Nets InitializeAsync failed", ex);
        }
    }

    public void SetTitleBarIcon(string icoPath)
    {
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new BitmapImage(new Uri(Path.GetFullPath(icoPath))),
        };
    }
}
