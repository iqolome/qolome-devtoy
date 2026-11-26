using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;
using Qolome.Nets.Core.Scanning;

namespace Qolome.Nets.Core.Data;

public sealed class InventoryService
{
    private const string MsgEnvironmentMissing = "网络环境不存在。";
    private const string MsgGroupMissing = "分组不存在。";
    private const string MsgDeviceTypeMissing = "设备类型不存在。";
    private const string MsgDeviceMissing = "设备不存在。";
    private const string MsgSelectEnvironment = "请先选择网络环境。";
    public static async Task<IReadOnlyList<NetworkEnvironment>> GetEnvironmentsAsync()
    {
        return await QueryWithSchemaRetryAsync(async () =>
        {
            await using var db = AppDatabase.CreateContext();
            return await db.Environments.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        }).ConfigureAwait(false);
    }

    private static async Task<T> QueryWithSchemaRetryAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return await query().ConfigureAwait(false);
        }
        catch (Exception ex) when (AppDatabase.IsMissingTableException(ex))
        {
            await AppDatabase.EnsureBusinessSchemaAsync().ConfigureAwait(false);
            return await query().ConfigureAwait(false);
        }
    }

    public async Task<NetworkEnvironment> AddEnvironmentAsync(string name, string? description, string? defaultCidr)
    {
        name = RequireName(name, "网络环境名称");
        await using var db = AppDatabase.CreateContext();
        if (await db.Environments.AnyAsync(x => x.Name == name))
        {
            throw new ScanException($"网络环境「{name}」已存在。");
        }

        var now = DateTime.UtcNow;
        var entity = new NetworkEnvironment
        {
            Name = name,
            Description = NullIfEmpty(description),
            DefaultCidr = NullIfEmpty(defaultCidr),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Environments.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public static async Task UpdateEnvironmentAsync(int id, string name, string? description, string? defaultCidr)
    {
        name = RequireName(name, "网络环境名称");
        await using var db = AppDatabase.CreateContext();
        var entity = await db.Environments.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgEnvironmentMissing);
        if (await db.Environments.AnyAsync(x => x.Name == name && x.Id != id))
        {
            throw new ScanException($"网络环境「{name}」已存在。");
        }

        entity.Name = name;
        entity.Description = NullIfEmpty(description);
        entity.DefaultCidr = NullIfEmpty(defaultCidr);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public static async Task DeleteEnvironmentAsync(int id)
    {
        await using var db = AppDatabase.CreateContext();
        if (await db.Devices.AnyAsync(x => x.NetworkEnvironmentId == id))
        {
            throw new ScanException("该网络环境仍有设备，无法删除。请先移动或删除设备。");
        }

        var entity = await db.Environments.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgEnvironmentMissing);
        var groups = await db.Groups.Where(x => x.NetworkEnvironmentId == id).ToListAsync();
        db.Groups.RemoveRange(groups);
        var topology = await db.Topologies.FirstOrDefaultAsync(x => x.NetworkEnvironmentId == id);
        if (topology is not null)
        {
            db.Topologies.Remove(topology);
        }

        db.Environments.Remove(entity);
        await db.SaveChangesAsync();
        TopologyStore.DeleteLocal(id);
    }

    public static async Task<IReadOnlyList<DeviceGroup>> GetGroupsAsync(int environmentId)
    {
        return await QueryWithSchemaRetryAsync(async () =>
        {
            await using var db = AppDatabase.CreateContext();
            return await db.Groups.AsNoTracking()
                .Where(x => x.NetworkEnvironmentId == environmentId)
                .OrderBy(x => x.Name)
                .ToListAsync();
        }).ConfigureAwait(false);
    }

    public async Task<DeviceGroup> AddGroupAsync(int environmentId, string name, string? description)
    {
        name = RequireName(name, "分组名称");
        await using var db = AppDatabase.CreateContext();
        if (!await db.Environments.AnyAsync(x => x.Id == environmentId))
        {
            throw new ScanException(MsgEnvironmentMissing);
        }

        if (await db.Groups.AnyAsync(x => x.NetworkEnvironmentId == environmentId && x.Name == name))
        {
            throw new ScanException($"分组「{name}」已存在。");
        }

        var entity = new DeviceGroup
        {
            NetworkEnvironmentId = environmentId,
            Name = name,
            Description = NullIfEmpty(description),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.Groups.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public static async Task UpdateGroupAsync(int id, string name, string? description)
    {
        name = RequireName(name, "分组名称");
        await using var db = AppDatabase.CreateContext();
        var entity = await db.Groups.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgGroupMissing);
        if (await db.Groups.AnyAsync(x =>
                x.NetworkEnvironmentId == entity.NetworkEnvironmentId && x.Name == name && x.Id != id))
        {
            throw new ScanException($"分组「{name}」已存在。");
        }

        entity.Name = name;
        entity.Description = NullIfEmpty(description);
        await db.SaveChangesAsync();
    }

    public static async Task DeleteGroupAsync(int id)
    {
        await using var db = AppDatabase.CreateContext();
        var entity = await db.Groups.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgGroupMissing);
        db.Groups.Remove(entity);
        await db.SaveChangesAsync();
    }

    public static async Task<IReadOnlyList<DeviceType>> GetDeviceTypesAsync()
    {
        return await QueryWithSchemaRetryAsync(async () =>
        {
            await using var db = AppDatabase.CreateContext();
            return await db.DeviceTypes.AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync();
        }).ConfigureAwait(false);
    }

    public async Task<DeviceType> AddDeviceTypeAsync(
        string code,
        string name,
        string? description,
        string? iconSourcePath = null,
        string? builtinIconKey = null,
        string? colorGroup = null)
    {
        code = RequireName(code, "设备类型编码").ToLowerInvariant();
        name = RequireName(name, "设备类型名称");
        await using var db = AppDatabase.CreateContext();
        if (await db.DeviceTypes.AnyAsync(x => x.Code == code || x.Name == name))
        {
            throw new ScanException("设备类型编码或名称已存在。");
        }

        var order = await db.DeviceTypes.Select(x => (int?)x.SortOrder).MaxAsync() ?? -1;
        var entity = new DeviceType
        {
            Code = code,
            Name = name,
            Description = NullIfEmpty(description),
            SortOrder = order + 1,
            ColorGroup = DeviceTypeColorGroups.Normalize(colorGroup, code)
        };
        DeviceTypeIconStore.ApplyChange(entity, iconSourcePath, restoreDefault: false, builtinIconKey);
        db.DeviceTypes.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public static async Task UpdateDeviceTypeAsync(int id, DeviceTypeEditInput input)
    {
        var code = RequireName(input.Code, "设备类型编码").ToLowerInvariant();
        var name = RequireName(input.Name, "设备类型名称");
        await using var db = AppDatabase.CreateContext();
        var entity = await db.DeviceTypes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgDeviceTypeMissing);
        if (await db.DeviceTypes.AnyAsync(x => (x.Code == code || x.Name == name) && x.Id != id))
        {
            throw new ScanException("设备类型编码或名称已存在。");
        }

        entity.Code = code;
        entity.Name = name;
        entity.Description = NullIfEmpty(input.Description);
        entity.ColorGroup = DeviceTypeColorGroups.Normalize(input.ColorGroup, code);
        DeviceTypeIconStore.ApplyChange(entity, input.IconSourcePath, input.RestoreDefaultIcon, input.BuiltinIconKey);
        await db.SaveChangesAsync();
    }

    public static async Task DeleteDeviceTypeAsync(int id)
    {
        await using var db = AppDatabase.CreateContext();
        var entity = await db.DeviceTypes.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ScanException(MsgDeviceTypeMissing);
        if (entity.IsCustomIcon)
        {
            DeviceTypeIconStore.DeleteCustom(entity.IconFile);
        }

        db.DeviceTypes.Remove(entity);
        await db.SaveChangesAsync();
    }

    public static async Task<IReadOnlyList<Device>> GetDevicesAsync(int environmentId, int? groupId)
    {
        return await QueryWithSchemaRetryAsync(async () =>
        {
            await using var db = AppDatabase.CreateContext();
            var query = db.Devices.AsNoTracking()
                .Include(x => x.DeviceType)
                .Include(x => x.DeviceGroup)
                .Include(x => x.Domains)
                .Include(x => x.Nics)
                .Where(x => x.NetworkEnvironmentId == environmentId);
            // null：全部；0：未分组；其他：指定分组。
            if (groupId is int gid)
            {
                query = gid == 0
                    ? query.Where(x => x.DeviceGroupId == null)
                    : query.Where(x => x.DeviceGroupId == gid);
            }

            var list = await query.ToListAsync();
            list.Sort(CompareDeviceIpThenCode);
            await BackfillDeviceTimestampsAsync(db, list).ConfigureAwait(false);
            return list;
        }).ConfigureAwait(false);
    }

    /// <summary>缺省/未写入的创建、修改时间补为当前 UTC，并写回库。</summary>
    private static async Task BackfillDeviceTimestampsAsync(NetsDbContext db, List<Device> trackedOrDetached)
    {
        var now = DateTime.UtcNow;
        var ids = trackedOrDetached
            .Where(d => d.CreatedAtUtc == default || d.UpdatedAtUtc == default)
            .Select(d => d.Id)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            foreach (var d in trackedOrDetached)
            {
                if (d.CreatedAtUtc == default)
                {
                    d.CreatedAtUtc = now;
                }

                if (d.UpdatedAtUtc == default)
                {
                    d.UpdatedAtUtc = d.CreatedAtUtc == default ? now : d.CreatedAtUtc;
                }
            }

            return;
        }

        var entities = await db.Devices.Where(x => ids.Contains(x.Id)).ToListAsync().ConfigureAwait(false);
        var dirty = false;
        foreach (var entity in entities)
        {
            if (entity.CreatedAtUtc == default)
            {
                entity.CreatedAtUtc = now;
                dirty = true;
            }

            if (entity.UpdatedAtUtc == default)
            {
                entity.UpdatedAtUtc = entity.CreatedAtUtc;
                dirty = true;
            }
        }

        if (dirty)
        {
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        var byId = entities.ToDictionary(x => x.Id);
        foreach (var d in trackedOrDetached)
        {
            if (byId.TryGetValue(d.Id, out var saved))
            {
                d.CreatedAtUtc = saved.CreatedAtUtc;
                d.UpdatedAtUtc = saved.UpdatedAtUtc;
            }
            else
            {
                if (d.CreatedAtUtc == default)
                {
                    d.CreatedAtUtc = now;
                }

                if (d.UpdatedAtUtc == default)
                {
                    d.UpdatedAtUtc = d.CreatedAtUtc;
                }
            }
        }
    }

    private static int CompareDeviceIpThenCode(Device left, Device right)
    {
        var ip = IpAddressComparer.Compare(left.Ip, right.Ip);
        return ip != 0 ? ip : string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task AssignDeviceGroupAsync(int deviceId, int? groupId)
    {
        var updated = await AssignDeviceGroupsAsync([deviceId], groupId);
        if (updated == 0)
        {
            throw new ScanException(MsgDeviceMissing);
        }
    }

    public static async Task<int> AssignDeviceGroupsAsync(IReadOnlyList<int> deviceIds, int? groupId)
    {
        var ids = deviceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        await using var db = AppDatabase.CreateContext();
        var devices = await db.Devices.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (devices.Count == 0)
        {
            return 0;
        }

        DeviceGroup? group = null;
        if (groupId is int gid)
        {
            group = await db.Groups.FirstOrDefaultAsync(x => x.Id == gid)
                ?? throw new ScanException(MsgGroupMissing);
        }

        var now = DateTime.UtcNow;
        foreach (var device in devices)
        {
            if (group is not null && group.NetworkEnvironmentId != device.NetworkEnvironmentId)
            {
                throw new ScanException("设备只能属于一个网络环境，不能分到其他环境的分组。");
            }

            device.DeviceGroupId = group?.Id;
            device.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync();
        return devices.Count;
    }

    public static async Task DeleteDeviceAsync(int deviceId)
    {
        var deleted = await DeleteDevicesAsync([deviceId]);
        if (deleted == 0)
        {
            throw new ScanException(MsgDeviceMissing);
        }
    }

    public static async Task<int> DeleteDevicesAsync(IReadOnlyList<int> deviceIds)
    {
        var ids = deviceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        await using var db = AppDatabase.CreateContext();
        var devices = await db.Devices.Where(x => ids.Contains(x.Id)).ToListAsync();
        if (devices.Count == 0)
        {
            return 0;
        }

        db.Devices.RemoveRange(devices);
        await db.SaveChangesAsync();
        return devices.Count;
    }

    public async Task AddDeviceAsync(int environmentId, DeviceEditInput input)
    {
        await using var db = AppDatabase.CreateContext();
        if (!await db.Environments.AnyAsync(x => x.Id == environmentId))
        {
            throw new ScanException(MsgSelectEnvironment);
        }

        await EnsureGroupInEnvironmentAsync(db, environmentId, input.DeviceGroupId);
        await EnsureDeviceTypeAsync(db, input.DeviceTypeId);
        var nics = await ResolveDeviceNicsAsync(db, environmentId, input, excludeDeviceId: null);

        var nextSerial = await NextSerialAsync(db);
        var code = NullIfEmpty(input.Code) ?? FormatCode(nextSerial);
        await EnsureUniqueMacsAsync(db, environmentId, nics.Select(n => n.Mac), excludeDeviceId: null);
        if (await db.Devices.AnyAsync(x => x.Code == code))
        {
            throw new ScanException($"设备编码「{code}」已存在。");
        }

        var now = DateTime.UtcNow;
        var entity = new Device
        {
            Code = code,
            NetworkEnvironmentId = environmentId,
            DeviceGroupId = input.DeviceGroupId,
            DeviceTypeId = input.DeviceTypeId,
            Hostname = NullIfEmpty(input.Hostname),
            Vendor = NullIfEmpty(input.Vendor),
            Brand = NullIfEmpty(input.Brand),
            Model = NullIfEmpty(input.Model),
            Description = NullIfEmpty(input.Description, keepLineBreaks: true),
            Ip = nics.FirstOrDefault()?.Ip,
            Mac = nics.FirstOrDefault()?.Mac,
            OsType = NullIfEmpty(input.OsType),
            DeviceInfo = NullIfEmpty(input.DeviceInfo, keepLineBreaks: true),
            OpenPorts = NullIfEmpty(input.OpenPorts),
            Remarks = NullIfEmpty(input.Remarks, keepLineBreaks: true),
            Source = "Manual",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        ReplaceNics(entity, nics);
        ReplaceDomains(entity, FlattenDomains(input.Domains));
        db.Devices.Add(entity);
        await db.SaveChangesAsync();
    }

    public static async Task UpdateDeviceAsync(int deviceId, DeviceEditInput input)
    {
        await using var db = AppDatabase.CreateContext();
        var entity = await db.Devices
            .Include(x => x.Domains)
            .Include(x => x.Nics)
            .FirstOrDefaultAsync(x => x.Id == deviceId)
            ?? throw new ScanException(MsgDeviceMissing);

        await EnsureGroupInEnvironmentAsync(db, entity.NetworkEnvironmentId, input.DeviceGroupId);
        await EnsureDeviceTypeAsync(db, input.DeviceTypeId);
        var nics = await ResolveDeviceNicsAsync(db, entity.NetworkEnvironmentId, input, deviceId);

        var code = NullIfEmpty(input.Code) ?? entity.Code;
        await EnsureUniqueMacsAsync(db, entity.NetworkEnvironmentId, nics.Select(n => n.Mac), deviceId);
        if (await db.Devices.AnyAsync(x => x.Code == code && x.Id != deviceId))
        {
            throw new ScanException($"设备编码「{code}」已存在。");
        }

        entity.Code = code;
        entity.DeviceGroupId = input.DeviceGroupId;
        entity.DeviceTypeId = input.DeviceTypeId;
        entity.Hostname = NullIfEmpty(input.Hostname);
        entity.Vendor = NullIfEmpty(input.Vendor);
        entity.Brand = NullIfEmpty(input.Brand);
        entity.Model = NullIfEmpty(input.Model);
        entity.Description = NullIfEmpty(input.Description, keepLineBreaks: true);
        ReplaceNics(entity, nics);
        entity.OsType = NullIfEmpty(input.OsType);
        entity.DeviceInfo = NullIfEmpty(input.DeviceInfo, keepLineBreaks: true);
        entity.OpenPorts = NullIfEmpty(input.OpenPorts);
        entity.Remarks = NullIfEmpty(input.Remarks, keepLineBreaks: true);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        ReplaceDomains(entity, FlattenDomains(input.Domains));
        await db.SaveChangesAsync();
    }

    public static async Task<DeviceRefreshResult> RefreshDevicesAsync(
        int environmentId,
        IReadOnlyList<int> deviceIds,
        DeviceRefreshOptions options,
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        PortPurposeStore? purposes = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(request);
        if (!options.Any)
        {
            throw new ScanException("请至少勾选一项要更新的内容。");
        }

        await using var db = AppDatabase.CreateContext();
        if (!await db.Environments.AnyAsync(x => x.Id == environmentId, cancellationToken))
        {
            throw new ScanException(MsgSelectEnvironment);
        }

        var wanted = deviceIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return new DeviceRefreshResult(0, 0, 0);
        }

        var all = await db.Devices
            .Include(x => x.Nics)
            .Where(x => x.NetworkEnvironmentId == environmentId)
            .ToListAsync(cancellationToken);
        var order = wanted
            .Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index);
        var devices = all
            .Where(d => order.ContainsKey(d.Id))
            .OrderBy(d => order[d.Id])
            .ToList();
        if (devices.Count == 0)
        {
            return new DeviceRefreshResult(0, 0, 0);
        }

        progress?.Report(new ScanProgress(0, devices.Count, $"准备更新 {devices.Count} 台设备…"));

        var probes = new ConcurrentDictionary<string, HostScanResult>(StringComparer.OrdinalIgnoreCase);
        var session = await RunRefreshNetworkProbesAsync(
            options,
            request,
            devices,
            probes,
            progress,
            purposes,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(devices.Count, devices.Count, "正在保存…"));

        var macToIpsFinal = options.Ip && session is not null
            ? MergeArpMap(ScanSession.SnapshotArpMacToIps(), probes.Values)
            : new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var updated = 0;
        var unchanged = 0;
        var offline = 0;
        foreach (var device in devices)
        {
            var outcome = ApplyRefreshToDevice(device, options, probes, macToIpsFinal, all);
            offline += outcome.OfflineDelta;
            if (outcome.Changed)
            {
                device.UpdatedAtUtc = DateTime.UtcNow;
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DeviceRefreshResult(updated, unchanged, offline);
    }

    private readonly record struct DeviceRefreshOutcome(bool Changed, int OfflineDelta);

    private static async Task<ScanSession?> RunRefreshNetworkProbesAsync(
        DeviceRefreshOptions options,
        ScanRequest request,
        IReadOnlyList<Device> devices,
        ConcurrentDictionary<string, HostScanResult> probes,
        IProgress<ScanProgress>? progress,
        PortPurposeStore? purposes,
        CancellationToken cancellationToken)
    {
        if (!options.NeedsNetwork)
        {
            return null;
        }

        var session = new ScanSession(purposes);
        var firstIps = UniqueIpv4(devices.SelectMany(d => d.NicIps()));
        await ProbeIpsAsync(new ProbeIpsRequest(
                session,
                request,
                firstIps,
                options.Ports,
                probes,
                progress,
                devices.Count,
                cancellationToken))
            .ConfigureAwait(false);

        var macToIps = MergeArpMap(ScanSession.SnapshotArpMacToIps(), probes.Values);
        var extraIps = UniqueIpv4(devices.SelectMany(d =>
                d.OrderedNics().Select(n => PickIpFromArp(n.Mac, n.Ip, macToIps))))
            .Where(ip => !probes.ContainsKey(ip))
            .ToList();
        if (extraIps.Count > 0)
        {
            await ProbeIpsAsync(new ProbeIpsRequest(
                    session,
                    request,
                    extraIps,
                    options.Ports,
                    probes,
                    progress,
                    devices.Count,
                    cancellationToken))
                .ConfigureAwait(false);
        }

        return session;
    }

    private static DeviceRefreshOutcome ApplyRefreshToDevice(
        Device device,
        DeviceRefreshOptions options,
        IReadOnlyDictionary<string, HostScanResult> probes,
        IReadOnlyDictionary<string, HashSet<string>> macToIpsFinal,
        IReadOnlyList<Device> allInEnvironment)
    {
        var changed = false;
        EnsureNicRows(device);
        HostScanResult? host = null;
        var hadIp = false;
        var anyAlive = false;
        foreach (var nic in device.Nics.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList())
        {
            var candidateIp = options.Ip
                ? PickIpFromArp(nic.Mac, nic.Ip, macToIpsFinal) ?? nic.Ip
                : nic.Ip;
            if (!string.IsNullOrWhiteSpace(candidateIp))
            {
                hadIp = true;
                if (TryGetAliveProbe(probes, candidateIp, nic.Ip, out var probed))
                {
                    anyAlive = true;
                    host ??= probed;
                }
            }

            if (options.Ip)
            {
                changed |= TryAssignNicIp(device, nic, candidateIp, allInEnvironment);
            }
        }

        if (options.Ip)
        {
            SyncPrimaryFromNics(device);
        }

        var offlineDelta = options.NeedsNetwork && hadIp && !anyAlive ? 1 : 0;
        changed |= TryApplyVendorFromOui(device, options);
        changed |= TryApplyHostMetadata(device, options, host);
        changed |= TryApplyHostPorts(device, options, host);
        return new DeviceRefreshOutcome(changed, offlineDelta);
    }

    private static bool TryGetAliveProbe(
        IReadOnlyDictionary<string, HostScanResult> probes,
        string candidateIp,
        string? nicIp,
        out HostScanResult probed)
    {
        if (probes.TryGetValue(candidateIp, out var candidateHit) && candidateHit.IsAlive)
        {
            probed = candidateHit;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(nicIp)
            && probes.TryGetValue(nicIp, out var nicHit)
            && nicHit.IsAlive)
        {
            probed = nicHit;
            return true;
        }

        probed = null!;
        return false;
    }

    private static bool TryApplyVendorFromOui(Device device, DeviceRefreshOptions options)
    {
        if (!options.Vendor || !string.IsNullOrWhiteSpace(device.Vendor))
        {
            return false;
        }

        foreach (var mac in device.NicMacs())
        {
            var vendor = OsTypeGuess.MeaningfulVendor(OuiStore.Lookup(mac));
            if (string.IsNullOrWhiteSpace(vendor))
            {
                continue;
            }

            device.Vendor = vendor;
            return true;
        }

        return false;
    }

    private static bool TryApplyHostMetadata(Device device, DeviceRefreshOptions options, HostScanResult? host)
    {
        if (!options.OsType || host is not { IsAlive: true })
        {
            return false;
        }

        var changed = false;
        if (!string.IsNullOrWhiteSpace(host.OsType) && device.OsType != host.OsType)
        {
            device.OsType = host.OsType;
            changed = true;
        }

        if (!string.IsNullOrWhiteSpace(host.DeviceInfo) && device.DeviceInfo != host.DeviceInfo)
        {
            device.DeviceInfo = host.DeviceInfo;
            changed = true;
        }

        return changed;
    }

    private static bool TryApplyHostPorts(Device device, DeviceRefreshOptions options, HostScanResult? host)
    {
        if (!options.Ports || host is not { IsAlive: true } || host.OpenPorts.Count == 0)
        {
            return false;
        }

        var ports = host.PortsDisplay;
        if (string.Equals(device.OpenPorts, ports, StringComparison.Ordinal))
        {
            return false;
        }

        device.OpenPorts = ports;
        return true;
    }

    private static async Task ProbeIpsAsync(ProbeIpsRequest probe)
    {
        if (probe.Ips.Count == 0)
        {
            return;
        }

        var completed = 0;
        var concurrency = Math.Clamp(probe.Request.HostConcurrency, 1, 64);
        await Parallel.ForEachAsync(probe.Ips, new ParallelOptions
        {
            MaxDegreeOfParallelism = concurrency,
            CancellationToken = probe.CancellationToken
        }, async (ip, token) =>
        {
            try
            {
                if (!IPAddress.TryParse(ip, out var address)
                    || address.AddressFamily != AddressFamily.InterNetwork)
                {
                    return;
                }

                var host = await probe.Session.RefreshHostAsync(address, probe.Request, probe.ScanPorts, enrich: true, token)
                    .ConfigureAwait(false);
                probe.Probes[address.ToString()] = host;
            }
            finally
            {
                var done = Interlocked.Increment(ref completed);
                probe.Progress?.Report(new ScanProgress(
                    Math.Min(done, probe.DeviceCount),
                    probe.DeviceCount,
                    $"已探测 {done}/{probe.Ips.Count}"));
            }
        }).ConfigureAwait(false);
    }

    private sealed record ProbeIpsRequest(
        ScanSession Session,
        ScanRequest Request,
        IReadOnlyList<string> Ips,
        bool ScanPorts,
        ConcurrentDictionary<string, HostScanResult> Probes,
        IProgress<ScanProgress>? Progress,
        int DeviceCount,
        CancellationToken CancellationToken);

    private static Dictionary<string, HashSet<string>> MergeArpMap(
        IReadOnlyDictionary<string, IReadOnlyList<string>> snapshot,
        IEnumerable<HostScanResult> hosts)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mac, ips) in snapshot)
        {
            var key = NormalizeMac(mac) ?? mac;
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[key] = set;
            }

            foreach (var ip in ips.Where(ip => !string.IsNullOrWhiteSpace(ip)))
            {
                set.Add(ip);
            }
        }

        foreach (var host in hosts)
        {
            if (!host.IsAlive)
            {
                continue;
            }

            var mac = NormalizeMac(host.Mac);
            if (mac is null)
            {
                continue;
            }

            if (!map.TryGetValue(mac, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[mac] = set;
            }

            set.Add(host.Ip);
        }

        return map;
    }

    private static string? PickIpFromArp(string? mac, string? currentIp, IReadOnlyDictionary<string, HashSet<string>> macToIps)
    {
        var key = NormalizeMac(mac);
        if (key is null || !macToIps.TryGetValue(key, out var ips) || ips.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(currentIp) && ips.Contains(currentIp))
        {
            return currentIp;
        }

        return ips.First();
    }

    private static bool TryAssignNicIp(
        Device device,
        DeviceNic nic,
        string? candidateIp,
        IReadOnlyList<Device> environmentDevices)
    {
        if (string.IsNullOrWhiteSpace(candidateIp)
            || string.Equals(nic.Ip, candidateIp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IPAddress.TryParse(candidateIp, out var address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var ip = address.ToString();
        if (environmentDevices.Any(d => d.Id != device.Id && DeviceHasIp(d, ip)))
        {
            return false;
        }

        if (device.Nics.Any(n => n.Id != nic.Id && string.Equals(n.Ip, ip, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        nic.Ip = ip;
        return true;
    }

    private static List<string> UniqueIpv4(IEnumerable<string?> ips)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in ips)
        {
            if (!IPAddress.TryParse(raw, out var address)
                || address.AddressFamily != AddressFamily.InterNetwork)
            {
                continue;
            }

            var ip = address.ToString();
            if (seen.Add(ip))
            {
                list.Add(ip);
            }
        }

        return list;
    }

    private static async Task EnsureGroupInEnvironmentAsync(NetsDbContext db, int environmentId, int? groupId)
    {
        if (groupId is not int gid)
        {
            return;
        }

        var group = await db.Groups.FirstOrDefaultAsync(x => x.Id == gid)
            ?? throw new ScanException(MsgGroupMissing);
        if (group.NetworkEnvironmentId != environmentId)
        {
            throw new ScanException("分组不属于当前网络环境。");
        }
    }

    private static async Task EnsureDeviceTypeAsync(NetsDbContext db, int? typeId)
    {
        if (typeId is int tid && !await db.DeviceTypes.AnyAsync(x => x.Id == tid))
        {
            throw new ScanException(MsgDeviceTypeMissing);
        }
    }

    private static async Task<IReadOnlyList<DeviceNicInput>> ResolveDeviceNicsAsync(
        NetsDbContext db,
        int environmentId,
        DeviceEditInput input,
        int? excludeDeviceId)
    {
        var bluetooth = await IsBluetoothTypeAsync(db, input.DeviceTypeId);
        var nics = NormalizeNicInputs(input);
        if (!bluetooth && (nics.Count == 0 || nics.All(n => string.IsNullOrWhiteSpace(n.Ip))))
        {
            throw new ScanException("请至少填写一个 IP。");
        }

        if (bluetooth && nics.Count == 0 && NullIfEmpty(input.Hostname) is null)
        {
            throw new ScanException("蓝牙设备请至少填写主机名或一块网卡的 MAC（蓝牙地址）。");
        }

        await EnsureUniqueMacsAsync(db, environmentId, nics.Select(n => n.Mac), excludeDeviceId);
        return nics;
    }

    private static List<DeviceNicInput> NormalizeNicInputs(DeviceEditInput input)
    {
        IReadOnlyList<DeviceNicInput> raw = input.Nics.Count > 0
            ? input.Nics
            : [new DeviceNicInput { Ip = input.Ip, Mac = input.Mac, Name = null }];
        var nics = new List<DeviceNicInput>();
        var seenMac = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in raw)
        {
            foreach (var nic in ExpandNicRow(row))
            {
                if (nic.Mac is not null && !seenMac.Add(nic.Mac))
                {
                    throw new ScanException($"同一设备上 MAC「{nic.Mac}」重复。");
                }

                nics.Add(nic);
            }
        }

        return nics;
    }

    private static IEnumerable<DeviceNicInput> ExpandNicRow(DeviceNicInput row)
    {
        var names = SplitNicValues(row.Name);
        var ips = SplitNicValues(row.Ip);
        var macs = SplitNicValues(row.Mac);
        var count = Math.Max(names.Count, Math.Max(ips.Count, macs.Count));
        if (count == 0)
        {
            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            var name = i < names.Count ? NullIfEmpty(names[i]) : null;
            var ip = i < ips.Count ? NullIfEmpty(ips[i]) : null;
            var mac = i < macs.Count ? NormalizeMac(macs[i]) : null;
            if (name is null && ip is null && mac is null)
            {
                continue;
            }

            yield return new DeviceNicInput { Name = name, Ip = ip, Mac = mac };
        }
    }

    private static async Task<bool> IsBluetoothTypeAsync(NetsDbContext db, int? typeId)
    {
        if (typeId is not int id)
        {
            return false;
        }

        var type = await db.DeviceTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return type is not null && DeviceTypeCatalog.IsBluetooth(type.Code, type.Name);
    }

    private static IReadOnlyList<string> FlattenDomains(IEnumerable<string>? domains)
    {
        return (domains ?? [])
            .SelectMany(ParseDomains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<int> SaveScanResultsAsync(int environmentId, int? groupId, IReadOnlyList<HostScanResult> hosts)
    {
        if (hosts.Count == 0)
        {
            return 0;
        }

        await using var db = AppDatabase.CreateContext();
        var env = await db.Environments.FirstOrDefaultAsync(x => x.Id == environmentId)
            ?? throw new ScanException(MsgSelectEnvironment);
        if (groupId is int gid)
        {
            var group = await db.Groups.FirstOrDefaultAsync(x => x.Id == gid)
                ?? throw new ScanException(MsgGroupMissing);
            if (group.NetworkEnvironmentId != environmentId)
            {
                throw new ScanException("分组不属于当前网络环境。");
            }
        }

        var devices = await db.Devices
            .Include(x => x.Domains)
            .Include(x => x.Nics)
            .Where(x => x.NetworkEnvironmentId == environmentId)
            .ToListAsync();
        var nextSerial = await NextSerialAsync(db);
        var saved = 0;
        foreach (var host in hosts)
        {
            var mac = NormalizeMac(host.Mac);
            var ip = host.Ip;
            var existing = FindDevice(devices, code: null, mac, ip);
            if (existing is null)
            {
                existing = new Device
                {
                    Code = FormatCode(nextSerial++),
                    NetworkEnvironmentId = environmentId,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = "Scan"
                };
                db.Devices.Add(existing);
                devices.Add(existing);
            }

            existing.DeviceGroupId = groupId ?? existing.DeviceGroupId;
            existing.Hostname = NullIfEmpty(host.Hostname) ?? existing.Hostname;
            existing.Vendor = OsTypeGuess.MeaningfulVendor(host.Vendor) ?? existing.Vendor;
            existing.Ip = ip;
            existing.Mac = mac ?? existing.Mac;
            existing.OsType = NullIfEmpty(host.OsType) ?? existing.OsType;
            existing.DeviceInfo = NullIfEmpty(host.DeviceInfo) ?? existing.DeviceInfo;
            existing.OpenPorts = host.OpenPorts.Count == 0 ? existing.OpenPorts : host.PortsDisplay;
            existing.Source = "Scan";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            UpsertNic(existing, ip, mac);
            saved++;
        }

        await db.SaveChangesAsync();
        _ = env;
        return saved;
    }

    public async Task<int> ImportDevicesAsync(int environmentId, IReadOnlyList<DeviceImportRow> rows)
    {
        await using var db = AppDatabase.CreateContext();
        if (!await db.Environments.AnyAsync(x => x.Id == environmentId))
        {
            throw new ScanException(MsgSelectEnvironment);
        }

        var types = await db.DeviceTypes.ToListAsync();
        var devices = await db.Devices
            .Include(x => x.Domains)
            .Include(x => x.Nics)
            .Where(x => x.NetworkEnvironmentId == environmentId)
            .ToListAsync();
        var nextSerial = await NextSerialAsync(db);
        var imported = 0;

        foreach (var row in rows)
        {
            if (IsEmptyRow(row))
            {
                continue;
            }

            var type = await ResolveTypeAsync(db, types, row.DeviceType);
            var nics = ParseNicPairs(row.Ip, row.Mac);
            var mac = nics.Select(n => n.Mac).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            var ip = nics.Select(n => n.Ip).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            var existing = FindDevice(devices, NullIfEmpty(row.Code), mac, ip);
            if (existing is null)
            {
                existing = new Device
                {
                    Code = NullIfEmpty(row.Code) ?? FormatCode(nextSerial++),
                    NetworkEnvironmentId = environmentId,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = "Import"
                };
                if (devices.Any(d => d.Code.Equals(existing.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.Code = FormatCode(nextSerial++);
                }

                db.Devices.Add(existing);
                devices.Add(existing);
            }

            existing.DeviceTypeId = type?.Id ?? existing.DeviceTypeId;
            existing.Hostname = NullIfEmpty(row.Hostname) ?? existing.Hostname;
            existing.Vendor = NullIfEmpty(row.Vendor) ?? existing.Vendor;
            existing.Brand = NullIfEmpty(row.Brand) ?? existing.Brand;
            existing.Model = NullIfEmpty(row.Model) ?? existing.Model;
            existing.Description = NullIfEmpty(row.Description) ?? existing.Description;
            if (nics.Count > 0)
            {
                ReplaceNics(existing, nics);
            }
            existing.OsType = NullIfEmpty(row.OsType) ?? existing.OsType;
            existing.DeviceInfo = NullIfEmpty(row.DeviceInfo) ?? existing.DeviceInfo;
            existing.OpenPorts = NullIfEmpty(row.OpenPorts) ?? existing.OpenPorts;
            existing.Remarks = NullIfEmpty(row.Remarks) ?? existing.Remarks;
            existing.Source = "Import";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            ReplaceDomains(existing, ParseDomains(row.Domains));
            imported++;
        }

        await db.SaveChangesAsync();
        return imported;
    }

    public async Task<MacMergeResult> MergeByMacAsync(int environmentId, IReadOnlyList<MacBindingRow> rows)
    {
        await using var db = AppDatabase.CreateContext();
        if (!await db.Environments.AnyAsync(x => x.Id == environmentId))
        {
            throw new ScanException(MsgSelectEnvironment);
        }

        var devices = await db.Devices
            .Include(x => x.Nics)
            .Where(x => x.NetworkEnvironmentId == environmentId)
            .ToListAsync();
        var nextSerial = await NextSerialAsync(db);
        var updated = 0;
        var added = 0;
        var now = DateTime.UtcNow;

        foreach (var row in rows)
        {
            var mac = NormalizeMac(row.Mac);
            var ip = NullIfEmpty(row.Ip);
            var remark = NullIfEmpty(row.Code);
            if (mac is null && ip is null && remark is null)
            {
                continue;
            }

            if (mac is null)
            {
                throw new ScanException($"「{remark ?? ip}」缺少 MAC，无法按 MAC 合并。");
            }

            var existing = FindDevice(devices, code: null, mac, ip: null);
            if (existing is null)
            {
                var code = AllocateCode(devices, remark ?? FormatCode(nextSerial++), exclude: null);
                existing = new Device
                {
                    Code = code,
                    NetworkEnvironmentId = environmentId,
                    Source = "Import",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                db.Devices.Add(existing);
                devices.Add(existing);
                UpsertNic(existing, ip, mac);
                added++;
                continue;
            }

            if (remark is not null)
            {
                existing.Code = AllocateCode(devices, MergeRemarkIntoCode(existing.Code, remark), existing);
            }

            UpsertNic(existing, ip, mac);
            existing.UpdatedAtUtc = now;
            updated++;
        }

        await db.SaveChangesAsync();
        return new MacMergeResult(updated, added);
    }

    public async Task ImportDeviceTypesAsync(IReadOnlyList<DeviceTypeImportRow> rows)
    {
        await using var db = AppDatabase.CreateContext();
        var existing = await db.DeviceTypes.ToListAsync();
        var order = existing.Select(x => x.SortOrder).DefaultIfEmpty(-1).Max();
        foreach (var row in rows)
        {
            var code = NullIfEmpty(row.Code)?.ToLowerInvariant();
            var name = NullIfEmpty(row.Name);
            if (code is null && name is null)
            {
                continue;
            }

            code ??= (name ?? "type").ToLowerInvariant();
            name ??= code;
            var entity = existing.FirstOrDefault(x =>
                x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)
                || x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entity is null)
            {
                entity = new DeviceType { SortOrder = ++order };
                db.DeviceTypes.Add(entity);
                existing.Add(entity);
            }

            entity.Code = code;
            entity.Name = name;
            entity.Description = NullIfEmpty(row.Description) ?? entity.Description;
            if (!string.IsNullOrWhiteSpace(row.ColorGroup) || string.IsNullOrWhiteSpace(entity.ColorGroup))
            {
                entity.ColorGroup = DeviceTypeColorGroups.Normalize(row.ColorGroup, code);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task<int> NextSerialAsync(NetsDbContext db)
    {
        var codes = await db.Devices.Select(x => x.Code).ToListAsync();
        var max = 0;
        foreach (var code in codes)
        {
            var digits = new string(code.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return max + 1;
    }

    private static Device? FindDevice(IEnumerable<Device> devices, string? code, string? mac, string? ip)
    {
        if (!string.IsNullOrWhiteSpace(mac))
        {
            return devices.FirstOrDefault(d => DeviceHasMac(d, mac));
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = devices.FirstOrDefault(d => d.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (byCode is not null)
            {
                return byCode;
            }
        }

        if (!string.IsNullOrWhiteSpace(ip))
        {
            return devices.FirstOrDefault(d => DeviceHasIp(d, ip) && !HasAnyMac(d));
        }

        return null;
    }

    private static bool DeviceHasMac(Device device, string mac) =>
        NormalizeMac(device.Mac) == mac || device.Nics.Any(n => NormalizeMac(n.Mac) == mac);

    private static bool DeviceHasIp(Device device, string ip) =>
        string.Equals(device.Ip, ip, StringComparison.OrdinalIgnoreCase)
        || device.Nics.Any(n => string.Equals(n.Ip, ip, StringComparison.OrdinalIgnoreCase));

    private static bool HasAnyMac(Device device) =>
        NormalizeMac(device.Mac) is not null || device.Nics.Any(n => NormalizeMac(n.Mac) is not null);

    private static async Task EnsureUniqueMacsAsync(
        NetsDbContext db,
        int environmentId,
        IEnumerable<string?> macs,
        int? excludeDeviceId)
    {
        var wanted = macs
            .Select(NormalizeMac)
            .Where(m => m is not null)
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wanted.Count == 0)
        {
            return;
        }

        var others = await db.Devices
            .AsNoTracking()
            .Include(x => x.Nics)
            .Where(x => x.NetworkEnvironmentId == environmentId)
            .ToListAsync();
        var conflict = wanted.FirstOrDefault(mac =>
            others.Any(d => d.Id != excludeDeviceId && DeviceHasMac(d, mac)));
        if (conflict is not null)
        {
            throw new ScanException($"MAC「{conflict}」在当前环境已存在，设备以 MAC 作为唯一标识。");
        }
    }

    private static async Task<DeviceType?> ResolveTypeAsync(NetsDbContext db, List<DeviceType> types, string? nameOrCode)
    {
        var key = NullIfEmpty(nameOrCode);
        if (key is null)
        {
            return null;
        }

        var match = types.FirstOrDefault(t =>
            t.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
            || t.Code.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        var created = new DeviceType
        {
            Code = key.ToLowerInvariant(),
            Name = key,
            SortOrder = types.Count,
            ColorGroup = DeviceTypeColorGroups.ForCode(key)
        };
        db.DeviceTypes.Add(created);
        types.Add(created);
        await db.SaveChangesAsync();
        return created;
    }

    private static void ReplaceNics(Device device, IReadOnlyList<DeviceNicInput> nics)
    {
        device.Nics.Clear();
        for (var i = 0; i < nics.Count; i++)
        {
            device.Nics.Add(new DeviceNic
            {
                Name = nics[i].Name,
                Ip = nics[i].Ip,
                Mac = nics[i].Mac,
                SortOrder = i
            });
        }

        SyncPrimaryFromNics(device);
    }

    private static void EnsureNicRows(Device device)
    {
        if (device.Nics.Count > 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(device.Ip) && string.IsNullOrWhiteSpace(device.Mac))
        {
            return;
        }

        device.Nics.Add(new DeviceNic
        {
            Ip = device.Ip,
            Mac = device.Mac,
            SortOrder = 0
        });
    }

    private static void UpsertNic(Device device, string? ip, string? mac)
    {
        ip = NullIfEmpty(ip);
        mac = NormalizeMac(mac);
        EnsureNicRows(device);
        if (TryUpsertNicByMac(device, ip, mac) || TryUpsertNicByIp(device, ip, mac))
        {
            return;
        }

        ApplyNicToPrimaryOrAdd(device, ip, mac);
        SyncPrimaryFromNics(device);
    }

    private static bool TryUpsertNicByMac(Device device, string? ip, string? mac)
    {
        if (mac is null)
        {
            return false;
        }

        var byMac = device.Nics.FirstOrDefault(n => NormalizeMac(n.Mac) == mac);
        if (byMac is null)
        {
            return false;
        }

        if (ip is not null)
        {
            byMac.Ip = ip;
        }

        SyncPrimaryFromNics(device);
        return true;
    }

    private static bool TryUpsertNicByIp(Device device, string? ip, string? mac)
    {
        if (ip is null)
        {
            return false;
        }

        var byIp = device.Nics.FirstOrDefault(n =>
            string.Equals(n.Ip, ip, StringComparison.OrdinalIgnoreCase));
        if (byIp is null)
        {
            return false;
        }

        if (mac is not null)
        {
            byIp.Mac = mac;
        }

        SyncPrimaryFromNics(device);
        return true;
    }

    private static void ApplyNicToPrimaryOrAdd(Device device, string? ip, string? mac)
    {
        if (device.Nics.Count == 0)
        {
            device.Nics.Add(new DeviceNic { Ip = ip, Mac = mac, SortOrder = 0 });
            return;
        }

        var primary = device.Nics.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).First();
        if (ip is not null)
        {
            primary.Ip = ip;
        }

        if (mac is not null)
        {
            primary.Mac = mac;
        }
    }

    private static void SyncPrimaryFromNics(Device device)
    {
        var nic = device.Nics.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).FirstOrDefault();
        device.Ip = nic?.Ip;
        device.Mac = nic?.Mac;
    }

    private static List<DeviceNicInput> ParseNicPairs(string? ipsText, string? macsText)
    {
        var ips = SplitNicValues(ipsText);
        var macs = SplitNicValues(macsText).Select(NormalizeMac).ToList();
        var count = Math.Max(ips.Count, macs.Count);
        var nics = new List<DeviceNicInput>();
        for (var i = 0; i < count; i++)
        {
            var ip = i < ips.Count ? NullIfEmpty(ips[i]) : null;
            var mac = i < macs.Count ? macs[i] : null;
            if (ip is null && mac is null)
            {
                continue;
            }

            nics.Add(new DeviceNicInput { Ip = ip, Mac = mac });
        }

        return nics;
    }

    public const string NicEmptySlot = "_";

    public static bool IsNicPlaceholder(string? value) =>
        value is not null && value.Trim() is "_" or "-" or "—";

    public static string NicSlotDisplay(string? value) =>
        string.IsNullOrWhiteSpace(value) ? NicEmptySlot : value;

    public static IReadOnlyList<string> SplitNicValues(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "—")
        {
            return [];
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized
            .Split([';', '；', ',', '，', '、', '\n'], StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Select(s => IsNicPlaceholder(s) ? "" : s)
            .ToList();
    }

    private static void ReplaceDomains(Device device, IReadOnlyList<string> domains)
    {
        device.Domains.Clear();
        foreach (var domain in domains.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            device.Domains.Add(new DeviceDomain { Domain = domain });
        }
    }

    public static IReadOnlyList<string> ParseDomains(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([';', ',', '，', '、', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac) || mac == "—" || IsNicPlaceholder(mac))
        {
            return null;
        }

        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length < 12)
        {
            return mac.Trim();
        }

        hex = hex[..12].ToUpperInvariant();
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    public static string FormatMacQuery(string? mac)
    {
        if (string.IsNullOrEmpty(mac))
        {
            return "";
        }

        return mac.Replace('-', ':').ToUpperInvariant();
    }

    public static bool MatchesMacQuery(string? mac, string? query)
    {
        var keyword = FormatMacQuery(query);
        if (keyword.Length == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(mac))
        {
            return false;
        }

        return FormatMacQuery(mac).Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string MergeRemarkIntoCode(string current, string remark)
    {
        if (string.IsNullOrWhiteSpace(current) || IsGeneratedCode(current))
        {
            return remark;
        }

        if (current.Equals(remark, StringComparison.OrdinalIgnoreCase)
            || current.Contains(remark, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var merged = $"{current}/{remark}";
        return merged.Length <= 64 ? merged : remark;
    }

    private static bool IsGeneratedCode(string code)
    {
        if (!code.StartsWith("DEV-", StringComparison.OrdinalIgnoreCase) || code.Length < 5)
        {
            return false;
        }

        for (var i = 4; i < code.Length; i++)
        {
            if (!char.IsDigit(code[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string AllocateCode(IReadOnlyCollection<Device> devices, string desired, Device? exclude)
    {
        if (devices.All(d => d == exclude || !d.Code.Equals(desired, StringComparison.OrdinalIgnoreCase)))
        {
            return desired;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{desired}-{i}";
            if (candidate.Length > 64)
            {
                candidate = $"{desired[..Math.Max(1, 64 - 4)]}-{i}";
            }

            if (devices.All(d => d == exclude || !d.Code.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return FormatCode(devices.Count + 1);
    }

    private static string FormatCode(int serial) => $"DEV-{serial:D4}";

    public static string TrimInput(string? value, bool keepLineBreaks = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .ToList();
        var start = 0;
        var end = lines.Count - 1;
        while (start <= end && lines[start].Length == 0)
        {
            start++;
        }

        while (end >= start && lines[end].Length == 0)
        {
            end--;
        }

        if (start > end)
        {
            return "";
        }

        var kept = lines.Skip(start).Take(end - start + 1);
        return keepLineBreaks
            ? string.Join("\n", kept)
            : string.Join(" ", kept.Where(static line => line.Length > 0));
    }

    private static string RequireName(string? value, string label)
    {
        var text = TrimInput(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ScanException($"{label}不能为空。");
        }

        return text;
    }

    private static string? NullIfEmpty(string? value, bool keepLineBreaks = false)
    {
        var text = TrimInput(value, keepLineBreaks);
        return string.IsNullOrWhiteSpace(text) || text == "—" ? null : text;
    }

    private static bool IsEmptyRow(DeviceImportRow row) =>
        string.IsNullOrWhiteSpace(row.Code)
        && string.IsNullOrWhiteSpace(row.Hostname)
        && string.IsNullOrWhiteSpace(row.Ip)
        && string.IsNullOrWhiteSpace(row.Mac);
}

public sealed class DeviceTypeEditInput
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public string? IconSourcePath { get; init; }
    public string? BuiltinIconKey { get; init; }
    public bool RestoreDefaultIcon { get; init; }
    public string? ColorGroup { get; init; }
}

public sealed class DeviceEditInput
{
    public string? Code { get; init; }
    public int? DeviceTypeId { get; init; }
    public int? DeviceGroupId { get; init; }
    public string? Hostname { get; init; }
    public string? Vendor { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Description { get; init; }
    public string? Ip { get; init; }
    public string? Mac { get; init; }
    public string? OsType { get; init; }
    public string? DeviceInfo { get; init; }
    public string? OpenPorts { get; init; }
    public string? Remarks { get; init; }
    public IReadOnlyList<string> Domains { get; init; } = [];
    public IReadOnlyList<DeviceNicInput> Nics { get; init; } = [];
}

public sealed class DeviceNicInput
{
    public string? Name { get; init; }
    public string? Ip { get; init; }
    public string? Mac { get; init; }
}

public sealed record MacBindingRow(string? Mac, string? Ip, string? Code);

public sealed record MacMergeResult(int Updated, int Added);

public sealed class DeviceImportRow
{
    public string? Code { get; set; }
    public string? DeviceType { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Description { get; set; }
    public string? Domains { get; set; }
    public string? Ip { get; set; }
    public string? Mac { get; set; }
    public string? OsType { get; set; }
    public string? DeviceInfo { get; set; }
    public string? OpenPorts { get; set; }
    public string? Remarks { get; set; }
}

public sealed class DeviceTypeImportRow
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? ColorGroup { get; set; }
}
