using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public static class DeviceTypeIconStore
{
    public const long MaxFileBytes = 2 * 1024 * 1024;

    public static readonly string[] Extensions = [".svg"];

    public static string BuiltinDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "Assets", "DeviceTypes");

    public static string CustomDirectory => Path.Combine(
        AppDatabase.DataDirectory,
        "icons");

    public static string? ResolvePath(DeviceType? type) =>
        type is null ? ResolveBuiltin("other") : ResolvePath(type.Code, type.IconFile, type.IsCustomIcon);

    public static string? ResolvePath(string? code, string? iconFile, bool isCustomIcon)
    {
        if (isCustomIcon)
        {
            var custom = SafeCustomPath(iconFile);
            if (custom is not null && File.Exists(custom) && IsSvg(custom))
            {
                return custom;
            }
        }
        else if (!string.IsNullOrWhiteSpace(iconFile))
        {
            var fromLibrary = ResolveBuiltinFile(iconFile) ?? ResolveBuiltin(Path.GetFileNameWithoutExtension(iconFile));
            if (fromLibrary is not null)
            {
                return fromLibrary;
            }
        }

        return ResolveBuiltin(code);
    }

    public static IReadOnlyList<BuiltinIcon> ListBuiltin()
    {
        if (!Directory.Exists(BuiltinDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(BuiltinDirectory)
            .Where(path => IsSupportedExtension(Path.GetExtension(path)))
            .Select(path => new BuiltinIcon(
                Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
                Path.GetFileName(path),
                path))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? ResolveBuiltinFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(BuiltinDirectory, name));
        var root = Path.GetFullPath(BuiltinDirectory) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    public static string? ResolveBuiltinExact(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(key.Trim()).ToLowerInvariant();
        foreach (var ext in Extensions)
        {
            var path = Path.Combine(BuiltinDirectory, name + ext);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return ResolveBuiltinFile(key);
    }

    public static string? ResolveBuiltin(string? code)
    {
        var key = string.IsNullOrWhiteSpace(code) ? "other" : code.Trim().ToLowerInvariant();
        foreach (var name in new[] { key, "other" }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(BuiltinDirectory, name + ext);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    public static string ImportCustom(string sourcePath)
    {
        ValidateSource(sourcePath);
        Directory.CreateDirectory(CustomDirectory);
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        var name = $"{Guid.NewGuid():N}{ext}";
        File.Copy(sourcePath, Path.Combine(CustomDirectory, name), overwrite: false);
        return name;
    }

    public static void InstallCustomFile(string fileName, byte[] bytes)
    {
        var safe = SanitizeFileName(fileName);
        var ext = Path.GetExtension(safe);
        if (!IsSupportedExtension(ext))
        {
            throw new ScanException("仅支持 SVG 图标。");
        }

        if (bytes.Length == 0 || bytes.Length > MaxFileBytes)
        {
            throw new ScanException("图标文件为空或超过 2 MB。");
        }

        Directory.CreateDirectory(CustomDirectory);
        File.WriteAllBytes(Path.Combine(CustomDirectory, safe), bytes);
    }

    public static void DeleteCustom(string? iconFile)
    {
        var path = SafeCustomPath(iconFile);
        if (path is not null && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static IReadOnlyDictionary<string, byte[]> ReadCustomFiles(IEnumerable<DeviceType> types)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types.Where(t => t.IsCustomIcon))
        {
            var path = SafeCustomPath(type.IconFile);
            if (path is null || !File.Exists(path))
            {
                continue;
            }

            var name = Path.GetFileName(path);
            if (!IsSvg(name))
            {
                continue;
            }

            map[name] = File.ReadAllBytes(path);
        }

        return map;
    }

    public static bool IsSvg(string? pathOrName) =>
        Path.GetExtension(pathOrName ?? "").Equals(".svg", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedExtension(string? ext) =>
        !string.IsNullOrWhiteSpace(ext)
        && Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ScanException("图标文件名无效。");
        }

        return name;
    }

    public static void ApplyChange(DeviceType entity, string? iconSourcePath, bool restoreDefault, string? builtinIconKey = null)
    {
        if (restoreDefault)
        {
            if (entity.IsCustomIcon)
            {
                DeleteCustom(entity.IconFile);
            }

            entity.IsCustomIcon = false;
            entity.IconFile = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(builtinIconKey))
        {
            var path = ResolveBuiltinExact(builtinIconKey);
            if (path is null)
            {
                throw new ScanException("所选图标不在软件自带图标库中。");
            }

            if (entity.IsCustomIcon)
            {
                DeleteCustom(entity.IconFile);
            }

            entity.IsCustomIcon = false;
            entity.IconFile = Path.GetFileName(path);
            return;
        }

        if (string.IsNullOrWhiteSpace(iconSourcePath))
        {
            return;
        }

        var name = ImportCustom(iconSourcePath);
        if (entity.IsCustomIcon)
        {
            DeleteCustom(entity.IconFile);
        }

        entity.IconFile = name;
        entity.IsCustomIcon = true;
    }

    private static string? SafeCustomPath(string? iconFile)
    {
        if (string.IsNullOrWhiteSpace(iconFile))
        {
            return null;
        }

        string name;
        try
        {
            name = SanitizeFileName(iconFile);
        }
        catch (ScanException)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(CustomDirectory, name));
        var root = Path.GetFullPath(CustomDirectory) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static void ValidateSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new ScanException("找不到所选图标文件。");
        }

        var ext = Path.GetExtension(sourcePath);
        if (!IsSupportedExtension(ext))
        {
            throw new ScanException("仅支持 SVG 图标。");
        }

        var size = new FileInfo(sourcePath).Length;
        if (size == 0 || size > MaxFileBytes)
        {
            throw new ScanException("图标文件为空或超过 2 MB。");
        }
    }
}

public sealed record BuiltinIcon(string Key, string FileName, string Path);
