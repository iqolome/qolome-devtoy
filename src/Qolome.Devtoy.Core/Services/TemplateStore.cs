namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

/// <summary>管理本机可编辑模板：首次从随包默认目录复制，支持读写与还原。</summary>
public static class TemplateStore
{
    private static readonly Dictionary<string, (string Display, string Category)> Meta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["controller.java.vm"] = ("Controller", "表/实体"),
            ["service.java.vm"] = ("Service 接口", "表/实体"),
            ["serviceImpl.java.vm"] = ("Service 实现", "表/实体"),
            ["entity.java.vm"] = ("Entity", "表/实体"),
            ["entity-comment.java.vm"] = ("Entity 注释", "表/实体"),
            ["entity.kt.vm"] = ("Entity (Kotlin)", "表/实体"),
            ["BaseModel.java.vm"] = ("实体基类 BaseModel", "表/实体"),
            ["mapper.java.vm"] = ("Mapper 接口", "表/实体"),
            ["mapper.xml.vm"] = ("Mapper XML", "表/实体"),
            ["TemplateConvert.java.vm"] = ("Convert", "DTO"),
            ["TemplateCreateRequestDto.java.vm"] = ("CreateRequestDto", "DTO"),
            ["TemplateEditRequestDto.java.vm"] = ("EditRequestDto", "DTO"),
            ["TemplateDeleteRequestDto.java.vm"] = ("DeleteRequestDto", "DTO"),
            ["TemplateDetailRequestDto.java.vm"] = ("DetailRequestDto", "DTO"),
            ["TemplateDetailResponseDto.java.vm"] = ("DetailResponseDto", "DTO"),
            ["TemplateQueryPageRequestDto.java.vm"] = ("QueryPageRequestDto", "DTO"),
            ["TemplateQueryPageResponseDto.java.vm"] = ("QueryPageResponseDto", "DTO"),
            ["TemplateQuerySimpleAllRequestDto.java.vm"] = ("QuerySimpleAllRequestDto", "DTO"),
            ["TemplateQuerySimpleAllResponseDto.java.vm"] = ("QuerySimpleAllResponseDto", "DTO"),
            ["enum.java.vm"] = ("Java 枚举", "字典"),
            ["dict.ts.vm"] = ("TypeScript 字典", "字典"),
        };

    /// <summary>
    /// 确保用户模板目录存在；缺失的文件从随包默认模板复制（不覆盖已有修改）。
    /// </summary>
    public static int EnsureSeeded()
    {
        Directory.CreateDirectory(DevtoyPaths.TemplatesRoot);
        var bundled = DevtoyPaths.BundledTemplatesRoot;
        if (!Directory.Exists(bundled))
        {
            return 0;
        }

        var copied = 0;
        foreach (var src in Directory.EnumerateFiles(bundled, "*.vm", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(src);
            var dest = Path.Combine(DevtoyPaths.TemplatesRoot, name);
            if (File.Exists(dest))
            {
                continue;
            }

            File.Copy(src, dest, overwrite: false);
            copied++;
        }

        return copied;
    }

    public static IReadOnlyList<CodegenTemplateInfo> ListTemplates()
    {
        EnsureSeeded();
        var root = DevtoyPaths.TemplatesRoot;
        if (!Directory.Exists(root))
        {
            return Array.Empty<CodegenTemplateInfo>();
        }

        return Directory.EnumerateFiles(root, "*.vm", SearchOption.TopDirectoryOnly)
            .OrderBy(p => CategoryOf(Path.GetFileName(p)), StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Select(ToInfo)
            .ToList();
    }

    public static string ReadContent(string fileName)
    {
        var path = ResolveUserPath(fileName);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    public static void SaveContent(string fileName, string content)
    {
        var path = ResolveUserPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content ?? "");
    }

    /// <summary>用随包默认覆盖指定用户模板。</summary>
    public static bool ResetToBundled(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var bundled = Path.Combine(DevtoyPaths.BundledTemplatesRoot, name);
        if (!File.Exists(bundled))
        {
            return false;
        }

        Directory.CreateDirectory(DevtoyPaths.TemplatesRoot);
        File.Copy(bundled, Path.Combine(DevtoyPaths.TemplatesRoot, name), overwrite: true);
        return true;
    }

    /// <summary>用随包默认覆盖全部用户模板。</summary>
    public static int ResetAllToBundled()
    {
        var bundled = DevtoyPaths.BundledTemplatesRoot;
        if (!Directory.Exists(bundled))
        {
            return 0;
        }

        Directory.CreateDirectory(DevtoyPaths.TemplatesRoot);
        var count = 0;
        foreach (var src in Directory.EnumerateFiles(bundled, "*.vm", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(src);
            File.Copy(src, Path.Combine(DevtoyPaths.TemplatesRoot, name), overwrite: true);
            count++;
        }

        return count;
    }

    public static string ResolveUserPath(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !name.EndsWith(".vm", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("非法模板文件名", nameof(fileName));
        }

        return Path.Combine(DevtoyPaths.TemplatesRoot, name);
    }

    private static CodegenTemplateInfo ToInfo(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        var (display, category) = Meta.TryGetValue(name, out var m)
            ? m
            : (name, "其他");
        return new CodegenTemplateInfo
        {
            FileName = name,
            FullPath = fullPath,
            DisplayName = display,
            Category = category,
        };
    }

    private static string CategoryOf(string fileName) =>
        Meta.TryGetValue(fileName, out var m) ? m.Category : "其他";
}
