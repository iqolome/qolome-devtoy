using System.Collections;
using Qolome.Devtoy.Core.Models;

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
            var context = BuildContext(settings, target, meta);
            written.AddRange(WriteArtifacts(settings, target, meta, context, generateEntityMapper));
        }

        return written;
    }

    private IEnumerable<string> WriteArtifacts(
        CodegenSettings settings,
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

    private static Hashtable BuildContext(CodegenSettings settings, DaoTarget target, TableMetadata meta)
    {
        var datetime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Zone)
            .ToString("yyyy年MM月dd日 HH:mm:ss");
        var entityName = EntityName(meta.Name);
        var entityLower = CaseFormats.UpperCamelToLowerCamel(entityName);
        var parentPackage = JoinPackage(target.BasePackage, target.ModelName);
        var fields = new VmList();
        var imports = new SortedSet<string>(StringComparer.Ordinal);
        imports.Add("com.baomidou.mybatisplus.annotation.TableName");
        imports.Add("com.baomidou.mybatisplus.annotation.TableId");
        imports.Add("com.baomidou.mybatisplus.annotation.TableField");
        imports.Add("com.baomidou.mybatisplus.annotation.IdType");
        imports.Add("com.baomidou.mybatisplus.annotation.FieldFill");

        string? fillImportNeeded = null;
        foreach (var column in meta.Columns)
        {
            var (propertyType, jdbcType, typeImports) = MySqlJavaTypeMapper.Map(column);
            foreach (var typeImport in typeImports)
            {
                imports.Add(typeImport);
            }

            var propertyName = CaseFormats.LowerUnderscoreToUpperCamel(column.Name);
            propertyName = CaseFormats.UpperCamelToLowerCamel(propertyName);
            var capitalName = CaseFormats.LowerCamelToUpperCamel(propertyName);
            var fill = ResolveFill(settings, column.Name);
            if (fill is not null)
            {
                fillImportNeeded = "com.baomidou.mybatisplus.annotation.FieldFill";
            }

            var versionField = string.Equals(column.Name, settings.VersionColumnName, StringComparison.OrdinalIgnoreCase);
            var logicDeleteField = string.Equals(column.Name, settings.LogicDeleteColumnName, StringComparison.OrdinalIgnoreCase);
            if (versionField)
            {
                imports.Add("com.baomidou.mybatisplus.annotation.Version");
            }

            if (logicDeleteField)
            {
                imports.Add("com.baomidou.mybatisplus.annotation.TableLogic");
            }

            fields.Add(new
            {
                name = new VmString(column.Name),
                comment = column.Comment,
                propertyName,
                propertyType = new VmString(propertyType),
                capitalName,
                annotationColumnName = column.Name,
                keyFlag = column.PrimaryKey,
                keyIdentityFlag = column.AutoIncrement,
                convert = true,
                fill,
                versionField,
                logicDeleteField,
                metaInfo = new { jdbcType },
            });
        }

        _ = fillImportNeeded;
        var packageInfo = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["Entity"] = JoinPackage(parentPackage, target.EntityPackage),
            ["Mapper"] = JoinPackage(parentPackage, target.MapperPackage),
            ["Service"] = JoinPackage(parentPackage, target.ServicePackage),
            ["ServiceImpl"] = JoinPackage(parentPackage, target.ServiceImplPackage),
            ["Controller"] = JoinPackage(parentPackage, target.ControllerPackage),
            ["Parent"] = parentPackage,
            ["ModuleName"] = string.IsNullOrWhiteSpace(target.ModelName) ? null : target.ModelName,
        };

        var tableInfo = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = meta.Name,
            ["comment"] = meta.Comment,
            ["fields"] = fields,
            ["commonFields"] = new VmList(),
            ["importPackages"] = new VmList(imports),
            ["convert"] = true,
            ["mapperName"] = entityName + "Mapper",
            ["serviceName"] = entityName + target.ServiceSuffixName,
            ["serviceImplName"] = entityName + target.ServiceSuffixName + "Impl",
            ["controllerName"] = entityName + "Controller",
            ["entityPath"] = entityLower,
            ["serviceInterface"] = true,
        };

        var hasModel = !string.IsNullOrWhiteSpace(target.ModelName);
        var moduleClassName = hasModel
            ? CaseFormats.LowerCamelToUpperCamel(target.ModelName)
            : "";

        var context = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["copyright"] = settings.Copyright,
            ["projectName"] = target.ProjectName,
            ["datetime"] = datetime,
            ["version"] = target.Version,
            ["author"] = target.Auth,
            ["package"] = packageInfo,
            ["table"] = tableInfo,
            ["entity"] = entityName,
            ["entityLowerCamel"] = entityLower,
            ["entityLombokModel"] = true,
            ["entityColumnConstant"] = true,
            ["entitySerialVersionUID"] = false,
            ["chainModel"] = false,
            ["activeRecord"] = false,
            ["springdoc"] = false,
            ["swagger"] = false,
            ["kotlin"] = false,
            ["superEntityClass"] = string.IsNullOrWhiteSpace(target.EntitySuperClass) ? null : target.EntitySuperClass,
            ["idType"] = settings.IdType,
            ["schemaName"] = "",
            ["mapperAnnotationClass"] = new VmAnnotationClass(settings.MapperAnnotation),
            ["superMapperClass"] = "BaseMapper",
            ["superMapperClassPackage"] = "com.baomidou.mybatisplus.core.mapper.BaseMapper",
            ["superServiceClass"] = "IService",
            ["superServiceImplClass"] = "ServiceImpl",
            ["restControllerStyle"] = target.ControllerRestStyle,
            ["controllerMappingHyphenStyle"] = false,
            ["controllerMappingHyphen"] = CaseFormats.UpperCamelToLowerHyphen(entityName),
            ["columnConstantFieldPrefix"] = target.ColumnConstantFieldPrefix,
            ["moduleClassName"] = moduleClassName,
            ["moduleConstantClassName"] = settings.Copyright,
            ["dtoGenEnabled"] = target.DtoGenEnabled,
            ["dtoPackageName"] = target.DtoPackageName,
            ["dtoConvertGenEnabled"] = target.DtoConvertGenEnabled,
            ["dtoConvertPackageName"] = target.DtoConvertPackageName,
            ["tableFields"] = fields,
            ["allFieldNames"] = string.Join(",", meta.Columns.Select(c => c.Name)),
            ["enableCache"] = false,
            ["baseResultMap"] = true,
            ["baseColumnList"] = true,
        };

        return context;
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
