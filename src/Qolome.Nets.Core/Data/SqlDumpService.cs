using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public sealed class SqlDumpService
{
    public async Task ExportAsync(string path)
    {
        await using var db = AppDatabase.CreateContext();
        var snapshot = await InventorySnapshot.LoadAsync(db);
        await File.WriteAllTextAsync(path, ToPortableSql(snapshot), new UTF8Encoding(false));
    }

    public async Task<int> ImportAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new ScanException("SQL 文件不存在。");
        }

        var text = await File.ReadAllTextAsync(path);
        var snapshot = TryParseSnapshot(text);
        await using var db = AppDatabase.CreateContext();
        if (snapshot is not null)
        {
            await snapshot.ReplaceAsync(db);
            return snapshot.Environments.Count + snapshot.DeviceTypes.Count + snapshot.Groups.Count
                + snapshot.Devices.Count + snapshot.Domains.Count + snapshot.Nics.Count + snapshot.PortPurposes.Count
                + snapshot.Topologies.Count;
        }

        return await ExecuteRawAsync(db, text);
    }

    public static string ToPortableSql(InventorySnapshot snapshot)
    {
        var sql = new StringBuilder();
        sql.AppendLine("-- qolome-nets SQL dump");
        sql.AppendLine("-- format: 2");
        sql.AppendLine("-- dialect: sqlite+mysql");
        sql.AppendLine($"-- exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sql.AppendLine("-- 仅含双方都支持的 DELETE / INSERT，可在 SQLite 与 MySQL 中导入。");
        sql.AppendLine();
        sql.AppendLine("BEGIN;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.NetworkTopology}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.DeviceNic}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.DeviceDomain}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.Device}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.DeviceGroup}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.PortPurpose}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.DeviceType}`;");
        sql.AppendLine($"DELETE FROM `{NetsTableNames.NetworkEnvironment}`;");
        sql.AppendLine();

        AppendInserts(sql, NetsTableNames.NetworkEnvironment, snapshot.Environments, x => Columns(
            ("Id", x.Id),
            ("Name", x.Name),
            ("Description", x.Description),
            ("DefaultCidr", x.DefaultCidr),
            ("CreatedAtUtc", x.CreatedAtUtc),
            ("UpdatedAtUtc", x.UpdatedAtUtc)));
        AppendInserts(sql, NetsTableNames.DeviceType, snapshot.DeviceTypes, x => Columns(
            ("Id", x.Id),
            ("Code", x.Code),
            ("Name", x.Name),
            ("Description", x.Description),
            ("SortOrder", x.SortOrder),
            ("IconFile", x.IconFile),
            ("IsCustomIcon", x.IsCustomIcon),
            ("ColorGroup", x.ColorGroup)));
        AppendInserts(sql, NetsTableNames.DeviceGroup, snapshot.Groups, x => Columns(
            ("Id", x.Id),
            ("NetworkEnvironmentId", x.NetworkEnvironmentId),
            ("Name", x.Name),
            ("Description", x.Description),
            ("CreatedAtUtc", x.CreatedAtUtc)));
        AppendInserts(sql, NetsTableNames.Device, snapshot.Devices, x => Columns(
            ("Id", x.Id),
            ("Code", x.Code),
            ("NetworkEnvironmentId", x.NetworkEnvironmentId),
            ("DeviceGroupId", x.DeviceGroupId),
            ("DeviceTypeId", x.DeviceTypeId),
            ("Hostname", x.Hostname),
            ("Vendor", x.Vendor),
            ("Brand", x.Brand),
            ("Model", x.Model),
            ("Description", x.Description),
            ("Ip", x.Ip),
            ("Mac", x.Mac),
            ("OsType", x.OsType),
            ("DeviceInfo", x.DeviceInfo),
            ("OpenPorts", x.OpenPorts),
            ("Remarks", x.Remarks),
            ("Source", x.Source),
            ("CreatedAtUtc", x.CreatedAtUtc),
            ("UpdatedAtUtc", x.UpdatedAtUtc)));
        AppendInserts(sql, NetsTableNames.DeviceDomain, snapshot.Domains, x => Columns(
            ("Id", x.Id),
            ("DeviceId", x.DeviceId),
            ("Domain", x.Domain)));
        AppendInserts(sql, NetsTableNames.DeviceNic, snapshot.Nics, x => Columns(
            ("Id", x.Id),
            ("DeviceId", x.DeviceId),
            ("Name", x.Name),
            ("Ip", x.Ip),
            ("Mac", x.Mac),
            ("SortOrder", x.SortOrder)));
        AppendInserts(sql, NetsTableNames.PortPurpose, snapshot.PortPurposes, x => Columns(
            ("Id", x.Id),
            ("Port", x.Port),
            ("Protocol", x.Protocol),
            ("Service", x.Service),
            ("Description", x.Description)));
        AppendInserts(sql, NetsTableNames.NetworkTopology, snapshot.Topologies, x => Columns(
            ("NetworkEnvironmentId", x.NetworkEnvironmentId),
            ("Json", x.Json),
            ("UpdatedAtUtc", x.UpdatedAtUtc)));

        sql.AppendLine("COMMIT;");
        return sql.ToString();
    }

    private static InventorySnapshot? TryParseSnapshot(string sql)
    {
        var snapshot = new InventorySnapshot();
        var found = false;
        foreach (var statement in SplitStatements(sql))
        {
            if (!TryParseInsert(statement, out var table, out var row))
            {
                continue;
            }

            found = true;
            switch (table)
            {
                case NetsTableNames.NetworkEnvironment:
                    snapshot.Environments.Add(MapEnvironment(row));
                    break;
                case NetsTableNames.DeviceType:
                    snapshot.DeviceTypes.Add(MapDeviceType(row));
                    break;
                case NetsTableNames.DeviceGroup:
                    snapshot.Groups.Add(MapGroup(row));
                    break;
                case NetsTableNames.Device:
                    snapshot.Devices.Add(MapDevice(row));
                    break;
                case NetsTableNames.DeviceDomain:
                    snapshot.Domains.Add(MapDomain(row));
                    break;
                case NetsTableNames.DeviceNic:
                    snapshot.Nics.Add(MapNic(row));
                    break;
                case NetsTableNames.PortPurpose:
                    snapshot.PortPurposes.Add(MapPortPurpose(row));
                    break;
                case NetsTableNames.NetworkTopology:
                    snapshot.Topologies.Add(MapTopology(row));
                    break;
            }
        }

        return found ? snapshot : null;
    }

    private static async Task<int> ExecuteRawAsync(NetsDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();
        var executed = 0;
        try
        {
            foreach (var statement in SplitStatements(sql))
            {
                if (ShouldSkip(statement))
                {
                    continue;
                }

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = statement;
                cmd.CommandTimeout = 120;
                await cmd.ExecuteNonQueryAsync();
                executed++;
            }

            return executed;
        }
        catch (Exception ex)
        {
            throw new ScanException($"导入 SQL 失败：{ex.Message}");
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static bool ShouldSkip(string statement)
    {
        var text = statement.TrimStart();
        return text.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("SET", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("LOCK", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("UNLOCK", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ALTER", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("USE", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendInserts<T>(
        StringBuilder sql,
        string table,
        IReadOnlyList<T> rows,
        Func<T, (string Name, object? Value)[]> columns)
    {
        foreach (var row in rows)
        {
            var cols = columns(row);
            sql.Append("INSERT INTO `").Append(table).Append("` (");
            sql.Append(string.Join(", ", cols.Select(c => $"`{c.Name}`")));
            sql.Append(") VALUES (");
            sql.Append(string.Join(", ", cols.Select(c => Literal(c.Value))));
            sql.AppendLine(");");
        }

        if (rows.Count > 0)
        {
            sql.AppendLine();
        }
    }

    private static (string Name, object? Value)[] Columns(params (string Name, object? Value)[] values) => values;

    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        bool flag => flag ? "1" : "0",
        byte or sbyte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        float or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        DateTime dt => $"'{dt.ToUniversalTime():yyyy-MM-dd HH:mm:ss}'",
        _ => $"'{Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}'"
    };

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static bool TryParseInsert(string statement, out string table, out Dictionary<string, object?> row)
    {
        table = "";
        row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var text = statement.Trim();
        if (!text.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var into = text.IndexOf("INTO", StringComparison.OrdinalIgnoreCase);
        var valuesAt = text.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (into < 0 || valuesAt < 0 || valuesAt <= into)
        {
            return false;
        }

        var head = text[(into + 4)..valuesAt].Trim();
        var paren = head.IndexOf('(');
        if (paren < 0)
        {
            return false;
        }

        table = NormalizeImportTable(Unquote(head[..paren].Trim()).ToLowerInvariant());
        var columns = SplitCsv(head[(paren + 1)..head.LastIndexOf(')')]).Select(Unquote).ToList();
        var valuesPart = text[(valuesAt + 6)..].Trim();
        if (valuesPart.StartsWith('(') && valuesPart.EndsWith(')'))
        {
            valuesPart = valuesPart[1..^1];
        }

        var values = ParseSqlValues(valuesPart);
        if (columns.Count != values.Count)
        {
            return false;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            row[columns[i]] = values[i];
        }

        return table is NetsTableNames.NetworkEnvironment
            or NetsTableNames.DeviceType
            or NetsTableNames.DeviceGroup
            or NetsTableNames.Device
            or NetsTableNames.DeviceDomain
            or NetsTableNames.DeviceNic
            or NetsTableNames.PortPurpose
            or NetsTableNames.NetworkTopology;
    }

    private static string NormalizeImportTable(string table) => table switch
    {
        "network_environment" => NetsTableNames.NetworkEnvironment,
        "network_topology" => NetsTableNames.NetworkTopology,
        "device_domain" => NetsTableNames.DeviceDomain,
        "device_group" => NetsTableNames.DeviceGroup,
        "device_type" => NetsTableNames.DeviceType,
        "device_nic" => NetsTableNames.DeviceNic,
        "port_purpose" => NetsTableNames.PortPurpose,
        "device" => NetsTableNames.Device,
        _ => table,
    };

    private static string Unquote(string name) => name.Trim().Trim('`', '"', '[', ']');

    private static List<string> SplitCsv(string text)
    {
        var items = new List<string>();
        var buffer = new StringBuilder();
        var inString = false;
        foreach (var c in text)
        {
            if (inString)
            {
                buffer.Append(c);
                if (c == '\'')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '\'')
            {
                inString = true;
                buffer.Append(c);
                continue;
            }

            if (c == ',')
            {
                items.Add(buffer.ToString().Trim());
                buffer.Clear();
                continue;
            }

            buffer.Append(c);
        }

        if (buffer.Length > 0)
        {
            items.Add(buffer.ToString().Trim());
        }

        return items;
    }

    private static List<object?> ParseSqlValues(string text)
    {
        var values = new List<object?>();
        var buffer = new StringBuilder();
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    buffer.Append('\'');
                    i++;
                    continue;
                }

                if (c == '\'')
                {
                    inString = false;
                    values.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    continue;
                }

                buffer.Append(c);
                continue;
            }

            if (c == '\'')
            {
                inString = true;
                continue;
            }

            if (c == ',')
            {
                AddToken(values, buffer.ToString());
                buffer.Clear();
                continue;
            }

            buffer.Append(c);
        }

        AddToken(values, buffer.ToString());
        return values;
    }

    private static void AddToken(List<object?> values, string token)
    {
        var text = token.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (text.Equals("NULL", StringComparison.OrdinalIgnoreCase))
        {
            values.Add(null);
            return;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            values.Add(number);
            return;
        }

        values.Add(text);
    }

    private static NetworkEnvironment MapEnvironment(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        Name = Str(row, "Name") ?? "",
        Description = Str(row, "Description"),
        DefaultCidr = Str(row, "DefaultCidr"),
        CreatedAtUtc = Time(row, "CreatedAtUtc"),
        UpdatedAtUtc = Time(row, "UpdatedAtUtc")
    };

    private static DeviceType MapDeviceType(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        Code = (Str(row, "Code") ?? "").Trim(),
        Name = (Str(row, "Name") ?? "").Trim(),
        Description = Str(row, "Description"),
        SortOrder = Int(row, "SortOrder"),
        IconFile = Str(row, "IconFile"),
        IsCustomIcon = Flag(row, "IsCustomIcon"),
        ColorGroup = DeviceTypeColorGroups.Normalize(Str(row, "ColorGroup"), Str(row, "Code"))
    };

    private static DeviceGroup MapGroup(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        NetworkEnvironmentId = Int(row, "NetworkEnvironmentId"),
        Name = Str(row, "Name") ?? "",
        Description = Str(row, "Description"),
        CreatedAtUtc = Time(row, "CreatedAtUtc")
    };

    private static Device MapDevice(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        Code = Str(row, "Code") ?? "",
        NetworkEnvironmentId = Int(row, "NetworkEnvironmentId"),
        DeviceGroupId = NInt(row, "DeviceGroupId"),
        DeviceTypeId = NInt(row, "DeviceTypeId"),
        Hostname = Str(row, "Hostname"),
        Vendor = Str(row, "Vendor"),
        Brand = Str(row, "Brand"),
        Model = Str(row, "Model"),
        Description = Str(row, "Description"),
        Ip = Str(row, "Ip"),
        Mac = Str(row, "Mac"),
        OsType = Str(row, "OsType"),
        DeviceInfo = Str(row, "DeviceInfo"),
        OpenPorts = Str(row, "OpenPorts"),
        Remarks = Str(row, "Remarks"),
        Source = Str(row, "Source") ?? "Import",
        CreatedAtUtc = Time(row, "CreatedAtUtc"),
        UpdatedAtUtc = Time(row, "UpdatedAtUtc")
    };

    private static DeviceDomain MapDomain(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        DeviceId = Int(row, "DeviceId"),
        Domain = Str(row, "Domain") ?? ""
    };

    private static DeviceNic MapNic(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        DeviceId = Int(row, "DeviceId"),
        Name = Str(row, "Name"),
        Ip = Str(row, "Ip"),
        Mac = Str(row, "Mac"),
        SortOrder = Int(row, "SortOrder")
    };

    private static PortPurpose MapPortPurpose(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, "Id"),
        Port = Int(row, "Port"),
        Protocol = Str(row, "Protocol") ?? "TCP",
        Service = Str(row, "Service"),
        Description = Str(row, "Description")
    };

    private static NetworkTopology MapTopology(Dictionary<string, object?> row) => new()
    {
        NetworkEnvironmentId = Int(row, "NetworkEnvironmentId"),
        Json = Str(row, "Json") ?? "",
        UpdatedAtUtc = Time(row, "UpdatedAtUtc")
    };

    private static int Int(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : 0;

    private static int? NInt(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : null;

    private static bool Flag(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null && Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;

    private static string? Str(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value as string ?? value?.ToString() : null;

    private static DateTime Time(Dictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null)
        {
            return DateTime.UtcNow;
        }

        if (value is DateTime dt)
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.UtcNow;
    }

    public static IEnumerable<string> SplitStatements(string sql)
    {
        var buffer = new StringBuilder();
        var inString = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (inString)
            {
                buffer.Append(c);
                if (c == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    buffer.Append(sql[++i]);
                    continue;
                }

                if (c == '\'')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '\'')
            {
                inString = true;
                buffer.Append(c);
                continue;
            }

            if (c == ';')
            {
                var statement = buffer.ToString().Trim();
                buffer.Clear();
                if (statement.Length > 0)
                {
                    yield return statement;
                }

                continue;
            }

            buffer.Append(c);
        }

        var last = buffer.ToString().Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }
}
