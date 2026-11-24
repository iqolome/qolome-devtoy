using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Excel;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.App.ViewModels;

public sealed class DeviceGroupFilter
{
    public static DeviceGroupFilter All { get; } = new() { GroupId = null, Name = "全部" };
    public static DeviceGroupFilter Ungrouped { get; } = new() { GroupId = 0, Name = "未分组" };

    public int? GroupId { get; init; }
    public string Name { get; init; } = "";
}

public partial class DevicesViewModel : ObservableObject
{
    private readonly InventoryService _inventory = AppServices.Inventory;
    private List<Device> _allDevices = [];
    private bool _suppressGroupFilterReload;

    public ObservableCollection<NetworkEnvironment> Environments { get; } = [];
    public ObservableCollection<DeviceGroup> Groups { get; } = [];
    public ObservableCollection<DeviceGroupFilter> GroupFilters { get; } =
    [
        DeviceGroupFilter.All,
        DeviceGroupFilter.Ungrouped
    ];
    public ObservableCollection<Device> Devices { get; } = [];
    public IReadOnlyList<DeviceType> DeviceTypes { get; private set; } = [];
    public DeviceListLayout Layout { get; } = DeviceListLayout.Shared;

    [ObservableProperty]
    public partial NetworkEnvironment? SelectedEnvironment { get; set; }

    [ObservableProperty]
    public partial DeviceGroupFilter? SelectedGroupFilter { get; set; } = DeviceGroupFilter.All;

    [ObservableProperty]
    public partial Device? SelectedDevice { get; set; }

    [ObservableProperty]
    public partial DeviceGroup? AssignGroup { get; set; }

    [ObservableProperty]
    public partial string HostnameQuery { get; set; } = "";

    [ObservableProperty]
    public partial string MacQuery { get; set; } = "";

    [ObservableProperty]
    public partial DeviceSortField SortField { get; set; } = DeviceSortField.Ip;

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "设备只能属于一个网络环境。";

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    private CancellationTokenSource? _refreshCts;

    public string CodeSortHeader => SortTitle("编码", DeviceSortField.Code);
    public string HostnameSortHeader => SortTitle("主机名", DeviceSortField.Hostname);
    public string VendorSortHeader => SortTitle("厂商", DeviceSortField.Vendor);
    public string BrandSortHeader => SortTitle("品牌", DeviceSortField.Brand);
    public string IpSortHeader => SortTitle("IP", DeviceSortField.Ip);

    public int? SelectedGroupIdForNewDevice =>
        SelectedGroupFilter?.GroupId is > 0 ? SelectedGroupFilter.GroupId : null;

    public async Task LoadAsync()
    {
        var environments = await _inventory.GetEnvironmentsAsync();
        Environments.Clear();
        foreach (var env in environments)
        {
            Environments.Add(env);
        }

        DeviceTypes = await _inventory.GetDeviceTypesAsync();

        SelectedEnvironment = Environments.FirstOrDefault(e => e.Id == AppServices.SelectedEnvironmentId)
            ?? Environments.FirstOrDefault();
    }

    public async Task<(IReadOnlyList<DeviceType> Types, IReadOnlyList<DeviceGroup> Groups)> GetEditorLookupsAsync()
    {
        DeviceTypes = await _inventory.GetDeviceTypesAsync();
        IReadOnlyList<DeviceGroup> groups = SelectedEnvironment is null
            ? []
            : await _inventory.GetGroupsAsync(SelectedEnvironment.Id);
        return (DeviceTypes, groups);
    }

    public async Task AddAsync(DeviceEditInput input)
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。");
        }

        await _inventory.AddDeviceAsync(SelectedEnvironment.Id, input);
        Status = "已新增设备。";
        await ReloadDevicesAsync();
    }

    public async Task EditAsync(DeviceEditInput input)
    {
        if (SelectedDevice is null)
        {
            throw new InvalidOperationException("请先选择设备。");
        }

        var id = SelectedDevice.Id;
        await _inventory.UpdateDeviceAsync(id, input);
        Status = "已更新设备。";
        await ReloadDevicesAsync();
        SelectedDevice = Devices.FirstOrDefault(d => d.Id == id);
    }

    partial void OnSelectedEnvironmentChanged(NetworkEnvironment? value)
    {
        AppServices.SelectedEnvironmentId = value?.Id;
        _ = ReloadAsync();
    }

    partial void OnSelectedGroupFilterChanged(DeviceGroupFilter? value)
    {
        if (_suppressGroupFilterReload || value is null)
        {
            return;
        }

        _ = ReloadDevicesAsync();
    }

    partial void OnHostnameQueryChanged(string value) => ApplyView();

    partial void OnMacQueryChanged(string value) => ApplyView();

    partial void OnSortFieldChanged(DeviceSortField value) => NotifySortHeaders();

    partial void OnSortDescendingChanged(bool value) => NotifySortHeaders();

    public void ToggleSort(DeviceSortField field)
    {
        if (SortField == field)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortField = field;
            SortDescending = false;
        }

        ApplyView();
    }

    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(CodeSortHeader));
        OnPropertyChanged(nameof(HostnameSortHeader));
        OnPropertyChanged(nameof(VendorSortHeader));
        OnPropertyChanged(nameof(BrandSortHeader));
        OnPropertyChanged(nameof(IpSortHeader));
    }

    private string SortTitle(string title, DeviceSortField field)
    {
        if (SortField != field)
        {
            return title;
        }

        return SortDescending ? $"{title} ↓" : $"{title} ↑";
    }

    private async Task ReloadAsync()
    {
        _suppressGroupFilterReload = true;
        try
        {
            Groups.Clear();
            GroupFilters.Clear();
            AssignGroup = null;
            GroupFilters.Add(DeviceGroupFilter.All);
            GroupFilters.Add(DeviceGroupFilter.Ungrouped);
            if (SelectedEnvironment is null)
            {
                SelectedGroupFilter = DeviceGroupFilter.All;
                _allDevices = [];
                ApplyView();
                return;
            }

            foreach (var group in await _inventory.GetGroupsAsync(SelectedEnvironment.Id))
            {
                Groups.Add(group);
                GroupFilters.Add(new DeviceGroupFilter { GroupId = group.Id, Name = group.Name });
            }

            SelectedGroupFilter = DeviceGroupFilter.All;
        }
        finally
        {
            _suppressGroupFilterReload = false;
        }

        await ReloadDevicesAsync();
    }

    private async Task ReloadDevicesAsync()
    {
        if (SelectedEnvironment is null)
        {
            _allDevices = [];
            ApplyView();
            return;
        }

        _allDevices = [.. await _inventory.GetDevicesAsync(SelectedEnvironment.Id, SelectedGroupFilter?.GroupId)];
        ApplyView();
    }

    private void ApplyView()
    {
        var selectedId = SelectedDevice?.Id;
        Devices.Clear();

        IEnumerable<Device> query = _allDevices;
        if (!string.IsNullOrWhiteSpace(HostnameQuery))
        {
            var keyword = HostnameQuery.Trim();
            query = query.Where(d =>
                d.Hostname?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || d.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(MacQuery))
        {
            query = query.Where(d =>
                InventoryService.MatchesMacQuery(d.Mac, MacQuery)
                || d.OrderedNics().Any(n => InventoryService.MatchesMacQuery(n.Mac, MacQuery)));
        }

        query = ApplySort(query);

        foreach (var device in query)
        {
            Devices.Add(device);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == selectedId);
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(HostnameQuery))
        {
            filters.Add("主机名/编码");
        }

        if (!string.IsNullOrWhiteSpace(MacQuery))
        {
            filters.Add("MAC");
        }

        var scope = SelectedGroupFilter?.GroupId is 0
            ? "未分组"
            : SelectedGroupFilter?.GroupId is > 0
                ? $"分组「{SelectedGroupFilter.Name}」"
                : "当前环境";
        Status = filters.Count == 0
            ? $"{scope} {Devices.Count} 台设备。"
            : $"{string.Join("、", filters)}匹配 {Devices.Count} / {_allDevices.Count} 台设备。";
    }

    private IEnumerable<Device> ApplySort(IEnumerable<Device> query)
    {
        var text = StringComparer.OrdinalIgnoreCase;
        return SortField switch
        {
            DeviceSortField.Code => SortText(query, d => d.Code, text),
            DeviceSortField.Hostname => SortText(query, d => d.Hostname ?? "", text),
            DeviceSortField.Vendor => SortText(query, d => d.Vendor ?? "", text),
            DeviceSortField.Brand => SortText(query, d => d.Brand ?? "", text),
            _ => SortDescending
                ? query.OrderByDescending(d => d.Ip, IpAddressComparer.TextComparer)
                    .ThenByDescending(d => d.Code, text)
                : query.OrderBy(d => d.Ip, IpAddressComparer.TextComparer)
                    .ThenBy(d => d.Code, text)
        };
    }

    private IOrderedEnumerable<Device> SortText(
        IEnumerable<Device> query,
        Func<Device, string> key,
        StringComparer comparer) =>
        SortDescending
            ? query.OrderByDescending(key, comparer).ThenByDescending(d => d.Code, comparer)
            : query.OrderBy(key, comparer).ThenBy(d => d.Code, comparer);

    public async Task AssignSelectedGroupAsync(IReadOnlyList<Device> devices)
    {
        var ids = devices.Select(d => d.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("请先选择设备。");
        }

        var count = await _inventory.AssignDeviceGroupsAsync(ids, AssignGroup?.Id);
        var groupName = AssignGroup?.Name ?? "未分组";
        Status = count == 1
            ? $"已将 1 台设备分到「{groupName}」。"
            : $"已将 {count} 台设备分到「{groupName}」。";
        await ReloadDevicesAsync();
    }

    public async Task DeleteSelectedAsync(IReadOnlyList<Device> devices)
    {
        var ids = devices.Select(d => d.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var count = await _inventory.DeleteDevicesAsync(ids);
        Status = count == 1 ? "已删除 1 台设备。" : $"已删除 {count} 台设备。";
        await ReloadDevicesAsync();
    }

    public async Task ExportAsync()
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。");
        }

        var path = await FileDialogs.PickSaveExcelAsync($"设备-{SelectedEnvironment.Name}");
        if (path is null)
        {
            return;
        }

        var list = await _inventory.GetDevicesAsync(SelectedEnvironment.Id, null);
        var types = await _inventory.GetDeviceTypesAsync();
        ExcelInventory.WriteDevices(path, list, types);
        Status = $"已导出 {list.Count} 台设备。";
    }

    public async Task ImportAsync()
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。导入的设备将属于该环境。");
        }

        var path = await FileDialogs.PickOpenExcelAsync();
        if (path is null)
        {
            return;
        }

        var rows = ExcelInventory.ReadDevices(path);
        var count = await _inventory.ImportDevicesAsync(SelectedEnvironment.Id, rows);
        await ReloadDevicesAsync();
        Status = $"已导入 {count} 台设备到「{SelectedEnvironment.Name}」。";
    }

    public async Task DownloadTemplateAsync()
    {
        var path = await FileDialogs.PickSaveExcelAsync("设备导入模板");
        if (path is null)
        {
            return;
        }

        var types = await _inventory.GetDeviceTypesAsync();
        ExcelInventory.WriteDeviceTemplate(path, types);
        Status = $"已保存导入模板：{path}";
    }

    public void CancelRefresh() => _refreshCts?.Cancel();

    public async Task RefreshCurrentListAsync(DeviceRefreshOptions options)
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。");
        }

        if (IsRefreshing)
        {
            return;
        }

        var ids = Devices.Select(d => d.Id).ToList();
        if (ids.Count == 0)
        {
            Status = "当前列表没有设备。";
            return;
        }

        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        IsRefreshing = true;
        var scan = AppServices.Scan;
        scan.Clamp();
        await AppServices.PortPurposes.EnsureLoadedAsync();
        var request = new ScanRequest
        {
            Target = "refresh",
            TcpPorts = options.Ports ? PortListParser.Parse(PortListParser.DefaultTcpPortsText) : [],
            UdpPorts = options.Ports ? PortListParser.Parse(PortListParser.DefaultUdpPortsText) : [],
            PingTimeoutMs = 1000,
            PingPayloadSize = 32,
            ConnectTimeoutMs = 400,
            HostConcurrency = scan.IpScanConcurrency,
            IpScanBatchSize = scan.IpScanBatchSize,
            PortScanConcurrency = scan.PortScanConcurrency,
            TreatUdpTimeoutAsOpen = false
        };
        var progress = new Progress<ScanProgress>(p => Status = p.Message);
        try
        {
            var result = await _inventory.RefreshDevicesAsync(
                SelectedEnvironment.Id,
                ids,
                options,
                request,
                progress,
                _refreshCts.Token,
                AppServices.PortPurposes);
            await ReloadDevicesAsync();
            var extra = result.Offline > 0 ? $"，离线 {result.Offline} 台未改对应项" : "";
            Status = $"一键更新完成：已更新 {result.Updated} 台，未变化 {result.Unchanged} 台{extra}。";
        }
        catch (OperationCanceledException)
        {
            Status = "已取消一键更新。";
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}

public enum DeviceSortField
{
    Ip,
    Code,
    Hostname,
    Vendor,
    Brand
}
