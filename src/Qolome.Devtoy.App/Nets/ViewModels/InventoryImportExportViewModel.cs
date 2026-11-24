using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.App.ViewModels;

/// <summary>本地网络资产 SQL 导入导出（供「配置管理 → 导入导出」使用）。</summary>
public partial class InventoryImportExportViewModel : ObservableObject
{
    private readonly SqlDumpService _sqlDump = new();

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } =
        "导入 / 导出本地 SQLite 中的网络库存 SQL（环境、分组、类型、设备、拓扑等）。字典 SQL 见下方分区。";

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [RelayCommand]
    private async Task ExportSqlAsync()
    {
        var path = await FileDialogs.PickSaveSqlAsync($"qolome-devtoy-nets-{DateTime.Now:yyyyMMdd-HHmm}");
        if (path is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            HasError = false;
            await _sqlDump.ExportAsync(path);
            Status = $"已导出网络资产 SQL：{path}";
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
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportSqlAsync()
    {
        var path = await FileDialogs.PickOpenSqlAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            HasError = false;
            var imported = await _sqlDump.ImportAsync(path);
            await AppServices.PortPurposes.ReloadAsync();
            Status = $"已导入网络资产 SQL（{imported} 条记录）到本地 SQLite。文件：{path}";
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
            IsBusy = false;
        }
    }

    private void ShowError(string message)
    {
        HasError = true;
        Status = message;
    }
}
