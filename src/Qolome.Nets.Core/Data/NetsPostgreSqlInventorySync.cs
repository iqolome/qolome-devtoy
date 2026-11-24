using System.Data.Common;
using Npgsql;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Nets.Core.Data;

/// <summary>PostgreSQL 侧网络资产表结构准备与快照读写（ADO，无 EF）。</summary>
internal static class NetsPostgreSqlInventorySync
{
    private const string PName = "@name";
    private const string PDesc = "@desc";

    private static readonly string[] ResetTables = NetsTableNames.PgInventoryTables;

    public static async Task EnsureSchemaAsync(NpgsqlConnection conn)
    {
        var statements = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS devtoy_network_environment (
                "Id" SERIAL PRIMARY KEY,
                "Name" VARCHAR(64) NOT NULL,
                "Description" VARCHAR(256) NULL,
                "DefaultCidr" VARCHAR(64) NULL,
                "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
                "UpdatedAtUtc" TIMESTAMPTZ NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_network_environment_name ON devtoy_network_environment ("Name");
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_device_type (
                "Id" SERIAL PRIMARY KEY,
                "Code" VARCHAR(32) NOT NULL,
                "Name" VARCHAR(64) NOT NULL,
                "Description" VARCHAR(256) NULL,
                "SortOrder" INT NOT NULL DEFAULT 0,
                "IconFile" VARCHAR(128) NULL,
                "IsCustomIcon" BOOLEAN NOT NULL DEFAULT FALSE,
                "ColorGroup" VARCHAR(32) NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_type_code ON devtoy_device_type ("Code");
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_type_name ON devtoy_device_type ("Name");
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_device_group (
                "Id" SERIAL PRIMARY KEY,
                "NetworkEnvironmentId" INT NOT NULL,
                "Name" VARCHAR(64) NOT NULL,
                "Description" VARCHAR(256) NULL,
                "CreatedAtUtc" TIMESTAMPTZ NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_group_env_name ON devtoy_device_group ("NetworkEnvironmentId", "Name");
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_device (
                "Id" SERIAL PRIMARY KEY,
                "Code" VARCHAR(64) NOT NULL,
                "NetworkEnvironmentId" INT NOT NULL,
                "DeviceGroupId" INT NULL,
                "DeviceTypeId" INT NULL,
                "Hostname" VARCHAR(255) NULL,
                "Vendor" VARCHAR(256) NULL,
                "Brand" VARCHAR(128) NULL,
                "Model" VARCHAR(128) NULL,
                "Description" VARCHAR(512) NULL,
                "Ip" VARCHAR(64) NULL,
                "Mac" VARCHAR(32) NULL,
                "OsType" VARCHAR(64) NULL,
                "DeviceInfo" TEXT NULL,
                "OpenPorts" TEXT NULL,
                "Remarks" TEXT NULL,
                "Source" VARCHAR(16) NULL,
                "CreatedAtUtc" TIMESTAMPTZ NOT NULL,
                "UpdatedAtUtc" TIMESTAMPTZ NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_devtoy_device_code ON devtoy_device ("Code");
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_device_domain (
                "Id" SERIAL PRIMARY KEY,
                "DeviceId" INT NOT NULL,
                "Domain" VARCHAR(255) NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_device_nic (
                "Id" SERIAL PRIMARY KEY,
                "DeviceId" INT NOT NULL,
                "Name" VARCHAR(64) NULL,
                "Ip" VARCHAR(64) NULL,
                "Mac" VARCHAR(32) NULL,
                "SortOrder" INT NOT NULL DEFAULT 0
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_port_purpose (
                "Id" SERIAL PRIMARY KEY,
                "Port" INT NOT NULL,
                "Protocol" VARCHAR(8) NOT NULL,
                "Service" VARCHAR(64) NULL,
                "Description" VARCHAR(256) NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS devtoy_network_topology (
                "NetworkEnvironmentId" INT PRIMARY KEY,
                "Json" TEXT NOT NULL,
                "UpdatedAtUtc" TIMESTAMPTZ NOT NULL
            );
            """,
        };

        foreach (var sql in statements)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    public static async Task<InventorySnapshot> LoadAsync(NpgsqlConnection conn)
    {
        return new InventorySnapshot
        {
            Environments = await ReadEnvironmentsAsync(conn).ConfigureAwait(false),
            DeviceTypes = await ReadDeviceTypesAsync(conn).ConfigureAwait(false),
            Groups = await ReadGroupsAsync(conn).ConfigureAwait(false),
            Devices = await ReadDevicesAsync(conn).ConfigureAwait(false),
            Domains = await ReadDomainsAsync(conn).ConfigureAwait(false),
            Nics = await ReadNicsAsync(conn).ConfigureAwait(false),
            PortPurposes = await ReadPortPurposesAsync(conn).ConfigureAwait(false),
            Topologies = await ReadTopologiesAsync(conn).ConfigureAwait(false),
        };
    }

    public static async Task ReplaceAsync(NpgsqlConnection conn, InventorySnapshot snapshot)
    {
        await using var tx = await conn.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_network_topology").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_device_nic").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_device_domain").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, $"DELETE FROM {NetsTableNames.Device}").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_device_group").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_port_purpose").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_device_type").ConfigureAwait(false);
            await ExecuteAsync(conn, tx, "DELETE FROM devtoy_network_environment").ConfigureAwait(false);

            foreach (var row in snapshot.Environments)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_network_environment ("Id", "Name", "Description", "DefaultCidr", "CreatedAtUtc", "UpdatedAtUtc")
                    VALUES (@id, @name, @desc, @cidr, @created, @updated)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, PName, row.Name);
                Add(cmd, PDesc, row.Description);
                Add(cmd, "@cidr", row.DefaultCidr);
                Add(cmd, "@created", row.CreatedAtUtc);
                Add(cmd, "@updated", row.UpdatedAtUtc);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.DeviceTypes)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_device_type ("Id", "Code", "Name", "Description", "SortOrder", "IconFile", "IsCustomIcon", "ColorGroup")
                    VALUES (@id, @code, @name, @desc, @sort, @icon, @custom, @color)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, "@code", row.Code);
                Add(cmd, PName, row.Name);
                Add(cmd, PDesc, row.Description);
                Add(cmd, "@sort", row.SortOrder);
                Add(cmd, "@icon", row.IconFile);
                Add(cmd, "@custom", row.IsCustomIcon);
                Add(cmd, "@color", row.ColorGroup);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.Groups)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_device_group ("Id", "NetworkEnvironmentId", "Name", "Description", "CreatedAtUtc")
                    VALUES (@id, @env, @name, @desc, @created)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, "@env", row.NetworkEnvironmentId);
                Add(cmd, PName, row.Name);
                Add(cmd, PDesc, row.Description);
                Add(cmd, "@created", row.CreatedAtUtc);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.Devices)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_device (
                        "Id", "Code", "NetworkEnvironmentId", "DeviceGroupId", "DeviceTypeId",
                        "Hostname", "Vendor", "Brand", "Model", "Description", "Ip", "Mac", "OsType",
                        "DeviceInfo", "OpenPorts", "Remarks", "Source", "CreatedAtUtc", "UpdatedAtUtc")
                    VALUES (
                        @id, @code, @env, @group, @type,
                        @host, @vendor, @brand, @model, @desc, @ip, @mac, @os,
                        @info, @ports, @remarks, @source, @created, @updated)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, "@code", row.Code);
                Add(cmd, "@env", row.NetworkEnvironmentId);
                Add(cmd, "@group", row.DeviceGroupId);
                Add(cmd, "@type", row.DeviceTypeId);
                Add(cmd, "@host", row.Hostname);
                Add(cmd, "@vendor", row.Vendor);
                Add(cmd, "@brand", row.Brand);
                Add(cmd, "@model", row.Model);
                Add(cmd, PDesc, row.Description);
                Add(cmd, "@ip", row.Ip);
                Add(cmd, "@mac", row.Mac);
                Add(cmd, "@os", row.OsType);
                Add(cmd, "@info", row.DeviceInfo);
                Add(cmd, "@ports", row.OpenPorts);
                Add(cmd, "@remarks", row.Remarks);
                Add(cmd, "@source", row.Source);
                Add(cmd, "@created", row.CreatedAtUtc);
                Add(cmd, "@updated", row.UpdatedAtUtc);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.Domains)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """INSERT INTO devtoy_device_domain ("Id", "DeviceId", "Domain") VALUES (@id, @device, @domain)""";
                Add(cmd, "@id", row.Id);
                Add(cmd, "@device", row.DeviceId);
                Add(cmd, "@domain", row.Domain);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.Nics)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_device_nic ("Id", "DeviceId", "Name", "Ip", "Mac", "SortOrder")
                    VALUES (@id, @device, @name, @ip, @mac, @sort)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, "@device", row.DeviceId);
                Add(cmd, PName, row.Name);
                Add(cmd, "@ip", row.Ip);
                Add(cmd, "@mac", row.Mac);
                Add(cmd, "@sort", row.SortOrder);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.PortPurposes)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_port_purpose ("Id", "Port", "Protocol", "Service", "Description")
                    VALUES (@id, @port, @protocol, @service, @desc)
                    """;
                Add(cmd, "@id", row.Id);
                Add(cmd, "@port", row.Port);
                Add(cmd, "@protocol", row.Protocol);
                Add(cmd, "@service", row.Service);
                Add(cmd, PDesc, row.Description);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var row in snapshot.Topologies)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO devtoy_network_topology ("NetworkEnvironmentId", "Json", "UpdatedAtUtc")
                    VALUES (@env, @json, @updated)
                    """;
                Add(cmd, "@env", row.NetworkEnvironmentId);
                Add(cmd, "@json", row.Json);
                Add(cmd, "@updated", row.UpdatedAtUtc);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            foreach (var table in ResetTables)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = ResolvePgSerialResetSql(table);
                try
                {
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
                catch
                {
                    // 序列尚不存在时忽略
                }
            }

            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task ReplaceIntoConnectionAsync(
        InventorySnapshot snapshot,
        DbConnection connection,
        DbTransaction? transaction = null)
    {
        if (connection is not NpgsqlConnection npg)
        {
            throw new InvalidOperationException("ReplaceIntoConnectionAsync 目前仅支持 NpgsqlConnection。");
        }

        if (transaction is NpgsqlTransaction npgTx)
        {
            await ReplaceAsync(npg, snapshot).ConfigureAwait(false);
            _ = npgTx;
            return;
        }

        await ReplaceAsync(npg, snapshot).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void Add(DbCommand cmd, string name, object? value)
    {
        DbConnectionFactory.AddParam(cmd, name, value);
    }

    private static async Task<List<NetworkEnvironment>> ReadEnvironmentsAsync(NpgsqlConnection conn)
    {
        var list = new List<NetworkEnvironment>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT "Id", "Name", "Description", "DefaultCidr", "CreatedAtUtc", "UpdatedAtUtc" FROM devtoy_network_environment ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new NetworkEnvironment
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                DefaultCidr = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAtUtc = reader.GetDateTime(4),
                UpdatedAtUtc = reader.GetDateTime(5),
            });
        }

        return list;
    }

    private static async Task<List<DeviceType>> ReadDeviceTypesAsync(NpgsqlConnection conn)
    {
        var list = new List<DeviceType>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "Id", "Code", "Name", "Description", "SortOrder", "IconFile", "IsCustomIcon", "ColorGroup" FROM devtoy_device_type ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new DeviceType
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                SortOrder = reader.GetInt32(4),
                IconFile = reader.IsDBNull(5) ? null : reader.GetString(5),
                IsCustomIcon = reader.GetBoolean(6),
                ColorGroup = reader.IsDBNull(7) ? DeviceTypeColorGroups.Other : reader.GetString(7),
            });
        }

        return list;
    }

    private static async Task<List<DeviceGroup>> ReadGroupsAsync(NpgsqlConnection conn)
    {
        var list = new List<DeviceGroup>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "Id", "NetworkEnvironmentId", "Name", "Description", "CreatedAtUtc" FROM devtoy_device_group ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new DeviceGroup
            {
                Id = reader.GetInt32(0),
                NetworkEnvironmentId = reader.GetInt32(1),
                Name = reader.GetString(2),
                Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                CreatedAtUtc = reader.GetDateTime(4),
            });
        }

        return list;
    }

    private static async Task<List<Device>> ReadDevicesAsync(NpgsqlConnection conn)
    {
        var list = new List<Device>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT "Id", "Code", "NetworkEnvironmentId", "DeviceGroupId", "DeviceTypeId",
                   "Hostname", "Vendor", "Brand", "Model", "Description", "Ip", "Mac", "OsType",
                   "DeviceInfo", "OpenPorts", "Remarks", "Source", "CreatedAtUtc", "UpdatedAtUtc"
            FROM devtoy_device ORDER BY "Id"
            """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(ReadDeviceRow(reader));
        }

        return list;
    }

    private static Device ReadDeviceRow(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Code = reader.GetString(1),
        NetworkEnvironmentId = reader.GetInt32(2),
        DeviceGroupId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
        DeviceTypeId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        Hostname = reader.IsDBNull(5) ? null : reader.GetString(5),
        Vendor = reader.IsDBNull(6) ? null : reader.GetString(6),
        Brand = reader.IsDBNull(7) ? null : reader.GetString(7),
        Model = reader.IsDBNull(8) ? null : reader.GetString(8),
        Description = reader.IsDBNull(9) ? null : reader.GetString(9),
        Ip = reader.IsDBNull(10) ? null : reader.GetString(10),
        Mac = reader.IsDBNull(11) ? null : reader.GetString(11),
        OsType = reader.IsDBNull(12) ? null : reader.GetString(12),
        DeviceInfo = reader.IsDBNull(13) ? null : reader.GetString(13),
        OpenPorts = reader.IsDBNull(14) ? null : reader.GetString(14),
        Remarks = reader.IsDBNull(15) ? null : reader.GetString(15),
        Source = reader.IsDBNull(16) ? "Scan" : reader.GetString(16),
        CreatedAtUtc = reader.GetDateTime(17),
        UpdatedAtUtc = reader.GetDateTime(18),
    };

    private static async Task<List<DeviceDomain>> ReadDomainsAsync(NpgsqlConnection conn)
    {
        var list = new List<DeviceDomain>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """SELECT "Id", "DeviceId", "Domain" FROM devtoy_device_domain ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new DeviceDomain
            {
                Id = reader.GetInt32(0),
                DeviceId = reader.GetInt32(1),
                Domain = reader.GetString(2),
            });
        }

        return list;
    }

    private static async Task<List<DeviceNic>> ReadNicsAsync(NpgsqlConnection conn)
    {
        var list = new List<DeviceNic>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "Id", "DeviceId", "Name", "Ip", "Mac", "SortOrder" FROM devtoy_device_nic ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new DeviceNic
            {
                Id = reader.GetInt32(0),
                DeviceId = reader.GetInt32(1),
                Name = reader.IsDBNull(2) ? null : reader.GetString(2),
                Ip = reader.IsDBNull(3) ? null : reader.GetString(3),
                Mac = reader.IsDBNull(4) ? null : reader.GetString(4),
                SortOrder = reader.GetInt32(5),
            });
        }

        return list;
    }

    private static async Task<List<PortPurpose>> ReadPortPurposesAsync(NpgsqlConnection conn)
    {
        var list = new List<PortPurpose>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "Id", "Port", "Protocol", "Service", "Description" FROM devtoy_port_purpose ORDER BY "Id" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new PortPurpose
            {
                Id = reader.GetInt32(0),
                Port = reader.GetInt32(1),
                Protocol = reader.GetString(2),
                Service = reader.IsDBNull(3) ? null : reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return list;
    }

    private static async Task<List<NetworkTopology>> ReadTopologiesAsync(NpgsqlConnection conn)
    {
        var list = new List<NetworkTopology>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """SELECT "NetworkEnvironmentId", "Json", "UpdatedAtUtc" FROM devtoy_network_topology ORDER BY "NetworkEnvironmentId" """;
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            list.Add(new NetworkTopology
            {
                NetworkEnvironmentId = reader.GetInt32(0),
                Json = reader.GetString(1),
                UpdatedAtUtc = reader.GetDateTime(2),
            });
        }

        return list;
    }

    private static string ResolvePgSerialResetSql(string table) => table switch
    {
        NetsTableNames.NetworkEnvironment =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_network_environment', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_network_environment), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_network_environment))
            """,
        NetsTableNames.DeviceType =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_device_type', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_device_type), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_device_type))
            """,
        NetsTableNames.DeviceGroup =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_device_group', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_device_group), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_device_group))
            """,
        NetsTableNames.Device =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_device', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_device), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_device))
            """,
        NetsTableNames.DeviceDomain =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_device_domain', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_device_domain), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_device_domain))
            """,
        NetsTableNames.DeviceNic =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_device_nic', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_device_nic), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_device_nic))
            """,
        NetsTableNames.PortPurpose =>
            """
            SELECT setval(
                pg_get_serial_sequence('devtoy_port_purpose', 'Id'),
                COALESCE((SELECT MAX("Id") FROM devtoy_port_purpose), 1),
                (SELECT COUNT(*) > 0 FROM devtoy_port_purpose))
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, "未知 PostgreSQL 清单表。"),
    };
}