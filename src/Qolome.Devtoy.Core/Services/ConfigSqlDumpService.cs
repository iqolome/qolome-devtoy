using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Qolome.Devtoy.Core.Services;

/// <summary>
/// 导出 / 导入本机配置表：<c>devtoy_doc</c>、<c>devtoy_workspace</c>、<c>devtoy_module</c>。
/// 不含远程业务表。
/// </summary>
public static class ConfigSqlDumpService
{
    public static string Export()
    {
        EnsureLocalSchema();
        var sb = new StringBuilder();
        sb.AppendLine("-- Qolome DevToy 本地配置 SQL");
        sb.AppendLine($"-- database={DevtoyPaths.LocalDbPath}");
        sb.AppendLine($"-- exported={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- 含 devtoy_doc / devtoy_workspace / devtoy_module，不含远程业务表。");
        sb.AppendLine();
        sb.AppendLine(ModuleStore.SchemaDdl.TrimEnd());
        sb.AppendLine(
            """
            CREATE TABLE IF NOT EXISTS `devtoy_doc` (
                `kind` TEXT PRIMARY KEY,
                `json` TEXT NOT NULL,
                `updated_at` TEXT NOT NULL
            );
            """);
        sb.AppendLine("BEGIN;");
        sb.AppendLine($"DELETE FROM `{DevtoyTableNames.Doc}`;");
        sb.AppendLine($"DELETE FROM `{DevtoyTableNames.Workspace}`;");
        sb.AppendLine($"DELETE FROM `{DevtoyTableNames.Module}`;");
        sb.AppendLine();

        using var conn = OpenLocalDb();
        var count = 0;
        count += AppendDocRows(sb, conn);
        count += AppendWorkspaceRows(sb, conn);
        count += AppendModuleRows(sb, conn);

        sb.AppendLine("COMMIT;");
        sb.AppendLine($"-- rows={count}");
        return sb.ToString();
    }

    public static void ExportToFile(string path) =>
        File.WriteAllText(path, Export(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static int ImportFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SQL 文件不存在。", path);
        }

        return Import(File.ReadAllText(path));
    }

    public static int Import(string script)
    {
        EnsureLocalSchema();
        using var conn = OpenLocalDb();
        using var tx = conn.BeginTransaction();
        var affected = 0;
        try
        {
            foreach (var batch in SplitSqlBatches(script))
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = batch;
                affected += cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        return affected;
    }

    private static int AppendDocRows(StringBuilder sb, SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT kind, json, updated_at FROM `{DevtoyTableNames.Doc}` ORDER BY kind";
        using var reader = cmd.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var kind = reader.GetString(0);
            // 旧导出可能仍含 module_overrides；导入侧可保留，加载时会迁移清理
            var json = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var updated = reader.IsDBNull(2)
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : reader.GetString(2);
            sb.Append("INSERT INTO `").Append(DevtoyTableNames.Doc)
                .Append("` (`kind`, `json`, `updated_at`) VALUES (")
                .Append(SqlString(kind)).Append(", ")
                .Append(SqlString(json)).Append(", ")
                .Append(SqlString(updated))
                .AppendLine(");");
            count++;
        }

        return count;
    }

    private static int AppendWorkspaceRows(StringBuilder sb, SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT path, sort_order, updated_at FROM `{DevtoyTableNames.Workspace}` ORDER BY sort_order, path COLLATE NOCASE";
        using var reader = cmd.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var path = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var sort = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            var updated = reader.IsDBNull(2)
                ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                : reader.GetString(2);
            sb.Append("INSERT INTO `").Append(DevtoyTableNames.Workspace)
                .Append("` (`path`, `sort_order`, `updated_at`) VALUES (")
                .Append(SqlString(path)).Append(", ")
                .Append(sort.ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(SqlString(updated))
                .AppendLine(");");
            count++;
        }

        return count;
    }

    private static int AppendModuleRows(StringBuilder sb, SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
            SELECT path, workspace_path, source, display_name, ignored,
                   java, web, flutter, version, manual,
                   codegen_model_name, codegen_dao, codegen_service, codegen_controller,
                   enable_codegen, enable_enum, enable_ts_dict, tag_boot_starter,
                   enum_package_path, ts_dict_path, create_time, modify_time, updated_at
            FROM `{DevtoyTableNames.Module}`
            ORDER BY path COLLATE NOCASE
            """;
        using var reader = cmd.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            sb.Append("INSERT INTO `").Append(DevtoyTableNames.Module).Append("` (")
                .Append("`path`, `workspace_path`, `source`, `display_name`, `ignored`, ")
                .Append("`java`, `web`, `flutter`, `version`, `manual`, ")
                .Append("`codegen_model_name`, `codegen_dao`, `codegen_service`, `codegen_controller`, ")
                .Append("`enable_codegen`, `enable_enum`, `enable_ts_dict`, `tag_boot_starter`, ")
                .Append("`enum_package_path`, `ts_dict_path`, `create_time`, `modify_time`, `updated_at`) VALUES (");
            for (var i = 0; i < 23; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(SqlLiteral(reader, i));
            }

            sb.AppendLine(");");
            count++;
        }

        return count;
    }

    private static string SqlLiteral(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return "NULL";
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            long or int or short or byte => Convert.ToInt64(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            float f => f.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            _ => SqlString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""),
        };
    }

    private static void EnsureLocalSchema()
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
        ModuleStore.EnsureSchema(conn, tx: null);
    }

    private static SqliteConnection OpenLocalDb()
    {
        var conn = new SqliteConnection($"Data Source={DevtoyPaths.LocalDbPath};Cache=Shared");
        conn.Open();
        return conn;
    }

    private static string SqlString(string value) =>
        "'" + value
            .Replace("'", "''", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal) + "'";

    private static IEnumerable<string> SplitSqlBatches(string script)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(script);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal)
                || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.Equals("BEGIN;", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("COMMIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine(line);
            if (!trimmed.EndsWith(';'))
            {
                continue;
            }

            var batch = sb.ToString().Trim();
            sb.Clear();
            if (batch.Length > 0)
            {
                yield return batch.TrimEnd(';').Trim();
            }
        }

        var tail = sb.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail.TrimEnd(';').Trim();
        }
    }
}
