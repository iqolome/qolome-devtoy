using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qolome.Nets.Core.Scanning;
using Qolome_Devtoy_App.Helpers;

namespace Qolome.Nets.App.ViewModels;

public partial class HostInfoViewModel : ObservableObject
{
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private List<LocalNicRow> _allAdapters = [];

    public ObservableCollection<LocalNicRow> Adapters { get; } = [];

    [ObservableProperty]
    public partial string DeviceName { get; set; } = "—";

    [ObservableProperty]
    public partial string DeviceModel { get; set; } = "—";

    [ObservableProperty]
    public partial string ProcessorDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string MemoryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string OsDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string DiskInfoDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string GpuInfoDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string EgressIpv4 { get; set; } = "—";

    [ObservableProperty]
    public partial string EgressIpv6 { get; set; } = "—";

    [ObservableProperty]
    public partial string Asn { get; set; } = "—";

    [ObservableProperty]
    public partial string Isp { get; set; } = "—";

    [ObservableProperty]
    public partial string Organization { get; set; } = "—";

    [ObservableProperty]
    public partial string TimeZone { get; set; } = "—";

    [ObservableProperty]
    public partial string Location { get; set; } = "—";

    [ObservableProperty]
    public partial string Status { get; set; } = "就绪";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial string FilterText { get; set; } = "";

    /// <summary>网卡类型筛选：全部 / 物理 / 虚拟 / NDIS 筛选器。</summary>
    public List<NicKindFilterOption> KindFilterOptions { get; } =
    [
        new(NicKindFilter.All, "全部"),
        new(NicKindFilter.Physical, "物理网卡"),
        new(NicKindFilter.Virtual, "虚拟网卡"),
        new(NicKindFilter.NdisFilter, "NDIS筛选器"),
    ];

    [ObservableProperty]
    public partial NicKindFilterOption? SelectedKindFilter { get; set; }

    [ObservableProperty]
    public partial LocalNicRow? SelectedAdapter { get; set; }

    public HostInfoViewModel()
    {
        SelectedKindFilter = KindFilterOptions[0];
    }

    public async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;
        var version = Interlocked.Increment(ref _loadVersion);

        IsBusy = true;
        HasError = false;
        ErrorMessage = "";
        Status = "正在刷新本机硬件与网络信息…";

        try
        {
            var parallel = UiPerfFlags.ParallelCollectors;
            var hardwareTask = LocalHardwareInventory.GetInfoAsync(parallel, token);
            var nicTask = LocalNicInventory.GetAdaptersAsync(parallel, token);
            var egressTask = EgressNetworkLookup.QueryAsync(token);
            await Task.WhenAll(hardwareTask, nicTask, egressTask).ConfigureAwait(true);

            if (version != _loadVersion || token.IsCancellationRequested)
            {
                return;
            }

            ApplyHardware(await hardwareTask.ConfigureAwait(true));

            var nics = await nicTask.ConfigureAwait(true);
            _allAdapters = nics.Select(LocalNicRow.From).ToList();
            ApplyAdapterFilter();

            var egress = await egressTask.ConfigureAwait(true);
            ApplyEgress(egress);
            Status = $"已刷新 · 显示 {Adapters.Count}/{_allAdapters.Count} 块网卡 · {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            Status = "刷新失败";
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void ApplyHardware(LocalHardwareInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.Error)
            && info.DeviceName == "—"
            && info.ProcessorDisplay == "—")
        {
            DeviceName = "—";
            DeviceModel = "—";
            ProcessorDisplay = "—";
            MemoryDisplay = "—";
            OsDisplay = "—";
            DiskInfoDisplay = info.Error;
            GpuInfoDisplay = "—";
            return;
        }

        DeviceName = Display(info.DeviceName);
        DeviceModel = Display(info.DeviceModel);
        ProcessorDisplay = Display(info.ProcessorDisplay);
        MemoryDisplay = Display(info.MemoryDisplay);
        OsDisplay = Display(info.OsDisplay);
        DiskInfoDisplay = info.DiskLines.Count > 0
            ? string.Join(Environment.NewLine, info.DiskLines)
            : "—";
        GpuInfoDisplay = info.GpuLines.Count > 0
            ? string.Join(Environment.NewLine, info.GpuLines)
            : "—";
    }

    private void ApplyEgress(EgressNetworkInfo egress)
    {
        if (!string.IsNullOrWhiteSpace(egress.Error)
            && string.IsNullOrWhiteSpace(egress.Ipv4)
            && string.IsNullOrWhiteSpace(egress.Ipv6))
        {
            HasError = true;
            ErrorMessage = egress.Error;
            EgressIpv4 = "—";
            EgressIpv6 = "—";
            Asn = "—";
            Isp = "—";
            Organization = "—";
            TimeZone = "—";
            Location = "—";
            return;
        }

        EgressIpv4 = Display(egress.Ipv4);
        EgressIpv6 = Display(egress.Ipv6);
        Asn = Display(egress.Asn);
        Isp = Display(egress.Isp);
        Organization = Display(egress.Organization);
        TimeZone = Display(egress.TimeZone);
        Location = Display(egress.Location);
        if (!string.IsNullOrWhiteSpace(egress.Error))
        {
            HasError = true;
            ErrorMessage = egress.Error;
        }
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    partial void OnFilterTextChanged(string value) => ApplyAdapterFilter();

    partial void OnSelectedKindFilterChanged(NicKindFilterOption? value) => ApplyAdapterFilter();

    private void ApplyAdapterFilter()
    {
        var keyword = (FilterText ?? string.Empty).Trim();
        var kind = SelectedKindFilter?.Kind ?? NicKindFilter.All;
        IEnumerable<LocalNicRow> query = _allAdapters;
        query = kind switch
        {
            NicKindFilter.Physical => query.Where(a => a.Kind == LocalNicKind.Physical),
            NicKindFilter.Virtual => query.Where(a => a.Kind == LocalNicKind.Virtual),
            NicKindFilter.NdisFilter => query.Where(a => a.Kind == LocalNicKind.NdisFilter),
            _ => query
        };

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(a =>
                Contains(a.Name, keyword)
                || Contains(a.NetworkName, keyword)
                || Contains(a.AdapterName, keyword)
                || Contains(a.Description, keyword)
                || Contains(a.Mac, keyword)
                || Contains(a.Ipv4Info, keyword)
                || Contains(a.Ipv6Info, keyword)
                || Contains(a.ConnectionDisplay, keyword));
        }

        var selectedId = SelectedAdapter?.InterfaceId;
        Adapters.Clear();
        foreach (var row in query)
        {
            Adapters.Add(row);
        }

        SelectedAdapter = Adapters.FirstOrDefault(a => a.InterfaceId == selectedId)
            ?? Adapters.FirstOrDefault();
        var kindLabel = kind switch
        {
            NicKindFilter.Physical => "（物理）",
            NicKindFilter.Virtual => "（虚拟）",
            NicKindFilter.NdisFilter => "（NDIS筛选器）",
            _ => string.Empty
        };
        Status = _allAdapters.Count == 0
            ? Status
            : $"显示 {Adapters.Count}/{_allAdapters.Count} 块网卡{kindLabel}"
              + (string.IsNullOrEmpty(keyword) ? string.Empty : $" · 筛选「{keyword}」");
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync() => await LoadAsync();

    private bool CanRefresh() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        NotifyActionAvailability();
    }

    public bool CanEnableSelected => !IsBusy && SelectedAdapter is { IsUp: false };
    public bool CanDisableSelected => !IsBusy && SelectedAdapter is { IsUp: true };
    public bool CanRemoveSelected => !IsBusy && SelectedAdapter is { IsPhysical: false };

    partial void OnSelectedAdapterChanged(LocalNicRow? value) => NotifyActionAvailability();

    private void NotifyActionAvailability()
    {
        OnPropertyChanged(nameof(CanEnableSelected));
        OnPropertyChanged(nameof(CanDisableSelected));
        OnPropertyChanged(nameof(CanRemoveSelected));
    }

    public Task<LocalNicActionResult> EnableSelectedAdapterAsync() =>
        RunAdapterActionAsync("启用", t => LocalNicInventory.EnableAdapter(t.InterfaceId, t.Name));

    public Task<LocalNicActionResult> DisableSelectedAdapterAsync() =>
        RunAdapterActionAsync("禁用", t => LocalNicInventory.DisableAdapter(t.InterfaceId, t.Name));

    public Task<LocalNicActionResult> RemoveSelectedAdapterAsync() =>
        RunAdapterActionAsync(
            "删除",
            t => LocalNicInventory.RemoveVirtualAdapter(
                t.InterfaceId, t.Name, t.PnpDeviceId, t.IsPhysical));

    private async Task<LocalNicActionResult> RunAdapterActionAsync(
        string actionLabel,
        Func<LocalNicRow, LocalNicActionResult> action)
    {
        if (SelectedAdapter is null)
        {
            return LocalNicActionResult.Fail("请先选择网卡。");
        }

        var target = SelectedAdapter;
        IsBusy = true;
        NotifyActionAvailability();
        Status = $"正在{actionLabel}网卡「{target.Name}」…";
        try
        {
            var result = await Task.Run(() => action(target)).ConfigureAwait(true);
            if (result.Success)
            {
                await LoadAsync().ConfigureAwait(true);
                Status = result.Message;
            }
            else
            {
                HasError = true;
                ErrorMessage = result.Message;
                Status = $"{actionLabel}失败";
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            NotifyActionAvailability();
        }
    }
}

public enum NicKindFilter
{
    All,
    Physical,
    Virtual,
    NdisFilter
}

public sealed record NicKindFilterOption(NicKindFilter Kind, string Label)
{
    public override string ToString() => Label;
}

public sealed class LocalNicRow
{
    public required string InterfaceId { get; init; }
    public string? PnpDeviceId { get; init; }
    public required string Name { get; init; }
    public required string NetworkName { get; init; }
    public required string AdapterName { get; init; }
    public required string ConnectionDisplay { get; init; }
    public required string SpeedDisplay { get; init; }
    public required string Ipv4Info { get; init; }
    public required string Ipv6Info { get; init; }
    public required string Gateway { get; init; }
    public required string Mac { get; init; }
    public required string Duration { get; init; }
    public required string Description { get; init; }
    public required LocalNicKind Kind { get; init; }
    public required bool IsUp { get; init; }
    public required string KindLabel { get; init; }

    public bool IsPhysical => Kind == LocalNicKind.Physical;

    public static LocalNicRow From(LocalNicInfo info) => new()
    {
        InterfaceId = info.InterfaceId,
        PnpDeviceId = info.PnpDeviceId,
        Name = info.Name,
        NetworkName = info.NetworkName,
        AdapterName = info.AdapterName,
        ConnectionDisplay = info.ConnectionDisplay,
        SpeedDisplay = info.SpeedDisplay,
        Ipv4Info = info.Ipv4Info,
        Ipv6Info = info.Ipv6Info,
        Gateway = info.Gateway,
        Mac = info.Mac,
        Duration = info.Duration,
        Description = info.Description,
        Kind = info.Kind,
        IsUp = info.IsUp,
        KindLabel = info.KindLabel
    };
}
