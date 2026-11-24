using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public sealed class InventoryBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<InventoryBackupPackage> CreateBackupAsync(InventoryBackupModules modules, string? appVersion)
    {
        if (!modules.Any)
        {
            throw new ScanException("请至少勾选一个要导出的模块。");
        }

        await using var db = AppDatabase.CreateContext();
        var file = new InventoryBackupFile
        {
            AppVersion = appVersion,
            ExportedAtUtc = DateTime.UtcNow
        };
        IReadOnlyDictionary<string, byte[]> customIcons = new Dictionary<string, byte[]>();

        if (modules.Environments)
        {
            file.Environments = await db.Environments.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new EnvironmentBackupDto
                {
                    Name = x.Name,
                    Description = x.Description,
                    DefaultCidr = x.DefaultCidr
                })
                .ToListAsync();
            file.Modules.Add("environments");
        }

        if (modules.Groups)
        {
            var groups = await db.Groups.AsNoTracking()
                .Include(x => x.NetworkEnvironment)
                .OrderBy(x => x.Name)
                .ToListAsync();
            file.Groups = groups
                .OrderBy(x => x.NetworkEnvironment.Name)
                .ThenBy(x => x.Name)
                .Select(x => new GroupBackupDto
                {
                    EnvironmentName = x.NetworkEnvironment.Name,
                    Name = x.Name,
                    Description = x.Description
                })
                .ToList();
            file.Modules.Add("groups");
        }

        if (modules.DeviceTypes)
        {
            var types = await db.DeviceTypes.AsNoTracking()
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync();
            file.DeviceTypes = types.Select(x => new DeviceTypeBackupDto
            {
                Code = x.Code,
                Name = x.Name,
                Description = x.Description,
                SortOrder = x.SortOrder,
                IconFile = x.IsCustomIcon ? x.IconFile : null,
                IsCustomIcon = x.IsCustomIcon,
                ColorGroup = x.ColorGroup
            }).ToList();
            customIcons = DeviceTypeIconStore.ReadCustomFiles(types);
            file.Modules.Add("deviceTypes");
        }

        if (modules.Devices)
        {
            var devices = await db.Devices.AsNoTracking()
                .Include(x => x.NetworkEnvironment)
                .Include(x => x.DeviceGroup)
                .Include(x => x.DeviceType)
                .Include(x => x.Domains)
                .Include(x => x.Nics)
                .OrderBy(x => x.Code)
                .ToListAsync();
            file.Devices = devices.Select(x => new DeviceBackupDto
            {
                Code = x.Code,
                EnvironmentName = x.NetworkEnvironment.Name,
                GroupName = x.DeviceGroup?.Name,
                DeviceTypeCode = x.DeviceType?.Code,
                DeviceTypeName = x.DeviceType?.Name,
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
                Domains = x.Domains.Select(d => d.Domain).ToList(),
                Nics = x.OrderedNics().Select(n => new DeviceNicBackupDto
                {
                    Name = n.Name,
                    Ip = n.Ip,
                    Mac = n.Mac,
                    SortOrder = n.SortOrder
                }).ToList()
            }).ToList();
            file.Modules.Add("devices");
        }

        if (modules.Topology)
        {
            file.Topologies = await ExportTopologiesAsync(db);
            file.Modules.Add("topology");
        }

        return new InventoryBackupPackage
        {
            File = file,
            CustomIcons = customIcons
        };
    }

    public static async Task WriteBackupAsync(string path, InventoryBackupPackage package)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var jsonEntry = zip.CreateEntry("qolome-nets.json");
            await using var stream = await jsonEntry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            using (MachineBoundSecret.OmitPassword())
            {
                await JsonSerializer.SerializeAsync(stream, package.File, JsonOptions).ConfigureAwait(false);
            }

            foreach (var (name, bytes) in package.CustomIcons)
            {
                var iconEntry = zip.CreateEntry($"icons/{name}");
                await using var iconStream = await iconEntry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                await iconStream.WriteAsync(bytes).ConfigureAwait(false);
            }

            return;
        }

        if (package.CustomIcons.Count > 0)
        {
            throw new ScanException("当前备份含自定义图标，请保存为 ZIP。默认图标随软件自带，不会写入备份。");
        }

        string json;
        using (MachineBoundSecret.OmitPassword())
        {
            json = JsonSerializer.Serialize(package.File, JsonOptions);
        }

        await File.WriteAllTextAsync(path, json);
    }

    public static async Task<(InventoryBackupFile File, IReadOnlyDictionary<string, byte[]> Icons)> ReadBackupAsync(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var zip = await ZipFile.OpenReadAsync(path).ConfigureAwait(false);
            var jsonEntry = zip.GetEntry("qolome-nets.json")
                ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ?? throw new ScanException("ZIP 中没有找到 qolome-nets.json。");
            await using var jsonStream = await jsonEntry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
            using var reader = new StreamReader(jsonStream);
            var zipJson = await reader.ReadToEndAsync();
            var zipFile = DeserializeBackup(zipJson);
            var icons = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var full = entry.FullName.Replace('\\', '/');
                if (full.Contains("..", StringComparison.Ordinal) || !full.StartsWith("icons/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = Path.GetFileName(full);
                if (string.IsNullOrWhiteSpace(name) || entry.Length == 0 || entry.Length > DeviceTypeIconStore.MaxFileBytes)
                {
                    continue;
                }

                if (!DeviceTypeIconStore.IsSupportedExtension(Path.GetExtension(name)))
                {
                    continue;
                }

                await using var iconStream = await entry.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                using var ms = new MemoryStream();
                await iconStream.CopyToAsync(ms);
                icons[name] = ms.ToArray();
            }

            return (zipFile, icons);
        }

        var json = await File.ReadAllTextAsync(path);
        return (DeserializeBackup(json), new Dictionary<string, byte[]>());
    }

    private static InventoryBackupFile DeserializeBackup(string json)
    {
        var file = JsonSerializer.Deserialize<InventoryBackupFile>(json, JsonOptions)
            ?? throw new ScanException("无法解析配置文件。");
        if (!string.Equals(file.App, "qolome-nets", StringComparison.OrdinalIgnoreCase)
            && file.Environments is null
            && file.Groups is null
            && file.DeviceTypes is null
            && file.Devices is null
            && file.Topologies is null)
        {
            throw new ScanException("这不是 qolome-nets 的配置备份文件。");
        }

        return file;
    }

    public static async Task<InventoryBackupResult> ImportAsync(
        InventoryBackupFile file,
        InventoryBackupModules modules,
        IReadOnlyDictionary<string, byte[]>? customIcons = null)
    {
        if (!modules.Any)
        {
            throw new ScanException("请至少勾选一个要导入的模块。");
        }

        var result = new InventoryBackupResult();
        await using var db = AppDatabase.CreateContext();

        await ImportDeviceTypesModuleAsync(db, file, modules, customIcons, result);
        await ImportEnvironmentsModuleAsync(db, file, modules, result);
        await ImportGroupsModuleAsync(db, file, modules, result);
        await ImportDevicesModuleAsync(db, file, modules, result);
        await ImportTopologyModuleAsync(db, file, modules, result);

        await db.SaveChangesAsync();
        return result;
    }

    private static async Task ImportDeviceTypesModuleAsync(
        NetsDbContext db,
        InventoryBackupFile file,
        InventoryBackupModules modules,
        IReadOnlyDictionary<string, byte[]>? customIcons,
        InventoryBackupResult result)
    {
        if (!modules.DeviceTypes)
        {
            return;
        }

        if (file.DeviceTypes is not { Count: > 0 })
        {
            result.Warnings.Add("文件中没有设备类型。");
            return;
        }

        var (count, icons) = await ImportDeviceTypesAsync(db, file.DeviceTypes, customIcons);
        result.DeviceTypes = count;
        result.CustomIcons = icons;
    }

    private static async Task ImportEnvironmentsModuleAsync(
        NetsDbContext db,
        InventoryBackupFile file,
        InventoryBackupModules modules,
        InventoryBackupResult result)
    {
        if (!modules.Environments)
        {
            return;
        }

        if (file.Environments is not { Count: > 0 })
        {
            result.Warnings.Add("文件中没有网络环境。");
            return;
        }

        result.Environments = await ImportEnvironmentsAsync(db, file.Environments);
    }

    private static async Task ImportGroupsModuleAsync(
        NetsDbContext db,
        InventoryBackupFile file,
        InventoryBackupModules modules,
        InventoryBackupResult result)
    {
        if (!modules.Groups)
        {
            return;
        }

        if (file.Groups is not { Count: > 0 })
        {
            result.Warnings.Add("文件中没有网络分组。");
            return;
        }

        var (count, skipped) = await ImportGroupsAsync(db, file.Groups);
        result.Groups = count;
        result.Skipped += skipped;
    }

    private static async Task ImportDevicesModuleAsync(
        NetsDbContext db,
        InventoryBackupFile file,
        InventoryBackupModules modules,
        InventoryBackupResult result)
    {
        if (!modules.Devices)
        {
            return;
        }

        if (file.Devices is not { Count: > 0 })
        {
            result.Warnings.Add("文件中没有设备信息。");
            return;
        }

        var (count, skipped) = await ImportDevicesAsync(db, file.Devices);
        result.Devices = count;
        result.Skipped += skipped;
    }

    private static async Task ImportTopologyModuleAsync(
        NetsDbContext db,
        InventoryBackupFile file,
        InventoryBackupModules modules,
        InventoryBackupResult result)
    {
        if (!modules.Topology)
        {
            return;
        }

        if (file.Topologies is not { Count: > 0 })
        {
            result.Warnings.Add("文件中没有拓扑图。");
            return;
        }

        var (count, skipped) = await ImportTopologiesAsync(db, file.Topologies);
        result.Topologies = count;
        result.Skipped += skipped;
    }

    private static async Task<int> ImportEnvironmentsAsync(NetsDbContext db, IReadOnlyList<EnvironmentBackupDto> rows)
    {
        var existing = await db.Environments.ToListAsync();
        var count = 0;
        foreach (var row in rows)
        {
            var name = row.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var entity = existing.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var now = DateTime.UtcNow;
            if (entity is null)
            {
                entity = new NetworkEnvironment
                {
                    Name = name,
                    CreatedAtUtc = now
                };
                db.Environments.Add(entity);
                existing.Add(entity);
            }

            entity.Description = NullIfEmpty(row.Description) ?? entity.Description;
            entity.DefaultCidr = NullIfEmpty(row.DefaultCidr) ?? entity.DefaultCidr;
            entity.UpdatedAtUtc = now;
            count++;
        }

        await db.SaveChangesAsync();
        return count;
    }

    private static async Task<(int Count, int Skipped)> ImportGroupsAsync(NetsDbContext db, IReadOnlyList<GroupBackupDto> rows)
    {
        var environments = await db.Environments.ToListAsync();
        var groups = await db.Groups.ToListAsync();
        var count = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var envName = row.EnvironmentName.Trim();
            var name = row.Name.Trim();
            if (envName.Length == 0 || name.Length == 0)
            {
                skipped++;
                continue;
            }

            var env = environments.FirstOrDefault(x => x.Name.Equals(envName, StringComparison.OrdinalIgnoreCase));
            if (env is null)
            {
                skipped++;
                continue;
            }

            var entity = groups.FirstOrDefault(x =>
                x.NetworkEnvironmentId == env.Id && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entity is null)
            {
                entity = new DeviceGroup
                {
                    NetworkEnvironmentId = env.Id,
                    Name = name,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Groups.Add(entity);
                groups.Add(entity);
            }

            entity.Description = NullIfEmpty(row.Description) ?? entity.Description;
            count++;
        }

        await db.SaveChangesAsync();
        return (count, skipped);
    }

    private static async Task<(int Count, int Icons)> ImportDeviceTypesAsync(
        NetsDbContext db,
        IReadOnlyList<DeviceTypeBackupDto> rows,
        IReadOnlyDictionary<string, byte[]>? customIcons)
    {
        var existing = await db.DeviceTypes.ToListAsync();
        var count = 0;
        var icons = 0;
        foreach (var row in rows)
        {
            if (!TryImportDeviceTypeRow(db, row, customIcons, existing, ref icons))
            {
                continue;
            }

            count++;
        }

        await db.SaveChangesAsync();
        return (count, icons);
    }

    private static bool TryImportDeviceTypeRow(
        NetsDbContext db,
        DeviceTypeBackupDto row,
        IReadOnlyDictionary<string, byte[]>? customIcons,
        List<DeviceType> existing,
        ref int icons)
    {
        var code = NullIfEmpty(row.Code)?.ToLowerInvariant();
        var name = NullIfEmpty(row.Name);
        if (code is null && name is null)
        {
            return false;
        }

        code ??= (name ?? "type").ToLowerInvariant();
        name ??= code;
        var entity = existing.FirstOrDefault(x =>
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)
            || x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            var order = existing.Select(x => x.SortOrder).DefaultIfEmpty(-1).Max() + 1;
            entity = new DeviceType { SortOrder = row.SortOrder > 0 ? row.SortOrder : order };
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

        if (row.SortOrder > 0)
        {
            entity.SortOrder = row.SortOrder;
        }

        if (TryInstallCustomTypeIcon(entity, row, customIcons))
        {
            icons++;
        }

        return true;
    }

    private static bool TryInstallCustomTypeIcon(
        DeviceType entity,
        DeviceTypeBackupDto row,
        IReadOnlyDictionary<string, byte[]>? customIcons)
    {
        if (!row.IsCustomIcon || NullIfEmpty(row.IconFile) is not string iconName || !DeviceTypeIconStore.IsSvg(iconName))
        {
            return false;
        }

        if (customIcons is null || !customIcons.TryGetValue(Path.GetFileName(iconName), out var bytes))
        {
            return false;
        }

        var safe = DeviceTypeIconStore.SanitizeFileName(iconName);
        DeviceTypeIconStore.InstallCustomFile(safe, bytes);
        if (entity.IsCustomIcon && !string.Equals(entity.IconFile, safe, StringComparison.OrdinalIgnoreCase))
        {
            DeviceTypeIconStore.DeleteCustom(entity.IconFile);
        }

        entity.IconFile = safe;
        entity.IsCustomIcon = true;
        return true;
    }

    private static async Task<(int Count, int Skipped)> ImportDevicesAsync(NetsDbContext db, IReadOnlyList<DeviceBackupDto> rows)
    {
        var environments = await db.Environments.ToListAsync();
        var groups = await db.Groups.Include(x => x.NetworkEnvironment).ToListAsync();
        var types = await db.DeviceTypes.ToListAsync();
        var devices = await db.Devices.Include(x => x.Domains).Include(x => x.Nics).ToListAsync();
        var nextSerial = await NextSerialAsync(db);
        var count = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            if (!TryResolveImportEnvironment(row, environments, out var env))
            {
                skipped++;
                continue;
            }

            var entity = FindOrCreateImportDevice(db, row, env.Id, devices, ref nextSerial);
            ApplyImportedDeviceFields(entity, row, env, groups, types);
            count++;
        }

        await db.SaveChangesAsync();
        return (count, skipped);
    }

    private static bool TryResolveImportEnvironment(
        DeviceBackupDto row,
        IReadOnlyList<NetworkEnvironment> environments,
        out NetworkEnvironment env)
    {
        var envName = NullIfEmpty(row.EnvironmentName);
        if (envName is null)
        {
            env = null!;
            return false;
        }

        env = environments.FirstOrDefault(x => x.Name.Equals(envName, StringComparison.OrdinalIgnoreCase))!;
        return env is not null;
    }

    private static Device FindOrCreateImportDevice(
        NetsDbContext db,
        DeviceBackupDto row,
        int environmentId,
        List<Device> devices,
        ref int nextSerial)
    {
        var code = NullIfEmpty(row.Code);
        var nics = BackupNics(row);
        var mac = nics.Select(n => InventoryService.NormalizeMac(n.Mac)).FirstOrDefault(m => m is not null)
            ?? InventoryService.NormalizeMac(row.Mac);
        var inEnv = devices.Where(x => x.NetworkEnvironmentId == environmentId);
        Device? entity = mac is not null
            ? inEnv.FirstOrDefault(x =>
                InventoryService.NormalizeMac(x.Mac) == mac
                || x.Nics.Any(n => InventoryService.NormalizeMac(n.Mac) == mac))
            : null;
        if (entity is null && code is not null)
        {
            entity = inEnv.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        }

        if (entity is not null)
        {
            return entity;
        }

        entity = new Device
        {
            Code = code ?? $"DEV-{nextSerial++:D4}",
            CreatedAtUtc = DateTime.UtcNow
        };
        if (devices.Exists(d => d.Code.Equals(entity.Code, StringComparison.OrdinalIgnoreCase)))
        {
            entity.Code = $"DEV-{nextSerial++:D4}";
        }

        db.Devices.Add(entity);
        devices.Add(entity);
        return entity;
    }

    private static void ApplyImportedDeviceFields(
        Device entity,
        DeviceBackupDto row,
        NetworkEnvironment env,
        IReadOnlyList<DeviceGroup> groups,
        IReadOnlyList<DeviceType> types)
    {
        int? groupId = null;
        var groupName = NullIfEmpty(row.GroupName);
        if (groupName is not null)
        {
            var group = groups.FirstOrDefault(x =>
                x.NetworkEnvironmentId == env.Id && x.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
            groupId = group?.Id;
        }

        int? typeId = null;
        var typeKey = NullIfEmpty(row.DeviceTypeCode) ?? NullIfEmpty(row.DeviceTypeName);
        if (typeKey is not null)
        {
            var type = types.FirstOrDefault(x =>
                x.Code.Equals(typeKey, StringComparison.OrdinalIgnoreCase)
                || x.Name.Equals(typeKey, StringComparison.OrdinalIgnoreCase));
            typeId = type?.Id;
        }

        var nics = BackupNics(row);
        var mac = nics.Select(n => InventoryService.NormalizeMac(n.Mac)).FirstOrDefault(m => m is not null)
            ?? InventoryService.NormalizeMac(row.Mac);

        entity.NetworkEnvironmentId = env.Id;
        entity.DeviceGroupId = groupId;
        entity.DeviceTypeId = typeId ?? entity.DeviceTypeId;
        entity.Hostname = NullIfEmpty(row.Hostname) ?? entity.Hostname;
        entity.Vendor = NullIfEmpty(row.Vendor) ?? entity.Vendor;
        entity.Brand = NullIfEmpty(row.Brand) ?? entity.Brand;
        entity.Model = NullIfEmpty(row.Model) ?? entity.Model;
        entity.Description = NullIfEmpty(row.Description) ?? entity.Description;
        entity.Ip = NullIfEmpty(row.Ip) ?? entity.Ip;
        entity.Mac = mac ?? entity.Mac;
        entity.OsType = NullIfEmpty(row.OsType) ?? entity.OsType;
        entity.DeviceInfo = NullIfEmpty(row.DeviceInfo) ?? entity.DeviceInfo;
        entity.OpenPorts = NullIfEmpty(row.OpenPorts) ?? entity.OpenPorts;
        entity.Remarks = NullIfEmpty(row.Remarks) ?? entity.Remarks;
        entity.Source = NullIfEmpty(row.Source) ?? "Import";
        entity.UpdatedAtUtc = DateTime.UtcNow;
        ReplaceDomains(entity, row.Domains);
        if (nics.Count > 0)
        {
            ReplaceNics(entity, nics);
        }
    }

    private static async Task<List<TopologyBackupDto>> ExportTopologiesAsync(NetsDbContext db)
    {
        var environments = await db.Environments.AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();
        var devices = await db.Devices.AsNoTracking()
            .Select(x => new { x.Id, x.Code, x.NetworkEnvironmentId })
            .ToListAsync();
        var store = new TopologyStore();
        var list = new List<TopologyBackupDto>();
        foreach (var env in environments)
        {
            var ids = devices.Where(d => d.NetworkEnvironmentId == env.Id).Select(d => d.Id).ToHashSet();
            var document = await store.LoadAsync(env.Id, ids);
            if (document.Nodes.Count == 0 && document.Links.Count == 0)
            {
                continue;
            }

            var codeById = devices
                .Where(d => d.NetworkEnvironmentId == env.Id)
                .ToDictionary(d => d.Id, d => d.Code);
            list.Add(ToTopologyBackup(env.Name, document, codeById));
        }

        return list;
    }

    private static TopologyBackupDto ToTopologyBackup(
        string environmentName,
        TopologyDocument document,
        IReadOnlyDictionary<int, string> codeById)
    {
        var nodes = new List<TopologyNodeBackupDto>();
        foreach (var node in document.Nodes)
        {
            var dto = new TopologyNodeBackupDto
            {
                Id = node.Id,
                Kind = node.Kind,
                IspKey = node.IspKey,
                Label = node.Label,
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height,
                ContainerId = node.ContainerId
            };
            if (node.IsDevice)
            {
                if (!codeById.TryGetValue(node.DeviceId, out var code) || string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                dto.DeviceCode = code;
            }

            nodes.Add(dto);
        }

        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        return new TopologyBackupDto
        {
            EnvironmentName = environmentName,
            Nodes = nodes,
            Links = document.Links
                .Where(l => nodeIds.Contains(l.FromNodeId) && nodeIds.Contains(l.ToNodeId))
                .Select(l => new TopologyLinkBackupDto
                {
                    FromNodeId = l.FromNodeId,
                    ToNodeId = l.ToNodeId,
                    Kind = l.Kind,
                    Color = l.Color,
                    Dashed = l.Dashed,
                    Label = l.Label,
                    Curve = l.Curve
                })
                .ToList()
        };
    }

    private static async Task<(int Count, int Skipped)> ImportTopologiesAsync(
        NetsDbContext db,
        IReadOnlyList<TopologyBackupDto> rows)
    {
        var environments = await db.Environments.AsNoTracking().ToListAsync();
        var devices = await db.Devices.AsNoTracking().ToListAsync();
        var count = 0;
        var skipped = 0;
        foreach (var row in rows)
        {
            var envName = row.EnvironmentName.Trim();
            if (envName.Length == 0)
            {
                skipped++;
                continue;
            }

            var env = environments.FirstOrDefault(x => x.Name.Equals(envName, StringComparison.OrdinalIgnoreCase));
            if (env is null)
            {
                skipped++;
                continue;
            }

            var envDevices = devices.Where(d => d.NetworkEnvironmentId == env.Id).ToList();
            var document = FromTopologyBackup(row, env.Id, envDevices);
            await TopologyStore.SaveAsync(document);
            count++;
        }

        return (count, skipped);
    }

    private static TopologyDocument FromTopologyBackup(
        TopologyBackupDto row,
        int environmentId,
        IReadOnlyList<Device> envDevices)
    {
        var idMap = new Dictionary<int, int>();
        var usedIds = new HashSet<int>();
        var nodes = BuildBackupNodes(row.Nodes, envDevices, idMap, usedIds);
        RemapBackupContainerParents(nodes, idMap);
        var links = BuildBackupLinks(row.Links, idMap);
        return new TopologyDocument
        {
            EnvironmentId = environmentId,
            Nodes = nodes,
            Links = links
        };
    }

    private static List<TopologyNodeDto> BuildBackupNodes(
        IReadOnlyList<TopologyNodeBackupDto> rows,
        IReadOnlyList<Device> envDevices,
        Dictionary<int, int> idMap,
        HashSet<int> usedIds)
    {
        var nodes = new List<TopologyNodeDto>();
        foreach (var node in rows)
        {
            var kind = TopologyNodeKinds.Normalize(node.Kind, string.IsNullOrWhiteSpace(node.DeviceCode) ? 0 : 1);
            if (kind == TopologyNodeKinds.Device)
            {
                AddBackupDeviceNode(node, envDevices, idMap, usedIds, nodes);
            }
            else
            {
                AddBackupSpecialNode(node, kind, idMap, usedIds, nodes);
            }
        }

        return nodes;
    }

    private static void AddBackupDeviceNode(
        TopologyNodeBackupDto node,
        IReadOnlyList<Device> envDevices,
        Dictionary<int, int> idMap,
        HashSet<int> usedIds,
        List<TopologyNodeDto> nodes)
    {
        var code = NullIfEmpty(node.DeviceCode);
        if (code is null)
        {
            return;
        }

        var device = envDevices.FirstOrDefault(d => d.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (device is null || !usedIds.Add(device.Id))
        {
            return;
        }

        idMap[node.Id] = device.Id;
        nodes.Add(new TopologyNodeDto
        {
            Id = device.Id,
            DeviceId = device.Id,
            Kind = TopologyNodeKinds.Device,
            Label = node.Label,
            X = node.X,
            Y = node.Y,
            ContainerId = node.ContainerId
        });
    }

    private static void AddBackupSpecialNode(
        TopologyNodeBackupDto node,
        string kind,
        Dictionary<int, int> idMap,
        HashSet<int> usedIds,
        List<TopologyNodeDto> nodes)
    {
        var newId = node.Id < 0 && usedIds.Add(node.Id)
            ? node.Id
            : NextIspBackupId(usedIds);
        idMap[node.Id] = newId;
        if (kind == TopologyNodeKinds.Container)
        {
            nodes.Add(new TopologyNodeDto
            {
                Id = newId,
                DeviceId = 0,
                Kind = TopologyNodeKinds.Container,
                Label = NullIfEmpty(node.Label) ?? "容器",
                X = node.X,
                Y = node.Y,
                Width = node.Width,
                Height = node.Height
            });
            return;
        }

        nodes.Add(new TopologyNodeDto
        {
            Id = newId,
            DeviceId = 0,
            Kind = TopologyNodeKinds.Isp,
            IspKey = IspPresets.Normalize(node.IspKey, node.Label),
            Label = NullIfEmpty(node.Label) ?? IspPresets.DefaultLabel(IspPresets.Custom),
            X = node.X,
            Y = node.Y
        });
    }

    private static void RemapBackupContainerParents(IReadOnlyList<TopologyNodeDto> nodes, IReadOnlyDictionary<int, int> idMap)
    {
        foreach (var node in nodes)
        {
            if (node.IsContainer || node.ContainerId is not int oldParent || !idMap.TryGetValue(oldParent, out var mapped))
            {
                node.ContainerId = null;
                continue;
            }

            node.ContainerId = nodes.Any(n => n.Id == mapped && n.IsContainer) ? mapped : null;
        }
    }

    private static List<TopologyLinkDto> BuildBackupLinks(
        IReadOnlyList<TopologyLinkBackupDto> rows,
        IReadOnlyDictionary<int, int> idMap)
    {
        var links = new List<TopologyLinkDto>();
        foreach (var link in rows)
        {
            if (!idMap.TryGetValue(link.FromNodeId, out var from) || !idMap.TryGetValue(link.ToNodeId, out var to))
            {
                continue;
            }

            links.Add(new TopologyLinkDto
            {
                FromNodeId = from,
                ToNodeId = to,
                FromDeviceId = from,
                ToDeviceId = to,
                Kind = TopologyLinkKinds.Normalize(link.Kind),
                Color = NullIfEmpty(link.Color),
                Dashed = link.Dashed,
                Label = NullIfEmpty(link.Label),
                Curve = link.Curve
            });
        }

        return links;
    }

    private static int NextIspBackupId(HashSet<int> used)
    {
        var id = -1;
        while (!used.Add(id))
        {
            id--;
        }

        return id;
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

    private static List<DeviceNicBackupDto> BackupNics(DeviceBackupDto row)
    {
        if (row.Nics.Count > 0)
        {
            return row.Nics
                .OrderBy(n => n.SortOrder)
                .Select(n => new DeviceNicBackupDto
                {
                    Name = NullIfEmpty(n.Name),
                    Ip = NullIfEmpty(n.Ip),
                    Mac = InventoryService.NormalizeMac(n.Mac),
                    SortOrder = n.SortOrder
                })
                .Where(n => n.Name is not null || n.Ip is not null || n.Mac is not null)
                .ToList();
        }

        var ip = NullIfEmpty(row.Ip);
        var mac = InventoryService.NormalizeMac(row.Mac);
        if (ip is null && mac is null)
        {
            return [];
        }

        return [new DeviceNicBackupDto { Ip = ip, Mac = mac, SortOrder = 0 }];
    }

    private static void ReplaceNics(Device device, IReadOnlyList<DeviceNicBackupDto> nics)
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

        var primary = device.Nics.OrderBy(x => x.SortOrder).FirstOrDefault();
        device.Ip = primary?.Ip;
        device.Mac = primary?.Mac;
    }

    private static void ReplaceDomains(Device device, IReadOnlyList<string>? domains)
    {
        if (domains is null)
        {
            return;
        }

        device.Domains.Clear();
        foreach (var domain in domains.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            device.Domains.Add(new DeviceDomain { Domain = domain.Trim() });
        }
    }

    private static string? NullIfEmpty(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) || text == "—" ? null : text;
    }
}
