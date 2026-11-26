using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App;
using Qolome_Devtoy_App.Pages;

namespace Qolome_Devtoy_App;

public sealed partial class MainPage : Page
{
    private bool _suppressNav;
    private bool _consoleVisible;

    public MainPage()
    {
        InitializeComponent();
        AppServices.Session.LogAppended += AppendLog;
        AppServices.Session.UiPreferencesChanged += OnUiPreferencesChanged;
        Loaded += (_, _) =>
        {
            AppServices.Busy.Attach(DispatcherQueue, ApplyBusyOverlay);
            _suppressNav = true;
            var generate = FindNavItemByTag("Generate");
            if (generate is not null)
            {
                NavView.SelectedItem = generate;
            }

            ContentFrame.Navigate(typeof(GeneratePage));
            _suppressNav = false;
            ApplyConsoleVisibility(AppServices.Session.Settings.ShowConsole);
        };
        Unloaded += (_, _) =>
        {
            AppServices.Busy.Detach();
            AppServices.Session.UiPreferencesChanged -= OnUiPreferencesChanged;
        };
    }

    private void ApplyBusyOverlay(bool visible, string message)
    {
        BusyOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BusyMessage.Text = string.IsNullOrWhiteSpace(message) ? "处理中…" : message;
        ContentFrame.IsHitTestVisible = !visible;
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNav)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        var pageType = tag switch
        {
            "Generate" => typeof(GeneratePage),
            "Dict" => typeof(DictPage),
            "Module" => typeof(ModulePage),
            "ConfigTheme" => typeof(ThemeSettingsPage),
            "ConfigBasic" => typeof(BasicSettingsPage),
            "ConfigDataSource" => typeof(DataSourcePage),
            "ConfigTemplates" => typeof(TemplatePage),
            "ConfigImportExport" => typeof(ImportExportPage),
            "NetsHostInfo" => typeof(HostInfoPage),
            "NetsScan" => typeof(Qolome.Nets.App.MainPage),
            "NetsDevices" => typeof(DevicesPage),
            "NetsTopology" => typeof(TopologyPage),
            "NetsEnvironments" => typeof(EnvironmentsPage),
            "NetsDeviceTypes" => typeof(DeviceTypesPage),
            "NetsSettings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            "IconLibrary" => typeof(IconLibraryPage),
            _ => typeof(PlaceholderPage),
        };

        if (pageType == typeof(PlaceholderPage))
        {
            ContentFrame.Navigate(pageType, new PlaceholderNavArgs("未实现", tag));
        }
        else
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private NavigationViewItem? FindNavItemByTag(string tag)
    {
        foreach (var root in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (root.Tag is string rootTag && rootTag == tag)
            {
                return root;
            }

            foreach (var child in root.MenuItems.OfType<NavigationViewItem>())
            {
                if (child.Tag is string childTag && childTag == tag)
                {
                    return child;
                }
            }
        }

        return null;
    }

    private void AppendLog(string message)
    {
        void Write()
        {
            ConsoleBox.Text += message + Environment.NewLine;
            ConsoleBox.SelectionStart = ConsoleBox.Text.Length;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Write();
        }
        else
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, Write);
        }
    }

    private void OnClearConsole(object sender, RoutedEventArgs e) => ConsoleBox.Text = string.Empty;

    private void OnConsoleVisibilityClick(object sender, RoutedEventArgs e)
    {
        var show = !_consoleVisible;
        AppServices.Session.SetShowConsole(show);
        ApplyConsoleVisibility(show);
    }

    private void OnUiPreferencesChanged() =>
        DispatcherQueue.TryEnqueue(() => ApplyConsoleVisibility(AppServices.Session.Settings.ShowConsole));

    private void ApplyConsoleVisibility(bool show)
    {
        _consoleVisible = show;
        ConsoleBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // 隐藏时点 HideBcc 展示；显示时点 ShowBcc 隐藏
        ConsoleVisibilityIcon.Symbol = show ? Symbol.ShowBcc : Symbol.HideBcc;
        ToolTipService.SetToolTip(ConsoleVisibilityButton, show ? "隐藏控制台" : "显示控制台");

        if (show)
        {
            ConsoleRow.Height = new GridLength(160);
            ConsoleRow.MinHeight = 100;
        }
        else
        {
            ConsoleRow.Height = GridLength.Auto;
            ConsoleRow.MinHeight = 0;
        }
    }
}
