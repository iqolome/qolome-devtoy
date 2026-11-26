namespace Qolome.Devtoy.Core;

/// <summary>产品关于信息。</summary>
public static class AboutInfo
{
    public const string ProductName = "Qolome DevToy";

    public const string CompanyNameZh = "Qolome";

    public const string CompanyNameEn = "Qolome";

#pragma warning disable S1075 // Product / license absolute URLs are intentional constants
    public const string WebsiteUrl = "https://www.qolome.com";

    public const string LicenseName = "Apache License 2.0";

    public const string LicenseUrl = "https://www.apache.org/licenses/LICENSE-2.0";
#pragma warning restore S1075

    public static string Version =>
        typeof(AboutInfo).Assembly.GetName().Version?.ToString(3) ?? "1.1.1";
}