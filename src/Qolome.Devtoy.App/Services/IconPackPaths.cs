namespace Qolome_Devtoy_App.Services;

using Qolome.Devtoy.Core.Models;

/// <summary>定位打包的 icons/ 目录与各主题文件。</summary>
public static class IconPackPaths
{
    public static string PackRoot
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("QOLOME_DEVTOY_ICONS");
            if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            {
                return Path.GetFullPath(fromEnv);
            }

            var baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "icons"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "icons")),
            };

            return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
        }
    }

    public static string GetVariantRoot(string? variantId)
    {
        var id = IconVariantInfo.Resolve(variantId).Id;
        if (string.Equals(id, "lighting", StringComparison.OrdinalIgnoreCase))
        {
            return PackRoot;
        }

        return Path.Combine(PackRoot, "variants", id);
    }

    public static string GetPngFolder(string? variantId) =>
        Path.Combine(GetVariantRoot(variantId), "png");

    public static string GetAppIcoPath(string? variantId)
    {
        var root = GetVariantRoot(variantId);
        var ico = Path.Combine(root, "app.ico");
        if (File.Exists(ico))
        {
            return ico;
        }

        var fallback = Path.Combine(PackRoot, "app.ico");
        return File.Exists(fallback) ? fallback : ico;
    }

    public static string? GetPreviewPath(string? variantId)
    {
        var id = IconVariantInfo.Resolve(variantId).Id;

        // lighting 资源在 icons/ 根目录，预览图在 variants/lighting/preview.png
        var candidates = new[]
        {
            Path.Combine(PackRoot, "variants", id, "preview.png"),
            Path.Combine(GetVariantRoot(variantId), "preview.png"),
            Path.Combine(GetPngFolder(variantId), "icon-128.png"),
            Path.Combine(GetPngFolder(variantId), "icon-64.png"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}