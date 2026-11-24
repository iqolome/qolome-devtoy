using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class ImportExportPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    public ImportExportPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
            ConfigPathText.Text = "配置库：" + DevtoyPaths.LocalDbPath;
    }

    private async void OnExportConfigSql(object sender, RoutedEventArgs e)
    {
        var path = await PickSaveSqlAsync($"qolome-devtoy-config-{DateTime.Now:yyyyMMdd-HHmm}")
            .ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        try
        {
            await UiBusy.RunAsync("正在导出配置 SQL…", () => _session.ExportConfigSqlAsync(path))
                .ConfigureAwait(true);
            StatusText.Text = "已导出：" + path;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await ShowErrorAsync("导出配置 SQL", ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnImportConfigSql(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmImportAsync().ConfigureAwait(true))
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
                .RunAsync("正在导入配置 SQL…", () => _session.ImportConfigSqlAsync(path))
                .ConfigureAwait(true);
            StatusText.Text = $"已导入（影响 {affected}）：{path}";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await ShowErrorAsync("导入配置 SQL", ex.Message).ConfigureAwait(true);
        }
    }

    private async Task<bool> ConfirmImportAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "导入配置 SQL",
            Content = "将覆盖本机 SQLite 中的配置表（devtoy_doc），不会修改远程业务库。确定继续？",
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
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
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
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.CurrentWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
