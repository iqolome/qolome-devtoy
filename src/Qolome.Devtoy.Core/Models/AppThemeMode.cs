namespace Qolome.Devtoy.Core.Models;

/// <summary>应用界面明暗主题。</summary>
public static class AppThemeMode
{
    public const string System = "System";

    public const string Light = "Light";

    public const string Dark = "Dark";

    public static IReadOnlyList<(string Id, string DisplayName)> All { get; } =
    [
        (System, "跟随系统"),
        (Light, "浅色"),
        (Dark, "深色"),
    ];

    public static string Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, Light, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "浅色", StringComparison.Ordinal))
        {
            return Light;
        }

        if (string.Equals(trimmed, Dark, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "深色", StringComparison.Ordinal))
        {
            return Dark;
        }

        return System;
    }
}
