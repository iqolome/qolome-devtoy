using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.App;
using Qolome.Nets.App.ViewModels;
using Qolome_Devtoy_App.Helpers;
using Windows.Storage.Pickers;
using WinRT.Interop;
using DevtoyApp = Qolome_Devtoy_App.App;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class ImportExportPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    public InventoryImportExportViewModel Nets { get; } = new();

    public ImportExportPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshDataSourceHint();
    }

    private void RefreshDataSourceHint()
    {
        var ds = _session.Settings.DataSource;
        DataSourceHintText.Text =
            $"当前数据源：{ds.DatabaseKind} {ds.Username}@{ds.Host}:{ds.Port}/{ds.Database}";
    }

    private async void ImportNetsSql_Click(object sender, RoutedEventArgs e)
    {
        if (!await PromptDialogs.ConfirmAsync(
                XamlRoot,
                "导入网络资产 SQL",
                "将按脚本清空并写入本地 SQLite 中的库存表（环境、分组、类型、设备、拓扑等）。确定继续？"))
        {
            return;
        }

        if (Nets.ImportSqlCommand.CanExecute(null))
        {
            await Nets.ImportSqlCommand.ExecuteAsync(null);
        }
    }

    private async void OnExportDictSql(object sender, RoutedEventArgs e)
    {
        RefreshDataSourceHint();
        var path = await PickSaveSqlAsync($"qolome-devtoy-dict-{DateTime.Now:yyyyMMdd-HHmm}")
            .ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await UiBusy.RunAsync("正在导出字典 SQL…", () => _session.ExportDictSqlAsync(path))
                .ConfigureAwait(true);
            DictStatusText.Text = "已导出：" + path;
        }
        catch (Exception ex)
        {
            DictStatusText.Text = ex.Message;
            await ShowErrorAsync("导出字典 SQL", ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnImportDictSql(object sender, RoutedEventArgs e)
    {
        RefreshDataSourceHint();
        if (!await ConfirmDictImportAsync().ConfigureAwait(true))
        {
            return;
        }

        var path = await PickOpenSqlAsync().ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            var affected = await UiBusy
                .RunAsync("正在导入字典 SQL…", () => _session.ImportDictSqlAsync(path))
                .ConfigureAwait(true);
            DictStatusText.Text = $"已导入（影响 {affected}）：{path}";
        }
        catch (Exception ex)
        {
            DictStatusText.Text = ex.Message;
            await ShowErrorAsync("导入字典 SQL", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task<bool> ConfirmDictImportAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "导入字典 SQL",
            Content = "将按脚本覆盖当前数据源中的字典表（先 DELETE 再执行 INSERT）。确定继续？",
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private static async Task<string?> PickSaveSqlAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("SQL 脚本", [".sql"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(DevtoyApp.CurrentWindow));
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static async Task<string?> PickOpenSqlAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".sql");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(DevtoyApp.CurrentWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
