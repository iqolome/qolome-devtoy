namespace Qolome.Devtoy.Core.Services;

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// 模块列表：<c>devtoy_workspace</c> / <c>devtoy_module</c>（Sqlite 或远程 MySQL）。
/// 内存仍经 <see cref="DevtoySettings"/> 暴露，供扫描与 UI 使用。
/// </summary>
public static class ModuleStore
{
    public const string SourceWorkspace = "workspace";
    public const string SourceSpecified = "specified";

    public static void EnsureSchema()
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        using var conn = OpenLocalDb();
        EnsureSchema(conn, tx: null);
    }

    public static void EnsureSchema(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null)
        {
            cmd.Transaction = tx;
        }

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS devtoy_workspace (
                path TEXT PRIMARY KEY NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0,
                create_time TEXT NOT NULL,
                modify_time TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();

        using var mod = conn.CreateCommand();
        if (tx is not null)
        {
            mod.Transaction = tx;
        }

        mod.CommandText =
            """
            CREATE TABLE IF NOT EXISTS devtoy_module (
                path TEXT PRIMARY KEY NOT NULL,
                workspace_path TEXT NULL,
                source TEXT NOT NULL,
                display_name TEXT NULL,
                ignored INTEGER NOT NULL DEFAULT 0,
                java INTEGER NULL,
                web INTEGER NULL,
                flutter INTEGER NULL,
                version TEXT NULL,
                manual INTEGER NULL,
                codegen_model_name TEXT NULL,
                codegen_dao INTEGER NULL,
                codegen_service INTEGER NULL,
                codegen_controller INTEGER NULL,
                enable_codegen INTEGER NULL,
                enable_enum INTEGER NULL,
                enable_ts_dict INTEGER NULL,
                tag_boot_starter INTEGER NULL,
                enum_package_path TEXT NULL,
                ts_dict_path TEXT NULL,
                create_time TEXT NOT NULL,
                modify_time TEXT NOT NULL
            );
            """;
        mod.ExecuteNonQuery();
        MigrateSqliteCatalogTimeColumns(conn, tx);
    }

    /// <summary>表中是否已有工作区或模块行（用于判断是否已迁移）。</summary>
    public static bool HasPersistedRows()
    {
        if (!DataSourceRuntime.IsSqliteMode)
        {
            return false;
        }

        if (!File.Exists(DataSourceRuntime.ResolvedSqlitePath))
        {
            return false;
        }

        try
        {
            EnsureSchema();
            using var conn = OpenLocalDb();
            return CountRows(conn, DevtoyTableNames.Workspace) > 0
                || CountRows(conn, DevtoyTableNames.Module) > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void LoadInto(DevtoySettings settings)
    {
        if (!File.Exists(DevtoyPaths.LocalDbPath))
        {
            return;
        }

        EnsureSchema();
        using var conn = OpenLocalDb();
        LoadInto(settings, conn, tx: null);
    }

    public static void LoadInto(DevtoySettings settings, SqliteConnection conn, SqliteTransaction? tx)
    {
        EnsureSchema(conn, tx);
        ClearModuleCollections(settings);

        using (var wsCmd = conn.CreateCommand())
        {
            if (tx is not null)
            {
                wsCmd.Transaction = tx;
            }

            wsCmd.CommandText =
                """
                SELECT path FROM devtoy_workspace
                ORDER BY sort_order ASC, path COLLATE NOCASE ASC
                """;
            using var reader = wsCmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    settings.WorkspaceRoots.Add(Path.GetFullPath(path));
                }
                catch
                {
                    settings.WorkspaceRoots.Add(path.Trim());
                }
            }
        }

        settings.NormalizeWorkspaceRoots();

        using var modCmd = conn.CreateCommand();
        if (tx is not null)
        {
            modCmd.Transaction = tx;
        }

        modCmd.CommandText =
            """
            SELECT path, workspace_path, source, display_name, ignored,
                   java, web, flutter, version, manual,
                   codegen_model_name, codegen_dao, codegen_service, codegen_controller,
                   enable_codegen, enable_enum, enable_ts_dict, tag_boot_starter,
                   enum_package_path, ts_dict_path,
                   create_time, modify_time
            FROM devtoy_module
            ORDER BY path COLLATE NOCASE ASC
            """;
        using var modReader = modCmd.ExecuteReader();
        while (modReader.Read())
        {
            ApplyModuleRow(settings, modReader);
        }
    }

    public static void SaveFrom(DevtoySettings settings)
    {
        Directory.CreateDirectory(DevtoyPaths.AppDataRoot);
        using var conn = OpenLocalDb();
        using var tx = conn.BeginTransaction();
        try
        {
            SaveFrom(settings, conn, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public static void SaveFrom(DevtoySettings settings, SqliteConnection conn, SqliteTransaction tx)
    {
        EnsureSchema(conn, tx);
        settings.NormalizeWorkspaceRoots();

        var now = DateTime.UtcNow.ToString("O");
        var existingWsTimes = LoadExistingWorkspaceTimes(conn, tx);
        using (var clearWs = conn.CreateCommand())
        {
            clearWs.Transaction = tx;
            clearWs.CommandText = "DELETE FROM devtoy_workspace";
            clearWs.ExecuteNonQuery();
        }

        for (var i = 0; i < settings.WorkspaceRoots.Count; i++)
        {
            var path = settings.WorkspaceRoots[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            var create = existingWsTimes.TryGetValue(full, out var times) && !string.IsNullOrWhiteSpace(times.Create)
                ? times.Create
                : now;
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                """
                INSERT INTO devtoy_workspace (path, sort_order, create_time, modify_time)
                VALUES ($path, $sort_order, $create_time, $modify_time)
                """;
            insert.Parameters.AddWithValue("$path", full);
            insert.Parameters.AddWithValue("$sort_order", i);
            insert.Parameters.AddWithValue("$create_time", create);
            insert.Parameters.AddWithValue("$modify_time", now);
            insert.ExecuteNonQuery();
        }

        var existingTimes = LoadExistingTimes(conn, tx);
        var rows = BuildRows(settings);
        using (var clearMod = conn.CreateCommand())
        {
            clearMod.Transaction = tx;
            clearMod.CommandText = "DELETE FROM devtoy_module";
            clearMod.ExecuteNonQuery();
        }

        foreach (var row in rows.Values.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            ApplyPersistTimes(row, settings, existingTimes, now);
            InsertModuleRow(conn, tx, row, now);
        }
    }

    /// <summary>DDL 片段，供配置 SQL 导出使用。</summary>
    public static string SchemaDdl =>
        """
        CREATE TABLE IF NOT EXISTS `devtoy_workspace` (
            `path` TEXT PRIMARY KEY NOT NULL,
            `sort_order` INTEGER NOT NULL DEFAULT 0,
            `create_time` TEXT NOT NULL,
            `modify_time` TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS `devtoy_module` (
            `path` TEXT PRIMARY KEY NOT NULL,
            `workspace_path` TEXT NULL,
            `source` TEXT NOT NULL,
            `display_name` TEXT NULL,
            `ignored` INTEGER NOT NULL DEFAULT 0,
            `java` INTEGER NULL,
            `web` INTEGER NULL,
            `flutter` INTEGER NULL,
            `version` TEXT NULL,
            `manual` INTEGER NULL,
            `codegen_model_name` TEXT NULL,
            `codegen_dao` INTEGER NULL,
            `codegen_service` INTEGER NULL,
            `codegen_controller` INTEGER NULL,
            `enable_codegen` INTEGER NULL,
            `enable_enum` INTEGER NULL,
            `enable_ts_dict` INTEGER NULL,
            `tag_boot_starter` INTEGER NULL,
            `enum_package_path` TEXT NULL,
            `ts_dict_path` TEXT NULL,
            `create_time` TEXT NOT NULL,
            `modify_time` TEXT NOT NULL
        );
        """;

    /// <summary>远程 MySQL：是否已有 workspace/module 行。</summary>
    public static bool HasRemotePersistedRows(DbConnection conn)
    {
        try
        {
            return CountRowsDb(conn, DevtoyTableNames.Workspace) > 0
                || CountRowsDb(conn, DevtoyTableNames.Module) > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>远程 MySQL：从关系表灌入设置。</summary>
    public static void LoadIntoRemote(DbConnection conn, DevtoySettings settings)
    {
        ClearModuleCollections(settings);

        using (var wsCmd = conn.CreateCommand())
        {
            wsCmd.CommandText =
                """
                SELECT `path` FROM `devtoy_workspace`
                ORDER BY `sort_order` ASC, `path` ASC
                """;
            using var reader = wsCmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                try
                {
                    settings.WorkspaceRoots.Add(Path.GetFullPath(path));
                }
                catch
                {
                    settings.WorkspaceRoots.Add(path.Trim());
                }
            }
        }

        settings.NormalizeWorkspaceRoots();

        using var modCmd = conn.CreateCommand();
        modCmd.CommandText =
            """
            SELECT `path`, `workspace_path`, `source`, `display_name`, `ignored`,
                   `java`, `web`, `flutter`, `version`, `manual`,
                   `codegen_model_name`, `codegen_dao`, `codegen_service`, `codegen_controller`,
                   `enable_codegen`, `enable_enum`, `enable_ts_dict`, `tag_boot_starter`,
                   `enum_package_path`, `ts_dict_path`,
                   `create_time`, `modify_time`
            FROM `devtoy_module`
            ORDER BY `path` ASC
            """;
        using var modReader = modCmd.ExecuteReader();
        while (modReader.Read())
        {
            ApplyModuleRow(settings, modReader);
        }
    }

    /// <summary>远程 MySQL：将内存模块列表写入关系表。返回写入的模块行数。</summary>
    public static int SaveFromRemote(DbConnection conn, DevtoySettings settings)
    {
        settings.NormalizeWorkspaceRoots();
        var now = DateTime.UtcNow.ToString("O");
        var existingWsTimes = LoadExistingWorkspaceTimesDb(conn);
        var existingTimes = LoadExistingTimesDb(conn);

        using (var clearWs = conn.CreateCommand())
        {
            clearWs.CommandText = "DELETE FROM `devtoy_workspace`";
            clearWs.ExecuteNonQuery();
        }

        for (var i = 0; i < settings.WorkspaceRoots.Count; i++)
        {
            var path = settings.WorkspaceRoots[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var full = Path.GetFullPath(path);
            var create = existingWsTimes.TryGetValue(full, out var times) && !string.IsNullOrWhiteSpace(times.Create)
                ? times.Create
                : now;
            using var insert = conn.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO `devtoy_workspace` (`path`, `sort_order`, `create_time`, `modify_time`)
                VALUES (@path, @sort_order, @create_time, @modify_time)
                """;
            AddDbParam(insert, "@path", full);
            AddDbParam(insert, "@sort_order", i);
            AddDbParam(insert, "@create_time", create);
            AddDbParam(insert, "@modify_time", now);
            insert.ExecuteNonQuery();
        }

        var rows = BuildRows(settings);
        // 内存尚未有可落库模块时，禁止 DELETE 清空远程已有行
        if (rows.Count == 0)
        {
            return 0;
        }

        using (var clearMod = conn.CreateCommand())
        {
            clearMod.CommandText = "DELETE FROM `devtoy_module`";
            clearMod.ExecuteNonQuery();
        }

        var written = 0;
        foreach (var row in rows.Values.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            ApplyPersistTimes(row, settings, existingTimes, now);
            InsertModuleRowDb(conn, row, now);
            written++;
        }

        return written;
    }

    /// <summary>远程模块表行数（失败视为 0）。</summary>
    public static long CountRemoteModules(DbConnection conn)
    {
        try
        {
            return CountRowsDb(conn, DevtoyTableNames.Module);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 从默认本机 SQLite 合并模块元数据到 settings（不整表清空已有工作区）。
    /// 用于首次切到 MySQL 时补齐 overrides / times。
    /// </summary>
    public static bool MergeCatalogFromDefaultLocalSqlite(DevtoySettings settings)
    {
        var localPath = DevtoyPaths.EnsureMigratedDefaultSqlitePath();
        if (!File.Exists(localPath))
        {
            return false;
        }

        try
        {
            using var conn = new SqliteConnection($"Data Source={localPath};Cache=Shared;Mode=ReadOnly");
            conn.Open();
            var temp = new DevtoySettings();
            LoadInto(temp, conn, tx: null);
            var merged = false;
            if (settings.WorkspaceRoots.Count == 0 && temp.WorkspaceRoots.Count > 0)
            {
                settings.WorkspaceRoots = temp.WorkspaceRoots;
                settings.NormalizeWorkspaceRoots();
                merged = true;
            }

            foreach (var spec in temp.SpecifiedProjects)
            {
                if (settings.SpecifiedProjects.Any(s =>
                        string.Equals(s.Root, spec.Root, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                settings.SpecifiedProjects.Add(spec);
                merged = true;
            }

            foreach (var path in temp.IgnoredModulePaths)
            {
                if (settings.IgnoredModulePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                settings.IgnoredModulePaths.Add(path);
                merged = true;
            }

            foreach (var (key, value) in temp.ModuleDisplayNames)
            {
                if (settings.ModuleDisplayNames.ContainsKey(key))
                {
                    continue;
                }

                settings.ModuleDisplayNames[key] = value;
                merged = true;
            }

            foreach (var (key, value) in temp.ModuleOverrides)
            {
                if (settings.ModuleOverrides.ContainsKey(key))
                {
                    continue;
                }

                settings.ModuleOverrides[key] = value;
                merged = true;
            }

            foreach (var (key, value) in temp.ModuleTimes)
            {
                if (settings.ModuleTimes.ContainsKey(key))
                {
                    continue;
                }

                settings.ModuleTimes[key] = value;
                merged = true;
            }

            return merged;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>工作区已配置但 ModuleTimes 为空时，扫描并灌入创建/修改时间。</summary>
    public static int SeedModuleTimesFromWorkspaceScan(DevtoySettings settings)
    {
        if (settings.ModuleTimes.Count > 0)
        {
            return 0;
        }

        settings.NormalizeWorkspaceRoots();
        var scanned = settings.WorkspaceRoots
            .Where(static r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .SelectMany(ModuleScanner.ScanWorkspace)
            .ToList();
        if (scanned.Count == 0 && settings.SpecifiedProjects.Count == 0)
        {
            return 0;
        }

        var specified = ModuleScanner.BuildSpecifiedModules(settings.SpecifiedProjects);
        var merged = ModuleScanner.Merge(scanned, specified);
        _ = ModuleCatalogFilter.Apply(merged, settings);
        return settings.ModuleTimes.Count;
    }

    private static void ClearModuleCollections(DevtoySettings settings)
    {
        settings.WorkspaceRoots.Clear();
        settings.WorkspaceRoot = "";
        settings.SpecifiedProjects.Clear();
        settings.IgnoredModulePaths.Clear();
        settings.ModuleDisplayNames.Clear();
        settings.ModuleOverrides.Clear();
        settings.ModuleTimes.Clear();
    }

    private static void ApplyModuleRow(DevtoySettings settings, DbDataReader reader)
    {
        var path = NormalizeStoredPath(reader.IsDBNull(0) ? "" : reader.GetString(0));
        if (path.Length == 0)
        {
            return;
        }

        var source = reader.IsDBNull(2) ? SourceWorkspace : reader.GetString(2);
        var displayName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var isSpecified = string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase);

        if (isSpecified)
        {
            AddSpecifiedFromReader(settings, path, reader);
        }

        if (!reader.IsDBNull(4) && ReadInt64(reader, 4) != 0)
        {
            settings.IgnoredModulePaths.Add(path);
        }

        ApplyOverrideFromReader(settings, path, displayName, isSpecified, reader);
        ApplyTimesFromReader(settings, path, reader);
    }

    private static void AddSpecifiedFromReader(DevtoySettings settings, string path, DbDataReader reader)
    {
        settings.SpecifiedProjects.Add(new SpecifiedProjectEntry
        {
            Root = path,
            Java = ReadBool(reader, 5) == true,
            Web = ReadBool(reader, 6) == true,
            Flutter = ReadBool(reader, 7) == true,
            Version = ReadOptionalString(reader, 8),
            Manual = ReadBool(reader, 9) == true,
        });
    }

    private static void ApplyOverrideFromReader(
        DevtoySettings settings,
        string path,
        string? displayName,
        bool isSpecified,
        DbDataReader reader)
    {
        var ov = new ModuleCatalogOverride
        {
            DisplayName = NullIfWhite(displayName),
            Version = isSpecified ? null : NullIfWhite(ReadOptionalString(reader, 8)),
            Java = isSpecified ? null : ReadBool(reader, 5),
            Web = isSpecified ? null : ReadBool(reader, 6),
            Flutter = isSpecified ? null : ReadBool(reader, 7),
            CodegenModelName = NullIfWhite(ReadOptionalString(reader, 10)),
            CodegenDao = ReadBool(reader, 11),
            CodegenService = ReadBool(reader, 12),
            CodegenController = ReadBool(reader, 13),
            EnableCodegen = ReadBool(reader, 14),
            EnableEnum = ReadBool(reader, 15),
            EnableTsDict = ReadBool(reader, 16),
            TagBootStarter = ReadBool(reader, 17),
            EnumPackagePath = NullIfWhite(ReadOptionalString(reader, 18)),
            TsDictPath = NullIfWhite(ReadOptionalString(reader, 19)),
        };

        if (!IsOverrideEmpty(ov))
        {
            settings.ModuleOverrides[path] = ov;
            if (!string.IsNullOrWhiteSpace(ov.DisplayName))
            {
                settings.ModuleDisplayNames[path] = ov.DisplayName;
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var trimmed = displayName.Trim();
        settings.ModuleDisplayNames[path] = trimmed;
        settings.ModuleOverrides[path] = new ModuleCatalogOverride { DisplayName = trimmed };
    }

    private static void ApplyTimesFromReader(DevtoySettings settings, string path, DbDataReader reader)
    {
        var create = ParseTimestamp(ReadOptionalString(reader, 20));
        var modify = ParseTimestamp(ReadOptionalString(reader, 21)) ?? create;
        if (create is null && modify is null)
        {
            var now = DateTime.Now;
            create = now;
            modify = now;
        }

        settings.ModuleTimes[path] = new ModulePersistTimes
        {
            CreateTime = create,
            ModifyTime = modify ?? create,
        };
    }

    private static Dictionary<string, ModuleRow> BuildRows(DevtoySettings settings)
    {
        var rows = new Dictionary<string, ModuleRow>(StringComparer.OrdinalIgnoreCase);
        MergeSpecifiedRows(settings, rows);
        MergeIgnoredRows(settings, rows);
        MergeDisplayNameRows(settings, rows);
        MergeOverrideRows(settings, rows);
        // 扫描得到的模块时间也要落库，否则重启后全部变成「刚才」
        MergeModuleTimesRows(settings, rows);
        return rows
            .Where(kv => IsPersistableRow(kv.Value) || settings.ModuleTimes.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void MergeModuleTimesRows(DevtoySettings settings, Dictionary<string, ModuleRow> rows)
    {
        foreach (var path in settings.ModuleTimes.Keys)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            GetOrAddRow(rows, path);
        }
    }

    /// <summary>优先内存 ModuleTimes，其次库中已有值；避免每次保存把 modify_time 刷成 now。</summary>
    private static void ApplyPersistTimes(
        ModuleRow row,
        DevtoySettings settings,
        IReadOnlyDictionary<string, (string Create, string Modify)> existing,
        string now)
    {
        string? create = null;
        string? modify = null;
        if (settings.ModuleTimes.TryGetValue(row.Path, out var meta))
        {
            if (meta.CreateTime is not null)
            {
                create = meta.CreateTime.Value.ToUniversalTime().ToString("O");
            }

            if (meta.ModifyTime is not null)
            {
                modify = meta.ModifyTime.Value.ToUniversalTime().ToString("O");
            }
        }

        if (existing.TryGetValue(row.Path, out var stored))
        {
            create ??= stored.Create;
            modify ??= stored.Modify;
        }

        row.CreateTime = string.IsNullOrWhiteSpace(create) ? now : create;
        row.ModifyTime = string.IsNullOrWhiteSpace(modify) ? now : modify;
    }

    private static ModuleRow GetOrAddRow(Dictionary<string, ModuleRow> rows, string path)
    {
        var full = Path.GetFullPath(path);
        if (!rows.TryGetValue(full, out var row))
        {
            row = new ModuleRow { Path = full, Source = SourceWorkspace };
            rows[full] = row;
        }

        return row;
    }

    private static void MergeSpecifiedRows(DevtoySettings settings, Dictionary<string, ModuleRow> rows)
    {
        foreach (var spec in settings.SpecifiedProjects)
        {
            if (string.IsNullOrWhiteSpace(spec.Root))
            {
                continue;
            }

            var row = GetOrAddRow(rows, spec.Root);
            row.Source = SourceSpecified;
            row.Java = spec.Java;
            row.Web = spec.Web || spec.JavaScript;
            row.Flutter = spec.Flutter;
            row.Version = NullIfWhite(spec.Version);
            row.Manual = spec.Manual;
        }
    }

    private static void MergeIgnoredRows(DevtoySettings settings, Dictionary<string, ModuleRow> rows)
    {
        foreach (var ignored in settings.IgnoredModulePaths)
        {
            if (string.IsNullOrWhiteSpace(ignored))
            {
                continue;
            }

            GetOrAddRow(rows, ignored).Ignored = true;
        }
    }

    private static void MergeDisplayNameRows(DevtoySettings settings, Dictionary<string, ModuleRow> rows)
    {
        foreach (var (key, name) in settings.ModuleDisplayNames)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            GetOrAddRow(rows, key).DisplayName = name.Trim();
        }
    }

    private static void MergeOverrideRows(DevtoySettings settings, Dictionary<string, ModuleRow> rows)
    {
        foreach (var (key, ov) in settings.ModuleOverrides)
        {
            if (string.IsNullOrWhiteSpace(key) || ov is null || IsOverrideEmpty(ov))
            {
                continue;
            }

            var row = GetOrAddRow(rows, key);
            ApplyOverrideToRow(row, ov);
        }
    }

    private static void ApplyOverrideToRow(ModuleRow row, ModuleCatalogOverride ov)
    {
        if (!string.IsNullOrWhiteSpace(ov.DisplayName))
        {
            row.DisplayName = ov.DisplayName.Trim();
        }

        if (row.Source != SourceSpecified)
        {
            if (!string.IsNullOrWhiteSpace(ov.Version))
            {
                row.Version = ov.Version.Trim();
            }

            row.Java = ov.Java;
            row.Web = ResolveOverrideWeb(ov);
            row.Flutter = ov.Flutter;
        }

        row.CodegenModelName = NullIfWhite(ov.CodegenModelName);
        row.CodegenDao = ov.CodegenDao;
        row.CodegenService = ov.CodegenService;
        row.CodegenController = ov.CodegenController;
        row.EnableCodegen = ov.EnableCodegen;
        row.EnableEnum = ov.EnableEnum;
        row.EnableTsDict = ov.EnableTsDict;
        row.TagBootStarter = ov.TagBootStarter;
        row.EnumPackagePath = NullIfWhite(ov.EnumPackagePath);
        row.TsDictPath = NullIfWhite(ov.TsDictPath);
    }

    private static bool? ResolveOverrideWeb(ModuleCatalogOverride ov)
    {
        if (ov.Web == true || ov.JavaScript == true)
        {
            return true;
        }

        return ov.Web;
    }

    private static bool IsPersistableRow(ModuleRow row) =>
        row.Source == SourceSpecified
        || row.Ignored
        || !string.IsNullOrWhiteSpace(row.DisplayName)
        || row.HasOverrideFields;

    private static void InsertModuleRow(SqliteConnection conn, SqliteTransaction tx, ModuleRow row, string now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var create = string.IsNullOrWhiteSpace(row.CreateTime) ? now : row.CreateTime;
        var modify = string.IsNullOrWhiteSpace(row.ModifyTime) ? now : row.ModifyTime;
        cmd.CommandText =
            """
            INSERT INTO devtoy_module (
                path, workspace_path, source, display_name, ignored,
                java, web, flutter, version, manual,
                codegen_model_name, codegen_dao, codegen_service, codegen_controller,
                enable_codegen, enable_enum, enable_ts_dict, tag_boot_starter,
                enum_package_path, ts_dict_path, create_time, modify_time
            ) VALUES (
                $path, $workspace_path, $source, $display_name, $ignored,
                $java, $web, $flutter, $version, $manual,
                $codegen_model_name, $codegen_dao, $codegen_service, $codegen_controller,
                $enable_codegen, $enable_enum, $enable_ts_dict, $tag_boot_starter,
                $enum_package_path, $ts_dict_path, $create_time, $modify_time
            )
            """;
        cmd.Parameters.AddWithValue("$path", row.Path);
        cmd.Parameters.AddWithValue("$workspace_path", GuessWorkspacePath(row.Path) ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$source", row.Source);
        cmd.Parameters.AddWithValue("$display_name", (object?)NullIfWhite(row.DisplayName) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ignored", row.Ignored ? 1 : 0);
        AddNullableBool(cmd, "$java", row.Java);
        AddNullableBool(cmd, "$web", row.Web);
        AddNullableBool(cmd, "$flutter", row.Flutter);
        cmd.Parameters.AddWithValue("$version", (object?)NullIfWhite(row.Version) ?? DBNull.Value);
        AddNullableBool(cmd, "$manual", row.Manual);
        cmd.Parameters.AddWithValue("$codegen_model_name", (object?)NullIfWhite(row.CodegenModelName) ?? DBNull.Value);
        AddNullableBool(cmd, "$codegen_dao", row.CodegenDao);
        AddNullableBool(cmd, "$codegen_service", row.CodegenService);
        AddNullableBool(cmd, "$codegen_controller", row.CodegenController);
        AddNullableBool(cmd, "$enable_codegen", row.EnableCodegen);
        AddNullableBool(cmd, "$enable_enum", row.EnableEnum);
        AddNullableBool(cmd, "$enable_ts_dict", row.EnableTsDict);
        AddNullableBool(cmd, "$tag_boot_starter", row.TagBootStarter);
        cmd.Parameters.AddWithValue("$enum_package_path", (object?)NullIfWhite(row.EnumPackagePath) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ts_dict_path", (object?)NullIfWhite(row.TsDictPath) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$create_time", create);
        cmd.Parameters.AddWithValue("$modify_time", modify);
        cmd.ExecuteNonQuery();
    }

    private static string? GuessWorkspacePath(string modulePath)
    {
        try
        {
            var full = Path.GetFullPath(modulePath);
            var parent = Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(parent) ? null : parent;
        }
        catch
        {
            return null;
        }
    }

    private static void AddNullableBool(SqliteCommand cmd, string name, bool? value)
    {
        object dbValue = DBNull.Value;
        if (value is not null)
        {
            dbValue = value.Value ? 1 : 0;
        }

        cmd.Parameters.AddWithValue(name, dbValue);
    }

    private static void InsertModuleRowDb(DbConnection conn, ModuleRow row, string now)
    {
        using var cmd = conn.CreateCommand();
        var create = string.IsNullOrWhiteSpace(row.CreateTime) ? now : row.CreateTime;
        var modify = string.IsNullOrWhiteSpace(row.ModifyTime) ? now : row.ModifyTime;
        cmd.CommandText =
            """
            INSERT INTO `devtoy_module` (
                `path`, `workspace_path`, `source`, `display_name`, `ignored`,
                `java`, `web`, `flutter`, `version`, `manual`,
                `codegen_model_name`, `codegen_dao`, `codegen_service`, `codegen_controller`,
                `enable_codegen`, `enable_enum`, `enable_ts_dict`, `tag_boot_starter`,
                `enum_package_path`, `ts_dict_path`, `create_time`, `modify_time`
            ) VALUES (
                @path, @workspace_path, @source, @display_name, @ignored,
                @java, @web, @flutter, @version, @manual,
                @codegen_model_name, @codegen_dao, @codegen_service, @codegen_controller,
                @enable_codegen, @enable_enum, @enable_ts_dict, @tag_boot_starter,
                @enum_package_path, @ts_dict_path, @create_time, @modify_time
            )
            """;
        AddDbParam(cmd, "@path", row.Path);
        AddDbParam(cmd, "@workspace_path", GuessWorkspacePath(row.Path) ?? (object)DBNull.Value);
        AddDbParam(cmd, "@source", row.Source);
        AddDbParam(cmd, "@display_name", (object?)NullIfWhite(row.DisplayName) ?? DBNull.Value);
        AddDbParam(cmd, "@ignored", row.Ignored ? 1 : 0);
        AddNullableBoolDb(cmd, "@java", row.Java);
        AddNullableBoolDb(cmd, "@web", row.Web);
        AddNullableBoolDb(cmd, "@flutter", row.Flutter);
        AddDbParam(cmd, "@version", (object?)NullIfWhite(row.Version) ?? DBNull.Value);
        AddNullableBoolDb(cmd, "@manual", row.Manual);
        AddDbParam(cmd, "@codegen_model_name", (object?)NullIfWhite(row.CodegenModelName) ?? DBNull.Value);
        AddNullableBoolDb(cmd, "@codegen_dao", row.CodegenDao);
        AddNullableBoolDb(cmd, "@codegen_service", row.CodegenService);
        AddNullableBoolDb(cmd, "@codegen_controller", row.CodegenController);
        AddNullableBoolDb(cmd, "@enable_codegen", row.EnableCodegen);
        AddNullableBoolDb(cmd, "@enable_enum", row.EnableEnum);
        AddNullableBoolDb(cmd, "@enable_ts_dict", row.EnableTsDict);
        AddNullableBoolDb(cmd, "@tag_boot_starter", row.TagBootStarter);
        AddDbParam(cmd, "@enum_package_path", (object?)NullIfWhite(row.EnumPackagePath) ?? DBNull.Value);
        AddDbParam(cmd, "@ts_dict_path", (object?)NullIfWhite(row.TsDictPath) ?? DBNull.Value);
        AddDbParam(cmd, "@create_time", create);
        AddDbParam(cmd, "@modify_time", modify);
        cmd.ExecuteNonQuery();
    }

    private static void AddDbParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddNullableBoolDb(DbCommand cmd, string name, bool? value)
    {
        object dbValue = DBNull.Value;
        if (value is not null)
        {
            dbValue = value.Value ? 1 : 0;
        }

        AddDbParam(cmd, name, dbValue);
    }

    private static long CountRowsDb(DbConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = table switch
        {
            DevtoyTableNames.Workspace => "SELECT COUNT(*) FROM `devtoy_workspace`",
            DevtoyTableNames.Module => "SELECT COUNT(*) FROM `devtoy_module`",
            DevtoyTableNames.Doc => "SELECT COUNT(*) FROM `devtoy_doc`",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "未知本机配置表。"),
        };
        var result = cmd.ExecuteScalar();
        return result is long n ? n : Convert.ToInt64(result);
    }

    private static Dictionary<string, (string Create, string Modify)> LoadExistingTimesDb(DbConnection conn)
    {
        var map = new Dictionary<string, (string Create, string Modify)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT `path`,
                   COALESCE(NULLIF(`create_time`, ''), NULLIF(`modify_time`, '')) AS created,
                   COALESCE(NULLIF(`modify_time`, ''), NULLIF(`create_time`, '')) AS modified
            FROM `devtoy_module`
            """;
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var created = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var modified = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string key;
                try
                {
                    key = Path.GetFullPath(path);
                }
                catch
                {
                    key = path.Trim();
                }

                map[key] = (created, string.IsNullOrWhiteSpace(modified) ? created : modified);
            }
        }
        catch
        {
            // 表尚未含时间列
        }

        return map;
    }

    private static bool? ReadBool(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return ReadInt64(reader, ordinal) != 0;
    }

    private static long ReadInt64(DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            bool b => b ? 1 : 0,
            _ => Convert.ToInt64(value),
        };
    }

    private static string? ReadOptionalString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string NormalizeStoredPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(rawPath);
        }
        catch
        {
            return rawPath.Trim();
        }
    }

    private static string? NullIfWhite(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsOverrideEmpty(ModuleCatalogOverride ov) =>
        string.IsNullOrWhiteSpace(ov.DisplayName)
        && string.IsNullOrWhiteSpace(ov.Version)
        && ov.Java is null
        && ov.Web is null
        && ov.Flutter is null
        && ov.JavaScript is null
        && string.IsNullOrWhiteSpace(ov.CodegenModelName)
        && ov.CodegenDao is null
        && ov.CodegenService is null
        && ov.CodegenController is null
        && ov.EnableCodegen is not true
        && ov.EnableEnum is not true
        && ov.EnableTsDict is not true
        && ov.TagBootStarter is null
        && string.IsNullOrWhiteSpace(ov.EnumPackagePath)
        && string.IsNullOrWhiteSpace(ov.TsDictPath);

    private static long CountRows(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = table switch
        {
            DevtoyTableNames.Workspace => "SELECT COUNT(*) FROM devtoy_workspace",
            DevtoyTableNames.Module => "SELECT COUNT(*) FROM devtoy_module",
            DevtoyTableNames.Doc => "SELECT COUNT(*) FROM devtoy_doc",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "未知本机配置表。"),
        };
        var result = cmd.ExecuteScalar();
        return result is long n ? n : Convert.ToInt64(result);
    }

    private static SqliteConnection OpenLocalDb()
    {
        if (!DataSourceRuntime.IsSqliteMode)
        {
            throw new InvalidOperationException("ModuleStore Sqlite API 仅在 Sqlite 数据源下可用。");
        }

        var path = DataSourceRuntime.ResolvedSqlitePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var conn = new SqliteConnection($"Data Source={path};Cache=Shared");
        conn.Open();
        return conn;
    }

    private sealed class ModuleRow
    {
        public required string Path { get; init; }

        public string Source { get; set; } = SourceWorkspace;

        public string? DisplayName { get; set; }

        public bool Ignored { get; set; }

        public bool? Java { get; set; }

        public bool? Web { get; set; }

        public bool? Flutter { get; set; }

        public string? Version { get; set; }

        public bool? Manual { get; set; }

        public string? CodegenModelName { get; set; }

        public bool? CodegenDao { get; set; }

        public bool? CodegenService { get; set; }

        public bool? CodegenController { get; set; }

        public bool? EnableCodegen { get; set; }

        public bool? EnableEnum { get; set; }

        public bool? EnableTsDict { get; set; }

        public bool? TagBootStarter { get; set; }

        public string? EnumPackagePath { get; set; }

        public string? TsDictPath { get; set; }

        public string? CreateTime { get; set; }

        public string? ModifyTime { get; set; }

        public bool HasOverrideFields =>
            !string.IsNullOrWhiteSpace(CodegenModelName)
            || CodegenDao is not null
            || CodegenService is not null
            || CodegenController is not null
            || EnableCodegen is true
            || EnableEnum is true
            || EnableTsDict is true
            || TagBootStarter is not null
            || !string.IsNullOrWhiteSpace(EnumPackagePath)
            || !string.IsNullOrWhiteSpace(TsDictPath)
            || (Source != SourceSpecified && (Java is not null || Web is not null || Flutter is not null || !string.IsNullOrWhiteSpace(Version)));
    }

    /// <summary>Sqlite：workspace 补 create_time / 将 updated_at 迁为 modify_time；module 去掉重复的 updated_at。</summary>
    private static void MigrateSqliteCatalogTimeColumns(SqliteConnection conn, SqliteTransaction? tx)
    {
        EnsureColumn(conn, tx, DevtoyTableNames.Workspace, "create_time", "TEXT");
        EnsureColumn(conn, tx, DevtoyTableNames.Workspace, "modify_time", "TEXT");
        EnsureColumn(conn, tx, DevtoyTableNames.Module, "create_time", "TEXT");
        EnsureColumn(conn, tx, DevtoyTableNames.Module, "modify_time", "TEXT");

        TryExec(conn, tx,
            """
            UPDATE devtoy_workspace
            SET create_time = COALESCE(NULLIF(create_time, ''), NULLIF(modify_time, ''), NULLIF(updated_at, ''), create_time),
                modify_time = COALESCE(NULLIF(modify_time, ''), NULLIF(updated_at, ''), NULLIF(create_time, ''), modify_time)
            """);
        TryExec(conn, tx,
            """
            UPDATE devtoy_module
            SET create_time = COALESCE(NULLIF(create_time, ''), NULLIF(modify_time, ''), NULLIF(updated_at, ''), create_time),
                modify_time = COALESCE(NULLIF(modify_time, ''), NULLIF(updated_at, ''), NULLIF(create_time, ''), modify_time)
            """);

        if (HasColumn(conn, tx, DevtoyTableNames.Workspace, "updated_at")
            && HasColumn(conn, tx, DevtoyTableNames.Workspace, "modify_time"))
        {
            TryExec(conn, tx, "ALTER TABLE devtoy_workspace DROP COLUMN updated_at");
        }

        if (HasColumn(conn, tx, DevtoyTableNames.Module, "updated_at"))
        {
            TryExec(conn, tx, "ALTER TABLE devtoy_module DROP COLUMN updated_at");
        }
    }

    /// <summary>MySQL：workspace 增 create_time、updated_at→modify_time；module 删 updated_at。</summary>
    public static void MigrateMySqlCatalogTimeColumns(DbConnection conn)
    {
        if (conn is not MySqlConnector.MySqlConnection)
        {
            return;
        }

        EnsureMySqlColumn(conn, DevtoyTableNames.Workspace, "create_time", "varchar(64) NULL");
        EnsureMySqlColumn(conn, DevtoyTableNames.Workspace, "modify_time", "varchar(64) NULL");
        EnsureMySqlColumn(conn, DevtoyTableNames.Module, "create_time", "varchar(64) NULL");
        EnsureMySqlColumn(conn, DevtoyTableNames.Module, "modify_time", "varchar(64) NULL");

        TryExecDb(conn,
            """
            UPDATE `devtoy_workspace`
            SET `create_time` = COALESCE(NULLIF(`create_time`, ''), NULLIF(`modify_time`, ''), NULLIF(`updated_at`, ''), `create_time`),
                `modify_time` = COALESCE(NULLIF(`modify_time`, ''), NULLIF(`updated_at`, ''), NULLIF(`create_time`, ''), `modify_time`)
            """);
        TryExecDb(conn,
            """
            UPDATE `devtoy_module`
            SET `create_time` = COALESCE(NULLIF(`create_time`, ''), NULLIF(`modify_time`, ''), NULLIF(`updated_at`, ''), `create_time`),
                `modify_time` = COALESCE(NULLIF(`modify_time`, ''), NULLIF(`updated_at`, ''), NULLIF(`create_time`, ''), `modify_time`)
            """);

        if (HasMySqlColumn(conn, DevtoyTableNames.Workspace, "updated_at"))
        {
            TryExecDb(conn, "ALTER TABLE `devtoy_workspace` DROP COLUMN `updated_at`");
        }

        if (HasMySqlColumn(conn, DevtoyTableNames.Module, "updated_at"))
        {
            TryExecDb(conn, "ALTER TABLE `devtoy_module` DROP COLUMN `updated_at`");
        }

        // 新建列允许 NULL，回填后改为 NOT NULL（失败则保留可空）
        TryExecDb(conn, "ALTER TABLE `devtoy_workspace` MODIFY COLUMN `create_time` varchar(64) NOT NULL");
        TryExecDb(conn, "ALTER TABLE `devtoy_workspace` MODIFY COLUMN `modify_time` varchar(64) NOT NULL");
        TryExecDb(conn, "ALTER TABLE `devtoy_module` MODIFY COLUMN `create_time` varchar(64) NOT NULL");
        TryExecDb(conn, "ALTER TABLE `devtoy_module` MODIFY COLUMN `modify_time` varchar(64) NOT NULL");
    }

    private static void TryExec(SqliteConnection conn, SqliteTransaction? tx, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            if (tx is not null)
            {
                cmd.Transaction = tx;
            }

            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // 列不存在等迁移差异忽略
        }
    }

    private static void TryExecDb(DbConnection conn, string sql)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // 列不存在等迁移差异忽略
        }
    }

    private static void EnsureMySqlColumn(DbConnection conn, string table, string column, string typeSql)
    {
        if (HasMySqlColumn(conn, table, column))
        {
            return;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {typeSql}";
        cmd.ExecuteNonQuery();
    }

    private static bool HasMySqlColumn(DbConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT COUNT(*)
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @table
              AND COLUMN_NAME = @column
            """;
        AddDbParam(cmd, "@table", table);
        AddDbParam(cmd, "@column", column);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result) > 0;
    }

    private static Dictionary<string, (string Create, string Modify)> LoadExistingWorkspaceTimes(
        SqliteConnection conn,
        SqliteTransaction tx)
    {
        var map = new Dictionary<string, (string Create, string Modify)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            SELECT path,
                   COALESCE(NULLIF(create_time, ''), NULLIF(modify_time, '')) AS created,
                   COALESCE(NULLIF(modify_time, ''), NULLIF(create_time, '')) AS modified
            FROM devtoy_workspace
            """;
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                AddPathTimes(map, reader);
            }
        }
        catch
        {
            // 旧列名 updated_at
            try
            {
                cmd.CommandText =
                    """
                    SELECT path,
                           COALESCE(NULLIF(updated_at, '')) AS created,
                           COALESCE(NULLIF(updated_at, '')) AS modified
                    FROM devtoy_workspace
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    AddPathTimes(map, reader);
                }
            }
            catch
            {
                // ignore
            }
        }

        return map;
    }

    private static Dictionary<string, (string Create, string Modify)> LoadExistingWorkspaceTimesDb(DbConnection conn)
    {
        var map = new Dictionary<string, (string Create, string Modify)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT `path`,
                   COALESCE(NULLIF(`create_time`, ''), NULLIF(`modify_time`, '')) AS created,
                   COALESCE(NULLIF(`modify_time`, ''), NULLIF(`create_time`, '')) AS modified
            FROM `devtoy_workspace`
            """;
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                AddPathTimes(map, reader);
            }
        }
        catch
        {
            try
            {
                cmd.Parameters.Clear();
                cmd.CommandText =
                    """
                    SELECT `path`,
                           COALESCE(NULLIF(`updated_at`, '')) AS created,
                           COALESCE(NULLIF(`updated_at`, '')) AS modified
                    FROM `devtoy_workspace`
                    """;
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    AddPathTimes(map, reader);
                }
            }
            catch
            {
                // ignore
            }
        }

        return map;
    }

    private static void AddPathTimes(
        Dictionary<string, (string Create, string Modify)> map,
        DbDataReader reader)
    {
        var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
        var created = reader.IsDBNull(1) ? "" : reader.GetString(1);
        var modified = reader.IsDBNull(2) ? "" : reader.GetString(2);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string key;
        try
        {
            key = Path.GetFullPath(path);
        }
        catch
        {
            key = path.Trim();
        }

        map[key] = (created, string.IsNullOrWhiteSpace(modified) ? created : modified);
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string table,
        string column,
        string typeSql)
    {
        if (HasColumn(conn, tx, table, column))
        {
            return;
        }

        using var alter = conn.CreateCommand();
        if (tx is not null)
        {
            alter.Transaction = tx;
        }

        alter.CommandText = ResolveAlterAddColumnSql(table, column, typeSql);
        alter.ExecuteNonQuery();
    }

    private static bool HasColumn(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string table,
        string column)
    {
        using var check = conn.CreateCommand();
        if (tx is not null)
        {
            check.Transaction = tx;
        }

        check.CommandText = ResolvePragmaTableInfoSql(table);
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolvePragmaTableInfoSql(string table) =>
        table switch
        {
            DevtoyTableNames.Module => "PRAGMA table_info(devtoy_module)",
            DevtoyTableNames.Workspace => "PRAGMA table_info(devtoy_workspace)",
            DevtoyTableNames.Doc => "PRAGMA table_info(devtoy_doc)",
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "未知本机配置表。"),
        };

    private static string ResolveAlterAddColumnSql(string table, string column, string typeSql)
    {
        if (!string.Equals(typeSql, "TEXT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(typeSql), typeSql, "仅支持 TEXT 列迁移。");
        }

        return (table, column) switch
        {
            (DevtoyTableNames.Module, "create_time") =>
                "ALTER TABLE devtoy_module ADD COLUMN create_time TEXT",
            (DevtoyTableNames.Module, "modify_time") =>
                "ALTER TABLE devtoy_module ADD COLUMN modify_time TEXT",
            (DevtoyTableNames.Workspace, "create_time") =>
                "ALTER TABLE devtoy_workspace ADD COLUMN create_time TEXT",
            (DevtoyTableNames.Workspace, "modify_time") =>
                "ALTER TABLE devtoy_workspace ADD COLUMN modify_time TEXT",
            _ => throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                $"不支持为表 {table} 添加列。"),
        };
    }

    private static Dictionary<string, (string Create, string Modify)> LoadExistingTimes(
        SqliteConnection conn,
        SqliteTransaction tx)
    {
        var map = new Dictionary<string, (string Create, string Modify)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            SELECT path,
                   COALESCE(NULLIF(create_time, ''), NULLIF(modify_time, '')) AS created,
                   COALESCE(NULLIF(modify_time, ''), NULLIF(create_time, '')) AS modified
            FROM devtoy_module
            """;
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var created = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var modified = reader.IsDBNull(2) ? "" : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string key;
                try
                {
                    key = Path.GetFullPath(path);
                }
                catch
                {
                    key = path.Trim();
                }

                map[key] = (created, string.IsNullOrWhiteSpace(modified) ? created : modified);
            }
        }
        catch
        {
            // 表尚未含时间列
        }

        return map;
    }

    private static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTime.TryParse(
                raw,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var dt))
        {
            return dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
        }

        return null;
    }
}
