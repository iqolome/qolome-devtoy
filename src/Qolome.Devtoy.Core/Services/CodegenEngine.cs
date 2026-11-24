namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Codegen;
using Qolome.Devtoy.Core.Models;

/// <summary>纯 C# 代码生成入口（对齐 qolome-codegen DaoGenerator / EnumGenerator / TsDictGenerator）。</summary>
public sealed class CodegenEngine
{
    private const string TaskDao = "dao";
    private const string TaskController = "controller";
    private const string TaskService = "service";
    private const string TaskEnum = "enum";
    private const string TaskTs = "ts";

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
        ValidateRequest(tasks, targetModule);

        var ctx = BuildContext(settings, targetModule!, modelName);
        var written = new List<string>();
        var taskSet = new HashSet<string>(tasks, StringComparer.OrdinalIgnoreCase);

        if (IsDaoLike(taskSet))
        {
            written.AddRange(GenerateDao(settings, ctx, taskSet, tableNames, cancellationToken));
            ctx.Lines.Add($"DAO 生成完成: tables={string.Join(",", tableNames)}, files={written.Count}");
        }

        if (taskSet.Contains(TaskEnum))
        {
            written.AddRange(GenerateEnums(settings, ctx, enumItemCodes, cancellationToken));
        }

        if (taskSet.Contains(TaskTs))
        {
            written.AddRange(GenerateTsDicts(settings, ctx, enumItemCodes, cancellationToken));
        }

        EnsureKnownTasks(tasks);
        ctx.Lines.Add($"完成，共写出 {written.Count} 个文件");
        return Task.FromResult(CodegenResult.Ok(ctx.Lines));
    }

    private static void ValidateRequest(IReadOnlyList<string>? tasks, ModuleEntry? targetModule)
    {
        if (tasks is null || tasks.Count == 0)
        {
            throw new InvalidOperationException("请至少选择一种生成任务");
        }

        if (targetModule is null || string.IsNullOrWhiteSpace(targetModule.ModulePath))
        {
            throw new InvalidOperationException("请先选择目标模块");
        }
    }

    private static RunContext BuildContext(DevtoySettings settings, ModuleEntry targetModule, string? modelName)
    {
        var version = string.IsNullOrWhiteSpace(targetModule.ProjectVersion)
            ? "1.0.0"
            : targetModule.ProjectVersion.Trim();
        var resolvedModelName = !string.IsNullOrWhiteSpace(modelName)
            ? modelName.Trim()
            : (targetModule.CodegenModelName?.Trim() ?? "");
        var author = settings.FormatAuthorHtml();
        // 按模块覆盖合并枚举包 / TS 字典路径，不改写全局 settings
        var codegenSettings = ModuleCatalogFilter.MergeCodegenSettings(settings, targetModule);
        var basePackage = settings.ResolveBasePackage();
        return new RunContext(
            version,
            targetModule.ModulePath,
            targetModule.ProjectNameDisplay,
            resolvedModelName,
            author,
            codegenSettings,
            basePackage,
            new List<string>
            {
                $"version={version}",
                $"module={targetModule.ModulePath}",
                $"modelName={resolvedModelName}",
                $"basePackage={basePackage}",
                $"enumPackage={codegenSettings.EnumPackage}",
                $"tsDictPath={codegenSettings.TsDictPath}",
                $"author={author}",
                $"jdbc={JdbcUrlBuilder.Resolve(settings.DataSource)}",
            });
    }

    private static bool IsDaoLike(HashSet<string> taskSet) =>
        taskSet.Contains(TaskDao)
        || taskSet.Contains(TaskController)
        || taskSet.Contains(TaskService);

    private List<string> GenerateDao(
        DevtoySettings settings,
        RunContext ctx,
        HashSet<string> taskSet,
        IReadOnlyList<string> tableNames,
        CancellationToken cancellationToken)
    {
        if (tableNames.Count == 0)
        {
            throw new InvalidOperationException("生成 Dao 时请勾选至少一张表");
        }

        var generateEntityMapper = taskSet.Contains(TaskDao)
            || taskSet.Contains(TaskService)
            || taskSet.Contains(TaskController);
        var serviceEnabled = taskSet.Contains(TaskService);
        var controllerEnabled = taskSet.Contains(TaskController);
        var target = new DaoTarget
        {
            ProjectPath = ctx.ProjectPath,
            ProjectName = ctx.ProjectName,
            ModelName = ctx.ModelName,
            Auth = ctx.Author,
            BasePackage = ctx.BasePackage,
            EntitySuperClass = settings.ResolveEntitySuperClass(),
            EntityPackage = settings.ResolveEntityPackage(),
            MapperPackage = settings.ResolveMapperPackage(),
            XmlPackage = settings.ResolveXmlPackage(),
            Version = ctx.Version,
            Tables = string.Join(",", tableNames),
            ServiceGenEnabled = serviceEnabled,
            ControllerGenEnabled = controllerEnabled,
            DtoGenEnabled = serviceEnabled,
            DtoConvertGenEnabled = serviceEnabled,
        };

        return new DaoGenerator(_velocityFiles).Generate(
                ctx.CodegenSettings,
                target,
                settings.DataSource,
                generateEntityMapper,
                cancellationToken)
            .ToList();
    }

    private List<string> GenerateEnums(
        DevtoySettings settings,
        RunContext ctx,
        IReadOnlyList<string>? enumItemCodes,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        var codes = RequireCodes(enumItemCodes, TaskEnum);
        foreach (var code in codes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = RequireItem(settings.DataSource, code);
            var values = EnabledValues(settings.DataSource, item.ItemId);
            var path = new EnumGenerator(_velocityFiles).Generate(
                ctx.CodegenSettings,
                new EnumProjectContext(ctx.ProjectPath, ctx.ProjectName, ctx.Author, ctx.Version),
                ctx.CodegenSettings.EnumPackage,
                item,
                values);
            written.Add(path);
            ctx.Lines.Add("生成枚举 " + path);
        }

        return written;
    }

    private List<string> GenerateTsDicts(
        DevtoySettings settings,
        RunContext ctx,
        IReadOnlyList<string>? enumItemCodes,
        CancellationToken cancellationToken)
    {
        var written = new List<string>();
        var codes = RequireCodes(enumItemCodes, TaskTs);
        foreach (var code in codes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = RequireItem(settings.DataSource, code);
            var values = EnabledValues(settings.DataSource, item.ItemId);
            var path = new TsDictGenerator(_velocityFiles).Generate(
                ctx.CodegenSettings,
                ctx.ProjectPath,
                ctx.ProjectName,
                ctx.Author,
                ctx.Version,
                item,
                values);
            written.Add(path);
            ctx.Lines.Add("生成 TS 字典 " + path);
        }

        return written;
    }

    private static void EnsureKnownTasks(IReadOnlyList<string> tasks)
    {
        var unknown = tasks.Where(t =>
            !t.Equals(TaskDao, StringComparison.OrdinalIgnoreCase)
            && !t.Equals(TaskController, StringComparison.OrdinalIgnoreCase)
            && !t.Equals(TaskService, StringComparison.OrdinalIgnoreCase)
            && !t.Equals(TaskEnum, StringComparison.OrdinalIgnoreCase)
            && !t.Equals(TaskTs, StringComparison.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException("未知任务: " + string.Join(",", unknown));
        }
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

    private sealed record RunContext(
        string Version,
        string ProjectPath,
        string ProjectName,
        string ModelName,
        string Author,
        CodegenSettings CodegenSettings,
        string BasePackage,
        List<string> Lines);
}

public sealed record CodegenResult(bool Success, IReadOnlyList<string> Lines)
{
    public static CodegenResult Ok(IReadOnlyList<string> lines) => new(true, lines);

    public static CodegenResult Fail(string message) => new(false, new[] { message });
}
