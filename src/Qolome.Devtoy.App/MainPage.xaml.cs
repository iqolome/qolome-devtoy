using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome_Devtoy_App.Pages;

namespace Qolome_Devtoy_App;

public sealed partial class MainPage : Page
{
    private bool _suppressNav;
    private bool _suppressConsoleToggle;

    public MainPage()
    {
        InitializeComponent();
        AppServices.Session.LogAppended += AppendLog;
        AppServices.Session.UiPreferencesChanged += OnUiPreferencesChanged;
        Loaded += (_, _) =>
        {
            AppServices.Busy.Attach(DispatcherQueue, ApplyBusyOverlay);
            _suppressNav = true;
            NavView.SelectedItem = NavView.MenuItems[0];
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
            "About" => typeof(AboutPage),
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

    private void OnConsoleVisibilityToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressConsoleToggle)
        {
            return;
        }

        AppServices.Session.SetShowConsole(ConsoleVisibleToggle.IsOn);
        ApplyConsoleVisibility(ConsoleVisibleToggle.IsOn);
    }

    private void OnUiPreferencesChanged() =>
        DispatcherQueue.TryEnqueue(() => ApplyConsoleVisibility(AppServices.Session.Settings.ShowConsole));

    private void ApplyConsoleVisibility(bool show)
    {
        _suppressConsoleToggle = true;
        ConsoleVisibleToggle.IsOn = show;
        _suppressConsoleToggle = false;

        ConsoleBody.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

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