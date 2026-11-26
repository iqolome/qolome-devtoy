using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App.ViewModels;
using Qolome_Devtoy_App.Helpers;

namespace Qolome.Nets.App;

public sealed partial class HostInfoPage : Page
{
    private ResizableColumnLayout? _nicColumnLayout;

    public HostInfoViewModel ViewModel { get; } = new();

    public HostInfoPage()
    {
        InitializeComponent();
        Loaded += OnHostInfoLoaded;
    }

    private async void OnHostInfoLoaded(object sender, RoutedEventArgs e)
    {
        if (_nicColumnLayout is null)
        {
            _nicColumnLayout = ResizableColumnLayout.Attach(
                NicHeaderGrid,
                NicList,
                [
                    TableColumnMetrics.IndexColumnSpec(),
                    new ColumnSpec(0, 96, 120),
                    new ColumnSpec(0, 120, 180),
                    new ColumnSpec(0, 48, 64),
                    new ColumnSpec(0, 64, 80),
                    new ColumnSpec(0, 64, 88),
                    new ColumnSpec(0, 120, 160),
                    new ColumnSpec(0, 120, 160),
                    new ColumnSpec(0, 88, 120),
                    new ColumnSpec(0, 100, 140),
                    new ColumnSpec(0, 80, 100),
                ],
                widthHost: NicTableHost);
        }

        await ViewModel.LoadAsync().ConfigureAwait(true);
    }

    private async void EnableAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelection("启用"))
        {
            return;
        }

        var nic = ViewModel.SelectedAdapter!;
        var ok = await PromptDialogs.ConfirmAsync(
            XamlRoot,
            "启用网卡",
            $"启用「{nic.Name}」（{nic.KindLabel}）？\n通常需要管理员权限。");
        if (!ok)
        {
            return;
        }

        await ViewModel.EnableSelectedAdapterAsync();
    }

    private async void DisableAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelection("禁用"))
        {
            return;
        }

        var nic = ViewModel.SelectedAdapter!;
        var ok = await PromptDialogs.ConfirmAsync(
            XamlRoot,
            "禁用网卡",
            $"禁用「{nic.Name}」（{nic.KindLabel}）将断开该接口连接。\n可稍后再次启用。通常需要管理员权限。确定？");
        if (!ok)
        {
            return;
        }

        await ViewModel.DisableSelectedAdapterAsync();
    }

    private async void RemoveAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelection("删除"))
        {
            return;
        }

        var nic = ViewModel.SelectedAdapter!;
        if (nic.IsPhysical)
        {
            ViewModel.HasError = true;
            ViewModel.ErrorMessage = "物理网卡不支持删除，仅可禁用 / 启用。";
            return;
        }

        var ok = await PromptDialogs.ConfirmAsync(
            XamlRoot,
            "删除软网卡",
            $"将卸载软网卡「{nic.Name}」（{nic.Description}）。\n\n"
            + "此操作会从系统移除该虚拟适配器，可能无法轻易恢复。\n"
            + "物理网卡不会出现此按钮。通常需要管理员权限。确定删除？");
        if (!ok)
        {
            return;
        }

        await ViewModel.RemoveSelectedAdapterAsync();
    }

    private bool EnsureSelection(string action)
    {
        if (ViewModel.SelectedAdapter is not null)
        {
            return true;
        }

        ViewModel.HasError = true;
        ViewModel.ErrorMessage = $"请先选择要{action}的网卡。";
        return false;
    }
}
