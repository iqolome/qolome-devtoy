using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App.ViewModels;

namespace Qolome.Nets.App;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel.ReloadConnectionStatus();
    }

    private async void SyncToRemote_Click(object sender, RoutedEventArgs e)
    {
        if (!await PromptDialogs.ConfirmAsync(
                XamlRoot,
                "同步到远程",
                "将用本地 SQLite 库存覆盖「数据源设置」中远程库的环境 / 分组 / 类型 / 设备等表。不含 OUI 厂商库；拓扑随库存表一并覆盖。确定继续？"))
        {
            return;
        }

        if (ViewModel.SyncToRemoteCommand.CanExecute(null))
        {
            await ViewModel.SyncToRemoteCommand.ExecuteAsync(null);
        }
    }

    private async void SyncToLocal_Click(object sender, RoutedEventArgs e)
    {
        if (!await PromptDialogs.ConfirmAsync(
                XamlRoot,
                "同步到本地",
                "将用「数据源设置」中远程库的库存覆盖本地 SQLite 的环境 / 分组 / 类型 / 设备等表。不含 OUI 厂商库；拓扑随库存表一并覆盖。确定继续？"))
        {
            return;
        }

        if (ViewModel.SyncToLocalCommand.CanExecute(null))
        {
            await ViewModel.SyncToLocalCommand.ExecuteAsync(null);
        }
    }
}
