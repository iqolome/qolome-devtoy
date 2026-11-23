namespace Qolome.Devtoy.Core.Models;

/// <summary>工作区扫描模块的用户覆盖（键为 ModulePath）。</summary>
public sealed class ModuleCatalogOverride
{
    public string? DisplayName { get; set; }

    public string? Version { get; set; }

    public bool? Java { get; set; }

    public bool? Web { get; set; }

    /// <summary>已废弃，读取时合并入 Web。</summary>
    public bool? JavaScript { get; set; }

    public bool? Flutter { get; set; }

    /// <summary>Java 代码生成模块名（对应 DaoTarget.ModelName / package.ModuleName）。</summary>
    public string? CodegenModelName { get; set; }

    /// <summary>习惯：默认勾选 Dao。</summary>
    public bool? CodegenDao { get; set; }

    public bool? CodegenService { get; set; }

    public bool? CodegenController { get; set; }
}