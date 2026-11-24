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

    /// <summary>Java：启用代码生成（出现在 Java 代码生成目标列表）。</summary>
    public bool? EnableCodegen { get; set; }

    /// <summary>Java：启用生成枚举。</summary>
    public bool? EnableEnum { get; set; }

    /// <summary>Web：启用生成 TS 字典。</summary>
    public bool? EnableTsDict { get; set; }

    /// <summary>模块级枚举包路径；空则用全局 EnumPackagePath。</summary>
    public string? EnumPackagePath { get; set; }

    /// <summary>模块级 TS 字典相对路径；空则用全局 TsDictPath。</summary>
    public string? TsDictPath { get; set; }
}