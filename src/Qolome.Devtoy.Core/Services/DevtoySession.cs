namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public sealed class DevtoySession
{
    private readonly ModuleScanner _scanner = new();

    private readonly SettingsStore _store = new();

    private readonly DictCatalogService _dict = new();

    private readonly CodegenEngine _codegen = new();

    private readonly TemplateStore _templates = new();

    private readonly SchemaInitService _schemaInit = new();

    public DevtoySettings Settings { get; private set; }

    public TemplateStore Templates => _templates;

    public SchemaInitService SchemaInit => _schemaInit;

    public IReadOnlyList<ModuleEntry> Modules { get; private set; } = Array.Empty<ModuleEntry>();

    public ModuleEntry? SelectedModule { get; set; }

    public string WorkspaceRoot { get; private set; } = "";

    public event Action<string>? LogAppended;

    public event Action? ModulesChanged;

    /// <summary>控制台显隐、主题设置等界面偏好变更（已写入 settings.json）。</summary>
    public event Action? UiPreferencesChanged;

    public DevtoySession()
    {
        Settings = _store.Load();
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();
        var seeded = _templates.EnsureSeeded();
        if (seeded > 0)
        {
            Log($"已初始化 {seeded} 个默认代码模板 -> {DevtoyPaths.TemplatesRoot}");
        }

        if (Settings.WorkspaceRoots.Count > 0)
        {
            Log($"已恢复 {Settings.WorkspaceRoots.Count} 个工作区，正在扫描模块…");
            _ = RefreshModulesAsync();
        }
        else if (Settings.SpecifiedProjects.Count > 0)
        {
            Log("已恢复指定工程列表，正在加载模块…");
            _ = RefreshModulesAsync();
        }
        else
        {
            Log("就绪。请在「模块管理」添加工作区；模块列表按磁盘实时扫描，不单独落库。");
        }
    }

    /// <summary>叠加添加工作区（不覆盖已有）；已存在则仅刷新。</summary>
    public Task AddWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            return Task.CompletedTask;
        }

        path = Path.GetFullPath(path);
        var exists = Settings.WorkspaceRoots.Any(r =>
            string.Equals(Path.GetFullPath(r), path, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            Settings.WorkspaceRoots.Add(path);
            Settings.NormalizeWorkspaceRoots();
            SyncWorkspaceRootProperty();
            SaveSettingsQuiet();
            Log($"已添加工作区: {path}（共 {Settings.WorkspaceRoots.Count} 个）");
        }
        else
        {
            Log($"工作区已在列表中，重新扫描: {path}");
        }

        return RefreshModulesAsync(cancellationToken);
    }

    /// <summary>兼容旧调用：等价于 <see cref="AddWorkspaceAsync"/>。</summary>
    public Task SetWorkspaceRootAsync(string path, CancellationToken cancellationToken = default) =>
        AddWorkspaceAsync(path, cancellationToken);

    private void SyncWorkspaceRootProperty()
    {
        WorkspaceRoot = Settings.WorkspaceRoots.Count > 0
            ? Settings.WorkspaceRoots[0]
            : "";
        Settings.WorkspaceRoot = WorkspaceRoot;
    }

    /// <summary>后台扫描模块；调用方可叠加 UI 遮罩。</summary>
    public Task RefreshModulesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RefreshModulesCore();
            },
            cancellationToken);

    public void RefreshModules() => RefreshModulesCore();

    private void RefreshModulesCore()
    {
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();

        var workspaceModules = new List<ModuleEntry>();
        var seenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Settings.WorkspaceRoots)
        {
            if (!Directory.Exists(root))
            {
                LogWarn($"工作区不存在，已跳过: {root}");
                continue;
            }

            foreach (var entry in _scanner.ScanWorkspace(root))
            {
                if (seenModules.Add(entry.ModulePath))
                {
                    workspaceModules.Add(entry);
                }
            }
        }

        var specified = _scanner.BuildSpecifiedModules(Settings.SpecifiedProjects);
        var merged = ModuleScanner.Merge(workspaceModules, specified);
        Modules = ModuleCatalogFilter.Apply(merged, Settings);
        ModulesChanged?.Invoke();
        var ignored = Modules.Count(m => m.IsIgnored);
        var wsCount = Settings.WorkspaceRoots.Count;
        Log(wsCount == 0
            ? $"已加载 {Modules.Count} 个模块（含 {specified.Count} 个指定工程"
                + (ignored > 0 ? $"，忽略 {ignored}" : "") + "）"
            : $"已扫描 {Modules.Count} 个模块（{wsCount} 个工作区，含 {specified.Count} 个指定工程"
                + (ignored > 0 ? $"，忽略 {ignored}" : "") + "）");
    }

    private void SaveSettingsQuiet()
    {
        _store.Save(Settings);
        UiPreferencesChanged?.Invoke();
    }

    public void SaveSettings()
    {
        _store.Save(Settings);
        UiPreferencesChanged?.Invoke();
        Log("配置已保存");
    }

    public void SetShowConsole(bool show)
    {
        if (Settings.ShowConsole == show)
        {
            return;
        }

        Settings.ShowConsole = show;
        _store.Save(Settings);
        UiPreferencesChanged?.Invoke();
    }

    public void ExportConfig()
    {
        _store.ExportGlobal(Settings);
        Log("已导出配置 -> " + _store.GlobalPath);
    }

    public bool ImportConfig()
    {
        var loaded = _store.ImportGlobal();
        if (loaded is null)
        {
            LogWarn("未找到配置文件: " + _store.GlobalPath);
            return false;
        }

        Settings = loaded;
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log("已导入配置");
        return true;
    }

    public Task<bool> ImportConfigAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ImportConfig();
            },
            cancellationToken);

    public Task ExportConfigAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExportConfig();
            },
            cancellationToken);

    public void AddSpecifiedProject(SpecifiedProjectEntry entry)
    {
        entry.Root = Path.GetFullPath(entry.Root);
        Settings.SpecifiedProjects.RemoveAll(p =>
            string.Equals(p.Root, entry.Root, StringComparison.OrdinalIgnoreCase));
        Settings.SpecifiedProjects.Add(entry);
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已添加指定工程: {entry.Root} ({entry.Java}, {entry.Web})");
    }

    public Task AddSpecifiedProjectAsync(SpecifiedProjectEntry entry, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddSpecifiedProject(entry);
            },
            cancellationToken);

    public void RemoveSpecifiedProject(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        Settings.SpecifiedProjects.RemoveAll(p =>
            string.Equals(p.Root, projectRoot, StringComparison.OrdinalIgnoreCase));
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已移除指定工程: {projectRoot}");
    }

    public void UpdateSpecifiedProject(SpecifiedProjectEntry entry)
    {
        entry.Root = Path.GetFullPath(entry.Root);
        var index = Settings.SpecifiedProjects.FindIndex(p =>
            string.Equals(p.Root, entry.Root, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            throw new InvalidOperationException("指定工程不存在: " + entry.Root);
        }

        Settings.SpecifiedProjects[index] = entry;
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已更新指定工程: {entry.Root}");
    }

    public Task UpdateSpecifiedProjectAsync(SpecifiedProjectEntry entry, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpdateSpecifiedProject(entry);
            },
            cancellationToken);

    /// <summary>从模块列表移除：指定工程从配置删除；工作区模块仅移出当前列表（不删磁盘，刷新可再次出现）。</summary>
    public void RemoveModules(IEnumerable<ModuleEntry> entries)
    {
        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var settingsChanged = false;
        foreach (var entry in entries)
        {
            string path;
            try
            {
                path = Path.GetFullPath(entry.ModulePath);
            }
            catch
            {
                continue;
            }

            if (entry.IsSpecified)
            {
                var before = Settings.SpecifiedProjects.Count;
                Settings.SpecifiedProjects.RemoveAll(p =>
                    string.Equals(Path.GetFullPath(p.Root), path, StringComparison.OrdinalIgnoreCase));
                if (Settings.SpecifiedProjects.Count < before)
                {
                    settingsChanged = true;
                }
            }

            if (RemoveIgnoredPath(path))
            {
                settingsChanged = true;
            }

            drop.Add(path);
        }

        if (drop.Count == 0)
        {
            return;
        }

        if (settingsChanged)
        {
            SaveSettingsQuiet();
        }

        Modules = Modules
            .Where(m =>
            {
                try
                {
                    return !drop.Contains(Path.GetFullPath(m.ModulePath));
                }
                catch
                {
                    return true;
                }
            })
            .ToList();
        if (SelectedModule is not null)
        {
            try
            {
                if (drop.Contains(Path.GetFullPath(SelectedModule.ModulePath)))
                {
                    SelectedModule = null;
                }
            }
            catch
            {
                // keep selection
            }
        }

        ModulesChanged?.Invoke();
        Log($"已从列表移除 {drop.Count} 个模块（未删除磁盘文件）");
    }

    public Task RemoveModulesAsync(IEnumerable<ModuleEntry> entries, CancellationToken cancellationToken = default)
    {
        var snapshot = entries.ToList();
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveModules(snapshot);
            },
            cancellationToken);
    }

    public void SetModuleIgnored(string modulePath, bool ignored)
    {
        modulePath = Path.GetFullPath(modulePath);
        var changed = ignored
            ? AddIgnoredPath(modulePath)
            : RemoveIgnoredPath(modulePath);
        if (!changed)
        {
            return;
        }

        SaveSettingsQuiet();
        Modules = ModuleCatalogFilter.Apply(Modules, Settings);
        if (ignored
            && SelectedModule is not null
            && string.Equals(
                Path.GetFullPath(SelectedModule.ModulePath),
                modulePath,
                StringComparison.OrdinalIgnoreCase))
        {
            SelectedModule = null;
        }

        ModulesChanged?.Invoke();
        Log(ignored ? $"已忽略模块: {modulePath}" : $"已取消忽略: {modulePath}");
    }

    public Task SetModuleIgnoredAsync(
        string modulePath,
        bool ignored,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetModuleIgnored(modulePath, ignored);
            },
            cancellationToken);

    private bool AddIgnoredPath(string path)
    {
        if (Settings.IgnoredModulePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        Settings.IgnoredModulePaths.Add(path);
        return true;
    }

    private bool RemoveIgnoredPath(string path)
    {
        var before = Settings.IgnoredModulePaths.Count;
        Settings.IgnoredModulePaths.RemoveAll(p =>
        {
            try
            {
                return string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
        return Settings.IgnoredModulePaths.Count < before;
    }

    public void UpdateWorkspaceModuleOverride(
        string modulePath,
        string? displayName,
        string? version,
        ProjectTypes types)
    {
        modulePath = Path.GetFullPath(modulePath);
        ModuleCatalogFilter.SaveWorkspaceOverride(Settings, modulePath, displayName, version, types);
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已更新模块: {modulePath}");
    }

    public Task UpdateWorkspaceModuleOverrideAsync(
        string modulePath,
        string? displayName,
        string? version,
        ProjectTypes types,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpdateWorkspaceModuleOverride(modulePath, displayName, version, types);
            },
            cancellationToken);

    /// <summary>保存模块的 Java 代码生成习惯（模块名 + Dao/Service/Controller）。</summary>
    public void SaveModuleCodegenPrefs(
        string modulePath,
        string? modelName,
        bool dao,
        bool service,
        bool controller)
    {
        modulePath = Path.GetFullPath(modulePath);
        ModuleCatalogFilter.SaveCodegenPrefs(Settings, modulePath, modelName, dao, service, controller);
        Settings.CodegenHabit.LastModulePath = modulePath;
        Settings.CodegenHabit.LastModelName = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
        Settings.CodegenHabit.Dao = dao;
        Settings.CodegenHabit.Service = service;
        Settings.CodegenHabit.Controller = controller;
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已保存代码生成配置: {modulePath}");
    }

    public Task SaveModuleCodegenPrefsAsync(
        string modulePath,
        string? modelName,
        bool dao,
        bool service,
        bool controller,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveModuleCodegenPrefs(modulePath, modelName, dao, service, controller);
            },
            cancellationToken);

    public SpecifiedProjectEntry? FindSpecifiedProject(string modulePath)
    {
        modulePath = Path.GetFullPath(modulePath);
        return Settings.SpecifiedProjects.FirstOrDefault(p =>
            string.Equals(Path.GetFullPath(p.Root), modulePath, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TableListItem>> ListTablesAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseGate.TryEnter())
        {
            LogWarn("已有数据库操作进行中，请稍候");
            return Array.Empty<TableListItem>();
        }

        try
        {
            var tables = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return JdbcTableLister.ListTables(Settings.DataSource);
            }, cancellationToken);
            Log($"已读取 {tables.Count} 张表");
            return tables;
        }
        catch (Exception ex)
        {
            LogError("读取表失败: " + ex.Message);
            return Array.Empty<TableListItem>();
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    public async Task<IReadOnlyList<DictItemRecord>> ListDictItemsAsync(
        string? itemCode = null,
        string? itemName = null,
        CancellationToken cancellationToken = default) =>
        await RunDictAsync(
            () => _dict.ListEnumItems(Settings.DataSource, itemCode, itemName),
            items => Log($"已查询到 {items.Count} 个字典项"),
            "查询字典失败",
            Array.Empty<DictItemRecord>(),
            cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<DictValueRow>> ListDictValuesAsync(long itemId, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => _dict.ListValues(Settings.DataSource, itemId),
            _ => { },
            "加载字典值失败",
            Array.Empty<DictValueRow>(),
            cancellationToken);

    public Task<long> CreateDictItemAsync(DictItemRecord record, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => _dict.CreateItem(Settings.DataSource, record),
            id => Log($"已创建字典项 {id}"),
            "创建字典项失败",
            0L,
            cancellationToken);

    public Task<bool> UpdateDictItemAsync(DictItemRecord record, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                _dict.UpdateItem(Settings.DataSource, record);
                return true;
            },
            _ => Log($"已更新字典项 {record.ItemId}"),
            "更新字典项失败",
            false,
            cancellationToken);

    public Task<bool> DeleteDictItemAsync(long itemId, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                _dict.DeleteItem(Settings.DataSource, itemId);
                return true;
            },
            _ => Log($"已删除字典项 {itemId}"),
            "删除字典项失败",
            false,
            cancellationToken);

    public Task<long> CreateDictValueAsync(DictValueRow value, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => _dict.CreateValue(Settings.DataSource, value),
            id => Log($"已创建字典值 {id}"),
            "创建字典值失败",
            0L,
            cancellationToken);

    public Task<bool> UpdateDictValueAsync(DictValueRow value, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                _dict.UpdateValue(Settings.DataSource, value);
                return true;
            },
            _ => Log($"已更新字典值 {value.ValId}"),
            "更新字典值失败",
            false,
            cancellationToken);

    public Task<bool> DeleteDictValueAsync(long valId, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                _dict.DeleteValue(Settings.DataSource, valId);
                return true;
            },
            _ => Log($"已删除字典值 {valId}"),
            "删除字典值失败",
            false,
            cancellationToken);

    public async Task GenerateDictForItemAsync(
        DictItemRecord item,
        ModuleEntry module,
        bool javaEnum,
        bool tsDict,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<string>();
        if (javaEnum)
        {
            tasks.Add("enum");
        }

        if (tsDict)
        {
            tasks.Add("ts");
        }

        if (tasks.Count == 0)
        {
            LogWarn("请至少选择一种生成类型");
            return;
        }

        SelectedModule = module;
        await RunCodegenAsync(tasks, Array.Empty<string>(), new[] { item.ItemCode }, modelName: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<T> RunDictAsync<T>(
        Func<T> action,
        Action<T> onSuccess,
        string failPrefix,
        T failValue,
        CancellationToken cancellationToken)
    {
        if (!DatabaseGate.TryEnter())
        {
            LogWarn("已有数据库操作进行中，请稍候");
            return failValue;
        }

        try
        {
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action();
            }, cancellationToken).ConfigureAwait(false);
            onSuccess(result);
            return result;
        }
        catch (Exception ex)
        {
            LogError(failPrefix + ": " + ex.Message);
            return failValue;
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    public async Task<bool> RunCodegenAsync(
        IReadOnlyList<string> tasks,
        IReadOnlyList<string> tables,
        IReadOnlyList<string>? dictCodes,
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseGate.TryEnter())
        {
            LogWarn("已有数据库操作进行中，请稍候");
            return false;
        }

        try
        {
            var result = await Task.Run(
                () => _codegen.ExecuteAsync(
                    Settings,
                    SelectedModule,
                    tasks,
                    tables,
                    dictCodes,
                    modelName,
                    cancellationToken).GetAwaiter().GetResult(),
                cancellationToken).ConfigureAwait(false);
            foreach (var line in result.Lines)
            {
                Log(line);
            }

            return result.Success;
        }
        catch (Exception ex)
        {
            LogError("生成失败: " + ex.Message);
            return false;
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    public async Task<string> TestDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var ds = Settings.DataSource;
        if (ds.DatabaseKind != DatabaseKind.MySql)
        {
            return "当前仅支持 MySQL 连接测试，其它类型后续补齐。";
        }

        if (!DatabaseGate.TryEnter())
        {
            return "已有数据库操作进行中，请稍候";
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = JdbcUrlBuilder.Resolve(ds);
                var cs = JdbcUrlBuilder.ToMySqlConnectionString(url, ds.Username, ds.Password);
                using var conn = new MySqlConnector.MySqlConnection(cs);
                conn.Open();
                return "数据库连接成功。\n" + url;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            return "数据库连接失败。\n" + ex.Message;
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    /// <summary>按当前数据源生成依赖表初始化 DDL（可保存 / 可直接用于初始化）。</summary>
    public Task<string> GenerateInitSqlAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _schemaInit.GenerateInitSql(Settings.DataSource);
            },
            cancellationToken);

    /// <summary>初始化库表：已存在则抛异常；否则按数据源生成建表 SQL 并执行。</summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseGate.TryEnter())
        {
            throw new InvalidOperationException("已有数据库操作进行中，请稍候");
        }

        try
        {
            await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _schemaInit.Initialize(Settings.DataSource);
                },
                cancellationToken).ConfigureAwait(false);
            Log($"数据库初始化成功（表：{string.Join(", ", SchemaInitService.RequiredTables)}）");
        }
        catch (Exception ex)
        {
            LogError("数据库初始化失败: " + ex.Message);
            throw;
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    public void Log(string message, DevtoyLogLevel level = DevtoyLogLevel.Info)
    {
        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var tag = level switch
        {
            DevtoyLogLevel.Warn => "WARN",
            DevtoyLogLevel.Error => "ERROR",
            _ => "INFO",
        };
        LogAppended?.Invoke($"[{ts}] [{tag}] {message}");
    }

    public void LogWarn(string message) => Log(message, DevtoyLogLevel.Warn);

    public void LogError(string message) => Log(message, DevtoyLogLevel.Error);

    public string ResolveCodegenVersion()
    {
        var v = SelectedModule?.ProjectVersion;
        return string.IsNullOrWhiteSpace(v) ? "1.0.0" : v;
    }
}