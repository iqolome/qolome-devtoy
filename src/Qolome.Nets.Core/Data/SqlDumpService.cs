using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public static class SqlDumpService
{
    private static class SqlColumns
    {
        public const string Id = "Id";
        public const string Name = "Name";
        public const string Description = "Description";
        public const string Code = "Code";
        public const string SortOrder = "SortOrder";
        public const string IconFile = "IconFile";
        public const string IsCustomIcon = "IsCustomIcon";
        public const string ColorGroup = "ColorGroup";
        public const string NetworkEnvironmentId = "NetworkEnvironmentId";
        public const string DeviceGroupId = "DeviceGroupId";
        public const string DeviceTypeId = "DeviceTypeId";
        public const string DefaultCidr = "DefaultCidr";
        public const string CreatedAtUtc = "CreatedAtUtc";
        public const string UpdatedAtUtc = "UpdatedAtUtc";
        public const string Hostname = "Hostname";
        public const string Vendor = "Vendor";
        public const string Brand = "Brand";
        public const string Model = "Model";
        public const string Ip = "Ip";
        public const string Mac = "Mac";
        public const string OsType = "OsType";
        public const string DeviceInfo = "DeviceInfo";
        public const string OpenPorts = "OpenPorts";
        public const string Remarks = "Remarks";
        public const string Source = "Source";
        public const string DeviceId = "DeviceId";
        public const string Domain = "Domain";
        public const string Port = "Port";
        public const string Protocol = "Protocol";
        public const string Service = "Service";
        public const string Json = "Json";
    }
    public static async Task ExportAsync(string path)
    {
        await using var db = AppDatabase.CreateContext();
        var snapshot = await InventorySnapshot.LoadAsync(db);
        await File.WriteAllTextAsync(path, ToPortableSql(snapshot), new UTF8Encoding(false));
    }

    public static async Task<int> ImportAsync(string path)
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
        sql.AppendLine("-- Qolome DevToy network asset SQL dump");
        sql.AppendLine("-- format: 2");
        sql.AppendLine("-- target: remote business database (MySQL)");
        sql.AppendLine($"-- exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sql.AppendLine("-- DELETE + INSERT；导入到「数据源设置」远程业务库。");
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
            (SqlColumns.Id, x.Id),
            (SqlColumns.Name, x.Name),
            (SqlColumns.Description, x.Description),
            (SqlColumns.DefaultCidr, x.DefaultCidr),
            (SqlColumns.CreatedAtUtc, x.CreatedAtUtc),
            (SqlColumns.UpdatedAtUtc, x.UpdatedAtUtc)));
        AppendInserts(sql, NetsTableNames.DeviceType, snapshot.DeviceTypes, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.Code, x.Code),
            (SqlColumns.Name, x.Name),
            (SqlColumns.Description, x.Description),
            (SqlColumns.SortOrder, x.SortOrder),
            (SqlColumns.IconFile, x.IconFile),
            (SqlColumns.IsCustomIcon, x.IsCustomIcon),
            (SqlColumns.ColorGroup, x.ColorGroup)));
        AppendInserts(sql, NetsTableNames.DeviceGroup, snapshot.Groups, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.NetworkEnvironmentId, x.NetworkEnvironmentId),
            (SqlColumns.Name, x.Name),
            (SqlColumns.Description, x.Description),
            (SqlColumns.CreatedAtUtc, x.CreatedAtUtc)));
        AppendInserts(sql, NetsTableNames.Device, snapshot.Devices, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.Code, x.Code),
            (SqlColumns.NetworkEnvironmentId, x.NetworkEnvironmentId),
            (SqlColumns.DeviceGroupId, x.DeviceGroupId),
            (SqlColumns.DeviceTypeId, x.DeviceTypeId),
            (SqlColumns.Hostname, x.Hostname),
            (SqlColumns.Vendor, x.Vendor),
            (SqlColumns.Brand, x.Brand),
            (SqlColumns.Model, x.Model),
            (SqlColumns.Description, x.Description),
            (SqlColumns.Ip, x.Ip),
            (SqlColumns.Mac, x.Mac),
            (SqlColumns.OsType, x.OsType),
            (SqlColumns.DeviceInfo, x.DeviceInfo),
            (SqlColumns.OpenPorts, x.OpenPorts),
            (SqlColumns.Remarks, x.Remarks),
            (SqlColumns.Source, x.Source),
            (SqlColumns.CreatedAtUtc, x.CreatedAtUtc),
            (SqlColumns.UpdatedAtUtc, x.UpdatedAtUtc)));
        AppendInserts(sql, NetsTableNames.DeviceDomain, snapshot.Domains, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.DeviceId, x.DeviceId),
            (SqlColumns.Domain, x.Domain)));
        AppendInserts(sql, NetsTableNames.DeviceNic, snapshot.Nics, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.DeviceId, x.DeviceId),
            (SqlColumns.Name, x.Name),
            (SqlColumns.Ip, x.Ip),
            (SqlColumns.Mac, x.Mac),
            (SqlColumns.SortOrder, x.SortOrder)));
        AppendInserts(sql, NetsTableNames.PortPurpose, snapshot.PortPurposes, x => Columns(
            (SqlColumns.Id, x.Id),
            (SqlColumns.Port, x.Port),
            (SqlColumns.Protocol, x.Protocol),
            (SqlColumns.Service, x.Service),
            (SqlColumns.Description, x.Description)));
        AppendInserts(sql, NetsTableNames.NetworkTopology, snapshot.Topologies, x => Columns(
            (SqlColumns.NetworkEnvironmentId, x.NetworkEnvironmentId),
            (SqlColumns.Json, x.Json),
            (SqlColumns.UpdatedAtUtc, x.UpdatedAtUtc)));

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
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    buffer.Append('\'');
                    i += 2;
                    continue;
                }

                if (c == '\'')
                {
                    inString = false;
                    values.Add(buffer.ToString().Trim());
                    buffer.Clear();
                    i++;
                    continue;
                }

                buffer.Append(c);
                i++;
                continue;
            }

            if (c == '\'')
            {
                inString = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                AddToken(values, buffer.ToString());
                buffer.Clear();
                i++;
                continue;
            }

            buffer.Append(c);
            i++;
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
        Id = Int(row, SqlColumns.Id),
        Name = Str(row, SqlColumns.Name) ?? "",
        Description = Str(row, SqlColumns.Description),
        DefaultCidr = Str(row, SqlColumns.DefaultCidr),
        CreatedAtUtc = Time(row, SqlColumns.CreatedAtUtc),
        UpdatedAtUtc = Time(row, SqlColumns.UpdatedAtUtc)
    };

    private static DeviceType MapDeviceType(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        Code = (Str(row, SqlColumns.Code) ?? "").Trim(),
        Name = (Str(row, SqlColumns.Name) ?? "").Trim(),
        Description = Str(row, SqlColumns.Description),
        SortOrder = Int(row, SqlColumns.SortOrder),
        IconFile = Str(row, SqlColumns.IconFile),
        IsCustomIcon = Flag(row, SqlColumns.IsCustomIcon),
        ColorGroup = DeviceTypeColorGroups.Normalize(Str(row, SqlColumns.ColorGroup), Str(row, SqlColumns.Code))
    };

    private static DeviceGroup MapGroup(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        NetworkEnvironmentId = Int(row, SqlColumns.NetworkEnvironmentId),
        Name = Str(row, SqlColumns.Name) ?? "",
        Description = Str(row, SqlColumns.Description),
        CreatedAtUtc = Time(row, SqlColumns.CreatedAtUtc)
    };

    private static Device MapDevice(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        Code = Str(row, SqlColumns.Code) ?? "",
        NetworkEnvironmentId = Int(row, SqlColumns.NetworkEnvironmentId),
        DeviceGroupId = NInt(row, SqlColumns.DeviceGroupId),
        DeviceTypeId = NInt(row, SqlColumns.DeviceTypeId),
        Hostname = Str(row, SqlColumns.Hostname),
        Vendor = Str(row, SqlColumns.Vendor),
        Brand = Str(row, SqlColumns.Brand),
        Model = Str(row, SqlColumns.Model),
        Description = Str(row, SqlColumns.Description),
        Ip = Str(row, SqlColumns.Ip),
        Mac = Str(row, SqlColumns.Mac),
        OsType = Str(row, SqlColumns.OsType),
        DeviceInfo = Str(row, SqlColumns.DeviceInfo),
        OpenPorts = Str(row, SqlColumns.OpenPorts),
        Remarks = Str(row, SqlColumns.Remarks),
        Source = Str(row, SqlColumns.Source) ?? "Import",
        CreatedAtUtc = Time(row, SqlColumns.CreatedAtUtc),
        UpdatedAtUtc = Time(row, SqlColumns.UpdatedAtUtc)
    };

    private static DeviceDomain MapDomain(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        DeviceId = Int(row, SqlColumns.DeviceId),
        Domain = Str(row, SqlColumns.Domain) ?? ""
    };

    private static DeviceNic MapNic(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        DeviceId = Int(row, SqlColumns.DeviceId),
        Name = Str(row, SqlColumns.Name),
        Ip = Str(row, SqlColumns.Ip),
        Mac = Str(row, SqlColumns.Mac),
        SortOrder = Int(row, SqlColumns.SortOrder)
    };

    private static PortPurpose MapPortPurpose(Dictionary<string, object?> row) => new()
    {
        Id = Int(row, SqlColumns.Id),
        Port = Int(row, SqlColumns.Port),
        Protocol = Str(row, SqlColumns.Protocol) ?? "TCP",
        Service = Str(row, SqlColumns.Service),
        Description = Str(row, SqlColumns.Description)
    };

    private static NetworkTopology MapTopology(Dictionary<string, object?> row) => new()
    {
        NetworkEnvironmentId = Int(row, SqlColumns.NetworkEnvironmentId),
        Json = Str(row, SqlColumns.Json) ?? "",
        UpdatedAtUtc = Time(row, SqlColumns.UpdatedAtUtc)
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
        var i = 0;
        while (i < sql.Length)
        {
            if (inString)
            {
                i = AppendInsideString(sql, buffer, ref inString, i);
                i++;
                continue;
            }

            if (TrySkipLineComment(sql, ref i))
            {
                i++;
                continue;
            }

            var c = sql[i];
            if (c == '\'')
            {
                inString = true;
                buffer.Append(c);
                i++;
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

                i++;
                continue;
            }

            buffer.Append(c);
            i++;
        }

        var last = buffer.ToString().Trim();
        if (last.Length > 0)
        {
            yield return last;
        }
    }

    private static int AppendInsideString(string sql, StringBuilder buffer, ref bool inString, int i)
    {
        var c = sql[i];
        buffer.Append(c);
        if (c == '\'' && i + 1 < sql.Length && sql[i + 1] == '\'')
        {
            buffer.Append(sql[i + 1]);
            return i + 1;
        }

        if (c == '\'')
        {
            inString = false;
        }

        return i;
    }

    private static bool TrySkipLineComment(string sql, ref int i) =>
        sql[i] == '-' && i + 1 < sql.Length && sql[i + 1] == '-'
        && AdvancePastLineEnd(sql, ref i);

    private static bool AdvancePastLineEnd(string sql, ref int i)
    {
        while (i < sql.Length && sql[i] != '\n')
        {
            i++;
        }

        return true;
    }
}
