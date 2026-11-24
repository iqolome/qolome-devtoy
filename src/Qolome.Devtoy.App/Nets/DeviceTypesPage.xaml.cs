using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App.ViewModels;

namespace Qolome.Nets.App;

public sealed partial class DeviceTypesPage : Page
{
    public DeviceTypesViewModel ViewModel { get; } = new();

    public DeviceTypesPage()
    {
        InitializeComponent();
        ViewModel.GroupsChanged += BindGroups;
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private void BindGroups()
    {
        TypesCvs.Source = null;
        TypesCvs.Source = ViewModel.GroupedTypes;
        TypesList.ItemsSource = TypesCvs.View;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
        => await RunAsync(() => PromptDialogs.PromptDeviceTypeAsync(XamlRoot, "新增设备类型", ViewModel.AddAsync));

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedType is null)
        {
            return;
        }

        await RunAsync(() => PromptDialogs.PromptDeviceTypeAsync(
            XamlRoot, "编辑设备类型", ViewModel.EditAsync, ViewModel.SelectedType));
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedType is null)
        {
            return;
        }

        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除设备类型", $"确定删除「{ViewModel.SelectedType.Name}」？"))
        {
            return;
        }

        await RunAsync(ViewModel.DeleteAsync);
    }

    private async void Template_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.DownloadTemplateAsync);

    private async void Import_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.ImportAsync);

    private async void Export_Click(object sender, RoutedEventArgs e) => await RunAsync(ViewModel.ExportAsync);

    private async void ExportIcons_Click(object sender, RoutedEventArgs e)
    {
        var format = await PromptDialogs.PromptIconExportFormatAsync(XamlRoot);
        if (format is null)
        {
            return;
        }

        await RunAsync(() => ViewModel.ExportIconsAsync(format.Value));
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
