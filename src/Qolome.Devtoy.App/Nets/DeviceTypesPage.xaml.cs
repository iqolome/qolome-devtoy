using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Qolome.Nets.App.ViewModels;
using Qolome.Nets.Core.Data;
using Qolome_Devtoy_App.Helpers;

namespace Qolome.Nets.App;

public sealed partial class DeviceTypesPage : Page
{
    private bool _suppressCheckSync;

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
        ListViewCheckOnlySelection.RunProgrammatic(TypesList, () => TypesList.SelectedItems.Clear());
        SyncHeaderSelectAll();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
        => await RunAsync(() => PromptDialogs.PromptDeviceTypeAsync(XamlRoot, "新增设备类型", ViewModel.AddAsync));

    private async void Delete_Click(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

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

    private void Types_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ListViewCheckOnlySelection.IsProgrammatic(TypesList))
        {
            return;
        }

        SyncHeaderSelectAll();
        SyncVisibleRowChecks();
    }

    private IReadOnlyList<DeviceType> GetSelectedTypes()
    {
        var items = TypesList.SelectedItems.OfType<DeviceType>().ToList();
        if (items.Count == 0 && ViewModel.SelectedType is DeviceType current)
        {
            return [current];
        }

        return items;
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelectedTypes();
        if (selected.Count == 0)
        {
            ViewModel.Status = "请先选择要删除的设备类型。";
            return;
        }

        var preview = selected.Count <= 8
            ? string.Join("、", selected.Select(t => t.Name))
            : string.Join("、", selected.Take(8).Select(t => t.Name)) + $" 等 {selected.Count} 种";
        var message = selected.Count == 1
            ? $"确定删除「{selected[0].Name}」？"
            : $"确定删除选中的 {selected.Count} 种设备类型？\n{preview}";
        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除设备类型", message))
        {
            return;
        }

        await RunAsync(() => ViewModel.DeleteSelectedAsync(selected));
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync || sender is not CheckBox box || !ReferenceEquals(box, HeaderSelectAll))
        {
            return;
        }

        ListViewCheckOnlySelection.RunProgrammatic(TypesList, () =>
        {
            if (box.IsChecked == true)
            {
                TypesList.SelectedItems.Clear();
                foreach (var item in TypesList.Items)
                {
                    TypesList.SelectedItems.Add(item);
                }
            }
            else if (box.IsChecked == false)
            {
                TypesList.SelectedItems.Clear();
            }
        });
    }

    private void OnRowCheckLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: DeviceType type } box)
        {
            return;
        }

        ListViewCheckOnlySelection.Attach(TypesList);
        _suppressCheckSync = true;
        box.IsChecked = TypesList.SelectedItems.Contains(type);
        _suppressCheckSync = false;
    }

    private void OnRowCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckSync || sender is not CheckBox { Tag: DeviceType type } box)
        {
            return;
        }

        ListViewCheckOnlySelection.RunProgrammatic(TypesList, () =>
        {
            if (box.IsChecked == true)
            {
                if (!TypesList.SelectedItems.Contains(type))
                {
                    TypesList.SelectedItems.Add(type);
                }
            }
            else
            {
                TypesList.SelectedItems.Remove(type);
            }
        });
    }

    private void SyncHeaderSelectAll()
    {
        var total = TypesList.Items?.Count ?? 0;
        var selected = TypesList.SelectedItems?.Count ?? 0;
        _suppressCheckSync = true;
        if (total == 0 || selected == 0)
        {
            HeaderSelectAll.IsChecked = false;
        }
        else if (selected >= total)
        {
            HeaderSelectAll.IsChecked = true;
        }
        else
        {
            HeaderSelectAll.IsChecked = null;
        }

        _suppressCheckSync = false;
    }

    private void SyncVisibleRowChecks()
    {
        if (TypesList.ItemsSource is null)
        {
            return;
        }

        _suppressCheckSync = true;
        foreach (var item in TypesList.Items)
        {
            if (item is not DeviceType type
                || TypesList.ContainerFromItem(type) is not ListViewItem container)
            {
                continue;
            }

            if (FindRowCheckBox(container) is { } box)
            {
                box.IsChecked = TypesList.SelectedItems.Contains(type);
            }
        }

        _suppressCheckSync = false;
    }

    private static CheckBox? FindRowCheckBox(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is CheckBox { Tag: DeviceType, Content: null } box)
            {
                return box;
            }

            var nested = FindRowCheckBox(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private async void RowEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceType type })
        {
            return;
        }

        ViewModel.SelectedType = type;
        await RunAsync(() => PromptDialogs.PromptDeviceTypeAsync(
            XamlRoot, "编辑设备类型", ViewModel.EditAsync, type));
    }

    private async void RowDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DeviceType type })
        {
            return;
        }

        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除设备类型", $"确定删除「{type.Name}」？"))
        {
            return;
        }

        await RunAsync(() => ViewModel.DeleteSelectedAsync([type]));
    }
}