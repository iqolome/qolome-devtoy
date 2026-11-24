using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Excel;

namespace Qolome.Nets.App.ViewModels;

public sealed class DeviceTypeColorFilter
{
    public string? Key { get; init; }
    public string Name { get; init; } = "";
    public string ColorHex { get; init; } = "#00000000";
}

public sealed class DeviceTypeColorGroupView : List<DeviceType>
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string ColorHex { get; init; } = "";
}

public partial class DeviceTypesViewModel : ObservableObject
{
    private readonly InventoryService _inventory = AppServices.Inventory;
    private List<DeviceType> _allTypes = [];

    public ObservableCollection<DeviceType> Types { get; } = [];
    public ObservableCollection<DeviceTypeColorGroupView> GroupedTypes { get; } = [];

    public IReadOnlyList<DeviceTypeColorFilter> GroupFilters { get; } =
    [
        new() { Key = null, Name = "全部分组", ColorHex = "#00000000" },
        .. DeviceTypeColorGroups.All.Select(g => new DeviceTypeColorFilter
        {
            Key = g.Key,
            Name = g.Name,
            ColorHex = g.Hex
        })
    ];

    public event Action? GroupsChanged;

    [ObservableProperty]
    public partial DeviceType? SelectedType { get; set; }

    [ObservableProperty]
    public partial DeviceTypeColorFilter? SelectedGroupFilter { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public DeviceTypesViewModel()
    {
        SelectedGroupFilter = GroupFilters[0];
    }

    public async Task LoadAsync()
    {
        _allTypes = [.. await _inventory.GetDeviceTypesAsync()];
        ApplyView();
    }

    partial void OnSearchQueryChanged(string value) => ApplyView();

    partial void OnSelectedGroupFilterChanged(DeviceTypeColorFilter? value)
    {
        if (value is null)
        {
            return;
        }

        ApplyView();
    }

    private void ApplyView()
    {
        var selectedId = SelectedType?.Id;
        Types.Clear();
        GroupedTypes.Clear();

        IEnumerable<DeviceType> query = _allTypes;
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var keyword = SearchQuery.Trim();
            query = query.Where(t =>
                t.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || t.Code.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || DeviceTypeColorGroups.DisplayName(t.ColorGroup, t.Code)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedGroupFilter?.Key is string groupKey)
        {
            query = query.Where(t => DeviceTypeColorGroups.Normalize(t.ColorGroup, t.Code) == groupKey);
        }

        var items = query
            .OrderBy(t => DeviceTypeColorGroups.SortOrder(t.ColorGroup, t.Code))
            .ThenBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToList();
        foreach (var item in items)
        {
            Types.Add(item);
        }

        foreach (var group in items.GroupBy(t => DeviceTypeColorGroups.Resolve(t.ColorGroup, t.Code)))
        {
            var view = new DeviceTypeColorGroupView
            {
                Key = group.Key.Key,
                Title = group.Key.Name,
                ColorHex = group.Key.Hex
            };
            view.AddRange(group);
            GroupedTypes.Add(view);
        }

        SelectedType = Types.FirstOrDefault(t => t.Id == selectedId);
        var suffix = string.IsNullOrWhiteSpace(SearchQuery) && SelectedGroupFilter?.Key is null
            ? $"{Types.Count} 种设备类型。"
            : $"当前显示 {Types.Count} / {_allTypes.Count} 种设备类型。";
        Status = suffix;
        GroupsChanged?.Invoke();
    }

    public async Task AddAsync(DeviceTypeEditInput input)
    {
        await _inventory.AddDeviceTypeAsync(
            input.Code,
            input.Name,
            input.Description,
            input.IconSourcePath,
            input.BuiltinIconKey,
            input.ColorGroup);
        await LoadAsync();
        Status = "已添加设备类型。";
    }

    public async Task EditAsync(DeviceTypeEditInput input)
    {
        if (SelectedType is null)
        {
            return;
        }

        var id = SelectedType.Id;
        await _inventory.UpdateDeviceTypeAsync(
            SelectedType.Id,
            input.Code,
            input.Name,
            input.Description,
            input.IconSourcePath,
            input.RestoreDefaultIcon,
            input.BuiltinIconKey,
            input.ColorGroup);
        await LoadAsync();
        SelectedType = Types.FirstOrDefault(t => t.Id == id);
        Status = "已更新设备类型。";
    }

    public async Task DeleteAsync()
    {
        if (SelectedType is null)
        {
            return;
        }

        await _inventory.DeleteDeviceTypeAsync(SelectedType.Id);
        await LoadAsync();
        Status = "已删除设备类型。";
    }

    public async Task ExportAsync()
    {
        var path = await FileDialogs.PickSaveExcelAsync("设备类型");
        if (path is null)
        {
            return;
        }

        ExcelInventory.WriteDeviceTypes(path, await _inventory.GetDeviceTypesAsync());
        Status = $"已导出：{path}";
    }

    public async Task ImportAsync()
    {
        var path = await FileDialogs.PickOpenExcelAsync();
        if (path is null)
        {
            return;
        }

        var rows = ExcelInventory.ReadDeviceTypes(path);
        await _inventory.ImportDeviceTypesAsync(rows);
        await LoadAsync();
        Status = $"已导入 {rows.Count} 行设备类型。";
    }

    public async Task DownloadTemplateAsync()
    {
        var path = await FileDialogs.PickSaveExcelAsync("设备类型导入模板");
        if (path is null)
        {
            return;
        }

        ExcelInventory.WriteDeviceTypeTemplate(path);
        Status = $"已保存导入模板：{path}";
    }

    public async Task ExportIconsAsync(DeviceTypeIconExportFormat format)
    {
        var types = Types.Count > 0 ? Types.ToList() : _allTypes;
        if (types.Count == 0)
        {
            Status = "没有可导出的设备类型图标。";
            return;
        }

        var kind = format == DeviceTypeIconExportFormat.Png ? "png" : "svg";
        var path = await FileDialogs.PickSaveZipAsync($"设备类型图标-{kind}-{DateTime.Now:yyyyMMdd-HHmm}");
        if (path is null)
        {
            return;
        }

        var count = await Task.Run(() => DeviceTypeIconExport.WriteZip(path, types, format));
        Status = $"已导出 {count} 个{kind.ToUpperInvariant()} 图标：{path}";
    }
}
