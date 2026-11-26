using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Qolome.Devtoy.Core;
using Qolome.Nets.App;
using Qolome_Devtoy_App.Services;

namespace Qolome_Devtoy_App.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        ProductText.Text = AboutInfo.ProductName;
        VersionText.Text = "版本 " + AboutInfo.Version;
        CompanyZhText.Text = AboutInfo.CompanyNameZh;
        CompanyEnText.Text = AboutInfo.CompanyNameEn;
        LicenseText.Text =
            $"本软件采用 {AboutInfo.LicenseName} 授权。完整协议见仓库根目录 LICENSE 文件。";
        WebsiteLink.Content = AboutInfo.WebsiteUrl;
        WebsiteLink.NavigateUri = new Uri(AboutInfo.WebsiteUrl);
        LicenseLink.NavigateUri = new Uri(AboutInfo.LicenseUrl);
        ServiceAgreementText.Text = AppInfo.ServiceAgreement;
        LoadProductLogo();
    }

    private void LoadProductLogo()
    {
        var path = IconPackPaths.GetPreviewPath(AppServices.IconTheme.ResolveEffectiveVariant());
        if (path is null)
        {
            return;
        }

        ProductLogo.Source = new BitmapImage(new Uri(path));
    }
}
