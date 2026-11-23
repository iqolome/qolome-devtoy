using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
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
        RootFrame.Navigate(typeof(MainPage));
        TitleBarChrome.Apply(this);
        TitleBarChrome.ListenToThemeChanges(this, RootFrame);
        Activated += (_, _) => TitleBarChrome.Apply(this);
    }

    public void SetTitleBarIcon(string icoPath)
    {
        AppTitleBar.IconSource = new ImageIconSource
        {
            ImageSource = new BitmapImage(new Uri(Path.GetFullPath(icoPath))),
        };
    }
}
