using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SkiaSharp;
using Svg.Skia;

namespace Qolome_Devtoy_App.Helpers;

/// <summary>
/// 将 SVG 栅格为 PNG 后用 BitmapIcon 显示。
/// 按「实际墨水包围盒」统一缩放到同一视觉占比，避免各 SVG 留白不同导致忽大忽小。
/// </summary>
public static partial class SvgPathIconFactory
{
    /// <summary>墨水占图标盒的比例（与 Segoe 字形视觉重量接近）。</summary>
    private const float InkFillRatio = 0.92f;

    [GeneratedRegex(@"viewBox\s*=\s*""\s*([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s*""", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBoxRegex();

    public static IconElement FromRelativePath(string relativePath, double? size = null, string? foregroundHex = null)
    {
        _ = foregroundHex;
        // 与 Symbol/Font 侧栏图标同一目标边长
        var side = size ?? UiGlyphResources.IconBox;
        var pngPath = RasterizeToCachePng(relativePath, side);
        return new BitmapIcon
        {
            UriSource = new Uri(pngPath),
            ShowAsMonochrome = true,
            Width = side,
            Height = side,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static bool TryAttach(
        NavigationViewItem item,
        string relativePath,
        double? size = null,
        string? foregroundHex = null)
    {
        try
        {
            item.Icon = FromRelativePath(relativePath, size, foregroundHex);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SvgPathIconFactory: {relativePath} → {ex}");
            return false;
        }
    }

    public static void ApplySize(IconElement icon, double? size = null)
    {
        var side = size ?? UiGlyphResources.IconBox;
        icon.Width = side;
        icon.Height = side;
        icon.VerticalAlignment = VerticalAlignment.Center;
    }

    private static string RasterizeToCachePng(string relativePath, double side)
    {
        var fullPath = ResolvePath(relativePath);
        var svgText = File.ReadAllText(fullPath, Encoding.UTF8)
            .Replace("currentColor", "#000000", StringComparison.Ordinal)
            .Replace("currentcolor", "#000000", StringComparison.OrdinalIgnoreCase);

        var px = Math.Max(64, (int)Math.Ceiling(side * 4));
        var key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{svgText}|v4-ink|{px}|{InkFillRatio:0.00}")))[..20];
        var cacheDir = Path.Combine(Path.GetTempPath(), "qolome-devtoy-nav-png");
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(fullPath) + "-" + key + ".png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        using var svg = new SKSvg();
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(svgText)))
        {
            if (svg.Load(input) is null || svg.Picture is null)
            {
                throw new InvalidOperationException($"无法解析 SVG: {relativePath}");
            }
        }

        var picture = svg.Picture;
        var view = ParseViewBox(svgText) ?? NormalizeCull(picture.CullRect);

        // 先画到探测图，量实际不透明像素包围盒
        const int probe = 256;
        using var probeBmp = new SKBitmap(probe, probe, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var probeCanvas = new SKCanvas(probeBmp))
        {
            probeCanvas.Clear(SKColors.Transparent);
            DrawPictureInRect(probeCanvas, picture, view, new SKRect(0, 0, probe, probe));
            probeCanvas.Flush();
        }

        var ink = MeasureInkBounds(probeBmp) ?? new SKRect(0, 0, probe, probe);

        using var bitmap = new SKBitmap(px, px, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            // 将探测图中的墨水区域映射到最终图的统一占比
            var target = px * InkFillRatio;
            var inkScale = Math.Min(target / ink.Width, target / ink.Height);
            var outW = ink.Width * inkScale;
            var outH = ink.Height * inkScale;
            var outX = (px - outW) / 2f;
            var outY = (px - outH) / 2f;

            // 探测坐标 → viewBox 坐标，再画到最终矩形
            var sx = view.Width / probe;
            var sy = view.Height / probe;
            var inkInView = new SKRect(
                view.Left + ink.Left * sx,
                view.Top + ink.Top * sy,
                view.Left + ink.Right * sx,
                view.Top + ink.Bottom * sy);

            DrawPictureSourceDest(canvas, picture, inkInView, new SKRect(outX, outY, outX + outW, outY + outH));
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException($"PNG 编码失败: {relativePath}");
        using var output = File.Open(cachePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(output);

        return cachePath;
    }

    private static void DrawPictureInRect(SKCanvas canvas, SKPicture picture, SKRect view, SKRect dest)
    {
        var scale = Math.Min(dest.Width / view.Width, dest.Height / view.Height);
        var dx = dest.Left + (dest.Width - view.Width * scale) / 2f - view.Left * scale;
        var dy = dest.Top + (dest.Height - view.Height * scale) / 2f - view.Top * scale;
        canvas.Save();
        canvas.Translate(dx, dy);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static void DrawPictureSourceDest(SKCanvas canvas, SKPicture picture, SKRect sourceInView, SKRect dest)
    {
        if (sourceInView.Width <= 0 || sourceInView.Height <= 0)
        {
            return;
        }

        var scale = Math.Min(dest.Width / sourceInView.Width, dest.Height / sourceInView.Height);
        var dx = dest.Left - sourceInView.Left * scale;
        var dy = dest.Top - sourceInView.Top * scale;
        // 居中到 dest（Uniform）
        dx += (dest.Width - sourceInView.Width * scale) / 2f;
        dy += (dest.Height - sourceInView.Height * scale) / 2f;

        canvas.Save();
        canvas.ClipRect(dest);
        canvas.Translate(dx, dy);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Restore();
    }

    private static SKRect? MeasureInkBounds(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        var pixels = bitmap.Pixels;
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                if (pixels[row + x].Alpha < 16)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return null;
        }

        // 含右下像素
        return new SKRect(minX, minY, maxX + 1, maxY + 1);
    }

    private static SKRect NormalizeCull(SKRect cull) =>
        cull.Width > 0 && cull.Height > 0 ? cull : new SKRect(0, 0, 24, 24);

    private static SKRect? ParseViewBox(string svg)
    {
        var m = ViewBoxRegex().Match(svg);
        if (!m.Success)
        {
            return null;
        }

        if (!float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
            || !float.TryParse(m.Groups[4].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)
            || w <= 0
            || h <= 0)
        {
            return null;
        }

        return new SKRect(x, y, x + w, y + h);
    }

    private static string ResolvePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, normalized),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", normalized)),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"未找到 SVG: {relativePath}", relativePath);
    }
}
