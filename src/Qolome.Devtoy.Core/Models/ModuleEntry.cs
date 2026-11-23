namespace Qolome.Devtoy.Core.Models;

public sealed class ModuleEntry
{
    public required string WorkspaceRoot { get; init; }

    public required string ModulePath { get; init; }

    public required string Label { get; init; }

    public ProjectTypes ProjectTypes { get; init; } = new();

    public string ProjectVersion { get; init; } = "";

    public string VersionDisplay => string.IsNullOrWhiteSpace(ProjectVersion) ? "—" : ProjectVersion;

    public string TypeDisplay => ProjectTypes.ToString();

    /// <summary>用户「添加指定工程」注册的根目录。</summary>
    public bool IsSpecified { get; init; }

    /// <summary>工作区模块的用户显示名覆盖。</summary>
    public string? CatalogDisplayName { get; init; }

    /// <summary>已忽略：不出现在代码生成 / 字典生成目标列表。</summary>
    public bool IsIgnored { get; init; }

    /// <summary>Java 生成模块名（package 段 / ModelName）。</summary>
    public string? CodegenModelName { get; init; }

    public bool CodegenDao { get; init; } = true;

    public bool CodegenService { get; init; }

    public bool CodegenController { get; init; }

    /// <summary>是否已保存过代码生成习惯配置。</summary>
    public bool HasCodegenPrefs { get; init; }

    public string CodegenModelDisplay =>
        string.IsNullOrWhiteSpace(CodegenModelName) ? "—" : CodegenModelName!;

    /// <summary>工作区目录名；指定工程显示「指定工程」。</summary>
    public string WorkspaceDisplay => IsSpecified
        ? "指定工程"
        : string.IsNullOrWhiteSpace(WorkspaceRoot)
            ? "—"
            : Path.GetFileName(WorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>项目名称：模块最终所在文件夹名（可被 CatalogDisplayName 覆盖）。</summary>
    public string ProjectNameDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CatalogDisplayName))
            {
                return CatalogDisplayName!;
            }

            return FolderNameOf(ModulePath);
        }
    }

    public string IgnoreDisplay => IsIgnored ? "已忽略" : "—";

    public string IgnoreActionDisplay => IsIgnored ? "取消忽略" : "忽略";

    /// <summary>下拉框等场景：模块名称 - 工作区（不含长路径前缀）。</summary>
    public string TargetModuleDisplay => $"{ProjectNameDisplay} - {WorkspaceDisplay}";

    private static string FolderNameOf(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }
}