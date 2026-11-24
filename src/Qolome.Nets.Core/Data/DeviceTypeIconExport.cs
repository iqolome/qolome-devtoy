using System.IO.Compression;
using Qolome.Nets.Core.Models;
using SkiaSharp;
using Svg.Skia;

namespace Qolome.Nets.Core.Data;

public enum DeviceTypeIconExportFormat
{
    Svg,
    Png
}

public static class DeviceTypeIconExport
{
    public const int PngSize = 256;

    public static int WriteZip(string path, IEnumerable<DeviceType> types, DeviceTypeIconExportFormat format)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var written = 0;
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var source = DeviceTypeIconStore.ResolvePath(type);
            if (source is null || !File.Exists(source))
            {
                continue;
            }

            var svg = File.ReadAllText(source);
            var entryName = UniqueName(used, type, format == DeviceTypeIconExportFormat.Png ? ".png" : ".svg");
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var output = entry.Open();
            if (format == DeviceTypeIconExportFormat.Png)
            {
                var png = RasterizePng(SvgColorTint.ApplyOutline(svg));
                output.Write(png);
            }
            else
            {
                using var writer = new StreamWriter(output);
                writer.Write(svg);
            }

            written++;
        }

        return written;
    }

    public static byte[] RasterizePng(string svg, int size = PngSize)
    {
        using var document = new SKSvg();
        document.FromSvg(svg);
        var picture = document.Picture;
        if (picture is null)
        {
            throw new ScanException("无法将 SVG 转为 PNG。");
        }

        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            bounds = new SKRect(0, 0, 32, 32);
        }

        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var pad = size * 0.12f;
        var inner = size - pad * 2;
        var scale = Math.Min(inner / bounds.Width, inner / bounds.Height);
        canvas.Translate(pad + (inner - bounds.Width * scale) / 2f, pad + (inner - bounds.Height * scale) / 2f);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new ScanException("PNG 编码失败。");
        return data.ToArray();
    }

    private static string UniqueName(HashSet<string> used, DeviceType type, string ext)
    {
        var stem = FileStem(type.Code, type.Name);
        var name = stem + ext;
        var i = 2;
        while (!used.Add(name))
        {
            name = $"{stem}-{i}{ext}";
            i++;
        }

        return name;
    }

    private static string FileStem(string? code, string? name)
    {
        var left = Sanitize(code);
        var right = Sanitize(name);
        if (left.Length == 0)
        {
            return right.Length == 0 ? "icon" : right;
        }

        if (right.Length == 0 || string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return left;
        }

        return $"{left}_{right}";
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim()
            .Select(c => c is '/' or '\\' or ':' || invalid.Contains(c) ? '-' : c)
            .ToArray();
        return new string(chars).Trim(' ', '.', '-');
    }
}
