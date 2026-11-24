using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Excel;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.App.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly ScanSession _session = new(AppServices.PortPurposes);
    private readonly InventoryService _inventory = AppServices.Inventory;
    private readonly Dictionary<string, HostItemViewModel> _hostsByIp = new(StringComparer.Ordinal);
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _pingCts;

    public MainPageViewModel()
    {
        PortsText = PortListParser.DefaultTcpPortsText;
        UdpPortsText = PortListParser.DefaultUdpPortsText;
        ApplyScanSettings();
        foreach (var adapter in NetworkAdapterEnumerator.GetActiveIpv4Adapters())
        {
            Adapters.Add(adapter);
        }

        if (Adapters.Count > 0)
        {
            SelectedAdapter = Adapters[0];
            TargetText = Adapters[0].Cidr;
        }
        else
        {
            TargetText = "127.0.0.1";
        }
    }

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public ObservableCollection<HostItemViewModel> Hosts { get; } = [];

    public ObservableCollection<PingLineViewModel> PingLines { get; } = [];

    public ObservableCollection<NetworkEnvironment> Environments { get; } = [];

    public ObservableCollection<DeviceGroup> Groups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    public partial NetworkAdapterInfo? SelectedAdapter { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    public partial string TargetText { get; set; }

    [ObservableProperty]
    public partial string PortsText { get; set; }

    [ObservableProperty]
    public partial string UdpPortsText { get; set; }

    [ObservableProperty]
    public partial bool TreatUdpTimeoutAsOpen { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopScanCommand))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "就绪。仅扫描你有权管理的网络。";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial int LiveHostCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPingCommand))]
    public partial HostItemViewModel? SelectedHost { get; set; }

    [ObservableProperty]
    public partial int PingPayloadSize { get; set; } = 32;

    [ObservableProperty]
    public partial int PingCount { get; set; } = 4;

    [ObservableProperty]
    public partial bool PingContinuous { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPingCommand))]
    public partial bool IsPinging { get; set; }

    [ObservableProperty]
    public partial string PingSummaryText { get; set; } = "选择一台主机后可发起 Ping。";

    [ObservableProperty]
    public partial int PingTimeoutMs { get; set; } = 1000;

    [ObservableProperty]
    public partial int ConnectTimeoutMs { get; set; } = 400;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveScanCommand))]
    public partial NetworkEnvironment? SelectedEnvironment { get; set; }

    [ObservableProperty]
    public partial DeviceGroup? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearCheckedCommand))]
    public partial int CheckedCount { get; set; }

    [ObservableProperty]
    public partial bool IsAllChecked { get; set; }

    public string SaveButtonText => CheckedCount > 0
        ? $"保存勾选到设备 ({CheckedCount})"
        : "保存勾选到设备";

    private bool _syncingSelectAll;

    public async Task LoadAsync()
    {
        ApplyScanSettings();
        try
        {
            var environments = await InventoryService.GetEnvironmentsAsync();
            Environments.Clear();
            foreach (var env in environments)
            {
                Environments.Add(env);
            }

            SelectedEnvironment = Environments.FirstOrDefault(e => e.Id == AppServices.SelectedEnvironmentId)
                ?? Environments.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value)
    {
        if (value is not null)
        {
            TargetText = value.Cidr;
        }
    }

    partial void OnSelectedEnvironmentChanged(NetworkEnvironment? value)
    {
        AppServices.SelectedEnvironmentId = value?.Id;
        _ = ReloadGroupsAsync();
    }

    partial void OnSelectedGroupChanged(DeviceGroup? value)
    {
        AppServices.SelectedGroupId = value?.Id;
    }

    partial void OnSelectedHostChanged(HostItemViewModel? value)
    {
        if (!IsPinging)
        {
            PingSummaryText = value is null
                ? "选择一台主机后可发起 Ping。"
                : value.PingStatistics;
        }
    }

    private void ApplyScanSettings()
    {
        var scan = AppServices.Scan;
        scan.Clamp();
        PingCount = scan.PingCount;
    }

    private async Task ReloadGroupsAsync()
    {
        Groups.Clear();
        SelectedGroup = null;
        if (SelectedEnvironment is null)
        {
            return;
        }

        var groups = await InventoryService.GetGroupsAsync(SelectedEnvironment.Id);
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.Id == AppServices.SelectedGroupId);
        AppServices.SelectedGroupId = SelectedGroup?.Id;
    }

    private bool CanStartScan() => !IsScanning && !string.IsNullOrWhiteSpace(TargetText);

    private bool CanStopScan() => IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task StartScanAsync()
    {
        HasError = false;
        ErrorMessage = "";
        Hosts.Clear();
        _hostsByIp.Clear();
        LiveHostCount = 0;
        SelectedHost = null;
        ProgressPercent = 0;
        RefreshCheckedState();

        IReadOnlyList<int> tcpPorts;
        IReadOnlyList<int> udpPorts;
        try
        {
            tcpPorts = PortListParser.ParseOrEmpty(PortsText);
            udpPorts = PortListParser.ParseOrEmpty(UdpPortsText);
            if (tcpPorts.Count == 0 && udpPorts.Count == 0)
            {
                throw new ScanException("请至少填写 TCP 或 UDP 端口。");
            }
        }
        catch (ScanException ex)
        {
            ShowError(ex.Message);
            return;
        }

        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ProgressText = "正在扫描…";

        var scan = AppServices.Scan;
        scan.Clamp();
        var request = new ScanRequest
        {
            Target = TargetText.Trim(),
            TcpPorts = tcpPorts,
            UdpPorts = udpPorts,
            PingTimeoutMs = PingTimeoutMs,
            PingPayloadSize = PingPayloadSize,
            ConnectTimeoutMs = ConnectTimeoutMs,
            HostConcurrency = scan.IpScanConcurrency,
            IpScanBatchSize = scan.IpScanBatchSize,
            PortScanConcurrency = scan.PortScanConcurrency,
            TreatUdpTimeoutAsOpen = TreatUdpTimeoutAsOpen
        };

        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressPercent = p.Percent;
            ProgressText = $"{p.Message}  ·  发现 {LiveHostCount} 台设备";
        });

        try
        {
            await _session.RunAsync(request, progress, OnHostFound, _scanCts.Token);
            ProgressText = _scanCts.IsCancellationRequested
                ? $"已停止。发现 {LiveHostCount} 台设备。"
                : $"扫描完成。发现 {LiveHostCount} 台设备。";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"已停止。发现 {LiveHostCount} 台设备。";
        }
        catch (ScanException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsScanning = false;
            RefreshCheckedState();
        }
    }

    public void RefreshPortPurposes()
    {
        foreach (var host in Hosts)
        {
            foreach (var port in host.Model.OpenPorts)
            {
                AppServices.PortPurposes.Apply(port);
            }

            host.Apply(host.Model);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopScan))]
    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    private bool CanStartPing() => !IsPinging && SelectedHost is not null;

    private bool CanStopPing() => IsPinging;

    [RelayCommand(CanExecute = nameof(CanStartPing))]
    private async Task StartPingAsync()
    {
        if (SelectedHost is null)
        {
            return;
        }

        _pingCts?.Dispose();
        _pingCts = new CancellationTokenSource();
        PingLines.Clear();
        IsPinging = true;
        PingSummaryText = "正在 Ping…";

        var request = new PingRequest
        {
            Target = SelectedHost.Ip,
            PayloadSize = PingPayloadSize,
            Count = PingContinuous ? 0 : Math.Max(1, PingCount),
            TimeoutMs = PingTimeoutMs,
            IntervalMs = 1000
        };

        var replies = new List<PingReplyInfo>();
        try
        {
            await foreach (var reply in _session.Ping.PingAsync(request, _pingCts.Token))
            {
                replies.Add(reply);
                PingLines.Add(new PingLineViewModel(reply));
                PingSummaryText = PingSummary.FromReplies(replies).StatisticsText;
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            PingSummaryText = ex.Message;
        }
        finally
        {
            if (replies.Count > 0)
            {
                PingSummaryText = PingSummary.FromReplies(replies).StatisticsText;
            }

            IsPinging = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopPing))]
    private void StopPing()
    {
        _pingCts?.Cancel();
    }

    private bool CanSaveScan() => SelectedEnvironment is not null && CheckedCount > 0 && !IsScanning;

    [RelayCommand(CanExecute = nameof(CanSaveScan))]
    private async Task SaveScanAsync()
    {
        if (SelectedEnvironment is null)
        {
            ShowError("请先选择网络环境。一台设备只能属于一个网络环境。");
            return;
        }

        var selected = Hosts.Where(h => h.IsChecked).Select(h => h.Model).ToList();
        if (selected.Count == 0)
        {
            ShowError("请先勾选要保存的设备。");
            return;
        }

        try
        {
            IsBusy = true;
            var count = await _inventory.SaveScanResultsAsync(
                SelectedEnvironment.Id,
                SelectedGroup?.Id,
                selected);
            ProgressText = $"已将 {count} 台勾选设备保存到「{SelectedEnvironment.Name}」。";
            HasError = false;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanToggleSelectAll() => Hosts.Count > 0;

    [RelayCommand(CanExecute = nameof(CanToggleSelectAll))]
    private void ToggleSelectAll()
    {
        var check = Hosts.Any(h => !h.IsChecked);
        foreach (var host in Hosts)
        {
            host.SetChecked(check);
        }

        RefreshCheckedState();
    }

    private bool CanClearChecked() => CheckedCount > 0;

    [RelayCommand(CanExecute = nameof(CanClearChecked))]
    private void ClearChecked()
    {
        foreach (var host in Hosts)
        {
            host.SetChecked(false);
        }

        RefreshCheckedState();
    }

    partial void OnIsAllCheckedChanged(bool value)
    {
        if (_syncingSelectAll)
        {
            return;
        }

        foreach (var host in Hosts)
        {
            host.SetChecked(value);
        }

        RefreshCheckedState();
    }

    partial void OnCheckedCountChanged(int value) => OnPropertyChanged(nameof(SaveButtonText));

    private void RefreshCheckedState()
    {
        var checkedCount = Hosts.Count(h => h.IsChecked);
        CheckedCount = checkedCount;
        _syncingSelectAll = true;
        IsAllChecked = Hosts.Count > 0 && checkedCount == Hosts.Count;
        _syncingSelectAll = false;
        OnPropertyChanged(nameof(SaveButtonText));
        SaveScanCommand.NotifyCanExecuteChanged();
        ToggleSelectAllCommand.NotifyCanExecuteChanged();
        ClearCheckedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ExportScanAsync()
    {
        if (Hosts.Count == 0)
        {
            ShowError("没有可导出的扫描结果。");
            return;
        }

        var path = await FileDialogs.PickSaveExcelAsync($"扫描结果-{DateTime.Now:yyyyMMdd-HHmm}");
        if (path is null)
        {
            return;
        }

        try
        {
            ExcelInventory.WriteScan(path, Hosts.Select(h => h.Model).ToList());
            ProgressText = $"已导出扫描结果：{path}";
            HasError = false;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OnHostFound(HostScanResult host)
    {
        var dispatcher = App.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (!dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => OnHostFound(host));
            return;
        }

        if (_hostsByIp.TryGetValue(host.Ip, out var existing))
        {
            existing.Apply(host);
            SaveScanCommand.NotifyCanExecuteChanged();
            return;
        }

        var item = new HostItemViewModel(host, RefreshCheckedState);
        _hostsByIp[host.Ip] = item;
        InsertSorted(item);
        LiveHostCount = Hosts.Count;
        RefreshCheckedState();
    }

    private void InsertSorted(HostItemViewModel item)
    {
        var index = 0;
        while (index < Hosts.Count && IpAddressComparer.Compare(Hosts[index].Ip, item.Ip) < 0)
        {
            index++;
        }

        Hosts.Insert(index, item);
    }

    private void ShowError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        ProgressText = message;
    }
}
