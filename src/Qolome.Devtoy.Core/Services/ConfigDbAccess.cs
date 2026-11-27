using System.Data.Common;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>配置/模块表的 ADO 入口：Sqlite 单一库或远程 MySQL。</summary>
public static class ConfigDbAccess
{
    public static DbConnection Open()
    {
        var ds = DataSourceRuntime.Current;
        if (ds.IsSqlite)
        {
            var path = DataSourceRuntime.ResolveSqlitePath(ds);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var conn = new SqliteConnection($"Data Source={path};Cache=Shared");
            conn.Open();
            return conn;
        }

        if (ds.DatabaseKind == DatabaseKind.MySql)
        {
            var conn = new MySqlConnection(AdoConnectionStrings.ToMySql(ds));
            conn.Open();
            return conn;
        }

        throw new InvalidOperationException(
            $"配置库目前仅支持 Sqlite 或 MySQL（当前为 {ds.DatabaseKind}）。");
    }

    public static void EnsureConfigSchema(DbConnection conn)
    {
        if (conn is SqliteConnection sqlite)
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS devtoy_doc (
                    kind TEXT PRIMARY KEY NOT NULL,
                    json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            ModuleStore.EnsureSchema(sqlite, tx: null);
            EnsureSqliteDictSchema(sqlite);
            return;
        }

        if (conn is MySqlConnection mysql)
        {
            EnsureMySqlConfigSchema(mysql);
        }
    }

    /// <summary>Sqlite 下字典表不在 EF 模型中，须与配置表一并 IF NOT EXISTS 创建。</summary>
    private static void EnsureSqliteDictSchema(SqliteConnection sqlite)
    {
        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS qolome_dict_item (
                    item_id INTEGER NOT NULL PRIMARY KEY,
                    item_code TEXT NULL,
                    item_name TEXT NULL,
                    item_scope INTEGER NULL,
                    item_desc TEXT NULL,
                    enum_flag INTEGER NULL,
                    data_reference_type INTEGER NULL,
                    remark TEXT NULL,
                    modify_version INTEGER NULL,
                    deleted INTEGER NULL,
                    enabled INTEGER NULL,
                    tenant_id INTEGER NULL,
                    create_time TEXT NULL,
                    creater_id INTEGER NULL,
                    modify_time TEXT NULL,
                    modifier_id INTEGER NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_qolome_dict_item_code ON qolome_dict_item (item_code);";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS qolome_dict_val (
                    val_id INTEGER NOT NULL PRIMARY KEY,
                    item_id INTEGER NULL,
                    val_value TEXT NULL,
                    val_code TEXT NULL,
                    val_name TEXT NULL,
                    val_enum TEXT NULL,
                    val_scope INTEGER NULL,
                    val_desc TEXT NULL,
                    seq INTEGER NULL,
                    remark TEXT NULL,
                    modify_version INTEGER NULL,
                    deleted INTEGER NULL,
                    enabled INTEGER NULL,
                    tenant_id INTEGER NULL,
                    create_time TEXT NULL,
                    creater_id INTEGER NULL,
                    modify_time TEXT NULL,
                    modifier_id INTEGER NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_qolome_dict_val_item_value ON qolome_dict_val (item_id, val_value);";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = sqlite.CreateCommand())
        {
            cmd.CommandText =
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_qolome_dict_val_item_code ON qolome_dict_val (item_id, val_code);";
            cmd.ExecuteNonQuery();
        }
    }

    private static void EnsureMySqlConfigSchema(MySqlConnection mysql)
    {
        const string script =
            """
            CREATE TABLE IF NOT EXISTS `devtoy_doc` (
              `kind` varchar(64) NOT NULL,
              `json` longtext NOT NULL,
              `updated_at` varchar(64) NOT NULL,
              PRIMARY KEY (`kind`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            CREATE TABLE IF NOT EXISTS `devtoy_workspace` (
              `path` varchar(512) NOT NULL,
              `sort_order` int NOT NULL DEFAULT 0,
              `create_time` varchar(64) NOT NULL,
              `modify_time` varchar(64) NOT NULL,
              PRIMARY KEY (`path`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            CREATE TABLE IF NOT EXISTS `devtoy_module` (
              `path` varchar(512) NOT NULL,
              `workspace_path` varchar(512) NULL,
              `source` varchar(32) NOT NULL,
              `display_name` varchar(256) NULL,
              `ignored` tinyint(1) NOT NULL DEFAULT 0,
              `java` tinyint(1) NULL,
              `web` tinyint(1) NULL,
              `flutter` tinyint(1) NULL,
              `version` varchar(64) NULL,
              `manual` tinyint(1) NULL,
              `codegen_model_name` varchar(128) NULL,
              `codegen_dao` tinyint(1) NULL,
              `codegen_service` tinyint(1) NULL,
              `codegen_controller` tinyint(1) NULL,
              `enable_codegen` tinyint(1) NULL,
              `enable_enum` tinyint(1) NULL,
              `enable_ts_dict` tinyint(1) NULL,
              `tag_boot_starter` tinyint(1) NULL,
              `enum_package_path` varchar(256) NULL,
              `ts_dict_path` varchar(512) NULL,
              `create_time` varchar(64) NOT NULL,
              `modify_time` varchar(64) NOT NULL,
              PRIMARY KEY (`path`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """;
        foreach (var batch in SplitStatements(script))
        {
            using var c = mysql.CreateCommand();
            c.CommandText = batch;
            c.ExecuteNonQuery();
        }

        ModuleStore.MigrateMySqlCatalogTimeColumns(mysql);
    }

    private static IEnumerable<string> SplitStatements(string script)
    {
        foreach (var part in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0)
            {
                yield return part;
            }
        }
    }
}
