using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Qolome.Nets.Core.Data;

public sealed class InventorySnapshot
{
    public List<NetworkEnvironment> Environments { get; set; } = [];
    public List<DeviceType> DeviceTypes { get; set; } = [];
    public List<DeviceGroup> Groups { get; set; } = [];
    public List<Device> Devices { get; set; } = [];
    public List<DeviceDomain> Domains { get; set; } = [];
    public List<DeviceNic> Nics { get; set; } = [];
    public List<PortPurpose> PortPurposes { get; set; } = [];
    public List<NetworkTopology> Topologies { get; set; } = [];

    public string Summary =>
        $"环境 {Environments.Count}，类型 {DeviceTypes.Count}，分组 {Groups.Count}，设备 {Devices.Count}，域名 {Domains.Count}，网卡 {Nics.Count}，端口 {PortPurposes.Count}，拓扑 {Topologies.Count}";

    public static async Task<InventorySnapshot> LoadAsync(NetsDbContext db) => new()
    {
        Environments = (await db.Environments.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        DeviceTypes = (await db.DeviceTypes.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        Groups = (await db.Groups.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        Devices = (await db.Devices.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        Domains = (await db.DeviceDomains.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        Nics = (await db.DeviceNics.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        PortPurposes = (await db.PortPurposes.AsNoTracking().OrderBy(x => x.Id).ToListAsync()).Select(Clone).ToList(),
        Topologies = (await db.Topologies.AsNoTracking().OrderBy(x => x.NetworkEnvironmentId).ToListAsync()).Select(Clone).ToList()
    };

    public Task ReplaceIntoConnectionAsync(DbConnection connection, DbTransaction? transaction = null) =>
        NetsPostgreSqlInventorySync.ReplaceIntoConnectionAsync(this, connection, transaction);

    public async Task ReplaceAsync(NetsDbContext db)
    {
        Sanitize();

        var mysql = ContextIsMySql(db);
        await db.Database.OpenConnectionAsync().ConfigureAwait(false);
        try
        {
            await SetForeignKeyChecksAsync(db, mysql, enabled: false).ConfigureAwait(false);

            await db.Topologies.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.DeviceNics.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.DeviceDomains.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.Devices.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.Groups.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.PortPurposes.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.DeviceTypes.ExecuteDeleteAsync().ConfigureAwait(false);
            await db.Environments.ExecuteDeleteAsync().ConfigureAwait(false);

            // 分两批写入：先父表再子表，避免仅设 FK 字段时 EF 插入顺序不当。
            db.Environments.AddRange(Environments.Select(Clone));
            db.DeviceTypes.AddRange(DeviceTypes.Select(Clone));
            db.Groups.AddRange(Groups.Select(Clone));
            db.Devices.AddRange(Devices.Select(Clone));
            db.PortPurposes.AddRange(PortPurposes.Select(Clone));
            await db.SaveChangesAsync().ConfigureAwait(false);
            db.ChangeTracker.Clear();

            db.DeviceDomains.AddRange(Domains.Select(Clone));
            db.DeviceNics.AddRange(Nics.Select(Clone));
            db.Topologies.AddRange(Topologies.Select(Clone));
            await db.SaveChangesAsync().ConfigureAwait(false);
            db.ChangeTracker.Clear();

            await ResetIdentitiesAsync(db, mysql).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await SetForeignKeyChecksAsync(db, mysql, enabled: true).ConfigureAwait(false);
            }
            finally
            {
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>丢弃远端可能存在的孤儿行，并把失效外键置空，避免 SQLite 强制 FK 失败。</summary>
    public void Sanitize()
    {
        var envIds = Environments.Select(x => x.Id).ToHashSet();
        var typeIds = DeviceTypes.Select(x => x.Id).ToHashSet();
        Groups = Groups.Where(g => envIds.Contains(g.NetworkEnvironmentId)).ToList();
        var groupIds = Groups.Select(x => x.Id).ToHashSet();

        Devices = Devices
            .Where(d => envIds.Contains(d.NetworkEnvironmentId))
            .Select(d =>
            {
                if (d.DeviceGroupId is int gid && !groupIds.Contains(gid))
                {
                    d.DeviceGroupId = null;
                }

                if (d.DeviceTypeId is int tid && !typeIds.Contains(tid))
                {
                    d.DeviceTypeId = null;
                }

                return d;
            })
            .ToList();

        var deviceIds = Devices.Select(x => x.Id).ToHashSet();
        Domains = Domains.Where(x => deviceIds.Contains(x.DeviceId)).ToList();
        Nics = Nics.Where(x => deviceIds.Contains(x.DeviceId)).ToList();
        Topologies = Topologies.Where(x => envIds.Contains(x.NetworkEnvironmentId)).ToList();
    }

    private static bool ContextIsMySql(NetsDbContext db) =>
        (db.Database.ProviderName ?? "").Contains("MySql", StringComparison.OrdinalIgnoreCase);

    private static async Task SetForeignKeyChecksAsync(NetsDbContext db, bool mysql, bool enabled)
    {
        string sql;
        if (mysql)
        {
            sql = enabled ? "SET FOREIGN_KEY_CHECKS=1" : "SET FOREIGN_KEY_CHECKS=0";
        }
        else
        {
            sql = enabled ? "PRAGMA foreign_keys=ON" : "PRAGMA foreign_keys=OFF";
        }
        await db.Database.ExecuteSqlRawAsync(sql).ConfigureAwait(false);
    }

    private static async Task ResetIdentitiesAsync(NetsDbContext db, bool mysql)
    {
        var connection = db.Database.GetDbConnection();
        foreach (var table in NetsTableNames.IdentityResetTables)
        {
            await using var maxCmd = connection.CreateCommand();
            maxCmd.CommandText = mysql
                ? $"SELECT IFNULL(MAX(`Id`), 0) FROM `{table}`"
                : $"SELECT IFNULL(MAX(Id), 0) FROM \"{table}\"";
            var max = Convert.ToInt64(await maxCmd.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = mysql
                ? $"ALTER TABLE `{table}` AUTO_INCREMENT = {max + 1}"
                : $"INSERT OR REPLACE INTO sqlite_sequence(name, seq) VALUES ('{table}', {max})";
            try
            {
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch
            {
                // sqlite_sequence 尚未创建时忽略。
            }
        }
    }

    public static NetworkEnvironment Clone(NetworkEnvironment x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        Description = x.Description,
        DefaultCidr = x.DefaultCidr,
        CreatedAtUtc = x.CreatedAtUtc,
        UpdatedAtUtc = x.UpdatedAtUtc
    };

    public static DeviceType Clone(DeviceType x) => new()
    {
        Id = x.Id,
        Code = x.Code,
        Name = x.Name,
        Description = x.Description,
        SortOrder = x.SortOrder,
        IconFile = x.IconFile,
        IsCustomIcon = x.IsCustomIcon,
        ColorGroup = x.ColorGroup
    };

    public static DeviceGroup Clone(DeviceGroup x) => new()
    {
        Id = x.Id,
        NetworkEnvironmentId = x.NetworkEnvironmentId,
        Name = x.Name,
        Description = x.Description,
        CreatedAtUtc = x.CreatedAtUtc
    };

    public static Device Clone(Device x) => new()
    {
        Id = x.Id,
        Code = x.Code,
        NetworkEnvironmentId = x.NetworkEnvironmentId,
        DeviceGroupId = x.DeviceGroupId,
        DeviceTypeId = x.DeviceTypeId,
        Hostname = x.Hostname,
        Vendor = x.Vendor,
        Brand = x.Brand,
        Model = x.Model,
        Description = x.Description,
        Ip = x.Ip,
        Mac = x.Mac,
        OsType = x.OsType,
        DeviceInfo = x.DeviceInfo,
        OpenPorts = x.OpenPorts,
        Remarks = x.Remarks,
        Source = x.Source,
        CreatedAtUtc = x.CreatedAtUtc,
        UpdatedAtUtc = x.UpdatedAtUtc
    };

    public static DeviceDomain Clone(DeviceDomain x) => new()
    {
        Id = x.Id,
        DeviceId = x.DeviceId,
        Domain = x.Domain
    };

    public static DeviceNic Clone(DeviceNic x) => new()
    {
        Id = x.Id,
        DeviceId = x.DeviceId,
        Name = x.Name,
        Ip = x.Ip,
        Mac = x.Mac,
        SortOrder = x.SortOrder
    };

    public static PortPurpose Clone(PortPurpose x) => new()
    {
        Id = x.Id,
        Port = x.Port,
        Protocol = x.Protocol,
        Service = x.Service,
        Description = x.Description
    };

    public static NetworkTopology Clone(NetworkTopology x) => new()
    {
        NetworkEnvironmentId = x.NetworkEnvironmentId,
        Json = x.Json,
        UpdatedAtUtc = x.UpdatedAtUtc
    };
}
