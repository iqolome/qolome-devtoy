namespace Qolome.Devtoy.Core.Models;

/// <summary>应用图标主题（对应 icons/ 下各 variant）。</summary>
public sealed record IconVariantInfo(string Id, string DisplayName)
{
    public const string Lighting = "lighting";

    public const string Dark = "dark";

    public static IReadOnlyList<IconVariantInfo> All { get; } =
    [
        new(Lighting, "默认 · 浅色"),
        new(Dark, "深色"),
        new("amber", "琥珀"),
        new("teal", "青绿"),
        new("modules", "模块"),
    ];

    public static IconVariantInfo Resolve(string? id)
    {
        var normalized = string.IsNullOrWhiteSpace(id) ? Lighting : id.Trim();
        return All.FirstOrDefault(v => string.Equals(v.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?? All[0];
    }

    public static bool IsLighting(string? id) =>
        string.Equals(Resolve(id).Id, Lighting, StringComparison.OrdinalIgnoreCase);

    public static bool IsDark(string? id) =>
        string.Equals(Resolve(id).Id, Dark, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 切换到固定浅色/深色界面时，将互斥的默认图标对齐；彩色主题（amber 等）保留。
    /// </summary>
    public static string CoerceForFixedAppTheme(string? iconVariant, string appTheme)
    {
        var theme = AppThemeMode.Resolve(appTheme);
        var id = Resolve(iconVariant).Id;
        if (theme == AppThemeMode.Light && IsDark(id))
        {
            return Lighting;
        }

        if (theme == AppThemeMode.Dark && IsLighting(id))
        {
            return Dark;
        }

        return id;
    }
}