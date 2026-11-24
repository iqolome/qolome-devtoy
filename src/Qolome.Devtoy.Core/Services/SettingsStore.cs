namespace Qolome.Devtoy.Core.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// 配置持久化：settings.json（全局）+ module-overrides.json（模块覆盖）；
/// 防抖单飞写入，临时文件 + Replace 原子落盘。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly object _pendingGate = new();

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private DevtoySettings? _pending;

    private CancellationTokenSource? _debounceCts;

    public static string SettingsPath => Path.Combine(DevtoyPaths.AppDataRoot, "settings.json");

    public static string ModuleOverridesPath => Path.Combine(DevtoyPaths.AppDataRoot, "module-overrides.json");

    public static string GlobalPath => Path.Combine(DevtoyPaths.GlobalRoot, "codegen-global.json");

    public DevtoySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
                var migrated = LegacySpecifiedMigration.TryApplyFromJson(json, settings);
                var hadEmbeddedOverrides = MergeEmbeddedModuleOverrides(json, settings);
                LoadModuleOverridesFile(settings);
                var rootsBefore = settings.WorkspaceRoots.Count;
                settings.NormalizeWorkspaceRoots();
                // 首次拆分：把内嵌 moduleOverrides 迁到侧车文件并写回干净的 settings.json
                if (migrated
                    || hadEmbeddedOverrides
                    || (rootsBefore == 0 && settings.WorkspaceRoots.Count > 0))
                {
                    SaveNow(settings);
                }

                return settings;
            }
        }
        catch
        {
            // ignored
        }

        var fresh = new DevtoySettings();
        LoadModuleOverridesFile(fresh);
        return fresh;
    }

    /// <summary>防抖保存：合并短时间内多次修改，单飞落盘。</summary>
    public void ScheduleSave(DevtoySettings settings)
    {
        CancellationToken token;
        lock (_pendingGate)
        {
            _pending = settings;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        _ = DebounceFlushAsync(token);
    }

    /// <summary>立即落盘（取消未完成的防抖）。</summary>
    public void SaveNow(DevtoySettings settings)
    {
        lock (_pendingGate)
        {
            _pending = settings;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        FlushPending();
    }

    /// <summary>兼容旧调用：立即保存。</summary>
    public void Save(DevtoySettings settings) => SaveNow(settings);

    /// <summary>将挂起的防抖写入刷盘。</summary>
    public void Flush() => FlushPending();

    public static void ExportGlobal(DevtoySettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.GlobalRoot);
        // 导出单文件仍内嵌 ModuleOverrides，便于一份带走
        AtomicWriteAllText(GlobalPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static DevtoySettings? ImportGlobal()
    {
        if (!File.Exists(GlobalPath))
        {
            return null;
        }

        var json = File.ReadAllText(GlobalPath);
        var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions);
        if (settings is not null)
        {
            LegacySpecifiedMigration.TryApplyFromJson(json, settings);
            MergeEmbeddedModuleOverrides(json, settings);
            settings.NormalizeWorkspaceRoots();
        }

        return settings;
    }

    private async Task DebounceFlushAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
            FlushPending();
        }
        catch (OperationCanceledException)
        {
            // 被更新的 ScheduleSave 取消
        }
    }

    private void FlushPending()
    {
        DevtoySettings? settings;
        lock (_pendingGate)
        {
            settings = _pending;
            _pending = null;
        }

        if (settings is null)
        {
            return;
        }

        _writeLock.Wait(CancellationToken.None);
        try
        {
            PersistSplit(settings);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void PersistSplit(DevtoySettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);

        var mainJson = SerializeSettingsWithoutOverrides(settings);
        AtomicWriteAllText(SettingsPath, mainJson);

        var overridesDoc = new ModuleOverridesDocument
        {
            ModuleOverrides = new Dictionary<string, ModuleCatalogOverride>(
                settings.ModuleOverrides,
                StringComparer.OrdinalIgnoreCase),
        };
        AtomicWriteAllText(ModuleOverridesPath, JsonSerializer.Serialize(overridesDoc, JsonOptions));
    }

    private static string SerializeSettingsWithoutOverrides(DevtoySettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)?.AsObject()
            ?? new JsonObject();
        node.Remove("moduleOverrides");
        return node.ToJsonString(JsonOptions);
    }

    /// <returns>是否从 settings.json 内嵌读取到了 overrides（需迁移拆分）。</returns>
    private static bool MergeEmbeddedModuleOverrides(string settingsJson, DevtoySettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty("moduleOverrides", out var embedded)
                || embedded.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, ModuleCatalogOverride>>(
                embedded.GetRawText(),
                JsonOptions);
            if (map is null || map.Count == 0)
            {
                return false;
            }

            foreach (var (key, value) in map)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                settings.ModuleOverrides[Path.GetFullPath(key)] = value;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LoadModuleOverridesFile(DevtoySettings settings)
    {
        if (!File.Exists(ModuleOverridesPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(ModuleOverridesPath);
            var doc = JsonSerializer.Deserialize<ModuleOverridesDocument>(json, JsonOptions);
            if (doc?.ModuleOverrides is null || doc.ModuleOverrides.Count == 0)
            {
                return;
            }

            foreach (var (key, value) in doc.ModuleOverrides)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                // 侧车文件覆盖同名内嵌项
                settings.ModuleOverrides[Path.GetFullPath(key)] = value;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void AtomicWriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private sealed class ModuleOverridesDocument
    {
        public Dictionary<string, ModuleCatalogOverride> ModuleOverrides { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
