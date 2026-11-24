namespace Qolome.Devtoy.Core.Services;

using Qolome.Devtoy.Core.Models;

public sealed class DevtoySession
{
    private const string DatabaseBusyMessage = "已有数据库操作进行中，请稍候";

    private readonly SettingsStore _store = new();

    private readonly CodegenEngine _codegen = new();

    public DevtoySettings Settings { get; private set; }

    public IReadOnlyList<ModuleEntry> Modules { get; private set; } = Array.Empty<ModuleEntry>();

    public ModuleEntry? SelectedModule { get; set; }

    public string WorkspaceRoot { get; private set; } = "";

    public event Action<string>? LogAppended;

    public event Action? ModulesChanged;

    /// <summary>模块扫描 / 列表变更进行中（供项目管理页展示 loading，不阻塞导航）。</summary>
    public event Action? ModulesBusyChanged;

    /// <summary>控制台显隐、主题设置等界面偏好变更（已写入 settings.json）。</summary>
    public event Action? UiPreferencesChanged;

    private readonly object _modulesBusyGate = new();

    private readonly SemaphoreSlim _modulesScanLock = new(1, 1);

    private int _modulesBusyDepth;

    private string _modulesBusyMessage = "正在处理模块…";

    public bool IsModulesBusy
    {
        get
        {
            lock (_modulesBusyGate)
            {
                return _modulesBusyDepth > 0;
            }
        }
    }

    public string ModulesBusyMessage
    {
        get
        {
            lock (_modulesBusyGate)
            {
                return _modulesBusyMessage;
            }
        }
    }

    public DevtoySession()
    {
        Settings = _store.Load();
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();
        var seeded = TemplateStore.EnsureSeeded();
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
            Log("就绪。请在「项目管理」添加工作区；项目列表按磁盘实时扫描，已有项目配置只追加不覆盖。");
        }
    }

    /// <summary>
    /// 叠加添加工作区（不覆盖已有工作区与 ModuleOverrides）；
    /// 扫描仅发现新项目，已存在路径的功能启用/生成配置保持不变。
    /// </summary>
    public async Task AddWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        EnterModulesBusy("正在添加并扫描工作区…");
        try
        {
            path = Path.GetFullPath(path);
            var exists = Settings.WorkspaceRoots.Any(r =>
                string.Equals(Path.GetFullPath(r), path, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                Settings.WorkspaceRoots.Add(path);
                Settings.NormalizeWorkspaceRoots();
                SyncWorkspaceRootProperty();
                SaveSettingsQuiet();
                Log($"已添加工作区: {path}（共 {Settings.WorkspaceRoots.Count} 个；已有项目配置不覆盖）");
            }
            else
            {
                Log($"工作区已在列表中，重新扫描（不覆盖已有项目配置）: {path}");
            }

            await ScanModulesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitModulesBusy();
        }
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

    /// <summary>后台扫描模块；通过 <see cref="IsModulesBusy"/> 通知 UI 展示页面 loading。</summary>
    public async Task RefreshModulesAsync(
        string? busyMessage = null,
        CancellationToken cancellationToken = default)
    {
        EnterModulesBusy(busyMessage ?? "正在扫描模块…");
        try
        {
            await ScanModulesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitModulesBusy();
        }
    }

    public void RefreshModules() => RefreshModulesCore();

    private async Task ScanModulesAsync(CancellationToken cancellationToken)
    {
        await _modulesScanLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        RefreshModulesCore();
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _modulesScanLock.Release();
        }
    }

    private void EnterModulesBusy(string message)
    {
        lock (_modulesBusyGate)
        {
            _modulesBusyDepth++;
            if (!string.IsNullOrWhiteSpace(message))
            {
                _modulesBusyMessage = message.Trim();
            }
        }

        ModulesBusyChanged?.Invoke();
    }

    private void ExitModulesBusy()
    {
        lock (_modulesBusyGate)
        {
            if (_modulesBusyDepth > 0)
            {
                _modulesBusyDepth--;
            }
        }

        ModulesBusyChanged?.Invoke();
    }

    /// <summary>在模块忙状态包裹下执行工作（项目管理页用页面 loading）。</summary>
    public async Task RunModulesWorkAsync(string message, Func<Task> work)
    {
        EnterModulesBusy(message);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            ExitModulesBusy();
        }
    }

    private void RefreshModulesCore()
    {
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();

        var workspaceModules = new List<ModuleEntry>();
        var seenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Settings.WorkspaceRoots.Where(static r => !Directory.Exists(r)))
        {
            LogWarn($"工作区不存在，已跳过: {root}");
        }

        workspaceModules.AddRange(
            Settings.WorkspaceRoots
                .Where(Directory.Exists)
                .SelectMany(ModuleScanner.ScanWorkspace)
                .Where(entry => seenModules.Add(entry.ModulePath)));

        var specified = ModuleScanner.BuildSpecifiedModules(Settings.SpecifiedProjects);
        var merged = ModuleScanner.Merge(workspaceModules, specified);
        Modules = ModuleCatalogFilter.Apply(merged, Settings);
        ModulesChanged?.Invoke();
        var ignored = Modules.Count(m => m.IsIgnored);
        var wsCount = Settings.WorkspaceRoots.Count;
        var ignoreSuffix = ignored > 0 ? $"，忽略 {ignored}" : "";
        if (wsCount == 0)
        {
            Log($"已加载 {Modules.Count} 个模块（含 {specified.Count} 个指定工程{ignoreSuffix}）");
        }
        else
        {
            Log($"已扫描 {Modules.Count} 个模块（{wsCount} 个工作区，含 {specified.Count} 个指定工程{ignoreSuffix}）");
        }
    }

    private void SaveSettingsQuiet()
    {
        _store.ScheduleSave(Settings);
        UiPreferencesChanged?.Invoke();
    }

    public void SaveSettings()
    {
        _store.SaveNow(Settings);
        UiPreferencesChanged?.Invoke();
        Log("配置已保存");
    }

    /// <summary>将防抖中的配置立即落盘（应用退出前调用）。</summary>
    public void FlushSettings() => _store.Flush();

    public void SetShowConsole(bool show)
    {
        if (Settings.ShowConsole == show)
        {
            return;
        }

        Settings.ShowConsole = show;
        _store.ScheduleSave(Settings);
        UiPreferencesChanged?.Invoke();
    }

    public void ExportConfig()
    {
        _store.Flush();
        SettingsStore.ExportGlobal(Settings);
        Log("已导出配置 -> " + SettingsStore.GlobalPath);
    }

    public bool ImportConfig()
    {
        var loaded = SettingsStore.ImportGlobal();
        if (loaded is null)
        {
            LogWarn("未找到配置文件: " + SettingsStore.GlobalPath);
            return false;
        }

        Settings = loaded;
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();
        _store.SaveNow(Settings);
        UiPreferencesChanged?.Invoke();
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
            if (!TryGetFullPath(entry.ModulePath, out var path))
            {
                continue;
            }

            if (entry.IsSpecified && TryRemoveSpecifiedFromSettings(path))
            {
                settingsChanged = true;
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

        Modules = Modules.Where(m => !IsPathInSet(m.ModulePath, drop)).ToList();
        ClearSelectionIfDropped(drop);
        ModulesChanged?.Invoke();
        Log($"已从列表移除 {drop.Count} 个模块（未删除磁盘文件）");
    }

    private bool TryRemoveSpecifiedFromSettings(string path)
    {
        var before = Settings.SpecifiedProjects.Count;
        Settings.SpecifiedProjects.RemoveAll(p =>
            string.Equals(Path.GetFullPath(p.Root), path, StringComparison.OrdinalIgnoreCase));
        return Settings.SpecifiedProjects.Count < before;
    }

    private void ClearSelectionIfDropped(HashSet<string> drop)
    {
        if (SelectedModule is null || !TryGetFullPath(SelectedModule.ModulePath, out var selected))
        {
            return;
        }

        if (drop.Contains(selected))
        {
            SelectedModule = null;
        }
    }

    private static bool IsPathInSet(string modulePath, HashSet<string> drop) =>
        TryGetFullPath(modulePath, out var path) && drop.Contains(path);

    private static bool TryGetFullPath(string path, out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            fullPath = "";
            return false;
        }
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

    /// <summary>保存项目标签 / 功能启用（代码生成 / 枚举 / TS 字典 / boot-starter）。</summary>
    public void SaveModuleFeatureFlags(
        string modulePath,
        bool? enableCodegen = null,
        bool? enableEnum = null,
        bool? enableTsDict = null,
        bool? tagBootStarter = null)
    {
        ModuleCatalogFilter.SaveFeatureFlags(
            Settings,
            modulePath,
            enableCodegen,
            enableEnum,
            enableTsDict,
            tagBootStarter);
        SaveSettingsQuiet();
        // 仅重套 override，避免整库重扫 + 列表重建风暴
        Modules = ModuleCatalogFilter.Apply(Modules, Settings);
        ModulesChanged?.Invoke();
    }

    public Task SaveModuleFeatureFlagsAsync(
        string modulePath,
        bool? enableCodegen = null,
        bool? enableEnum = null,
        bool? enableTsDict = null,
        bool? tagBootStarter = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveModuleFeatureFlags(
                    modulePath,
                    enableCodegen,
                    enableEnum,
                    enableTsDict,
                    tagBootStarter);
            },
            cancellationToken);

    public void SaveModuleEnumPath(string modulePath, string? enumPackagePath)
    {
        ModuleCatalogFilter.SaveEnumPath(Settings, modulePath, enumPackagePath);
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已保存枚举路径: {modulePath}");
    }

    public Task SaveModuleEnumPathAsync(
        string modulePath,
        string? enumPackagePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveModuleEnumPath(modulePath, enumPackagePath);
            },
            cancellationToken);

    public void SaveModuleTsDictPath(string modulePath, string? tsDictPath)
    {
        ModuleCatalogFilter.SaveTsDictPath(Settings, modulePath, tsDictPath);
        SaveSettingsQuiet();
        RefreshModulesCore();
        Log($"已保存字典路径: {modulePath}");
    }

    public Task SaveModuleTsDictPathAsync(
        string modulePath,
        string? tsDictPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SaveModuleTsDictPath(modulePath, tsDictPath);
            },
            cancellationToken);

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
            LogWarn(DatabaseBusyMessage);
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
            () => DictCatalogService.ListEnumItems(Settings.DataSource, itemCode, itemName),
            items => Log($"已查询到 {items.Count} 个字典项"),
            "查询字典失败",
            Array.Empty<DictItemRecord>(),
            cancellationToken).ConfigureAwait(false);

    public Task<IReadOnlyList<DictValueRow>> ListDictValuesAsync(long itemId, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => DictCatalogService.ListValues(Settings.DataSource, itemId),
            _ => { },
            "加载字典值失败",
            Array.Empty<DictValueRow>(),
            cancellationToken);

    public Task<long> CreateDictItemAsync(DictItemRecord record, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => DictCatalogService.CreateItem(Settings.DataSource, record),
            id => Log($"已创建字典项 {id}"),
            "创建字典项失败",
            0L,
            cancellationToken);

    public Task<bool> UpdateDictItemAsync(DictItemRecord record, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                DictCatalogService.UpdateItem(Settings.DataSource, record);
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
                DictCatalogService.DeleteItem(Settings.DataSource, itemId);
                return true;
            },
            _ => Log($"已删除字典项 {itemId}"),
            "删除字典项失败",
            false,
            cancellationToken);

    public Task<long> CreateDictValueAsync(DictValueRow value, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () => DictCatalogService.CreateValue(Settings.DataSource, value),
            id => Log($"已创建字典值 {id}"),
            "创建字典值失败",
            0L,
            cancellationToken);

    public Task<bool> UpdateDictValueAsync(DictValueRow value, CancellationToken cancellationToken = default) =>
        RunDictAsync(
            () =>
            {
                DictCatalogService.UpdateValue(Settings.DataSource, value);
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
                DictCatalogService.DeleteValue(Settings.DataSource, valId);
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

    /// <summary>按各目标模块已启用的枚举/字典功能，分别生成到对应项目。</summary>
    public async Task GenerateDictForItemToModulesAsync(
        DictItemRecord item,
        IReadOnlyList<ModuleEntry> modules,
        CancellationToken cancellationToken = default)
    {
        if (modules.Count == 0)
        {
            LogWarn("请至少选择一个目标项目");
            return;
        }

        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var javaEnum = module.ProjectTypes.Java && module.EnableEnum;
            var tsDict = module.ProjectTypes.Web && module.EnableTsDict;
            if (!javaEnum && !tsDict)
            {
                LogWarn($"跳过（未启用枚举/字典）: {module.TargetModuleDisplay}");
                continue;
            }

            Log($"生成 {item.ItemCode} -> {module.TargetModuleDisplay}（enum={javaEnum}, ts={tsDict}）");
            await GenerateDictForItemAsync(item, module, javaEnum, tsDict, cancellationToken)
                .ConfigureAwait(false);
        }
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
            LogWarn(DatabaseBusyMessage);
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

    public async Task<CodegenResult> RunCodegenAsync(
        IReadOnlyList<string> tasks,
        IReadOnlyList<string> tables,
        IReadOnlyList<string>? dictCodes,
        string? modelName = null,
        CancellationToken cancellationToken = default)
    {
        if (!DatabaseGate.TryEnter())
        {
            LogWarn(DatabaseBusyMessage);
            return CodegenResult.Fail(DatabaseBusyMessage);
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

            return result;
        }
        catch (Exception ex)
        {
            LogError("生成失败: " + ex.Message);
            return CodegenResult.Fail(ex.Message);
        }
        finally
        {
            DatabaseGate.Exit();
        }
    }

    public async Task<string> TestDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var ds = Settings.DataSource;
        if (!DatabaseGate.TryEnter())
        {
            return DatabaseBusyMessage;
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var display = ds.DatabaseKind == DatabaseKind.Odbc
                    ? AdoConnectionStrings.ToOdbc(ds)
                    : JdbcUrlBuilder.Resolve(ds);
                using var conn = DbConnectionFactory.Open(ds);
                return $"数据库连接成功（{ds.DatabaseKind}）。\n{display}";
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
                return SchemaInitService.GenerateInitSql(Settings.DataSource);
            },
            cancellationToken);

    /// <summary>导出本地 SQLite 配置表 <c>devtoy_doc</c> 为 SQL（不含远程业务表）。</summary>
    public Task ExportConfigSqlAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _store.Flush();
                ConfigSqlDumpService.ExportToFile(path);
                Log("已导出本地配置 SQL -> " + path);
            },
            cancellationToken);

    /// <summary>将配置 SQL 导入本地 SQLite 并重新加载设置。</summary>
    public async Task<int> ImportConfigSqlAsync(string path, CancellationToken cancellationToken = default)
    {
        var affected = await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ConfigSqlDumpService.ImportFromFile(path);
            },
            cancellationToken).ConfigureAwait(false);

        Settings = _store.Load();
        Settings.NormalizeWorkspaceRoots();
        SyncWorkspaceRootProperty();
        UiPreferencesChanged?.Invoke();
        RefreshModulesCore();
        Log($"已导入本地配置 SQL（影响 {affected}）<- {path}");
        return affected;
    }

    /// <summary>初始化库表：已存在则抛异常；否则按数据源生成建表 SQL 并执行。</summary>
    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (!DatabaseGate.TryEnter())
        {
            throw new InvalidOperationException(DatabaseBusyMessage);
        }

        try
        {
            await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    SchemaInitService.Initialize(Settings.DataSource);
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