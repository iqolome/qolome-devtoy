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

    /// <summary>可供代码生成 / 字典生成选择的模块（排除已忽略）。</summary>
    public static IEnumerable<ModuleEntry> ForCodegen(IEnumerable<ModuleEntry> modules) =>
        modules.Where(m => !m.IsIgnored);

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
            ? ov.DisplayName!.Trim()
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
                : ov!.CodegenModelName!.Trim(),
            CodegenDao = ov?.CodegenDao ?? true,
            CodegenService = ov?.CodegenService ?? false,
            CodegenController = ov?.CodegenController ?? false,
            HasCodegenPrefs = hasCodegen,
        };
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
        if (!settings.ModuleOverrides.TryGetValue(modulePath, out var ov))
        {
            ov = new ModuleCatalogOverride();
            settings.ModuleOverrides[modulePath] = ov;
        }

        ov.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        ov.Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        ov.Java = types.Java;
        ov.Web = types.Web;
        ov.Flutter = types.Flutter;
        ov.JavaScript = null;

        if (IsOverrideEmpty(ov))
        {
            settings.ModuleOverrides.Remove(modulePath);
        }

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
        if (!settings.ModuleOverrides.TryGetValue(modulePath, out var ov))
        {
            ov = new ModuleCatalogOverride();
            settings.ModuleOverrides[modulePath] = ov;
        }

        ov.CodegenModelName = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
        ov.CodegenDao = dao;
        ov.CodegenService = service;
        ov.CodegenController = controller;

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
        && ov.CodegenController is null;

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
