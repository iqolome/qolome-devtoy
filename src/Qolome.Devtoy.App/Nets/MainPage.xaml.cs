using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Qolome.Nets.App.ViewModels;

namespace Qolome.Nets.App;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void AnnotatePort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OpenPortItemViewModel item })
        {
            return;
        }

        try
        {
            await PromptDialogs.PromptPortPurposeAsync(
                XamlRoot,
                item.Port,
                item.Protocol,
                async result =>
                {
                    await AppServices.PortPurposes.UpsertAsync(
                        item.Port, item.Protocol, result.Service, result.Description);
                    ViewModel.RefreshPortPurposes();
                },
                item.Service,
                item.Description);
        }
        catch (Exception ex)
        {
            ViewModel.ProgressText = ex.Message;
            ViewModel.HasError = true;
            ViewModel.ErrorMessage = ex.Message;
        }
    }
}
