using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Qolome.Devtoy.Core.Models;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly NetworkAssetSyncService _assetSync = new();
    private bool _loadingScan;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial int PortScanConcurrency { get; set; } = ScanOptions.DefaultPortScanConcurrency;

    [ObservableProperty]
    public partial int IpScanBatchSize { get; set; } = 50;

    [ObservableProperty]
    public partial int IpScanConcurrency { get; set; } = 3;

    [ObservableProperty]
    public partial int PingCount { get; set; } = 4;

    [ObservableProperty]
    public partial string ScanSettingsHint { get; set; } = "";

    [ObservableProperty]
    public partial string LocalConnectionText { get; set; } = "";

    [ObservableProperty]
    public partial string RemoteConnectionText { get; set; } = "";

    [ObservableProperty]
    public partial string SyncHint { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncEnabled))]
    public partial bool IsSyncing { get; set; }

    [ObservableProperty]
    public partial double SyncProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool SyncProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial Visibility SyncProgressVisibility { get; set; } = Visibility.Collapsed;

    public bool SyncEnabled => !IsSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OuiUpdateEnabled))]
    public partial bool IsOuiUpdating { get; set; }

    [ObservableProperty]
    public partial double OuiProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool OuiProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial Visibility OuiProgressVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial string OuiHint { get; set; } = "";

    public bool OuiUpdateEnabled => !IsOuiUpdating;

    public string PortConcurrencyDefaultText =>
        $"本机 {Environment.ProcessorCount} 核，默认 {ScanOptions.DefaultPortScanConcurrency}（核心数 × 3）";

    public SettingsViewModel()
    {
        LoadScanFromStore();
    }

    private void LoadScanFromStore()
    {
        _loadingScan = true;
        var scan = AppServices.Scan;
        scan.Clamp();
        PortScanConcurrency = scan.PortScanConcurrency;
        IpScanBatchSize = scan.IpScanBatchSize;
        IpScanConcurrency = scan.IpScanConcurrency;
        PingCount = scan.PingCount;
        ScanSettingsHint = "更改后立即保存，下次扫描生效。";
        _loadingScan = false;
        LoadOuiStatus();
        RefreshConnectionStatus();
        if (!string.IsNullOrWhiteSpace(AppDatabase.StartupWarning))
        {
            SyncHint = AppDatabase.StartupWarning;
            HasError = true;
        }
    }

    partial void OnPortScanConcurrencyChanged(int value) => PersistScanSettings();

    partial void OnIpScanBatchSizeChanged(int value) => PersistScanSettings();

    partial void OnIpScanConcurrencyChanged(int value) => PersistScanSettings();

    partial void OnPingCountChanged(int value) => PersistScanSettings();

    private void PersistScanSettings()
    {
        if (_loadingScan)
        {
            return;
        }

        AppServices.Scan.PortScanConcurrency = PortScanConcurrency;
        AppServices.Scan.IpScanBatchSize = IpScanBatchSize;
        AppServices.Scan.IpScanConcurrency = IpScanConcurrency;
        AppServices.Scan.PingCount = PingCount;
        AppServices.SaveScanSettings();
        ScanSettingsHint = "已保存。";
    }

    [RelayCommand]
    private void ResetScanDefaults()
    {
        _loadingScan = true;
        var defaults = ScanOptions.Defaults();
        PortScanConcurrency = defaults.PortScanConcurrency;
        IpScanBatchSize = defaults.IpScanBatchSize;
        IpScanConcurrency = defaults.IpScanConcurrency;
        PingCount = defaults.PingCount;
        _loadingScan = false;
        PersistScanSettings();
        ScanSettingsHint = "已恢复默认：端口并发 = CPU×3，单次 IP 50，IP 并发 3，Ping 4 次。";
    }

    private void LoadOuiStatus() => OuiHint = OuiStore.Describe();

    [RelayCommand]
    private async Task UpdateOuiAsync()
    {
        try
        {
            IsBusy = true;
            IsOuiUpdating = true;
            HasError = false;
            OuiProgressVisibility = Visibility.Visible;
            OuiProgressIndeterminate = true;
            OuiProgressPercent = 0;
            OuiHint = "正在从 IEEE 下载 MA-L / MA-M / MA-S…";
            var previous = OuiStore.Meta?.BatchKey;
            var progress = new Progress<ScanProgress>(p =>
            {
                OuiProgressIndeterminate = p.Indeterminate;
                OuiProgressPercent = p.Percent;
                OuiHint = p.Message;
            });
            await OuiStore.UpdateFromIeeeAsync(progress);
            var sameBatch = previous is not null
                && string.Equals(previous, OuiStore.Meta?.BatchKey, StringComparison.OrdinalIgnoreCase);
            OuiProgressIndeterminate = false;
            OuiProgressPercent = 100;
            OuiHint = (sameBatch ? "IEEE 批次未变，未改 OUI 表。" : "已按批次增量写入当前数据库。")
                + OuiStore.Describe();
        }
        catch (ScanException ex)
        {
            HasError = true;
            OuiHint = ex.Message;
        }
        catch (Exception ex)
        {
            HasError = true;
            OuiHint = ex.Message;
        }
        finally
        {
            IsOuiUpdating = false;
            OuiProgressVisibility = Visibility.Collapsed;
            IsBusy = false;
        }
    }

    /// <summary>进入页面时刷新本地/远程连接摘要（不探测连通性）。</summary>
    public void ReloadConnectionStatus() => RefreshConnectionStatus();

    private void RefreshConnectionStatus()
    {
        UpdateConnectionTexts(out var remoteOk, out var remoteError);
        if (remoteOk)
        {
            SyncHint = "使用下方按钮在本地 SQLite 与「数据源设置」中的远程库之间覆盖同步网络资产（环境 / 分组 / 类型 / 设备等；不含 OUI）。备份与 SQL 请到「配置管理 → 导入导出」。";
            HasError = false;
        }
        else
        {
            SyncHint = remoteError;
            HasError = true;
        }
    }

    private void UpdateConnectionTexts(out bool remoteOk, out string remoteError)
    {
        LocalConnectionText = $"本地：{AppDatabase.DescribeConnection()}";
        try
        {
            var remote = NetsRuntime.RequireRemoteDataSource();
            RemoteConnectionText = $"远程（数据源设置）：{DescribeRemote(remote)}";
            remoteOk = true;
            remoteError = "";
        }
        catch (Exception ex)
        {
            RemoteConnectionText = "远程（数据源设置）：未配置或不可用";
            remoteOk = false;
            remoteError = ex.Message;
        }
    }

    private static string DescribeRemote(DataSourceSettings remote) =>
        $"{remote.DatabaseKind} {remote.Username}@{remote.Host}:{remote.Port}/{remote.Database}";

    [RelayCommand]
    private async Task SyncToRemoteAsync() => await RunSyncAsync("本地 → 远程", (remote, progress) =>
        _assetSync.SyncLocalToRemoteAsync(remote, progress));

    [RelayCommand]
    private async Task SyncToLocalAsync() => await RunSyncAsync("远程 → 本地", (remote, progress) =>
        _assetSync.SyncRemoteToLocalAsync(remote, progress));

    private async Task RunSyncAsync(
        string directionLabel,
        Func<DataSourceSettings, IProgress<ScanProgress>, Task<string>> sync)
    {
        if (IsSyncing)
        {
            return;
        }

        try
        {
            IsBusy = true;
            IsSyncing = true;
            HasError = false;
            BeginSyncProgress($"{directionLabel}：准备中…");
            var remote = NetsRuntime.RequireRemoteDataSource();
            var progress = new Progress<ScanProgress>(ApplySyncProgress);
            SyncHint = await sync(remote, progress);
            SyncProgressIndeterminate = false;
            SyncProgressPercent = 100;
            LoadOuiStatus();
            await AppServices.PortPurposes.ReloadAsync();
            UpdateConnectionTexts(out _, out _);
        }
        catch (ScanException ex)
        {
            HasError = true;
            SyncHint = ex.Message;
        }
        catch (Exception ex)
        {
            HasError = true;
            SyncHint = DbExceptionFormatter.Format(ex, directionLabel);
        }
        finally
        {
            EndSyncProgress();
            IsSyncing = false;
            IsBusy = false;
        }
    }

    private void BeginSyncProgress(string message)
    {
        SyncProgressVisibility = Visibility.Visible;
        SyncProgressIndeterminate = true;
        SyncProgressPercent = 0;
        SyncHint = message;
    }

    private void ApplySyncProgress(ScanProgress p)
    {
        SyncProgressIndeterminate = p.Indeterminate;
        SyncProgressPercent = p.Percent;
        SyncHint = p.Message;
    }

    private void EndSyncProgress() => SyncProgressVisibility = Visibility.Collapsed;
}
