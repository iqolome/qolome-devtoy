using Qolome.Devtoy.Core.Codegen;

namespace Qolome.Devtoy.Core.Models;

public sealed class DevtoySettings
{
    public string AuthorName { get; set; } = "Qolome";

    public string AuthorEmail { get; set; } = "qolome@outlook.com";

    /// <summary>Java 根包，如 com.qolome.starter。</summary>
    public string BasePackage { get; set; } = "com.qolome.starter";

    /// <summary>业务枚举包路径（对齐 CodegenSettings.EnumPackage）。</summary>
    public string EnumPackagePath { get; set; } = "com.qolome.common.dict.biz.enums";

    /// <summary>枚举类名后缀。</summary>
    public string EnumNameSuffix { get; set; } = "Enum";

    /// <summary>枚举接口全限定名。</summary>
    public string EnumInterface { get; set; } = "com.qolome.common.dict.biz.inft.IBizEnum";

    /// <summary>实体基类全限定名；空则不继承。</summary>
    public string EntitySuperClass { get; set; } = "com.qolome.starter.dao.model.BaseModel";

    /// <summary>Entity 相对包名。</summary>
    public string EntityPackage { get; set; } = "dao.model";

    /// <summary>Mapper 接口相对包名。</summary>
    public string MapperPackage { get; set; } = "dao.mapper";

    /// <summary>Mapper XML 相对资源包名。</summary>
    public string XmlPackage { get; set; } = "mapper";

    /// <summary>TS 字典输出相对路径。</summary>
    public string TsDictPath { get; set; } = Path.Combine("src", "dict");

    /// <summary>逻辑删除字段名。</summary>
    public string LogicDeleteColumnName { get; set; } = "deleted";

    /// <summary>多租户字段名。</summary>
    public string TenantIdColumnName { get; set; } = "tenant_id";

    /// <summary>创建日期字段名。</summary>
    public string CreateTimeColumnName { get; set; } = "create_time";

    /// <summary>创建人字段名。</summary>
    public string CreaterIdColumnName { get; set; } = "creater_id";

    /// <summary>修改日期字段名。</summary>
    public string ModifyTimeColumnName { get; set; } = "modify_time";

    /// <summary>修改人字段名。</summary>
    public string ModifierIdColumnName { get; set; } = "modifier_id";

    /// <summary>主键生成类型，见 <see cref="MybatisIdType"/>。</summary>
    public string IdType { get; set; } = MybatisIdType.AssignId;

    /// <summary>应用图标主题 ID，见 <see cref="IconVariantInfo"/>。</summary>
    public string IconVariant { get; set; } = "lighting";

    /// <summary>界面明暗：System / Light / Dark。</summary>
    public string AppTheme { get; set; } = AppThemeMode.System;

    /// <summary>主界面底部是否显示日志控制台。</summary>
    public bool ShowConsole { get; set; } = true;

    /// <summary>已添加的工作区根目录（可叠加多个；扫描时合并模块）。</summary>
    public List<string> WorkspaceRoots { get; set; } = new();

    /// <summary>兼容旧配置的单一工作区；加载后会合并进 <see cref="WorkspaceRoots"/>。</summary>
    public string WorkspaceRoot { get; set; } = "";

    public DataSourceSettings DataSource { get; set; } = new();

    /// <summary>将旧版单一工作区并入列表，并规范化路径去重。</summary>
    public void NormalizeWorkspaceRoots()
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var full = Path.GetFullPath(path.Trim());
                if (seen.Add(full))
                {
                    normalized.Add(full);
                }
            }
            catch
            {
                // ignore bad paths
            }
        }

        foreach (var root in WorkspaceRoots)
        {
            TryAdd(root);
        }

        TryAdd(WorkspaceRoot);
        WorkspaceRoots = normalized;
        WorkspaceRoot = normalized.Count > 0 ? normalized[0] : "";
    }

    public List<SpecifiedProjectEntry> SpecifiedProjects { get; set; } = new();

    public Dictionary<string, string> ModuleDisplayNames { get; set; } = new();

    /// <summary>已忽略的模块路径：仍显示在模块管理，但不出现在代码生成 / 字典生成目标中。</summary>
    public List<string> IgnoredModulePaths { get; set; } = new();

    public Dictionary<string, ModuleCatalogOverride> ModuleOverrides { get; set; } = new();

    /// <summary>Java 代码生成页上次使用的勾选与模块习惯。</summary>
    public CodegenHabitSettings CodegenHabit { get; set; } = new();

    /// <summary>作者注释：有邮箱时为 <c>&lt;a href="mailto:邮箱"&gt;作者&lt;/a&gt;</c>。</summary>
    public string FormatAuthorHtml()
    {
        var name = string.IsNullOrWhiteSpace(AuthorName) ? "Qolome" : AuthorName.Trim();
        var email = AuthorEmail.Trim();
        return string.IsNullOrWhiteSpace(email)
            ? name
            : $"<a href=\"mailto:{email}\">{name}</a>";
    }

    /// <summary>由持久化配置构造运行时 CodegenSettings。</summary>
    public CodegenSettings ToCodegenSettings()
    {
        return new CodegenSettings
        {
            LogicDeleteColumnName = NormalizeColumn(LogicDeleteColumnName, "deleted"),
            TenantIdColumnName = NormalizeColumn(TenantIdColumnName, "tenant_id"),
            CreateTimeColumnName = NormalizeColumn(CreateTimeColumnName, "create_time"),
            CreaterIdColumnName = NormalizeColumn(CreaterIdColumnName, "creater_id"),
            ModifyTimeColumnName = NormalizeColumn(ModifyTimeColumnName, "modify_time"),
            ModifierIdColumnName = NormalizeColumn(ModifierIdColumnName, "modifier_id"),
            IdType = MybatisIdType.Resolve(IdType),
            EnumPackage = ResolveEnumPackagePath(),
            EnumNameSuffix = string.IsNullOrWhiteSpace(EnumNameSuffix) ? "Enum" : EnumNameSuffix.Trim(),
            EnumInterface = string.IsNullOrWhiteSpace(EnumInterface)
                ? "com.qolome.common.dict.biz.inft.IBizEnum"
                : EnumInterface.Trim(),
            TsDictPath = ResolveTsDictPath(),
        };
    }

    public string ResolveBasePackage() =>
        string.IsNullOrWhiteSpace(BasePackage) ? "com.qolome.starter" : BasePackage.Trim();

    public string ResolveEnumPackagePath() =>
        string.IsNullOrWhiteSpace(EnumPackagePath)
            ? "com.qolome.common.dict.biz.enums"
            : EnumPackagePath.Trim();

    public string? ResolveEntitySuperClass()
    {
        var value = EntitySuperClass?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public string ResolveEntityPackage() =>
        string.IsNullOrWhiteSpace(EntityPackage) ? "dao.model" : EntityPackage.Trim();

    public string ResolveMapperPackage() =>
        string.IsNullOrWhiteSpace(MapperPackage) ? "dao.mapper" : MapperPackage.Trim();

    public string ResolveXmlPackage() =>
        string.IsNullOrWhiteSpace(XmlPackage) ? "mapper" : XmlPackage.Trim();

    public string ResolveTsDictPath() =>
        string.IsNullOrWhiteSpace(TsDictPath) ? Path.Combine("src", "dict") : TsDictPath.Trim();

    private static string NormalizeColumn(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public enum JdbcInputMode
{
    Url,
    Fields,
}

public enum DatabaseKind
{
    MySql,
    PostgreSql,
    Oracle,
    Sqlite,
}

public sealed class DataSourceSettings
{
    public JdbcInputMode InputMode { get; set; } = JdbcInputMode.Fields;

    public DatabaseKind DatabaseKind { get; set; } = DatabaseKind.MySql;

    public string Host { get; set; } = "127.0.0.1";

    public string Port { get; set; } = "3306";

    public string Database { get; set; } = "qolome";

    public string ConnectionParams { get; set; } =
        "useSSL=false&serverTimezone=Asia/Shanghai&characterEncoding=UTF-8";

    public string JdbcUrl { get; set; } = "";

    public string Username { get; set; } = "root";

    public string Password { get; set; } = "";
}