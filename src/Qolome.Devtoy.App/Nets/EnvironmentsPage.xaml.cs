using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App.ViewModels;

namespace Qolome.Nets.App;

public sealed partial class EnvironmentsPage : Page
{
    public EnvironmentsViewModel ViewModel { get; } = new();

    public EnvironmentsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void AddEnv_Click(object sender, RoutedEventArgs e)
        => await RunAsync(() => PromptDialogs.PromptTwoFieldsAsync(
            XamlRoot, "新增网络环境", "名称", "默认 CIDR", ViewModel.AddEnvironmentAsync));

    private async void EditEnv_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            return;
        }

        var env = ViewModel.SelectedEnvironment;
        await RunAsync(() => PromptDialogs.PromptTwoFieldsAsync(
            XamlRoot, "编辑网络环境", "名称", "默认 CIDR", ViewModel.EditEnvironmentAsync, env.Name, env.DefaultCidr));
    }

    private async void DeleteEnv_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEnvironment is null)
        {
            return;
        }

        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除网络环境", $"确定删除「{ViewModel.SelectedEnvironment.Name}」？"))
        {
            return;
        }

        await RunAsync(ViewModel.DeleteEnvironmentAsync);
    }

    private async void AddGroup_Click(object sender, RoutedEventArgs e)
        => await RunAsync(() => PromptDialogs.PromptTwoFieldsAsync(
            XamlRoot, "新增分组", "名称", "说明", ViewModel.AddGroupAsync));

    private async void EditGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is null)
        {
            return;
        }

        var group = ViewModel.SelectedGroup;
        await RunAsync(() => PromptDialogs.PromptTwoFieldsAsync(
            XamlRoot, "编辑分组", "名称", "说明", ViewModel.EditGroupAsync, group.Name, group.Description));
    }

    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGroup is null)
        {
            return;
        }

        if (!await PromptDialogs.ConfirmAsync(XamlRoot, "删除分组", $"确定删除分组「{ViewModel.SelectedGroup.Name}」？"))
        {
            return;
        }

        await RunAsync(ViewModel.DeleteGroupAsync);
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
