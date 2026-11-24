using System.Collections;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>对齐 Java DaoGenerator：按表生成 Entity/Mapper/XML 及可选 Service、Controller、DTO。</summary>
public sealed class DaoGenerator
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai");

    private static readonly string[] RequestDtos =
        ["Create", "Delete", "Detail", "Edit", "QueryPage", "QuerySimpleAll"];

    private static readonly string[] ResponseDtos =
        ["Detail", "QueryPage", "QuerySimpleAll"];

    private readonly VelocityFiles _velocityFiles;
    private readonly MySqlTableReader _tableReader = new();
    private readonly DictCatalogService _dictCatalog = new();

    public DaoGenerator(VelocityFiles velocityFiles) => _velocityFiles = velocityFiles;

    public IReadOnlyList<string> Generate(
        CodegenSettings settings,
        DaoTarget target,
        DataSourceSettings dataSource,
        bool generateEntityMapper,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target.Tables))
        {
            throw new InvalidOperationException("生成 Dao 时 tables 不能为空");
        }

        if (string.IsNullOrWhiteSpace(target.ProjectPath))
        {
            throw new InvalidOperationException("生成 Dao 时 projectPath 不能为空");
        }

        var written = new List<string>();
        var tables = target.Tables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var tableName in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var meta = _tableReader.Read(dataSource, tableName);
            var context = BuildContext(settings, target, meta, dataSource);
            written.AddRange(WriteArtifacts(target, meta, context, generateEntityMapper));
        }

        return written;
    }

    private IEnumerable<string> WriteArtifacts(
        DaoTarget target,
        TableMetadata meta,
        Hashtable context,
        bool generateEntityMapper)
    {
        var javaRoot = Path.Combine(target.ProjectPath, "src", "main", "java");
        var resourceRoot = Path.Combine(target.ProjectPath, "src", "main", "resources");
        var parentPackage = JoinPackage(target.BasePackage, target.ModelName);
        var entityName = EntityName(meta.Name);

        if (generateEntityMapper)
        {
            yield return WriteJava(javaRoot, parentPackage, target.EntityPackage, entityName + ".java", "entity.java", context);
            yield return WriteJava(javaRoot, parentPackage, target.MapperPackage, entityName + "Mapper.java", "mapper.java", context);
            var xmlDir = Path.Combine(resourceRoot, target.XmlPackage.Replace('.', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(xmlDir);
            var xmlPath = Path.Combine(xmlDir, entityName + "Mapper.xml");
            _velocityFiles.WriteMap("mapper.xml", xmlPath, context);
            yield return xmlPath;
        }

        if (target.ServiceGenEnabled)
        {
            yield return WriteJava(
                javaRoot,
                parentPackage,
                target.ServicePackage,
                entityName + target.ServiceSuffixName + ".java",
                "service.java",
                context);
            yield return WriteJava(
                javaRoot,
                parentPackage,
                target.ServiceImplPackage,
                entityName + target.ServiceSuffixName + "Impl.java",
                "serviceImpl.java",
                context);
        }

        if (target.ControllerGenEnabled)
        {
            yield return WriteJava(
                javaRoot,
                parentPackage,
                target.ControllerPackage,
                entityName + "Controller.java",
                "controller.java",
                context);
        }

        if (target.DtoGenEnabled)
        {
            foreach (var dto in RequestDtos)
            {
                yield return WriteJava(
                    javaRoot,
                    parentPackage,
                    target.DtoPackageName + ".request",
                    entityName + dto + "RequestDto.java",
                    "Template" + dto + "RequestDto.java",
                    context);
            }

            foreach (var dto in ResponseDtos)
            {
                yield return WriteJava(
                    javaRoot,
                    parentPackage,
                    target.DtoPackageName + ".response",
                    entityName + dto + "ResponseDto.java",
                    "Template" + dto + "ResponseDto.java",
                    context);
            }

            if (target.DtoConvertGenEnabled)
            {
                yield return WriteJava(
                    javaRoot,
                    parentPackage,
                    target.DtoConvertPackageName,
                    entityName + "Convert.java",
                    "TemplateConvert.java",
                    context);
            }
        }

        if (target.RegisterBootStarterAutoConfig)
        {
            RegisterBootStarterImports(target, parentPackage, entityName);
        }
    }

    private static void RegisterBootStarterImports(DaoTarget target, string parentPackage, string entityName)
    {
        var entries = new List<(string Section, string Fqcn)>();
        if (target.DtoConvertGenEnabled)
        {
            entries.Add((
                "convert",
                JoinPackage(parentPackage, target.DtoConvertPackageName) + "." + entityName + "ConvertImpl"));
        }

        if (target.ServiceGenEnabled)
        {
            entries.Add((
                "service",
                JoinPackage(parentPackage, target.ServiceImplPackage)
                + "." + entityName + target.ServiceSuffixName + "Impl"));
        }

        if (target.ControllerGenEnabled)
        {
            entries.Add((
                "controller",
                JoinPackage(parentPackage, target.ControllerPackage) + "." + entityName + "Controller"));
        }

        if (entries.Count == 0)
        {
            return;
        }

        new AutoConfigurationImportsEditor(target.ProjectPath).EnsureClasses(entries);
    }

    private string WriteJava(
        string javaRoot,
        string parentPackage,
        string relativePackage,
        string fileName,
        string templateName,
        Hashtable context)
    {
        var packagePath = JoinPackage(parentPackage, relativePackage).Replace('.', Path.DirectorySeparatorChar);
        var path = Path.Combine(javaRoot, packagePath, fileName);
        _velocityFiles.WriteMap(templateName, path, context);
        return path;
    }

    private Hashtable BuildContext(
        CodegenSettings settings,
        DaoTarget target,
        TableMetadata meta,
        DataSourceSettings dataSource)
    {
        var datetime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone)
            .ToString("yyyy年MM月dd日 HH:mm:ss");
        var entityName = EntityName(meta.Name);
        var entityLower = CaseFormats.UpperCamelToLowerCamel(entityName);
        var parentPackage = JoinPackage(target.BasePackage, target.ModelName);
        var (superSimpleName, superFullName) = ResolveSuperEntity(target.EntitySuperClass);
        var commentRenderer = new DictCommentRenderer(
            settings,
            dataSource,
            _dictCatalog,
            _velocityFiles,
            meta.Name,
            meta.Columns);

        var columnBags = BuildColumnBags(
            settings,
            target,
            meta,
            commentRenderer,
            excludeBaseColumns: superSimpleName is not null,
            superFullName);

        var packageInfo = BuildPackageInfo(parentPackage, target);
        var tableInfo = BuildTableInfo(meta, entityName, entityLower, target, columnBags);
        return BuildRootContext(new RootContextArgs(
            settings,
            target,
            datetime,
            entityName,
            entityLower,
            superSimpleName,
            packageInfo,
            tableInfo,
            columnBags));
    }

    private sealed record RootContextArgs(
        CodegenSettings Codegen,
        DaoTarget Dao,
        string Datetime,
        string EntityClassName,
        string EntityLowerCamel,
        string? SuperSimpleName,
        Hashtable PackageInfo,
        Hashtable TableInfo,
        ColumnBags Bags);

    private sealed record ColumnBags(
        VmList Fields,
        VmList CommonFields,
        VmList AllFields,
        List<string> AllFieldNames,
        VmList SortColumnConstants,
        SortedSet<string> Imports);

    private static ColumnBags BuildColumnBags(
        CodegenSettings settings,
        DaoTarget target,
        TableMetadata meta,
        DictCommentRenderer commentRenderer,
        bool excludeBaseColumns,
        string? superFullName)
    {
        var imports = CreateEntityImports();
        if (superFullName is not null)
        {
            imports.Add(superFullName);
        }

        var bags = new ColumnBags(
            new VmList(),
            new VmList(),
            new VmList(),
            new List<string>(),
            new VmList(),
            imports);

        foreach (var column in meta.Columns)
        {
            AppendColumn(settings, target, commentRenderer, excludeBaseColumns, bags, column);
        }

        return bags;
    }

    private static SortedSet<string> CreateEntityImports() =>
        new(StringComparer.Ordinal)
        {
            "com.baomidou.mybatisplus.annotation.TableName",
            "com.baomidou.mybatisplus.annotation.TableId",
            "com.baomidou.mybatisplus.annotation.TableField",
            "com.baomidou.mybatisplus.annotation.IdType",
            "com.baomidou.mybatisplus.annotation.FieldFill",
        };

    private static void AppendColumn(
        CodegenSettings settings,
        DaoTarget target,
        DictCommentRenderer commentRenderer,
        bool excludeBaseColumns,
        ColumnBags bags,
        ColumnMetadata column)
    {
        var isCommon = excludeBaseColumns && IsSuperEntityColumn(settings, column.Name);
        var (propertyType, jdbcType, typeImports) = MySqlJavaTypeMapper.Map(column);
        if (!isCommon)
        {
            ApplyEntityColumnTyping(commentRenderer, bags.Imports, column, ref propertyType, ref typeImports);
        }

        var propertyName = CaseFormats.UpperCamelToLowerCamel(
            CaseFormats.LowerUnderscoreToUpperCamel(column.Name));
        var versionField = string.Equals(column.Name, settings.VersionColumnName, StringComparison.OrdinalIgnoreCase);
        var logicDeleteField = string.Equals(column.Name, settings.LogicDeleteColumnName, StringComparison.OrdinalIgnoreCase);
        if (!isCommon && versionField)
        {
            bags.Imports.Add("com.baomidou.mybatisplus.annotation.Version");
        }

        if (!isCommon && logicDeleteField)
        {
            bags.Imports.Add("com.baomidou.mybatisplus.annotation.TableLogic");
        }

        var fieldVm = new
        {
            name = new VmString(column.Name),
            comment = isCommon ? "" : commentRenderer.Render(column.Comment, column.Name),
            propertyName,
            propertyType = new VmString(propertyType),
            capitalName = CaseFormats.LowerCamelToUpperCamel(propertyName),
            annotationColumnName = column.Name,
            keyFlag = column.PrimaryKey,
            keyIdentityFlag = column.AutoIncrement,
            convert = true,
            fill = ResolveFill(settings, column.Name),
            versionField,
            logicDeleteField,
            metaInfo = new { jdbcType },
        };

        bags.AllFields.Add(fieldVm);
        bags.AllFieldNames.Add(column.Name);
        if (isCommon)
        {
            // 基类字段：实体类不生成，但 mapper resultMap / 列清单需要完整对象
            bags.CommonFields.Add(fieldVm);
            return;
        }

        bags.Fields.Add(fieldVm);
        bags.SortColumnConstants.Add(target.ColumnConstantFieldPrefix + column.Name.ToUpperInvariant());
    }

    private static void ApplyEntityColumnTyping(
        DictCommentRenderer commentRenderer,
        SortedSet<string> imports,
        ColumnMetadata column,
        ref string propertyType,
        ref IReadOnlyList<string> typeImports)
    {
        var enumJavaType = commentRenderer.TryResolveEnumJavaType(column.Comment, column.Name);
        if (enumJavaType is not null)
        {
            propertyType = enumJavaType.Value.SimpleName;
            typeImports = new[] { enumJavaType.Value.ImportName };
        }

        foreach (var typeImport in typeImports)
        {
            imports.Add(typeImport);
        }
    }

    private static Hashtable BuildPackageInfo(string parentPackage, DaoTarget target) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Entity"] = JoinPackage(parentPackage, target.EntityPackage),
            ["Mapper"] = JoinPackage(parentPackage, target.MapperPackage),
            ["Service"] = JoinPackage(parentPackage, target.ServicePackage),
            ["ServiceImpl"] = JoinPackage(parentPackage, target.ServiceImplPackage),
            ["Controller"] = JoinPackage(parentPackage, target.ControllerPackage),
            ["Parent"] = parentPackage,
            ["ModuleName"] = string.IsNullOrWhiteSpace(target.ModelName) ? null : target.ModelName,
        };

    private static Hashtable BuildTableInfo(
        TableMetadata meta,
        string entityName,
        string entityLower,
        DaoTarget target,
        ColumnBags bags) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = meta.Name,
            ["comment"] = meta.Comment,
            ["fields"] = bags.Fields,
            ["commonFields"] = bags.CommonFields,
            ["importPackages"] = new VmList(bags.Imports),
            ["convert"] = true,
            ["mapperName"] = entityName + "Mapper",
            ["serviceName"] = entityName + target.ServiceSuffixName,
            ["serviceImplName"] = entityName + target.ServiceSuffixName + "Impl",
            ["controllerName"] = entityName + "Controller",
            ["entityPath"] = entityLower,
            ["serviceInterface"] = true,
        };

    private static Hashtable BuildRootContext(RootContextArgs args)
    {
        var hasModel = !string.IsNullOrWhiteSpace(args.Dao.ModelName);
        var moduleClassName = hasModel
            ? CaseFormats.LowerCamelToUpperCamel(args.Dao.ModelName)
            : "";

        return new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["copyright"] = args.Codegen.Copyright,
            ["projectName"] = args.Dao.ProjectName,
            ["datetime"] = args.Datetime,
            ["version"] = args.Dao.Version,
            ["author"] = args.Dao.Auth,
            ["package"] = args.PackageInfo,
            ["table"] = args.TableInfo,
            ["entity"] = args.EntityClassName,
            ["entityLowerCamel"] = args.EntityLowerCamel,
            ["entityLombokModel"] = true,
            ["entityColumnConstant"] = true,
            ["entitySerialVersionUID"] = false,
            ["chainModel"] = false,
            ["activeRecord"] = false,
            ["springdoc"] = false,
            ["swagger"] = false,
            ["kotlin"] = false,
            // 模板 extends 使用简单类名；全限定名已加入 importPackages
            ["superEntityClass"] = args.SuperSimpleName,
            ["idType"] = args.Codegen.IdType,
            ["schemaName"] = "",
            ["mapperAnnotationClass"] = new VmAnnotationClass(args.Codegen.MapperAnnotation),
            ["superMapperClass"] = "BaseMapper",
            ["superMapperClassPackage"] = "com.baomidou.mybatisplus.core.mapper.BaseMapper",
            ["superServiceClass"] = "IService",
            ["superServiceImplClass"] = "ServiceImpl",
            ["restControllerStyle"] = args.Dao.ControllerRestStyle,
            ["controllerMappingHyphenStyle"] = false,
            ["controllerMappingHyphen"] = CaseFormats.UpperCamelToLowerHyphen(args.EntityClassName),
            ["columnConstantFieldPrefix"] = args.Dao.ColumnConstantFieldPrefix,
            ["moduleClassName"] = moduleClassName,
            ["moduleConstantClassName"] = args.Codegen.Copyright,
            ["dtoGenEnabled"] = args.Dao.DtoGenEnabled,
            ["dtoPackageName"] = args.Dao.DtoPackageName,
            ["dtoConvertGenEnabled"] = args.Dao.DtoConvertGenEnabled,
            ["dtoConvertPackageName"] = args.Dao.DtoConvertPackageName,
            // mapper batch* 与 Base_Column_List 使用全列（含基类字段），顺序与表结构一致
            ["tableFields"] = args.Bags.AllFields,
            ["sortColumnConstants"] = args.Bags.SortColumnConstants,
            ["allFieldNames"] = string.Join(",", args.Bags.AllFieldNames),
            ["enableCache"] = false,
            ["baseResultMap"] = true,
            ["baseColumnList"] = true,
        };
    }

    /// <summary>BaseModel 中的审计字段：有基类时不在子实体中重复生成。</summary>
    private static bool IsSuperEntityColumn(CodegenSettings settings, string columnName) =>
        EqualsCol(columnName, settings.TenantIdColumnName)
        || EqualsCol(columnName, settings.CreateTimeColumnName)
        || EqualsCol(columnName, settings.CreaterIdColumnName)
        || EqualsCol(columnName, settings.ModifyTimeColumnName)
        || EqualsCol(columnName, settings.ModifierIdColumnName);

    /// <summary>返回 (简单类名, 全限定名)；无基类时均为 null。</summary>
    private static (string? SimpleName, string? FullName) ResolveSuperEntity(string? entitySuperClass)
    {
        if (string.IsNullOrWhiteSpace(entitySuperClass))
        {
            return (null, null);
        }

        var full = entitySuperClass.Trim();
        var dot = full.LastIndexOf('.');
        if (dot < 0)
        {
            return (full, null);
        }

        return (full[(dot + 1)..], full);
    }

    private static string? ResolveFill(CodegenSettings settings, string columnName)
    {
        if (EqualsCol(columnName, settings.TenantIdColumnName)
            || EqualsCol(columnName, settings.CreaterIdColumnName)
            || EqualsCol(columnName, settings.CreateTimeColumnName)
            || EqualsCol(columnName, settings.LogicDeleteColumnName)
            || EqualsCol(columnName, settings.VersionColumnName))
        {
            return "INSERT";
        }

        if (EqualsCol(columnName, settings.ModifierIdColumnName)
            || EqualsCol(columnName, settings.ModifyTimeColumnName))
        {
            return "INSERT_UPDATE";
        }

        return null;
    }

    private static bool EqualsCol(string columnName, string expected) =>
        string.Equals(columnName, expected, StringComparison.OrdinalIgnoreCase);

    private static string EntityName(string tableName) =>
        CaseFormats.LowerUnderscoreToUpperCamel(tableName);

    private static string JoinPackage(string parent, string? child)
    {
        if (string.IsNullOrWhiteSpace(child))
        {
            return parent.Trim('.');
        }

        return (parent.Trim('.') + "." + child.Trim('.')).Trim('.');
    }
}
