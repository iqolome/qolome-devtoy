namespace Qolome_Devtoy_App.Services;

using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

/// <summary>
/// 运行时图标：直接使用 icons/ 包内文件，避免覆盖进程已锁定的 Assets。
/// 可选尽力同步到 AppData 缓存（不写 BaseDirectory，避免 MSIX/GDI+ 锁文件失败）。
/// </summary>
public static class IconPackWriter
{
    public static string AssetsRoot => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string CacheRoot => Path.Combine(DevtoyPaths.AppDataRoot, "icon-cache");

    /// <summary>返回可供 SetIcon / 标题栏使用的 .ico 路径（优先包内源文件）。</summary>
    public static string ApplyVariant(string? variantId)
    {
        var info = IconVariantInfo.Resolve(variantId);
        var icoSrc = IconPackPaths.GetAppIcoPath(info.Id);
        if (!File.Exists(icoSrc))
        {
            throw new FileNotFoundException("找不到应用图标: " + icoSrc, icoSrc);
        }

        // 复制到用户缓存，避免长期占用源文件句柄；双缓冲文件名，绕过上次 SetIcon 的锁
        var dir = Path.Combine(CacheRoot, info.Id);
        Directory.CreateDirectory(dir);
        var slotA = Path.Combine(dir, "AppIcon-a.ico");
        var slotB = Path.Combine(dir, "AppIcon-b.ico");
        var preferA = !File.Exists(slotA)
            || (File.Exists(slotB) && File.GetLastWriteTimeUtc(slotA) <= File.GetLastWriteTimeUtc(slotB));
        var dest = preferA ? slotA : slotB;
        try
        {
            File.Copy(icoSrc, dest, overwrite: true);
            return dest;
        }
        catch (IOException)
        {
            dest = preferA ? slotB : slotA;
            File.Copy(icoSrc, dest, overwrite: true);
            return dest;
        }
    }
}
