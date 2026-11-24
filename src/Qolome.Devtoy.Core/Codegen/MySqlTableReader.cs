using System.Data.Common;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>按数据源方言读取表结构（MySQL / PostgreSQL / SQLite / Oracle / ODBC information_schema）。</summary>
public sealed class MySqlTableReader
{
    public TableMetadata Read(DataSourceSettings dataSource, string tableName)
    {
        using var conn = DbConnectionFactory.Open(dataSource);
        return dataSource.DatabaseKind switch
        {
            DatabaseKind.Sqlite => ReadSqlite(conn, tableName),
            DatabaseKind.Oracle => ReadOracle(conn, tableName),
            DatabaseKind.PostgreSql => ReadPostgres(conn, dataSource, tableName),
            _ => ReadInformationSchema(conn, dataSource, tableName),
        };
    }

    private static TableMetadata ReadInformationSchema(
        DbConnection conn,
        DataSourceSettings dataSource,
        string tableName)
    {
        var schema = ResolveSchema(conn, dataSource);
        const string tableSql = """
            SELECT TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            LIMIT 1
            """;
        using (var tableCmd = DbConnectionFactory.CreateCommand(conn, tableSql))
        {
            DbConnectionFactory.AddParam(tableCmd, "@schema", schema);
            DbConnectionFactory.AddParam(tableCmd, "@table", tableName);
            var commentObj = tableCmd.ExecuteScalar();
            if (commentObj is null || commentObj is DBNull)
            {
                // ODBC / 部分库无 TABLE_COMMENT 列时再试仅校验存在
                if (!TableExistsLoose(conn, schema, tableName))
                {
                    throw new InvalidOperationException("找不到表: " + tableName);
                }

                commentObj = "";
            }

            var table = new TableMetadata
            {
                Name = tableName,
                Comment = Convert.ToString(commentObj) ?? "",
            };

            const string colSql = """
                SELECT COLUMN_NAME, COLUMN_COMMENT, DATA_TYPE, COLUMN_TYPE, IS_NULLABLE,
                       COLUMN_KEY, EXTRA
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY ORDINAL_POSITION
                """;
            try
            {
                using var colCmd = DbConnectionFactory.CreateCommand(conn, colSql);
                DbConnectionFactory.AddParam(colCmd, "@schema", schema);
                DbConnectionFactory.AddParam(colCmd, "@table", tableName);
                FillColumnsMySqlShape(colCmd, table);
            }
            catch
            {
                // 无 COLUMN_COMMENT/COLUMN_TYPE/EXTRA 的方言（如部分 ODBC）
                const string simpleSql = """
                    SELECT COLUMN_NAME, '' AS COLUMN_COMMENT, DATA_TYPE, DATA_TYPE AS COLUMN_TYPE,
                           IS_NULLABLE, COLUMN_KEY, '' AS EXTRA
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                    ORDER BY ORDINAL_POSITION
                    """;
                using var colCmd = DbConnectionFactory.CreateCommand(conn, simpleSql);
                DbConnectionFactory.AddParam(colCmd, "@schema", schema);
                DbConnectionFactory.AddParam(colCmd, "@table", tableName);
                FillColumnsMySqlShape(colCmd, table);
            }

            if (table.Columns.Count == 0)
            {
                throw new InvalidOperationException("表无字段: " + tableName);
            }

            return table;
        }
    }

    private static TableMetadata ReadPostgres(DbConnection conn, DataSourceSettings dataSource, string tableName)
    {
        var schema = string.IsNullOrWhiteSpace(dataSource.Database) ? "public" : ResolveSchema(conn, dataSource);
        if (string.IsNullOrWhiteSpace(schema) || string.Equals(schema, dataSource.Database, StringComparison.OrdinalIgnoreCase))
        {
            schema = "public";
        }

        const string existsSql = """
            SELECT 1 FROM information_schema.tables
            WHERE table_schema = @schema AND table_name = @table
            LIMIT 1
            """;
        using (var existsCmd = DbConnectionFactory.CreateCommand(conn, existsSql))
        {
            DbConnectionFactory.AddParam(existsCmd, "@schema", schema);
            DbConnectionFactory.AddParam(existsCmd, "@table", tableName);
            if (existsCmd.ExecuteScalar() is null)
            {
                throw new InvalidOperationException("找不到表: " + tableName);
            }
        }

        var table = new TableMetadata { Name = tableName, Comment = "" };
        const string colSql = """
            SELECT column_name, '' AS column_comment, data_type, udt_name AS column_type,
                   is_nullable, '' AS column_key, '' AS extra
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position
            """;
        using var colCmd = DbConnectionFactory.CreateCommand(conn, colSql);
        DbConnectionFactory.AddParam(colCmd, "@schema", schema);
        DbConnectionFactory.AddParam(colCmd, "@table", tableName);
        FillColumnsMySqlShape(colCmd, table);
        if (table.Columns.Count == 0)
        {
            throw new InvalidOperationException("表无字段: " + tableName);
        }

        return table;
    }

    private static TableMetadata ReadSqlite(DbConnection conn, string tableName)
    {
        const string existsSql = """
            SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @table LIMIT 1
            """;
        using (var existsCmd = DbConnectionFactory.CreateCommand(conn, existsSql))
        {
            DbConnectionFactory.AddParam(existsCmd, "@table", tableName);
            if (existsCmd.ExecuteScalar() is null)
            {
                throw new InvalidOperationException("找不到表: " + tableName);
            }
        }

        var table = new TableMetadata { Name = tableName, Comment = "" };
        using var colCmd = DbConnectionFactory.CreateCommand(conn, $"PRAGMA table_info('{tableName.Replace("'", "''", StringComparison.Ordinal)}')");
        using var reader = colCmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            var type = reader.IsDBNull(2) ? "TEXT" : reader.GetString(2);
            var notNull = !reader.IsDBNull(3) && Convert.ToInt32(reader.GetValue(3)) != 0;
            var pk = !reader.IsDBNull(5) && Convert.ToInt32(reader.GetValue(5)) != 0;
            table.Columns.Add(new ColumnMetadata
            {
                Name = name,
                Comment = "",
                DataType = type,
                ColumnType = type,
                Nullable = !notNull,
                PrimaryKey = pk,
                AutoIncrement = pk,
                Extra = "",
            });
        }

        if (table.Columns.Count == 0)
        {
            throw new InvalidOperationException("表无字段: " + tableName);
        }

        return table;
    }

    private static TableMetadata ReadOracle(DbConnection conn, string tableName)
    {
        const string existsSql = """
            SELECT 1 FROM user_tables WHERE table_name = :table_name
            """;
        using (var existsCmd = DbConnectionFactory.CreateCommand(conn, existsSql))
        {
            DbConnectionFactory.AddParam(existsCmd, "table_name", tableName.ToUpperInvariant());
            if (existsCmd.ExecuteScalar() is null)
            {
                throw new InvalidOperationException("找不到表: " + tableName);
            }
        }

        var table = new TableMetadata { Name = tableName, Comment = "" };
        const string colSql = """
            SELECT column_name, '' AS column_comment, data_type, data_type AS column_type,
                   nullable, '' AS column_key, '' AS extra
            FROM user_tab_columns
            WHERE table_name = :table_name
            ORDER BY column_id
            """;
        using var colCmd = DbConnectionFactory.CreateCommand(conn, colSql);
        DbConnectionFactory.AddParam(colCmd, "table_name", tableName.ToUpperInvariant());
        using var reader = colCmd.ExecuteReader();
        while (reader.Read())
        {
            var nullable = reader.IsDBNull(4) || string.Equals(reader.GetString(4), "Y", StringComparison.OrdinalIgnoreCase);
            table.Columns.Add(new ColumnMetadata
            {
                Name = reader.GetString(0),
                Comment = "",
                DataType = reader.GetString(2),
                ColumnType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Nullable = nullable,
                PrimaryKey = false,
                AutoIncrement = false,
                Extra = "",
            });
        }

        if (table.Columns.Count == 0)
        {
            throw new InvalidOperationException("表无字段: " + tableName);
        }

        return table;
    }

    private static void FillColumnsMySqlShape(DbCommand colCmd, TableMetadata table)
    {
        using var reader = colCmd.ExecuteReader();
        while (reader.Read())
        {
            var extra = reader.FieldCount > 6 && !reader.IsDBNull(6)
                ? Convert.ToString(reader.GetValue(6)) ?? ""
                : "";
            var key = reader.FieldCount > 5 && !reader.IsDBNull(5)
                ? Convert.ToString(reader.GetValue(5)) ?? ""
                : "";
            var nullableRaw = reader.IsDBNull(4) ? "YES" : Convert.ToString(reader.GetValue(4)) ?? "YES";
            table.Columns.Add(new ColumnMetadata
            {
                Name = reader.GetString(0),
                Comment = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1)) ?? "",
                DataType = reader.IsDBNull(2) ? "varchar" : Convert.ToString(reader.GetValue(2)) ?? "varchar",
                ColumnType = reader.IsDBNull(3) ? "" : Convert.ToString(reader.GetValue(3)) ?? "",
                Nullable = string.Equals(nullableRaw, "YES", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(nullableRaw, "Y", StringComparison.OrdinalIgnoreCase),
                PrimaryKey = string.Equals(key, "PRI", StringComparison.OrdinalIgnoreCase),
                AutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                Extra = extra,
            });
        }
    }

    private static string ResolveSchema(DbConnection conn, DataSourceSettings dataSource)
    {
        if (!string.IsNullOrWhiteSpace(dataSource.Database))
        {
            return dataSource.Database.Trim();
        }

        return string.IsNullOrWhiteSpace(conn.Database) ? "" : conn.Database;
    }

    private static bool TableExistsLoose(DbConnection conn, string schema, string tableName)
    {
        try
        {
            const string sql = """
                SELECT 1 FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                """;
            using var cmd = DbConnectionFactory.CreateCommand(conn, sql);
            DbConnectionFactory.AddParam(cmd, "@schema", schema);
            DbConnectionFactory.AddParam(cmd, "@table", tableName);
            return cmd.ExecuteScalar() is not null and not DBNull;
        }
        catch
        {
            return false;
        }
    }
}
