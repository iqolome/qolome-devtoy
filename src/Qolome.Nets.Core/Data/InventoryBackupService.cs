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

    public async Task WriteBackupAsync(string path, InventoryBackupPackage package)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var fs = File.Create(path);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            var jsonEntry = zip.CreateEntry("qolome-nets.json");
            await using (var stream = jsonEntry.Open())
            {
                using (MachineBoundSecret.OmitPassword())
                {
                    await JsonSerializer.SerializeAsync(stream, package.File, JsonOptions);
                }
            }

            foreach (var (name, bytes) in package.CustomIcons)
            {
                var iconEntry = zip.CreateEntry($"icons/{name}");
                await using var stream = iconEntry.Open();
                await stream.WriteAsync(bytes);
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

    public async Task<(InventoryBackupFile File, IReadOnlyDictionary<string, byte[]> Icons)> ReadBackupAsync(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var jsonEntry = zip.GetEntry("qolome-nets.json")
                ?? zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ?? throw new ScanException("ZIP 中没有找到 qolome-nets.json。");
            await using var jsonStream = jsonEntry.Open();
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

                await using var iconStream = entry.Open();
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

    public async Task<InventoryBackupResult> ImportAsync(
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

        if (modules.DeviceTypes)
        {
            if (file.DeviceTypes is { Count: > 0 })
            {
                var (count, icons) = await ImportDeviceTypesAsync(db, file.DeviceTypes, customIcons);
                result.DeviceTypes = count;
                result.CustomIcons = icons;
            }
            else
            {
                result.Warnings.Add("文件中没有设备类型。");
            }
        }

        if (modules.Environments)
        {
            if (file.Environments is { Count: > 0 })
            {
                result.Environments = await ImportEnvironmentsAsync(db, file.Environments);
            }
            else
            {
                result.Warnings.Add("文件中没有网络环境。");
            }
        }

        if (modules.Groups)
        {
            if (file.Groups is { Count: > 0 })
            {
                var (count, skipped) = await ImportGroupsAsync(db, file.Groups);
                result.Groups = count;
                result.Skipped += skipped;
            }
            else
            {
                result.Warnings.Add("文件中没有网络分组。");
            }
        }

        if (modules.Devices)
        {
            if (file.Devices is { Count: > 0 })
            {
                var (count, skipped) = await ImportDevicesAsync(db, file.Devices);
                result.Devices = count;
                result.Skipped += skipped;
            }
            else
            {
                result.Warnings.Add("文件中没有设备信息。");
            }
        }

        if (modules.Topology)
        {
            if (file.Topologies is { Count: > 0 })
            {
                var (count, skipped) = await ImportTopologiesAsync(db, file.Topologies);
                result.Topologies = count;
                result.Skipped += skipped;
            }
            else
            {
                result.Warnings.Add("文件中没有拓扑图。");
            }
        }

        await db.SaveChangesAsync();
        return result;
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

            if (row.IsCustomIcon && NullIfEmpty(row.IconFile) is string iconName
                && DeviceTypeIconStore.IsSvg(iconName)
                && customIcons is not null
                && customIcons.TryGetValue(Path.GetFileName(iconName), out var bytes))
            {
                var safe = DeviceTypeIconStore.SanitizeFileName(iconName);
                DeviceTypeIconStore.InstallCustomFile(safe, bytes);
                if (entity.IsCustomIcon && !string.Equals(entity.IconFile, safe, StringComparison.OrdinalIgnoreCase))
                {
                    DeviceTypeIconStore.DeleteCustom(entity.IconFile);
                }

                entity.IconFile = safe;
                entity.IsCustomIcon = true;
                icons++;
            }

            count++;
        }

        await db.SaveChangesAsync();
        return (count, icons);
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
            var envName = NullIfEmpty(row.EnvironmentName);
            if (envName is null)
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

            int? groupId = null;
            var groupName = NullIfEmpty(row.GroupName);
            if (groupName is not null)
            {
                var group = groups.FirstOrDefault(x =>
                    x.NetworkEnvironmentId == env.Id
                    && x.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase));
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

            var code = NullIfEmpty(row.Code);
            var nics = BackupNics(row);
            var mac = nics.Select(n => InventoryService.NormalizeMac(n.Mac)).FirstOrDefault(m => m is not null)
                ?? InventoryService.NormalizeMac(row.Mac);
            var inEnv = devices.Where(x => x.NetworkEnvironmentId == env.Id);
            var entity = mac is not null
                ? inEnv.FirstOrDefault(x =>
                    InventoryService.NormalizeMac(x.Mac) == mac
                    || x.Nics.Any(n => InventoryService.NormalizeMac(n.Mac) == mac))
                : null;
            if (entity is null && code is not null)
            {
                entity = inEnv.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            }
            if (entity is null)
            {
                entity = new Device
                {
                    Code = code ?? $"DEV-{nextSerial++:D4}",
                    CreatedAtUtc = DateTime.UtcNow
                };
                if (devices.Any(d => d.Code.Equals(entity.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    entity.Code = $"DEV-{nextSerial++:D4}";
                }

                db.Devices.Add(entity);
                devices.Add(entity);
            }

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
            count++;
        }

        await db.SaveChangesAsync();
        return (count, skipped);
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
        var store = new TopologyStore();
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
            await store.SaveAsync(document);
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
        var nodes = new List<TopologyNodeDto>();
        foreach (var node in row.Nodes)
        {
            var kind = TopologyNodeKinds.Normalize(node.Kind, string.IsNullOrWhiteSpace(node.DeviceCode) ? 0 : 1);
            if (kind == TopologyNodeKinds.Device)
            {
                var code = NullIfEmpty(node.DeviceCode);
                if (code is null)
                {
                    continue;
                }

                var device = envDevices.FirstOrDefault(d =>
                    d.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                if (device is null)
                {
                    continue;
                }

                idMap[node.Id] = device.Id;
                if (!usedIds.Add(device.Id))
                {
                    continue;
                }

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
                continue;
            }

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
                continue;
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

        foreach (var node in nodes)
        {
            if (node.ContainerId is int oldParent && idMap.TryGetValue(oldParent, out var mapped)
                && nodes.Any(n => n.Id == mapped && n.IsContainer)
                && !node.IsContainer)
            {
                node.ContainerId = mapped;
            }
            else
            {
                node.ContainerId = null;
            }
        }

        var links = row.Links
            .Select(link =>
            {
                if (!idMap.TryGetValue(link.FromNodeId, out var from)
                    || !idMap.TryGetValue(link.ToNodeId, out var to))
                {
                    return null;
                }

                return new TopologyLinkDto
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
                };
            })
            .Where(l => l is not null)
            .Cast<TopologyLinkDto>()
            .ToList();

        return new TopologyDocument
        {
            EnvironmentId = environmentId,
            Nodes = nodes,
            Links = links
        };
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
