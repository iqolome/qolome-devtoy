namespace Qolome.Devtoy.Core.Services;

using MySqlConnector;
using Qolome.Devtoy.Core.Models;

public sealed class DictCatalogService
{
    private const string ListValuesSql = """
        SELECT val_id, val_value, val_code, val_name, val_enum, val_scope, val_desc, seq, remark, enabled
        FROM qolome_dict_val
        WHERE item_id = @item_id AND deleted = 0
        ORDER BY seq, val_id
        """;

    public IReadOnlyList<DictItemRecord> ListEnumItems(
        DataSourceSettings dataSource,
        string? itemCode = null,
        string? itemName = null)
    {
        var sql = """
            SELECT item_id, item_code, item_name, item_scope, item_desc, enum_flag,
                   data_reference_type, remark, enabled, create_time, modify_time
            FROM qolome_dict_item
            WHERE enum_flag = 1 AND deleted = 0 AND enabled = 1
            """;
        var code = NormalizeFilter(itemCode);
        var name = NormalizeFilter(itemName);
        if (code is not null)
        {
            sql += " AND item_code LIKE @item_code ESCAPE '\\\\'";
        }

        if (name is not null)
        {
            sql += " AND item_name LIKE @item_name ESCAPE '\\\\'";
        }

        sql += " ORDER BY item_code";

        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        if (code is not null)
        {
            cmd.Parameters.AddWithValue("@item_code", "%" + EscapeLike(code) + "%");
        }

        if (name is not null)
        {
            cmd.Parameters.AddWithValue("@item_name", "%" + EscapeLike(name) + "%");
        }

        using var reader = cmd.ExecuteReader();
        var list = new List<DictItemRecord>();
        while (reader.Read())
        {
            list.Add(ReadItemRecord(reader));
        }

        return list;
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>按 item_code 查找字典项（生成枚举 / TS 用）。</summary>
    public DictItemRecord? FindItemByCode(DataSourceSettings dataSource, string itemCode)
    {
        const string sql = """
            SELECT item_id, item_code, item_name, item_scope, item_desc, enum_flag,
                   data_reference_type, remark, enabled, create_time, modify_time
            FROM qolome_dict_item
            WHERE item_code = @item_code AND deleted = 0
            LIMIT 1
            """;
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@item_code", itemCode);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadItemRecord(reader) : null;
    }

    public IReadOnlyList<DictValueRow> ListValues(DataSourceSettings dataSource, long itemId)
    {
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(ListValuesSql, conn);
        cmd.Parameters.AddWithValue("@item_id", itemId);
        using var reader = cmd.ExecuteReader();
        var list = new List<DictValueRow>();
        while (reader.Read())
        {
            list.Add(ReadValue(reader, itemId));
        }

        return list;
    }

    public long CreateItem(DataSourceSettings dataSource, DictItemRecord record)
    {
        EnsureItemCodeFree(dataSource, record.ItemCode, null);
        var itemId = record.ItemId > 0 ? record.ItemId : DictIdGenerator.NextId();
        const string sql = """
            INSERT INTO qolome_dict_item (
                item_id, item_code, item_name, item_scope, item_desc, enum_flag, data_reference_type,
                remark, modify_version, deleted, enabled, tenant_id, create_time, modify_time
            ) VALUES (
                @item_id, @item_code, @item_name, @item_scope, @item_desc, @enum_flag, @data_reference_type,
                @remark, 0, 0, @enabled, 0, @now, @now
            )
            """;
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        var now = DateTime.Now;
        cmd.Parameters.AddWithValue("@item_id", itemId);
        cmd.Parameters.AddWithValue("@item_code", record.ItemCode);
        cmd.Parameters.AddWithValue("@item_name", record.ItemName);
        SetNullableInt(cmd, "@item_scope", record.ItemScope);
        cmd.Parameters.AddWithValue("@item_desc", (object?)record.ItemDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enum_flag", BoolToInt(record.EnumFlag));
        SetNullableInt(cmd, "@data_reference_type", record.DataReferenceType);
        cmd.Parameters.AddWithValue("@remark", (object?)record.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enabled", BoolToInt(record.Enabled));
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
        return itemId;
    }

    public void UpdateItem(DataSourceSettings dataSource, DictItemRecord record)
    {
        if (record.ItemId <= 0)
        {
            throw new ArgumentException("itemId 无效");
        }

        EnsureItemCodeFree(dataSource, record.ItemCode, record.ItemId);
        const string sql = """
            UPDATE qolome_dict_item
            SET item_code = @item_code, item_name = @item_name, item_scope = @item_scope, item_desc = @item_desc,
                enum_flag = @enum_flag, data_reference_type = @data_reference_type, remark = @remark,
                enabled = @enabled, modify_time = @now
            WHERE item_id = @item_id AND deleted = 0
            """;
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@item_code", record.ItemCode);
        cmd.Parameters.AddWithValue("@item_name", record.ItemName);
        SetNullableInt(cmd, "@item_scope", record.ItemScope);
        cmd.Parameters.AddWithValue("@item_desc", (object?)record.ItemDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enum_flag", BoolToInt(record.EnumFlag));
        SetNullableInt(cmd, "@data_reference_type", record.DataReferenceType);
        cmd.Parameters.AddWithValue("@remark", (object?)record.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enabled", BoolToInt(record.Enabled));
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        cmd.Parameters.AddWithValue("@item_id", record.ItemId);
        if (cmd.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("字典项不存在或已删除: " + record.ItemId);
        }
    }

    public void DeleteItem(DataSourceSettings dataSource, long itemId)
    {
        const string sql = "UPDATE qolome_dict_item SET deleted = 1, modify_time = @now WHERE item_id = @item_id AND deleted = 0";
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        cmd.Parameters.AddWithValue("@item_id", itemId);
        if (cmd.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("字典项不存在: " + itemId);
        }
    }

    public long CreateValue(DataSourceSettings dataSource, DictValueRow value)
    {
        EnsureValueUnique(dataSource, value.ItemId, value.ValCode, value.ValValue, null);
        var valId = value.ValId > 0 ? value.ValId : DictIdGenerator.NextId();
        const string sql = """
            INSERT INTO qolome_dict_val (
                val_id, item_id, val_value, val_code, val_name, val_enum, val_scope, val_desc, seq,
                remark, modify_version, deleted, enabled, tenant_id, create_time, modify_time
            ) VALUES (
                @val_id, @item_id, @val_value, @val_code, @val_name, @val_enum, @val_scope, @val_desc, @seq,
                @remark, 0, 0, @enabled, 0, @now, @now
            )
            """;
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        var now = DateTime.Now;
        cmd.Parameters.AddWithValue("@val_id", valId);
        cmd.Parameters.AddWithValue("@item_id", value.ItemId);
        cmd.Parameters.AddWithValue("@val_value", value.ValValue);
        cmd.Parameters.AddWithValue("@val_code", value.ValCode);
        cmd.Parameters.AddWithValue("@val_name", value.ValName);
        cmd.Parameters.AddWithValue("@val_enum", (object?)value.ValEnum ?? DBNull.Value);
        SetNullableInt(cmd, "@val_scope", value.ValScope);
        cmd.Parameters.AddWithValue("@val_desc", (object?)value.ValDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seq", value.Seq);
        cmd.Parameters.AddWithValue("@remark", (object?)value.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enabled", BoolToInt(value.Enabled));
        cmd.Parameters.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
        return valId;
    }

    public void UpdateValue(DataSourceSettings dataSource, DictValueRow value)
    {
        if (value.ValId <= 0)
        {
            throw new ArgumentException("valId 无效");
        }

        EnsureValueUnique(dataSource, value.ItemId, value.ValCode, value.ValValue, value.ValId);
        const string sql = """
            UPDATE qolome_dict_val
            SET val_value = @val_value, val_code = @val_code, val_name = @val_name, val_enum = @val_enum,
                val_scope = @val_scope, val_desc = @val_desc, seq = @seq, remark = @remark,
                enabled = @enabled, modify_time = @now
            WHERE val_id = @val_id AND deleted = 0
            """;
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@val_value", value.ValValue);
        cmd.Parameters.AddWithValue("@val_code", value.ValCode);
        cmd.Parameters.AddWithValue("@val_name", value.ValName);
        cmd.Parameters.AddWithValue("@val_enum", (object?)value.ValEnum ?? DBNull.Value);
        SetNullableInt(cmd, "@val_scope", value.ValScope);
        cmd.Parameters.AddWithValue("@val_desc", (object?)value.ValDesc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@seq", value.Seq);
        cmd.Parameters.AddWithValue("@remark", (object?)value.Remark ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enabled", BoolToInt(value.Enabled));
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        cmd.Parameters.AddWithValue("@val_id", value.ValId);
        if (cmd.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("字典值不存在: " + value.ValId);
        }
    }

    public void DeleteValue(DataSourceSettings dataSource, long valId)
    {
        const string sql = "UPDATE qolome_dict_val SET deleted = 1, modify_time = @now WHERE val_id = @val_id AND deleted = 0";
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@now", DateTime.Now);
        cmd.Parameters.AddWithValue("@val_id", valId);
        if (cmd.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("字典值不存在: " + valId);
        }
    }

    private static void EnsureItemCodeFree(DataSourceSettings dataSource, string itemCode, long? excludeItemId)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            throw new ArgumentException("字典项编码不能为空");
        }

        var sql = excludeItemId is null
            ? "SELECT 1 FROM qolome_dict_item WHERE item_code = @code AND deleted = 0 LIMIT 1"
            : "SELECT 1 FROM qolome_dict_item WHERE item_code = @code AND item_id <> @id AND deleted = 0 LIMIT 1";
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@code", itemCode.Trim());
        if (excludeItemId is not null)
        {
            cmd.Parameters.AddWithValue("@id", excludeItemId.Value);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException("字典项编码已存在: " + itemCode);
        }
    }

    private static void EnsureValueUnique(
        DataSourceSettings dataSource,
        long itemId,
        string valCode,
        string valValue,
        long? excludeValId)
    {
        var sql = """
            SELECT 1 FROM qolome_dict_val
            WHERE item_id = @item_id AND deleted = 0 AND (val_code = @val_code OR val_value = @val_value)
            """ + (excludeValId is null ? "" : " AND val_id <> @val_id") + " LIMIT 1";
        using var conn = MySqlConnectionFactory.Open(dataSource);
        using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@item_id", itemId);
        cmd.Parameters.AddWithValue("@val_code", valCode);
        cmd.Parameters.AddWithValue("@val_value", valValue);
        if (excludeValId is not null)
        {
            cmd.Parameters.AddWithValue("@val_id", excludeValId.Value);
        }

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException("同项下 val_code 或 val_value 已存在");
        }
    }

    private static DictItemRecord ReadItemRecord(MySqlDataReader reader)
    {
        return new DictItemRecord
        {
            ItemId = reader.GetInt64(0),
            ItemCode = reader.GetString(1),
            ItemName = reader.GetString(2),
            ItemScope = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            ItemDesc = reader.IsDBNull(4) ? null : reader.GetString(4),
            EnumFlag = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
            DataReferenceType = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            Remark = reader.IsDBNull(7) ? null : reader.GetString(7),
            Enabled = reader.IsDBNull(8) || reader.GetInt32(8) != 0,
            CreateTime = ReadDateTime(reader, 9),
            ModifyTime = ReadDateTime(reader, 10),
        };
    }

    private static DateTime? ReadDateTime(MySqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            _ => reader.GetDateTime(ordinal),
        };
    }

    private static DictValueRow ReadValue(MySqlDataReader reader, long itemId)
    {
        return new DictValueRow
        {
            ValId = reader.GetInt64(0),
            ItemId = itemId,
            ValValue = reader.GetString(1),
            ValCode = reader.GetString(2),
            ValName = reader.GetString(3),
            ValEnum = reader.IsDBNull(4) ? null : reader.GetString(4),
            ValScope = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            ValDesc = reader.IsDBNull(6) ? null : reader.GetString(6),
            Seq = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            Remark = reader.IsDBNull(8) ? null : reader.GetString(8),
            Enabled = reader.IsDBNull(9) || reader.GetInt32(9) != 0,
        };
    }

    private static int BoolToInt(bool value) => value ? 1 : 0;

    private static void SetNullableInt(MySqlCommand cmd, string name, int? value)
    {
        cmd.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);
    }
}