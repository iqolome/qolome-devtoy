namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Codegen;
using Qolome.Devtoy.Core.Models;

/// <summary>纯 C# 代码生成入口（对齐 qolome-codegen DaoGenerator / EnumGenerator / TsDictGenerator）。</summary>
public sealed class CodegenEngine
{
    private readonly VelocityFiles _velocityFiles = new();
    private readonly DictCatalogService _dict = new();

    public Task<CodegenResult> ExecuteAsync(
        DevtoySettings settings,
        ModuleEntry? targetModule,
        IReadOnlyList<string> tasks,
        IReadOnlyList<string> tableNames,
        IReadOnlyList<string>? enumItemCodes,
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (tasks is null || tasks.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一种生成任务");
        }

        if (targetModule is null || string.IsNullOrWhiteSpace(targetModule.ModulePath))
        {
            throw new InvalidOperationException("请先选择目标模块");
        }

        var version = string.IsNullOrWhiteSpace(targetModule.ProjectVersion)
            ? "1.0.0"
            : targetModule.ProjectVersion.Trim();
        var projectPath = targetModule.ModulePath;
        var projectName = targetModule.ProjectNameDisplay;
        var resolvedModelName = !string.IsNullOrWhiteSpace(modelName)
            ? modelName.Trim()
            : (targetModule.CodegenModelName?.Trim() ?? "");
        var author = settings.FormatAuthorHtml();
        var codegenSettings = settings.ToCodegenSettings();
        var basePackage = settings.ResolveBasePackage();
        var lines = new List<string>
        {
            $"version={version}",
            $"module={projectPath}",
            $"modelName={resolvedModelName}",
            $"basePackage={basePackage}",
            $"author={author}",
            $"jdbc={JdbcUrlBuilder.Resolve(settings.DataSource)}",
        };

        var written = new List<string>();
        var taskSet = new HashSet<string>(tasks, StringComparer.OrdinalIgnoreCase);
        var daoLike = taskSet.Contains("dao")
            || taskSet.Contains("controller")
            || taskSet.Contains("service");

        if (daoLike)
        {
            if (tableNames.Count == 0)
            {
                throw new InvalidOperationException("生成 Dao 时请勾选至少一张表");
            }

            if (settings.DataSource.DatabaseKind != DatabaseKind.MySql)
            {
                throw new InvalidOperationException("当前仅支持 MySQL 表代码生成");
            }

            var generateEntityMapper = taskSet.Contains("dao");
            // Service / Controller 依赖实体；未勾选 Dao 时仍生成 Entity/Mapper，避免残缺产物
            if (!generateEntityMapper && (taskSet.Contains("service") || taskSet.Contains("controller")))
            {
                generateEntityMapper = true;
            }

            var serviceEnabled = taskSet.Contains("service");
            var controllerEnabled = taskSet.Contains("controller");
            var target = new DaoTarget
            {
                ProjectPath = projectPath,
                ProjectName = projectName,
                ModelName = resolvedModelName,
                Auth = author,
                BasePackage = basePackage,
                Version = version,
                Tables = string.Join(",", tableNames),
                ServiceGenEnabled = serviceEnabled,
                ControllerGenEnabled = controllerEnabled,
                DtoGenEnabled = serviceEnabled,
                DtoConvertGenEnabled = serviceEnabled,
            };

            var daoFiles = new DaoGenerator(_velocityFiles).Generate(
                codegenSettings,
                target,
                settings.DataSource,
                generateEntityMapper,
                cancellationToken);
            written.AddRange(daoFiles);
            lines.Add($"DAO 生成完成: tables={target.Tables}, files={daoFiles.Count}");
        }

        if (taskSet.Contains("enum"))
        {
            var codes = RequireCodes(enumItemCodes, "enum");
            foreach (var code in codes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = RequireItem(settings.DataSource, code);
                var values = EnabledValues(settings.DataSource, item.ItemId);
                var path = new EnumGenerator(_velocityFiles).Generate(
                    codegenSettings,
                    projectPath,
                    projectName,
                    author,
                    version,
                    codegenSettings.EnumPackage,
                    item,
                    values);
                written.Add(path);
                lines.Add("生成枚举 " + path);
            }
        }

        if (taskSet.Contains("ts"))
        {
            var codes = RequireCodes(enumItemCodes, "ts");
            foreach (var code in codes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = RequireItem(settings.DataSource, code);
                var values = EnabledValues(settings.DataSource, item.ItemId);
                var path = new TsDictGenerator(_velocityFiles).Generate(
                    codegenSettings,
                    projectPath,
                    projectName,
                    author,
                    version,
                    item,
                    values);
                written.Add(path);
                lines.Add("生成 TS 字典 " + path);
            }
        }

        var unknown = tasks.Where(t =>
            !t.Equals("dao", StringComparison.OrdinalIgnoreCase)
            && !t.Equals("controller", StringComparison.OrdinalIgnoreCase)
            && !t.Equals("service", StringComparison.OrdinalIgnoreCase)
            && !t.Equals("enum", StringComparison.OrdinalIgnoreCase)
            && !t.Equals("ts", StringComparison.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException("未知任务: " + string.Join(",", unknown));
        }

        lines.Add($"完成，共写出 {written.Count} 个文件");
        return Task.FromResult(CodegenResult.Ok(lines));
    }

    private DictItemRecord RequireItem(DataSourceSettings dataSource, string itemCode)
    {
        var item = _dict.FindItemByCode(dataSource, itemCode);
        if (item is null)
        {
            throw new InvalidOperationException("找不到枚举字典项: " + itemCode);
        }

        return item;
    }

    private IReadOnlyList<DictValueRow> EnabledValues(DataSourceSettings dataSource, long itemId)
    {
        var values = _dict.ListValues(dataSource, itemId)
            .Where(v => v.Enabled)
            .ToList();
        if (values.Count == 0)
        {
            throw new InvalidOperationException("字典项 " + itemId + " 没有启用的字典值，无法生成");
        }

        return values;
    }

    private static IReadOnlyList<string> RequireCodes(IReadOnlyList<string>? codes, string name)
    {
        if (codes is null || codes.Count == 0)
        {
            throw new InvalidOperationException(name + ".itemCodes 不能为空");
        }

        return codes;
    }
}

public sealed record CodegenResult(bool Success, IReadOnlyList<string> Lines)
{
    public static CodegenResult Ok(IReadOnlyList<string> lines) => new(true, lines);

    public static CodegenResult Fail(string message) => new(false, new[] { message });
}
