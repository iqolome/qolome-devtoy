namespace Qolome.Devtoy.Core;

/// <summary>产品关于信息。</summary>
public static class AboutInfo
{
    public const string ProductName = "Qolome Devtoy";

    public const string CompanyNameZh = "忻州市尖锋达科技有限公司";

    public const string CompanyNameEn = "Xinzhou jfonda Technology Co., Ltd.";

#pragma warning disable S1075 // Product / license absolute URLs are intentional constants
    public const string WebsiteUrl = "https://www.jfonda.com/";

    public const string LicenseName = "Apache License 2.0";

    public const string LicenseUrl = "https://www.apache.org/licenses/LICENSE-2.0";
#pragma warning restore S1075

    public static string Version =>
        typeof(AboutInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}