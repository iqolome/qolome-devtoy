namespace Qolome.Devtoy.Core.Services;

using Microsoft.Data.Sqlite;
using Qolome.Devtoy.Core.Models;

/// <summary>
/// 本机 SQLite 模块列表：<c>devtoy_workspace</c> / <c>devtoy_module</c>。
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
                updated_at TEXT NOT NULL
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
                modify_time TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """;
        mod.ExecuteNonQuery();
        EnsureModuleTimeColumns(conn, tx);
    }

    /// <summary>表中是否已有工作区或模块行（用于判断是否已迁移）。</summary>
    public static bool HasPersistedRows()
    {
        if (!File.Exists(DevtoyPaths.LocalDbPath))
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

        settings.WorkspaceRoots.Clear();
        settings.WorkspaceRoot = "";
        settings.SpecifiedProjects.Clear();
        settings.IgnoredModulePaths.Clear();
        settings.ModuleDisplayNames.Clear();
        settings.ModuleOverrides.Clear();
        settings.ModuleTimes.Clear();

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
                   create_time, modify_time, updated_at
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

            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                """
                INSERT INTO devtoy_workspace (path, sort_order, updated_at)
                VALUES ($path, $sort_order, $updated_at)
                """;
            insert.Parameters.AddWithValue("$path", Path.GetFullPath(path));
            insert.Parameters.AddWithValue("$sort_order", i);
            insert.Parameters.AddWithValue("$updated_at", now);
            insert.ExecuteNonQuery();
        }

        var existingCreate = LoadExistingCreateTimes(conn, tx);
        var rows = BuildRows(settings);
        using (var clearMod = conn.CreateCommand())
        {
            clearMod.Transaction = tx;
            clearMod.CommandText = "DELETE FROM devtoy_module";
            clearMod.ExecuteNonQuery();
        }

        foreach (var row in rows.Values.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (settings.ModuleTimes.TryGetValue(row.Path, out var meta)
                && meta.CreateTime is not null)
            {
                row.CreateTime = meta.CreateTime.Value.ToUniversalTime().ToString("O");
            }
            else if (existingCreate.TryGetValue(row.Path, out var created))
            {
                row.CreateTime = created;
            }
            else
            {
                row.CreateTime = now;
            }

            row.ModifyTime = now;
            InsertModuleRow(conn, tx, row, now);
        }
    }

    /// <summary>DDL 片段，供配置 SQL 导出使用。</summary>
    public static string SchemaDdl =>
        """
        CREATE TABLE IF NOT EXISTS `devtoy_workspace` (
            `path` TEXT PRIMARY KEY NOT NULL,
            `sort_order` INTEGER NOT NULL DEFAULT 0,
            `updated_at` TEXT NOT NULL
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
            `modify_time` TEXT NOT NULL,
            `updated_at` TEXT NOT NULL
        );
        """;

    private static void ApplyModuleRow(DevtoySettings settings, SqliteDataReader reader)
    {
        var rawPath = reader.IsDBNull(0) ? "" : reader.GetString(0);
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        string path;
        try
        {
            path = Path.GetFullPath(rawPath);
        }
        catch
        {
            path = rawPath.Trim();
        }

        var source = reader.IsDBNull(2) ? SourceWorkspace : reader.GetString(2);
        var displayName = reader.IsDBNull(3) ? null : reader.GetString(3);
        var ignored = !reader.IsDBNull(4) && reader.GetInt64(4) != 0;

        if (string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase))
        {
            settings.SpecifiedProjects.Add(new SpecifiedProjectEntry
            {
                Root = path,
                Java = ReadBool(reader, 5) == true,
                Web = ReadBool(reader, 6) == true,
                Flutter = ReadBool(reader, 7) == true,
                Version = reader.IsDBNull(8) ? null : reader.GetString(8),
                Manual = ReadBool(reader, 9) == true,
            });
        }

        if (ignored)
        {
            settings.IgnoredModulePaths.Add(path);
        }

        var ov = new ModuleCatalogOverride
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
            Version = string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase)
                ? null
                : (reader.IsDBNull(8) ? null : NullIfWhite(reader.GetString(8))),
            Java = string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase)
                ? null
                : ReadBool(reader, 5),
            Web = string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase)
                ? null
                : ReadBool(reader, 6),
            Flutter = string.Equals(source, SourceSpecified, StringComparison.OrdinalIgnoreCase)
                ? null
                : ReadBool(reader, 7),
            CodegenModelName = reader.IsDBNull(10) ? null : NullIfWhite(reader.GetString(10)),
            CodegenDao = ReadBool(reader, 11),
            CodegenService = ReadBool(reader, 12),
            CodegenController = ReadBool(reader, 13),
            EnableCodegen = ReadBool(reader, 14),
            EnableEnum = ReadBool(reader, 15),
            EnableTsDict = ReadBool(reader, 16),
            TagBootStarter = ReadBool(reader, 17),
            EnumPackagePath = reader.IsDBNull(18) ? null : NullIfWhite(reader.GetString(18)),
            TsDictPath = reader.IsDBNull(19) ? null : NullIfWhite(reader.GetString(19)),
        };

        if (!IsOverrideEmpty(ov))
        {
            settings.ModuleOverrides[path] = ov;
            if (!string.IsNullOrWhiteSpace(ov.DisplayName))
            {
                settings.ModuleDisplayNames[path] = ov.DisplayName;
            }
        }
        else if (!string.IsNullOrWhiteSpace(displayName))
        {
            settings.ModuleDisplayNames[path] = displayName.Trim();
            settings.ModuleOverrides[path] = new ModuleCatalogOverride { DisplayName = displayName.Trim() };
        }

        var create = ParseTimestamp(reader.IsDBNull(20) ? null : reader.GetString(20))
            ?? ParseTimestamp(reader.IsDBNull(22) ? null : reader.GetString(22));
        var modify = ParseTimestamp(reader.IsDBNull(21) ? null : reader.GetString(21))
            ?? ParseTimestamp(reader.IsDBNull(22) ? null : reader.GetString(22));
        if (create is not null || modify is not null)
        {
            settings.ModuleTimes[path] = new ModulePersistTimes
            {
                CreateTime = create,
                ModifyTime = modify,
            };
        }
    }

    private static Dictionary<string, ModuleRow> BuildRows(DevtoySettings settings)
    {
        var rows = new Dictionary<string, ModuleRow>(StringComparer.OrdinalIgnoreCase);

        ModuleRow GetOrAdd(string path)
        {
            var full = Path.GetFullPath(path);
            if (!rows.TryGetValue(full, out var row))
            {
                row = new ModuleRow { Path = full, Source = SourceWorkspace };
                rows[full] = row;
            }

            return row;
        }

        foreach (var spec in settings.SpecifiedProjects)
        {
            if (string.IsNullOrWhiteSpace(spec.Root))
            {
                continue;
            }

            var row = GetOrAdd(spec.Root);
            row.Source = SourceSpecified;
            row.Java = spec.Java;
            row.Web = spec.Web || spec.JavaScript;
            row.Flutter = spec.Flutter;
            row.Version = NullIfWhite(spec.Version);
            row.Manual = spec.Manual;
        }

        foreach (var ignored in settings.IgnoredModulePaths)
        {
            if (string.IsNullOrWhiteSpace(ignored))
            {
                continue;
            }

            GetOrAdd(ignored).Ignored = true;
        }

        foreach (var (key, name) in settings.ModuleDisplayNames)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var row = GetOrAdd(key);
            row.DisplayName = name.Trim();
        }

        foreach (var (key, ov) in settings.ModuleOverrides)
        {
            if (string.IsNullOrWhiteSpace(key) || ov is null || IsOverrideEmpty(ov))
            {
                continue;
            }

            var row = GetOrAdd(key);
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
                row.Web = ov.Web == true || ov.JavaScript == true ? true : ov.Web;
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

        // 丢弃无任何用户状态的空行（理论上不会产生）
        return rows
            .Where(kv => kv.Value.Source == SourceSpecified
                || kv.Value.Ignored
                || !string.IsNullOrWhiteSpace(kv.Value.DisplayName)
                || kv.Value.HasOverrideFields)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }

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
                enum_package_path, ts_dict_path, create_time, modify_time, updated_at
            ) VALUES (
                $path, $workspace_path, $source, $display_name, $ignored,
                $java, $web, $flutter, $version, $manual,
                $codegen_model_name, $codegen_dao, $codegen_service, $codegen_controller,
                $enable_codegen, $enable_enum, $enable_ts_dict, $tag_boot_starter,
                $enum_package_path, $ts_dict_path, $create_time, $modify_time, $updated_at
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
        cmd.Parameters.AddWithValue("$updated_at", modify);
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
        cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value ? 1 : 0);
    }

    private static bool? ReadBool(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetInt64(ordinal) != 0;
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
        cmd.CommandText = $"SELECT COUNT(*) FROM `{table}`";
        var result = cmd.ExecuteScalar();
        return result is long n ? n : Convert.ToInt64(result);
    }

    private static SqliteConnection OpenLocalDb()
    {
        var conn = new SqliteConnection($"Data Source={DevtoyPaths.LocalDbPath};Cache=Shared");
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

    private static void EnsureModuleTimeColumns(SqliteConnection conn, SqliteTransaction? tx)
    {
        EnsureColumn(conn, tx, DevtoyTableNames.Module, "create_time", "TEXT");
        EnsureColumn(conn, tx, DevtoyTableNames.Module, "modify_time", "TEXT");

        using var backfill = conn.CreateCommand();
        if (tx is not null)
        {
            backfill.Transaction = tx;
        }

        backfill.CommandText =
            """
            UPDATE devtoy_module
            SET create_time = COALESCE(NULLIF(create_time, ''), updated_at, create_time),
                modify_time = COALESCE(NULLIF(modify_time, ''), updated_at, modify_time)
            WHERE (create_time IS NULL OR create_time = '')
               OR (modify_time IS NULL OR modify_time = '')
            """;
        try
        {
            backfill.ExecuteNonQuery();
        }
        catch
        {
            // 旧库无 updated_at 时忽略
        }
    }

    private static void EnsureColumn(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string table,
        string column,
        string typeSql)
    {
        using var check = conn.CreateCommand();
        if (tx is not null)
        {
            check.Transaction = tx;
        }

        check.CommandText = $"PRAGMA table_info(`{table}`)";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Close();
        using var alter = conn.CreateCommand();
        if (tx is not null)
        {
            alter.Transaction = tx;
        }

        alter.CommandText = $"ALTER TABLE `{table}` ADD COLUMN `{column}` {typeSql}";
        alter.ExecuteNonQuery();
    }

    private static Dictionary<string, string> LoadExistingCreateTimes(
        SqliteConnection conn,
        SqliteTransaction tx)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            SELECT path,
                   COALESCE(NULLIF(create_time, ''), updated_at) AS created
            FROM devtoy_module
            """;
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var created = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(created))
                {
                    continue;
                }

                try
                {
                    map[Path.GetFullPath(path)] = created;
                }
                catch
                {
                    map[path.Trim()] = created;
                }
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
