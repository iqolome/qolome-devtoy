using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Devtoy.Core.Services;
using Qolome_Devtoy_App.Helpers;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class ImportExportPage : Page
{
    private readonly DevtoySession _session = AppServices.Session;

    public ImportExportPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
            ConfigPathText.Text = Path.Combine(DevtoyPaths.GlobalRoot, "codegen-global.json");
    }

    private async void OnExportConfig(object sender, RoutedEventArgs e) =>
        await UiBusy.RunAsync("正在导出配置…", () => _session.ExportConfigAsync())
            .ConfigureAwait(true);

    private async void OnImportConfig(object sender, RoutedEventArgs e) =>
        await UiBusy.RunAsync("正在导入配置…", () => _session.ImportConfigAsync())
            .ConfigureAwait(true);
}
