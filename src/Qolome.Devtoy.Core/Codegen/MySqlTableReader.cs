using MySqlConnector;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Devtoy.Core.Codegen;

/// <summary>从 information_schema 读取表结构。</summary>
public sealed class MySqlTableReader
{
    public TableMetadata Read(DataSourceSettings dataSource, string tableName)
    {
        using var conn = MySqlConnectionFactory.Open(dataSource);
        var schema = string.IsNullOrWhiteSpace(dataSource.Database)
            ? conn.Database
            : dataSource.Database;

        const string tableSql = """
            SELECT TABLE_COMMENT
            FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            LIMIT 1
            """;
        using (var tableCmd = new MySqlCommand(tableSql, conn))
        {
            tableCmd.Parameters.AddWithValue("@schema", schema);
            tableCmd.Parameters.AddWithValue("@table", tableName);
            var commentObj = tableCmd.ExecuteScalar();
            if (commentObj is null || commentObj is DBNull)
            {
                throw new InvalidOperationException("找不到表: " + tableName);
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
            using var colCmd = new MySqlCommand(colSql, conn);
            colCmd.Parameters.AddWithValue("@schema", schema);
            colCmd.Parameters.AddWithValue("@table", tableName);
            using var reader = colCmd.ExecuteReader();
            while (reader.Read())
            {
                var extra = reader.IsDBNull(6) ? "" : reader.GetString(6);
                table.Columns.Add(new ColumnMetadata
                {
                    Name = reader.GetString(0),
                    Comment = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    DataType = reader.GetString(2),
                    ColumnType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Nullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase),
                    PrimaryKey = string.Equals(reader.GetString(5), "PRI", StringComparison.OrdinalIgnoreCase),
                    AutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                    Extra = extra,
                });
            }

            if (table.Columns.Count == 0)
            {
                throw new InvalidOperationException("表无字段: " + tableName);
            }

            return table;
        }
    }
}
