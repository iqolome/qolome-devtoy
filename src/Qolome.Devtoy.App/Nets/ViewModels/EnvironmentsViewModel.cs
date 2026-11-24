using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Qolome.Nets.Core.Data;

namespace Qolome.Nets.App.ViewModels;

public partial class EnvironmentsViewModel : ObservableObject
{
    private readonly InventoryService _inventory = AppServices.Inventory;

    public ObservableCollection<NetworkEnvironment> Environments { get; } = [];

    public ObservableCollection<DeviceGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial NetworkEnvironment? SelectedEnvironment { get; set; }

    [ObservableProperty]
    public partial DeviceGroup? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public async Task LoadAsync()
    {
        var list = await _inventory.GetEnvironmentsAsync();
        Environments.Clear();
        foreach (var item in list)
        {
            Environments.Add(item);
        }

        SelectedEnvironment = Environments.FirstOrDefault(e => e.Id == AppServices.SelectedEnvironmentId)
            ?? Environments.FirstOrDefault();
    }

    partial void OnSelectedEnvironmentChanged(NetworkEnvironment? value)
    {
        AppServices.SelectedEnvironmentId = value?.Id;
        _ = ReloadGroupsAsync();
    }

    private async Task ReloadGroupsAsync()
    {
        Groups.Clear();
        SelectedGroup = null;
        if (SelectedEnvironment is null)
        {
            return;
        }

        foreach (var group in await _inventory.GetGroupsAsync(SelectedEnvironment.Id))
        {
            Groups.Add(group);
        }
    }

    public async Task AddEnvironmentAsync(string name, string extra)
    {
        await _inventory.AddEnvironmentAsync(name, null, extra);
        Status = "已添加网络环境。";
        await LoadAsync();
    }

    public async Task EditEnvironmentAsync(string name, string extra)
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        await _inventory.UpdateEnvironmentAsync(SelectedEnvironment.Id, name, SelectedEnvironment.Description, extra);
        Status = "已更新网络环境。";
        await LoadAsync();
    }

    public async Task DeleteEnvironmentAsync()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        await _inventory.DeleteEnvironmentAsync(SelectedEnvironment.Id);
        Status = "已删除网络环境。";
        await LoadAsync();
    }

    public async Task AddGroupAsync(string name, string extra)
    {
        if (SelectedEnvironment is null)
        {
            throw new InvalidOperationException("请先选择网络环境。");
        }

        await _inventory.AddGroupAsync(SelectedEnvironment.Id, name, extra);
        Status = "已添加分组。设备只能属于当前网络环境。";
        await ReloadGroupsAsync();
    }

    public async Task EditGroupAsync(string name, string extra)
    {
        if (SelectedGroup is null)
        {
            return;
        }

        await _inventory.UpdateGroupAsync(SelectedGroup.Id, name, extra);
        Status = "已更新分组。";
        await ReloadGroupsAsync();
    }

    public async Task DeleteGroupAsync()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        await _inventory.DeleteGroupAsync(SelectedGroup.Id);
        Status = "已删除分组。";
        await ReloadGroupsAsync();
    }
}
