using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Qolome.Nets.Core.Data;

namespace Qolome.Nets.App;

public static class DeviceTypeIconXaml
{
    private static readonly Dictionary<string, string> TintedPathCache = new(StringComparer.Ordinal);

    public static ImageSource? FromType(DeviceType? type) =>
        From(type?.Code, type?.IconFile, type?.IsCustomIcon ?? false, type?.ColorGroup);

    public static ImageSource? From(string? code, string? iconFile, bool isCustomIcon) =>
        From(code, iconFile, isCustomIcon, DeviceTypeColorGroups.ForCode(code));

    public static ImageSource? From(string? code, string? iconFile, bool isCustomIcon, string? colorGroup)
    {
        var path = DeviceTypeIconStore.ResolvePath(code, iconFile, isCustomIcon);
        var hex = DeviceTypeColorGroups.Hex(colorGroup, code);
        return FromPath(path, hex);
    }

    public static ImageSource? Globe() => Isp(IspPresets.Custom);

    public static ImageSource? Isp(string? key, string? label = null)
    {
        var file = IspPresets.IconFileName(key, label);
        return FromPath(Path.Combine(AppContext.BaseDirectory, "Assets", "Topology", file));
    }

    public static ImageSource? FromPath(string? path) => FromPath(path, tintHex: null);

    public static ImageSource? FromPath(string? path, string? tintHex)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = SvgColorTint.NormalizeHex(tintHex);
        if (hex is null)
        {
            return SvgFromUri(path);
        }

        var stamp = File.GetLastWriteTimeUtc(path).Ticks;
        var key = $"{path}|{stamp}|{hex}";
        if (!TintedPathCache.TryGetValue(key, out var cachePath))
        {
            try
            {
                var tinted = SvgColorTint.Apply(File.ReadAllText(path), hex);
                var cacheDir = Path.Combine(AppDatabase.DataDirectory, "icon-tint-cache");
                Directory.CreateDirectory(cacheDir);
                var fileName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(key)))[..32] + ".svg";
                cachePath = Path.Combine(cacheDir, fileName);
                if (!File.Exists(cachePath))
                {
                    File.WriteAllText(cachePath, tinted);
                }

                TintedPathCache[key] = cachePath;
            }
            catch
            {
                return SvgFromUri(path);
            }
        }

        return SvgFromUri(cachePath);
    }

    private static ImageSource SvgFromUri(string path) =>
        new SvgImageSource(new Uri(Path.GetFullPath(path)))
        {
            RasterizePixelWidth = 64,
            RasterizePixelHeight = 64
        };
}
