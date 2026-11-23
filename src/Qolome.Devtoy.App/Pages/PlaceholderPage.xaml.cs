using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is PlaceholderNavArgs args)
        {
            TitleText.Text = args.Title;
            BodyText.Text = args.Body;
        }
    }
}

public sealed record PlaceholderNavArgs(string Title, string Body);