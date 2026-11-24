using System.Data.Common;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

public static class JdbcTableLister
{
    private static readonly HashSet<string> SystemTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATABASECHANGELOG",
        "DATABASECHANGELOGLOCK",
    };

    public static IReadOnlyList<TableListItem> ListTables(DataSourceSettings dataSource)
    {
        using var conn = DbConnectionFactory.Open(dataSource);
        return dataSource.DatabaseKind switch
        {
            DatabaseKind.PostgreSql => ListInformationSchema(conn, currentSchemaSql: "SELECT current_schema()"),
            DatabaseKind.Sqlite => ListSqlite(conn),
            DatabaseKind.Oracle => ListOracle(conn),
            DatabaseKind.Odbc => ListInformationSchema(conn, currentSchemaSql: null, schemaFallback: dataSource.Database),
            _ => ListInformationSchema(conn, currentSchemaSql: "SELECT DATABASE()", schemaFallback: dataSource.Database),
        };
    }

    private static IReadOnlyList<TableListItem> ListInformationSchema(
        DbConnection conn,
        string? currentSchemaSql,
        string? schemaFallback = null)
    {
        var schema = schemaFallback?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(schema) && !string.IsNullOrWhiteSpace(currentSchemaSql))
        {
            using var schemaCmd = DbConnectionFactory.CreateCommand(conn, currentSchemaSql);
            schema = Convert.ToString(schemaCmd.ExecuteScalar()) ?? "";
        }

        if (string.IsNullOrWhiteSpace(schema))
        {
            schema = conn.Database ?? "";
        }

        const string sql = """
            SELECT TABLE_NAME, TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_NAME
            """;
        // PostgreSQL: TABLE_COMMENT 不在 TABLES，用 obj_description；简化为空注释
        var pg = conn.GetType().Name.Contains("Npgsql", StringComparison.Ordinal);
        var listSql = pg
            ? """
                SELECT table_name, '' AS table_comment
                FROM information_schema.tables
                WHERE table_schema = @schema AND table_type = 'BASE TABLE'
                ORDER BY table_name
                """
            : sql;

        using var cmd = DbConnectionFactory.CreateCommand(conn, listSql);
        DbConnectionFactory.AddParam(cmd, "@schema", schema);
        return ReadNameComment(cmd);
    }

    private static IReadOnlyList<TableListItem> ListSqlite(DbConnection conn)
    {
        const string sql = """
            SELECT name, '' AS comment
            FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """;
        using var cmd = DbConnectionFactory.CreateCommand(conn, sql);
        return ReadNameComment(cmd);
    }

    private static IReadOnlyList<TableListItem> ListOracle(DbConnection conn)
    {
        const string sql = """
            SELECT table_name, NVL(comments, '') AS table_comment
            FROM user_tab_comments
            WHERE table_type = 'TABLE'
            ORDER BY table_name
            """;
        using var cmd = DbConnectionFactory.CreateCommand(conn, sql);
        return ReadNameComment(cmd);
    }

    private static List<TableListItem> ReadNameComment(DbCommand cmd)
    {
        var list = new List<TableListItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (IsSystemTable(name))
            {
                continue;
            }

            var comment = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1)) ?? "";
            list.Add(new TableListItem { Name = name, Comment = comment });
        }

        return list;
    }

    private static bool IsSystemTable(string name) =>
        name.StartsWith("QRTZ_", StringComparison.OrdinalIgnoreCase)
        || SystemTables.Contains(name);
}
