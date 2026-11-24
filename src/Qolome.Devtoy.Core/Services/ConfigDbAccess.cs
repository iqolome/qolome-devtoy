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
        if (conn is SqliteConnection)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS devtoy_doc (
                    kind TEXT PRIMARY KEY NOT NULL,
                    json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            ModuleStore.EnsureSchema((SqliteConnection)conn, tx: null);
            return;
        }

        if (conn is MySqlConnection)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
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
                  `updated_at` varchar(64) NOT NULL,
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
                  `updated_at` varchar(64) NOT NULL,
                  PRIMARY KEY (`path`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
                """;
            // MySqlConnector may not run multiple statements depending on settings — split
            foreach (var batch in SplitStatements(cmd.CommandText))
            {
                using var c = conn.CreateCommand();
                c.CommandText = batch;
                c.ExecuteNonQuery();
            }
        }
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
