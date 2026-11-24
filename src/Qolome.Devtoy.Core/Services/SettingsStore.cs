namespace Qolome.Devtoy.Core.Services;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// 配置持久化：本地 SQLite <c>devtoy_doc</c>（settings / module_overrides）；
/// 防抖单飞写入，事务原子落盘。Export/Import 仍可用 JSON 文件。
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
        try
        {
            EnsureDocTable();
            var fromDb = TryLoadSettingsFromSqlite();
            if (fromDb is not null)
            {
                return fromDb;
            }

            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<DevtoySettings>(json, JsonOptions) ?? new DevtoySettings();
                var migrated = LegacySpecifiedMigration.TryApplyFromJson(json, settings);
                var hadEmbeddedOverrides = MergeEmbeddedModuleOverrides(json, settings);
                LoadModuleOverridesFile(settings);
                var rootsBefore = settings.WorkspaceRoots.Count;
                settings.NormalizeWorkspaceRoots();
                if (migrated
                    || hadEmbeddedOverrides
                    || (rootsBefore == 0 && settings.WorkspaceRoots.Count > 0))
                {
                    SaveNow(settings);
                }
                else
                {
                    PersistSplit(settings);
                }

                return settings;
            }
        }
        catch
        {
            // ignored
        }

        var fresh = new DevtoySettings();
        LoadModuleOverridesFromSqliteOrFile(fresh);
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
        EnsureDocTable();

        var mainJson = SerializeSettingsWithoutOverrides(settings);
        var overridesDoc = new ModuleOverridesDocument
        {
            ModuleOverrides = new Dictionary<string, ModuleCatalogOverride>(
                settings.ModuleOverrides,
                StringComparer.OrdinalIgnoreCase),
        };
        var overridesJson = JsonSerializer.Serialize(overridesDoc, JsonOptions);
        var now = DateTime.UtcNow.ToString("O");

        using var conn = OpenLocalDb();
        using var tx = conn.BeginTransaction();
        UpsertDoc(conn, tx, KindSettings, mainJson, now);
        UpsertDoc(conn, tx, KindModuleOverrides, overridesJson, now);
        tx.Commit();
    }

    private static string SerializeSettingsWithoutOverrides(DevtoySettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)?.AsObject()
            ?? new JsonObject();
        node.Remove("moduleOverrides");
        return node.ToJsonString(JsonOptions);
    }

    private static DevtoySettings? TryLoadSettingsFromSqlite()
    {
        if (!File.Exists(LocalDbPath))
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
        LoadModuleOverridesFromSqliteOrFile(settings);
        settings.NormalizeWorkspaceRoots();
        return settings;
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
            MergeModuleOverridesJson(settings, json);
        }
        catch
        {
            // ignore
        }
    }

    private static void EnsureDocTable()
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        using var conn = OpenLocalDb();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS devtoy_doc (
                kind TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection OpenLocalDb()
    {
        var conn = new SqliteConnection($"Data Source={LocalDbPath};Cache=Shared");
        conn.Open();
        return conn;
    }

    private static string? ReadDocJson(string kind)
    {
        if (!File.Exists(LocalDbPath))
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