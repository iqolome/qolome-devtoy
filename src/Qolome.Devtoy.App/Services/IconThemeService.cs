namespace Qolome_Devtoy_App.Services;

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

public sealed class IconThemeService
{
    private readonly DevtoySession _session;
    private MainWindow? _window;
    private string? _appliedVariantId;

    public IconThemeService(DevtoySession session)
    {
        _session = session;
    }

    public void Attach(MainWindow window) => _window = window;

    /// <summary>按界面主题解析实际应使用的图标包。</summary>
    public string ResolveEffectiveVariant(string? appTheme = null, string? iconVariant = null)
    {
        var theme = AppThemeMode.Resolve(appTheme ?? _session.Settings.AppTheme);
        if (theme == AppThemeMode.System)
        {
            return AppThemeService.IsOsDark() ? IconVariantInfo.Dark : IconVariantInfo.Lighting;
        }

        return IconVariantInfo.Resolve(iconVariant ?? _session.Settings.IconVariant).Id;
    }

    public void ApplyFromSettings() =>
        SyncWithAppTheme(coerceUserVariant: false, log: true);

    /// <summary>
    /// 同步图标与界面主题。
    /// 跟随系统：强制浅色/深色图标，不改用户配置。
    /// 固定浅色/深色：可按需把 lighting↔dark 对齐，并应用用户选择的图标主题。
    /// </summary>
    public void SyncWithAppTheme(bool coerceUserVariant, bool log = true)
    {
        var theme = AppThemeMode.Resolve(_session.Settings.AppTheme);
        if (theme != AppThemeMode.System && coerceUserVariant)
        {
            var coerced = IconVariantInfo.CoerceForFixedAppTheme(
                _session.Settings.IconVariant,
                theme);
            if (!string.Equals(coerced, _session.Settings.IconVariant, StringComparison.OrdinalIgnoreCase))
            {
                _session.Settings.IconVariant = coerced;
                _session.SaveSettings();
            }
        }

        Apply(ResolveEffectiveVariant(), log);
    }

    public void Apply(string? variantId, bool log = true)
    {
        var info = IconVariantInfo.Resolve(variantId);
        if (string.Equals(_appliedVariantId, info.Id, StringComparison.OrdinalIgnoreCase)
            && _window is not null)
        {
            return;
        }

        try
        {
            IconPackWriter.ApplyVariant(info.Id);
            var icoPath = Path.Combine(IconPackWriter.AssetsRoot, "AppIcon.ico");
            _window?.AppWindow.SetIcon(icoPath);
            _window?.SetTitleBarIcon(icoPath);
            _appliedVariantId = info.Id;
            if (log)
            {
                _session.Log($"已应用图标主题: {info.DisplayName}");
            }
        }
        catch (Exception ex)
        {
            _session.LogError("应用图标主题失败: " + ex.Message);
        }
    }

    public void SetVariant(string variantId)
    {
        var theme = AppThemeMode.Resolve(_session.Settings.AppTheme);
        if (theme == AppThemeMode.System)
        {
            _session.LogWarn("界面主题为「跟随系统」时，图标随系统自动切换，不可手动修改");
            return;
        }

        _session.Settings.IconVariant = IconVariantInfo.Resolve(variantId).Id;
        _session.SaveSettings();
        Apply(_session.Settings.IconVariant);
    }

    public static Image CreatePreviewImage(string? variantId, double size = 64)
    {
        var path = IconPackPaths.GetPreviewPath(variantId);
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
        };
        if (path is not null)
        {
            image.Source = new BitmapImage(new Uri(path));
        }

        return image;
    }
}
