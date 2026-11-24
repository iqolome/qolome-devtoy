namespace Qolome.Devtoy.Core.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// 配置持久化：本地 SQLite <c>devtoy_doc</c>（应用/codegen/数据源）+
/// <c>devtoy_workspace</c> / <c>devtoy_module</c>（模块列表）；防抖单飞写入。
/// </summary>
public sealed class SettingsStore
{
    private const string KindSettings = "settings";
    private const string KindModuleOverrides = "module_overrides";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string KindModuleState = "module_state";

    private static readonly string[] ModuleJsonProperties =
    [
        "workspaceRoots",
        "workspaceRoot",
        "specifiedProjects",
        "moduleDisplayNames",
        "ignoredModulePaths",
        "moduleOverrides",
        "moduleTimes",
    ];

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

    private readonly object _pendingGate = new();

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private DevtoySettings? _pending;

    private CancellationTokenSource? _debounceCts;

    public static string LocalDbPath => DevtoyPaths.LocalDbPath;

    public static string SettingsPath => Path.Combine(DevtoyPaths.AppDataRoot, "settings.json");

    public static string ModuleOverridesPath => Path.Combine(DevtoyPaths.AppDataRoot, "module-overrides.json");

    public static string GlobalPath => Path.Combine(DevtoyPaths.GlobalRoot, "codegen-global.json");

    public DevtoySettings Load()
    {
        _ = DataSourceRuntime.Bootstrap();
        try
        {
            // 无 .dat 时先从遗留本地库/JSON 读出 DataSource 再写 .dat
            if (DataSourceConnectionStore.TryLoad() is null)
            {
                TryMigrateLegacyConnection();
            }

            _ = DataSourceRuntime.Bootstrap();

            if (DataSourceRuntime.IsRemoteMySql)
            {
                var remote = TryLoadFromRemoteMySql();
                if (remote is not null)
                {
                    remote.DataSource = DataSourceRuntime.Current;
                    return remote;
                }
            }

            if (DataSourceRuntime.IsSqliteMode || DataSourceConnectionStore.TryLoad() is null)
            {
                if (!DataSourceRuntime.IsSqliteMode && DataSourceConnectionStore.TryLoad() is null)
                {
                    // 仍无连接配置：默认 Sqlite
                    DataSourceRuntime.UseDefaultSqliteIfUnset();
                }

                EnsureLocalSchema();
                var fromDb = TryLoadSettingsFromSqlite();
                if (fromDb is not null)
                {
                    fromDb.DataSource = DataSourceRuntime.Current;
                    return fromDb;
                }
            }

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
                LegacySpecifiedMigration.TryApplyFromJson(json, settings);
                MergeEmbeddedModuleOverrides(json, settings);
                LoadModuleOverridesFile(settings);
                settings.NormalizeWorkspaceRoots();
                if (DataSourceConnectionStore.TryLoad() is null && settings.DataSource is not null)
                {
                    DataSourceRuntime.PersistCurrent(settings.DataSource);
                }
                else
                {
                    DataSourceRuntime.UseDefaultSqliteIfUnset();
                }

                settings.DataSource = DataSourceRuntime.Current;
                SaveNow(settings);
                return settings;
            }
        }
        catch
        {
            // ignored
        }

        DataSourceRuntime.UseDefaultSqliteIfUnset();
        var fresh = new DevtoySettings { DataSource = DataSourceRuntime.Current };
        if (ModuleStore.HasPersistedRows())
        {
            ModuleStore.LoadInto(fresh);
        }
        else
        {
            LoadModuleOverridesFile(fresh);
        }

        return fresh;
    }

    private static void TryMigrateLegacyConnection()
    {
        try
        {
            // 临时用默认路径打开旧库读 settings JSON 里的 DataSource
            var legacyPath = File.Exists(DevtoyPaths.EnsureMigratedDefaultSqlitePath())
                ? DevtoyPaths.DefaultSqliteDbPath
                : DevtoyPaths.LegacyLocalDbPath;
            if (!File.Exists(legacyPath) && !File.Exists(DevtoyPaths.LegacyLocalDbPath))
            {
                return;
            }

            var path = File.Exists(DevtoyPaths.DefaultSqliteDbPath)
                ? DevtoyPaths.DefaultSqliteDbPath
                : DevtoyPaths.LegacyLocalDbPath;
            using var conn = new SqliteConnection($"Data Source={path};Cache=Shared");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM devtoy_doc WHERE kind = $kind";
            cmd.Parameters.AddWithValue("$kind", KindSettings);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions);
            if (settings?.DataSource is null)
            {
                return;
            }

            DataSourceRuntime.PersistCurrent(settings.DataSource);
        }
        catch
        {
            // ignore
        }
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
        settings.NormalizeWorkspaceRoots();
        DataSourceRuntime.PersistCurrent(settings.DataSource);

        var mainJson = SerializeSettingsWithoutSecrets(settings);
        var now = DateTime.UtcNow.ToString("O");

        if (DataSourceRuntime.IsRemoteMySql)
        {
            PersistSplitRemoteMySql(settings, mainJson, now);
            return;
        }

        EnsureLocalSchema();
        using var conn = OpenLocalDb();
        using var tx = conn.BeginTransaction();
        try
        {
            UpsertDoc(conn, tx, KindSettings, mainJson, now);
            ModuleStore.SaveFrom(settings, conn, tx);
            DeleteDocKind(conn, tx, KindModuleOverrides);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static void PersistSplitRemoteMySql(DevtoySettings settings, string mainJson, string now)
    {
        using var conn = ConfigDbAccess.Open();
        ConfigDbAccess.EnsureConfigSchema(conn);
        UpsertDocDb(conn, KindSettings, mainJson, now);
        ModuleStore.SaveFromRemote(conn, settings);
        // 清理过渡期 JSON 备份，模块已落关系表
        DeleteDocKindDb(conn, KindModuleState);
    }

    private static DevtoySettings? TryLoadFromRemoteMySql()
    {
        try
        {
            using var conn = ConfigDbAccess.Open();
            ConfigDbAccess.EnsureConfigSchema(conn);
            var json = ReadDocJsonDb(conn, KindSettings);
            var settings = string.IsNullOrWhiteSpace(json)
                ? new DevtoySettings()
                : JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();

            if (ModuleStore.HasRemotePersistedRows(conn))
            {
                ModuleStore.LoadIntoRemote(conn, settings);
            }
            else
            {
                var moduleJson = ReadDocJsonDb(conn, KindModuleState);
                if (!string.IsNullOrWhiteSpace(moduleJson))
                {
                    ApplyModuleStateJson(settings, moduleJson);
                    ModuleStore.SaveFromRemote(conn, settings);
                    DeleteDocKindDb(conn, KindModuleState);
                }
            }

            settings.NormalizeWorkspaceRoots();
            settings.DataSource = DataSourceRuntime.Current;
            return settings;
        }
        catch
        {
            return null;
        }
    }

    private static string SerializeSettingsWithoutSecrets(DevtoySettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)?.AsObject()
            ?? new JsonObject();
        foreach (var prop in ModuleJsonProperties)
        {
            node.Remove(prop);
        }

        // 连接仅存加密 datasource.dat，不进 devtoy_doc
        node.Remove("dataSource");

        return node.ToJsonString(JsonOptions);
    }

    private static void DeleteDocKindDb(System.Data.Common.DbConnection conn, string kind)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM devtoy_doc WHERE kind = @kind";
        AddParam(cmd, "@kind", kind);
        cmd.ExecuteNonQuery();
    }

    private static void ApplyModuleStateJson(DevtoySettings settings, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var patch = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions);
            if (patch is null)
            {
                return;
            }

            settings.WorkspaceRoots = patch.WorkspaceRoots;
            settings.SpecifiedProjects = patch.SpecifiedProjects;
            settings.ModuleDisplayNames = patch.ModuleDisplayNames;
            settings.IgnoredModulePaths = patch.IgnoredModulePaths;
            settings.ModuleOverrides = patch.ModuleOverrides;
            settings.ModuleTimes = patch.ModuleTimes;
            settings.NormalizeWorkspaceRoots();
            _ = root;
        }
        catch
        {
            // ignore
        }
    }

    private static string SerializeSettingsWithoutModuleList(DevtoySettings settings) =>
        SerializeSettingsWithoutSecrets(settings);

    private static DevtoySettings? TryLoadSettingsFromSqlite()
    {
        if (!DataSourceRuntime.IsSqliteMode || !File.Exists(DataSourceRuntime.ResolvedSqlitePath))
        {
            return null;
        }

        var json = ReadDocJson(KindSettings);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
        LegacySpecifiedMigration.TryApplyFromJson(json, settings);
        settings.NormalizeWorkspaceRoots();

        var hadLegacyModuleJson = SettingsJsonContainsModuleList(json)
            || !string.IsNullOrWhiteSpace(ReadDocJson(KindModuleOverrides));

        if (ModuleStore.HasPersistedRows())
        {
            ModuleStore.LoadInto(settings);
            if (hadLegacyModuleJson)
            {
                // 表已有数据：清理 settings / module_overrides 中的旧模块字段
                PersistSplit(settings);
            }

            return settings;
        }

        // 尚未迁到关系表：从旧 JSON / 侧车文件灌入内存后再落表
        LoadModuleOverridesFromSqliteOrFile(settings);
        MergeDisplayNamesIntoOverrides(settings);
        PersistSplit(settings);
        return settings;
    }

    private static bool SettingsJsonContainsModuleList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var name in ModuleJsonProperties)
            {
                if (!root.TryGetProperty(name, out var prop))
                {
                    continue;
                }

                if (prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() > 0)
                {
                    return true;
                }

                if (prop.ValueKind == JsonValueKind.Object)
                {
                    using var obj = prop.EnumerateObject();
                    if (obj.MoveNext())
                    {
                        return true;
                    }
                }

                if (prop.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.GetString()))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void MergeDisplayNamesIntoOverrides(DevtoySettings settings)
    {
        foreach (var (key, name) in settings.ModuleDisplayNames)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string path;
            try
            {
                path = Path.GetFullPath(key);
            }
            catch
            {
                continue;
            }

            if (!settings.ModuleOverrides.TryGetValue(path, out var ov))
            {
                ov = new ModuleCatalogOverride();
                settings.ModuleOverrides[path] = ov;
            }

            if (string.IsNullOrWhiteSpace(ov.DisplayName))
            {
                ov.DisplayName = name.Trim();
            }
        }
    }

    private static void LoadModuleOverridesFromSqliteOrFile(DevtoySettings settings)
    {
        var json = ReadDocJson(KindModuleOverrides);
        if (!string.IsNullOrWhiteSpace(json))
        {
            MergeModuleOverridesJson(settings, json);
            return;
        }

        LoadModuleOverridesFile(settings);
    }

    private static void MergeModuleOverridesJson(DevtoySettings settings, string json)
    {
        try
        {
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

                settings.ModuleOverrides[Path.GetFullPath(key)] = value;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void MergeEmbeddedModuleOverrides(string settingsJson, DevtoySettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            if (!doc.RootElement.TryGetProperty("moduleOverrides", out var embedded)
                || embedded.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var map = JsonSerializer.Deserialize<Dictionary<string, ModuleCatalogOverride>>(
                embedded.GetRawText(),
                JsonOptions);
            if (map is null || map.Count == 0)
            {
                return;
            }

            foreach (var (key, value) in map)
            {
                if (string.IsNullOrWhiteSpace(key) || value is null)
                {
                    continue;
                }

                settings.ModuleOverrides[Path.GetFullPath(key)] = value;
            }
        }
        catch
        {
            // ignore
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
            MergeModuleOverridesJson(settings, json);
        }
        catch
        {
            // ignore
        }
    }

    private static void EnsureLocalSchema()
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        using var conn = (SqliteConnection)ConfigDbAccess.Open();
        ConfigDbAccess.EnsureConfigSchema(conn);
    }

    private static SqliteConnection OpenLocalDb()
    {
        if (ConfigDbAccess.Open() is not SqliteConnection conn)
        {
            throw new InvalidOperationException("当前数据源不是 Sqlite。");
        }

        return conn;
    }

    private static string? ReadDocJson(string kind)
    {
        if (!DataSourceRuntime.IsSqliteMode || !File.Exists(DataSourceRuntime.ResolvedSqlitePath))
        {
            return null;
        }

        try
        {
            using var conn = OpenLocalDb();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT json FROM devtoy_doc WHERE kind = $kind";
            cmd.Parameters.AddWithValue("$kind", kind);
            return cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadDocJsonDb(System.Data.Common.DbConnection conn, string kind)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT json FROM devtoy_doc WHERE kind = @kind LIMIT 1";
        var p = cmd.CreateParameter();
        p.ParameterName = "@kind";
        p.Value = kind;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() as string;
    }

    private static void UpsertDocDb(System.Data.Common.DbConnection conn, string kind, string json, string updatedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO devtoy_doc (kind, json, updated_at)
            VALUES (@kind, @json, @updated_at)
            ON DUPLICATE KEY UPDATE json = VALUES(json), updated_at = VALUES(updated_at)
            """;
        AddParam(cmd, "@kind", kind);
        AddParam(cmd, "@json", json);
        AddParam(cmd, "@updated_at", updatedAt);
        cmd.ExecuteNonQuery();
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void UpsertDoc(SqliteConnection conn, SqliteTransaction tx, string kind, string json, string updatedAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO devtoy_doc (kind, json, updated_at)
            VALUES ($kind, $json, $updated_at)
            ON CONFLICT(kind) DO UPDATE SET json = excluded.json, updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$updated_at", updatedAt);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteDocKind(SqliteConnection conn, SqliteTransaction tx, string kind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM devtoy_doc WHERE kind = $kind";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.ExecuteNonQuery();
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
