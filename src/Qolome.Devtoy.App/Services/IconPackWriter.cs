namespace Qolome_Devtoy_App.Services;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Qolome.Devtoy.Core.Models;

/// <summary>将选中主题写入运行目录 Assets/（供 MSIX 与标题栏使用）。</summary>
public static class IconPackWriter
{
    public static string AssetsRoot => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static void ApplyVariant(string? variantId)
    {
        var pngDir = IconPackPaths.GetPngFolder(variantId);
        if (!Directory.Exists(pngDir))
        {
            throw new DirectoryNotFoundException("找不到图标 PNG 目录: " + pngDir);
        }

        Directory.CreateDirectory(AssetsRoot);
        var master = Path.Combine(pngDir, "icon-1024.png");
        if (!File.Exists(master))
        {
            master = Path.Combine(pngDir, "icon-512.png");
        }

        SavePng(Pick(pngDir, "icon-48.png"), Path.Combine(AssetsRoot, "StoreLogo.png"), 50, 50);
        SavePng(Pick(pngDir, "icon-24.png"), Path.Combine(AssetsRoot, "Square44x44Logo.targetsize-24_altform-unplated.png"), 24, 24);
        SavePng(Pick(pngDir, "icon-48.png"), Path.Combine(AssetsRoot, "Square44x44Logo.targetsize-48_altform-lightunplated.png"), 48, 48);
        SavePng(Pick(pngDir, "icon-96.png"), Path.Combine(AssetsRoot, "LockScreenLogo.scale-200.png"), 96, 96);
        SavePng(Pick(pngDir, "icon-96.png"), Path.Combine(AssetsRoot, "Square44x44Logo.scale-200.png"), 88, 88);
        SavePng(master, Path.Combine(AssetsRoot, "Square150x150Logo.scale-200.png"), 300, 300);
        SavePng(master, Path.Combine(AssetsRoot, "SplashScreen.scale-200.png"), 620, 620);
        SaveWide(master, Path.Combine(AssetsRoot, "Wide310x150Logo.scale-200.png"), 620, 300);

        var icoSrc = IconPackPaths.GetAppIcoPath(variantId);
        File.Copy(icoSrc, Path.Combine(AssetsRoot, "AppIcon.ico"), overwrite: true);
    }

    private static string Pick(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (File.Exists(path))
        {
            return path;
        }

        throw new FileNotFoundException("缺少图标文件", path);
    }

    private static void SavePng(string sourcePath, string destPath, int width, int height)
    {
        using var bmp = new Bitmap(sourcePath);
        using var dest = new Bitmap(width, height);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(bmp, 0, 0, width, height);
        dest.Save(destPath, ImageFormat.Png);
    }

    private static void SaveWide(string sourcePath, string destPath, int width, int height)
    {
        using var bmp = new Bitmap(sourcePath);
        using var dest = new Bitmap(width, height);
        using var g = Graphics.FromImage(dest);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.Clear(Color.Transparent);
        var side = Math.Min(bmp.Width, bmp.Height);
        var scale = Math.Min(height / (double)side, width * 0.45 / side);
        var w = (int)(side * scale);
        var h = (int)(side * scale);
        var x = (width - w) / 2;
        var y = (height - h) / 2;
        g.DrawImage(bmp, x, y, w, h);
        dest.Save(destPath, ImageFormat.Png);
    }
}