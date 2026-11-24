using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Qolome.Nets.App.ViewModels;
using Qolome.Nets.Core.Data;
using Windows.System;

namespace Qolome.Nets.App;

public sealed partial class DevicesPage : Page
{
    private string? _resizeKey;
    private double _resizeStartWidth;
    private double _resizeStartX;

    public DevicesViewModel ViewModel { get; } = new();

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
        Unloaded += (_, _) => ViewModel.CancelRefresh();
    }

    private async void Add_Click(object sender, RoutedEventArgs e) => await EditOrAddAsync(isNew: true);

    private async void Edit_Click(object sender, RoutedEventArgs e) => await EditOrAddAsync(isNew: false);

    private async void Devices_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        => await EditOrAddAsync(isNew: false);

    private void SortCode_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleSort(DeviceSortField.Code);

    private void SortHostname_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleSort(DeviceSortField.Hostname);

    private void SortVendor_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleSort(DeviceSortField.Vendor);

    private void SortBrand_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleSort(DeviceSortField.Brand);

    private void SortIp_Click(object sender, RoutedEventArgs e) => ViewModel.ToggleSort(DeviceSortField.Ip);

    private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string key)
        {
            return;
        }

        _resizeKey = key;
        _resizeStartWidth = ViewModel.Layout.Get(key);
        _resizeStartX = e.GetCurrentPoint(this).Position.X;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_resizeKey is null)
        {
            return;
        }

        var dx = e.GetCurrentPoint(this).Position.X - _resizeStartX;
        ViewModel.Layout.Resize(_resizeKey, _resizeStartWidth + dx);
    }

    private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _resizeKey = null;
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }
    }

    private void ColumnSplitter_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => _resizeKey = null;

    private async Task EditOrAddAsync(bool isNew)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            ViewModel.Status = "请先选择网络环境。";
            return;
        }

        if (!isNew && ViewModel.SelectedDevice is null)
        {
            ViewModel.Status = "请先选择要编辑的设备。";
            return;
        }

        try
        {
            var lookups = await ViewModel.GetEditorLookupsAsync();
            Device? seed = isNew
                ? new Device { DeviceGroupId = ViewModel.SelectedGroupIdForNewDevice }
                : ViewModel.SelectedDevice;
            await PromptDialogs.PromptDeviceAsync(
                XamlRoot,
                isNew ? "新增设备" : "编辑设备",
                lookups.Types,
                lookups.Groups,
                seed,
                input => isNew ? ViewModel.AddAsync(input) : ViewModel.EditAsync(input));
        }
        catch (Exception ex)
        {
            ViewModel.Status = ex.Message;
        }
    }

    private async void Assign_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedDevices();
        if (selected.Count == 0)
        {
            ViewModel.Status = "请先选择要分配分组的设备。";
            return;
        }

        await RunAsync(() => ViewModel.AssignSelectedGroupAsync(selected));
    }

    private async void Template_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.DownloadTemplateAsync);

    private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.ImportAsync);

    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.ExportAsync);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsRefreshing)
        {
            ViewModel.CancelRefresh();
            ViewModel.Status = "正在停止一键更新…";
            return;
        }

        if (ViewModel.SelectedEnvironment is null)
        {
            ViewModel.Status = "请先选择网络环境。";
            return;
        }

        if (ViewModel.Devices.Count == 0)
        {
            ViewModel.Status = "当前列表没有设备。";
            return;
        }

        var options = await PromptDialogs.PromptDeviceRefreshOptionsAsync(XamlRoot, ViewModel.Devices.Count);
        if (options is null)
        {
            return;
        }

        if (!options.Any)
        {
            ViewModel.Status = "请至少勾选一项要填充的内容。";
            return;
        }

        await RunAsync(() => ViewModel.RefreshCurrentListAsync(options));
    }

    private async void Devices_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Delete)
        {
            return;
        }

        e.Handled = true;
        await DeleteSelectedAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelectedDevices();
        if (selected.Count == 0)
        {
            ViewModel.Status = "请先选择要删除的设备。";
            return;
        }

        var preview = selected.Count <= 8
            ? string.Join("、", selected.Select(d => d.Code))
            : string.Join("、", selected.Take(8).Select(d => d.Code)) + $" 等 {selected.Count} 台";
        var message = selected.Count == 1
            ? $"确定删除「{selected[0].Code}」？"
            : $"确定删除选中的 {selected.Count} 台设备？\n{preview}";
        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除设备", message))
        {
            return;
        }

        await RunAsync(() => ViewModel.DeleteSelectedAsync(selected));
    }

    private IReadOnlyList<Device> GetSelectedDevices()
    {
        var items = DevicesList.SelectedItems.OfType<Device>().ToList();
        if (items.Count == 0 && ViewModel.SelectedDevice is Device current)
        {
            return [current];
        }

        return items;
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ViewModel.Status = ex.Message;
        }
    }
}
