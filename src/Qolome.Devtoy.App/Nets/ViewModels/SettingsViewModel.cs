using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
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

    public static string PortConcurrencyDefaultText =>
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
            OuiHint = (sameBatch ? "IEEE 批次未变，未改 OUI 表。" : "已按批次增量写入远程业务库。")
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

}
