namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public static class ModuleCatalogFilter
{
    public static IReadOnlyList<ModuleEntry> Apply(IReadOnlyList<ModuleEntry> modules, DevtoySettings settings)
    {
        var ignored = new HashSet<string>(
            settings.IgnoredModulePaths.Select(SafeFullPath).Where(p => p.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var list = new List<ModuleEntry>(modules.Count);
        foreach (var entry in modules)
        {
            var path = SafeFullPath(entry.ModulePath);
            var isIgnored = path.Length > 0 && ignored.Contains(path);
            list.Add(entry.IsSpecified
                ? ApplySpecified(entry, settings, isIgnored)
                : ApplyOverride(entry, settings, isIgnored));
        }

        return list;
    }

    /// <summary>未忽略的模块（兼容旧调用）。</summary>
    public static IEnumerable<ModuleEntry> ForCodegen(IEnumerable<ModuleEntry> modules) =>
        modules.Where(m => !m.IsIgnored);

    /// <summary>Java 代码生成目标：未忽略且启用代码生成。</summary>
    public static IEnumerable<ModuleEntry> ForJavaCodegen(IEnumerable<ModuleEntry> modules) =>
        modules.Where(m => !m.IsIgnored && m.ProjectTypes.Java && m.EnableCodegen);

    /// <summary>字典/枚举生成目标：未忽略且启用枚举或 TS 字典。</summary>
    public static IEnumerable<ModuleEntry> ForDictTargets(IEnumerable<ModuleEntry> modules) =>
        modules.Where(m =>
            !m.IsIgnored
            && ((m.ProjectTypes.Java && m.EnableEnum) || (m.ProjectTypes.Web && m.EnableTsDict)));

    private static ModuleEntry ApplyOverride(ModuleEntry entry, DevtoySettings settings, bool isIgnored)
    {
        var path = Path.GetFullPath(entry.ModulePath);
        settings.ModuleOverrides.TryGetValue(path, out var ov);
        settings.ModuleDisplayNames.TryGetValue(path, out var legacyName);

        var types = CloneTypes(entry.ProjectTypes);
        ApplyTypeOverride(types, ov);

        var version = entry.ProjectVersion;
        if (ov is not null && !string.IsNullOrWhiteSpace(ov.Version))
        {
            version = ov.Version.Trim();
        }

        var displayName = !string.IsNullOrWhiteSpace(ov?.DisplayName)
            ? ov.DisplayName.Trim()
            : legacyName;

        return BuildEntry(
            entry,
            types,
            version,
            isSpecified: false,
            displayName,
            isIgnored,
            ov);
    }

    private static ModuleEntry ApplySpecified(ModuleEntry entry, DevtoySettings settings, bool isIgnored)
    {
        settings.ModuleOverrides.TryGetValue(Path.GetFullPath(entry.ModulePath), out var ov);
        return BuildEntry(
            entry,
            entry.ProjectTypes,
            entry.ProjectVersion,
            isSpecified: true,
            entry.CatalogDisplayName,
            isIgnored,
            ov);
    }

    private static ModuleEntry BuildEntry(
        ModuleEntry entry,
        ProjectTypes types,
        string version,
        bool isSpecified,
        string? displayName,
        bool isIgnored,
        ModuleCatalogOverride? ov)
    {
        var hasCodegen = ov is not null && (
            !string.IsNullOrWhiteSpace(ov.CodegenModelName)
            || ov.CodegenDao is not null
            || ov.CodegenService is not null
            || ov.CodegenController is not null);

        return new ModuleEntry
        {
            WorkspaceRoot = entry.WorkspaceRoot,
            ModulePath = entry.ModulePath,
            Label = entry.Label,
            ProjectTypes = types,
            ProjectVersion = version,
            IsSpecified = isSpecified,
            CatalogDisplayName = displayName,
            IsIgnored = isIgnored,
            CodegenModelName = string.IsNullOrWhiteSpace(ov?.CodegenModelName)
                ? null
                : ov.CodegenModelName.Trim(),
            CodegenDao = ov?.CodegenDao ?? true,
            CodegenService = ov?.CodegenService ?? false,
            CodegenController = ov?.CodegenController ?? false,
            HasCodegenPrefs = hasCodegen,
            EnableCodegen = ov?.EnableCodegen == true,
            EnableEnum = ov?.EnableEnum == true,
            EnableTsDict = ov?.EnableTsDict == true,
            TagBootStarter = ResolveTagBootStarter(entry.ModulePath, types, ov),
            EnumPackagePath = string.IsNullOrWhiteSpace(ov?.EnumPackagePath)
                ? null
                : ov.EnumPackagePath.Trim(),
            TsDictPath = string.IsNullOrWhiteSpace(ov?.TsDictPath)
                ? null
                : ov.TsDictPath.Trim(),
        };
    }

    /// <summary>手动覆盖优先；未设置时 Java 模块按 AutoConfiguration.imports 自动识别。</summary>
    private static bool ResolveTagBootStarter(string modulePath, ProjectTypes types, ModuleCatalogOverride? ov)
    {
        if (ov?.TagBootStarter is not null)
        {
            return ov.TagBootStarter.Value;
        }

        return types.Java && BootStarterDetector.HasImportsFile(modulePath);
    }

    private static ProjectTypes CloneTypes(ProjectTypes source) => new()
    {
        Java = source.Java,
        Web = source.Web,
        Flutter = source.Flutter,
    };

    private static void ApplyTypeOverride(ProjectTypes types, ModuleCatalogOverride? ov)
    {
        if (ov is null)
        {
            return;
        }

        if (ov.Java is not null)
        {
            types.Java = ov.Java.Value;
        }

        if (ov.Web is not null)
        {
            types.Web = ov.Web.Value;
        }

        if (ov.Flutter is not null)
        {
            types.Flutter = ov.Flutter.Value;
        }

        if (ov.JavaScript is true)
        {
            types.Web = true;
        }
    }

    public static void SaveWorkspaceOverride(
        DevtoySettings settings,
        string modulePath,
        string? displayName,
        string? version,
        ProjectTypes types)
    {
        modulePath = Path.GetFullPath(modulePath);
        var ov = GetOrCreateOverride(settings, modulePath);

        ov.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ov.Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        ov.Java = types.Java;
        ov.Web = types.Web;
        ov.Flutter = types.Flutter;
        ov.JavaScript = null;

        CleanupOverride(settings, modulePath, ov);

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            settings.ModuleDisplayNames[modulePath] = displayName.Trim();
        }
        else
        {
            settings.ModuleDisplayNames.Remove(modulePath);
        }
    }

    public static void SaveCodegenPrefs(
        DevtoySettings settings,
        string modulePath,
        string? modelName,
        bool dao,
        bool service,
        bool controller)
    {
        modulePath = Path.GetFullPath(modulePath);
        var ov = GetOrCreateOverride(settings, modulePath);

        ov.CodegenModelName = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
        ov.CodegenDao = dao;
        ov.CodegenService = service;
        ov.CodegenController = controller;

        CleanupOverride(settings, modulePath, ov);
    }

    /// <summary>保存标签 / 功能启用标志；不覆盖已有其它 override 字段。</summary>
    public static void SaveFeatureFlags(
        DevtoySettings settings,
        string modulePath,
        bool? enableCodegen = null,
        bool? enableEnum = null,
        bool? enableTsDict = null,
        bool? tagBootStarter = null)
    {
        modulePath = Path.GetFullPath(modulePath);
        var ov = GetOrCreateOverride(settings, modulePath);

        if (enableCodegen is not null)
        {
            ov.EnableCodegen = enableCodegen.Value ? true : null;
        }

        if (enableEnum is not null)
        {
            ov.EnableEnum = enableEnum.Value ? true : null;
        }

        if (enableTsDict is not null)
        {
            ov.EnableTsDict = enableTsDict.Value ? true : null;
        }

        if (tagBootStarter is not null)
        {
            // 显式 true/false，打断自动识别
            ov.TagBootStarter = tagBootStarter.Value;
        }

        CleanupOverride(settings, modulePath, ov);
    }

    public static void SaveEnumPath(DevtoySettings settings, string modulePath, string? enumPackagePath)
    {
        modulePath = Path.GetFullPath(modulePath);
        var ov = GetOrCreateOverride(settings, modulePath);
        ov.EnumPackagePath = string.IsNullOrWhiteSpace(enumPackagePath) ? null : enumPackagePath.Trim();
        CleanupOverride(settings, modulePath, ov);
    }

    public static void SaveTsDictPath(DevtoySettings settings, string modulePath, string? tsDictPath)
    {
        modulePath = Path.GetFullPath(modulePath);
        var ov = GetOrCreateOverride(settings, modulePath);
        ov.TsDictPath = string.IsNullOrWhiteSpace(tsDictPath) ? null : tsDictPath.Trim();
        CleanupOverride(settings, modulePath, ov);
    }

    /// <summary>按模块覆盖合并枚举/字典路径到 CodegenSettings 副本。</summary>
    public static Codegen.CodegenSettings MergeCodegenSettings(DevtoySettings settings, ModuleEntry module)
    {
        var codegen = settings.ToCodegenSettings();
        if (!string.IsNullOrWhiteSpace(module.EnumPackagePath))
        {
            codegen.EnumPackage = module.EnumPackagePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(module.TsDictPath))
        {
            codegen.TsDictPath = module.TsDictPath.Trim();
        }

        return codegen;
    }

    private static ModuleCatalogOverride GetOrCreateOverride(DevtoySettings settings, string modulePath)
    {
        if (!settings.ModuleOverrides.TryGetValue(modulePath, out var ov))
        {
            ov = new ModuleCatalogOverride();
            settings.ModuleOverrides[modulePath] = ov;
        }

        return ov;
    }

    private static void CleanupOverride(DevtoySettings settings, string modulePath, ModuleCatalogOverride ov)
    {
        if (IsOverrideEmpty(ov))
        {
            settings.ModuleOverrides.Remove(modulePath);
        }
    }

    private static bool IsOverrideEmpty(ModuleCatalogOverride ov) =>
        string.IsNullOrWhiteSpace(ov.DisplayName)
        && string.IsNullOrWhiteSpace(ov.Version)
        && ov.Java is null
        && ov.Web is null
        && ov.Flutter is null
        && ov.JavaScript is null
        && string.IsNullOrWhiteSpace(ov.CodegenModelName)
        && ov.CodegenDao is null
        && ov.CodegenService is null
        && ov.CodegenController is null
        && ov.EnableCodegen is not true
        && ov.EnableEnum is not true
        && ov.EnableTsDict is not true
        && ov.TagBootStarter is null
        && string.IsNullOrWhiteSpace(ov.EnumPackagePath)
        && string.IsNullOrWhiteSpace(ov.TsDictPath);

    private static string SafeFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
        }
        catch
        {
            return "";
        }
    }
}
