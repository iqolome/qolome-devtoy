using System.Data.Common;
using System.Globalization;
using System.Text;
using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>按当前数据源导入 / 导出字典表 SQL（<c>devtoy_qolome_dict_*</c>）。</summary>
public static class DictSqlDumpService
{
    private static readonly string[] ItemColumns =
    [
        "item_id", "item_code", "item_name", "item_scope", "item_desc", "enum_flag",
        "data_reference_type", "remark", "modify_version", "deleted", "enabled",
        "tenant_id", "create_time", "creater_id", "modify_time", "modifier_id",
    ];

    private static readonly string[] ValColumns =
    [
        "val_id", "item_id", "val_value", "val_code", "val_name", "val_enum",
        "val_scope", "val_desc", "seq", "remark", "modify_version", "deleted",
        "enabled", "tenant_id", "create_time", "creater_id", "modify_time", "modifier_id",
    ];

    public static string Export(DataSourceSettings dataSource)
    {
        using var conn = DbConnectionFactory.Open(dataSource);
        var sb = new StringBuilder();
        sb.AppendLine("-- Qolome DevToy 字典 SQL 导出");
        sb.AppendLine($"-- databaseKind={dataSource.DatabaseKind}");
        sb.AppendLine($"-- exported={DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("-- 覆盖写入：先 DELETE 再 INSERT。");
        sb.AppendLine();
        sb.AppendLine("SET NAMES utf8mb4;");
        sb.AppendLine("SET FOREIGN_KEY_CHECKS=0;");
        sb.AppendLine();
        sb.AppendLine($"DELETE FROM `{DevtoyTableNames.DictVal}`;");
        sb.AppendLine($"DELETE FROM `{DevtoyTableNames.DictItem}`;");
        sb.AppendLine();

        var itemCount = AppendTableInserts(conn, sb, DevtoyTableNames.DictItem, ItemColumns);
        sb.AppendLine();
        var valCount = AppendTableInserts(conn, sb, DevtoyTableNames.DictVal, ValColumns);
        sb.AppendLine();
        sb.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
        sb.AppendLine($"-- rows: item={itemCount}, val={valCount}");
        return sb.ToString();
    }

    public static void ExportToFile(DataSourceSettings dataSource, string path) =>
        File.WriteAllText(path, Export(dataSource), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public static int ImportFromFile(DataSourceSettings dataSource, string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SQL 文件不存在。", path);
        }

        var script = File.ReadAllText(path);
        return Import(dataSource, script);
    }

    public static int Import(DataSourceSettings dataSource, string script)
    {
        using var conn = DbConnectionFactory.Open(dataSource);
        var executed = 0;
        foreach (var batch in SplitSqlBatches(script))
        {
            using var cmd = DbConnectionFactory.CreateCommand(conn, batch);
            cmd.CommandTimeout = 120;
            executed += cmd.ExecuteNonQuery();
        }

        return executed;
    }

    private static int AppendTableInserts(
        DbConnection conn,
        StringBuilder sb,
        string table,
        IReadOnlyList<string> columns)
    {
        var colList = string.Join(", ", columns.Select(c => $"`{c}`"));
        using var cmd = DbConnectionFactory.CreateCommand(conn, $"SELECT {colList} FROM `{table}`");
        using var reader = cmd.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var values = new string[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                values[i] = FormatSqlLiteral(reader.IsDBNull(i) ? null : reader.GetValue(i));
            }

            sb.Append("INSERT INTO `").Append(table).Append("` (").Append(colList).Append(") VALUES (");
            sb.Append(string.Join(", ", values));
            sb.AppendLine(");");
            count++;
        }

        return count;
    }

    private static string FormatSqlLiteral(object? value) => value switch
    {
        null or DBNull => "NULL",
        bool b => b ? "1" : "0",
        byte or sbyte or short or ushort or int or uint or long or ulong =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        float or double or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",
        DateTimeOffset dto =>
            $"'{dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        _ => "'" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "") + "'",
    };

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "''", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

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
