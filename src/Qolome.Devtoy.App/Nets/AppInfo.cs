using System.Reflection;
using Qolome.Devtoy.Core;

namespace Qolome.Nets.App;

/// <summary>网络模块侧产品信息（与 Devtoy 关于页统一为 Qolome DevToy）。</summary>
public static class AppInfo
{
    public const string Product = AboutInfo.ProductName;

    public const string Description =
        "Qolome DevToy：代码生成、字典管理与局域网发现、端口扫描、网络拓扑。";

    public static string Version { get; } = AboutInfo.Version;

    public static string Copyright { get; } =
        ReadAttribute<AssemblyCopyrightAttribute>(a => a.Copyright)
        ?? $"Copyright © {DateTime.Now.Year} {AboutInfo.CompanyNameEn}";

    public static string Company { get; } =
        ReadAttribute<AssemblyCompanyAttribute>(a => a.Company)
        ?? AboutInfo.CompanyNameZh;

    public static string ServiceAgreement { get; } = LoadServiceAgreement();

    private static string? ReadAttribute<T>(Func<T, string?> selector)
        where T : Attribute
    {
        var value = typeof(AppInfo).Assembly.GetCustomAttribute<T>();
        return value is null ? null : selector(value);
    }

    private static string LoadServiceAgreement()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Legal", "ServiceAgreement.txt");
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        return "未找到服务协议文件。请确认程序目录包含 Assets\\Legal\\ServiceAgreement.txt。";
    }
}
