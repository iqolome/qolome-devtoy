using System.ComponentModel.DataAnnotations.Schema;

namespace Qolome.Nets.Core.Data;

public sealed class NetworkEnvironment
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? DefaultCidr { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<DeviceGroup> Groups { get; set; } = [];
    public ICollection<Device> Devices { get; set; } = [];
}

public sealed class DeviceGroup
{
    public int Id { get; set; }
    public int NetworkEnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public NetworkEnvironment NetworkEnvironment { get; set; } = null!;
    public ICollection<Device> Devices { get; set; } = [];
}

public sealed class DeviceType
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public string? IconFile { get; set; }
    public bool IsCustomIcon { get; set; }
    public string ColorGroup { get; set; } = DeviceTypeColorGroups.Other;

    public ICollection<Device> Devices { get; set; } = [];
}

public sealed class Device
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public int NetworkEnvironmentId { get; set; }
    public int? DeviceGroupId { get; set; }
    public int? DeviceTypeId { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Description { get; set; }
    public string? Ip { get; set; }
    public string? Mac { get; set; }
    public string? OsType { get; set; }
    public string? DeviceInfo { get; set; }
    public string? OpenPorts { get; set; }
    public string? Remarks { get; set; }
    public string Source { get; set; } = "Scan";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public NetworkEnvironment NetworkEnvironment { get; set; } = null!;
    public DeviceGroup? DeviceGroup { get; set; }
    public DeviceType? DeviceType { get; set; }
    public ICollection<DeviceDomain> Domains { get; set; } = [];
    public ICollection<DeviceNic> Nics { get; set; } = [];

    [NotMapped]
    public string DomainsText => string.Join("; ", Domains.Select(d => d.Domain));

    [NotMapped]
    public string TypeName => DeviceType?.Name ?? "—";

    [NotMapped]
    public string GroupName => DeviceGroup?.Name ?? "未分组";

    [NotMapped]
    public string IpsText => string.Join("\n", OrderedNics().Select(n => InventoryService.NicSlotDisplay(n.Ip)));

    [NotMapped]
    public string MacsText => string.Join("\n", OrderedNics().Select(n => InventoryService.NicSlotDisplay(n.Mac)));

    [NotMapped]
    public string NicsTooltip
    {
        get
        {
            var nics = OrderedNics();
            if (nics.Count == 0)
            {
                return "—";
            }

            return string.Join("\n", nics.Select((n, i) =>
            {
                var name = string.IsNullOrWhiteSpace(n.Name) ? $"网卡{i + 1}" : n.Name.Trim();
                var ip = string.IsNullOrWhiteSpace(n.Ip) ? InventoryService.NicEmptySlot : n.Ip;
                var mac = string.IsNullOrWhiteSpace(n.Mac) ? InventoryService.NicEmptySlot : n.Mac;
                return $"{name}  {ip}  {mac}";
            }));
        }
    }

    public IReadOnlyList<DeviceNic> OrderedNics()
    {
        if (Nics is { Count: > 0 })
        {
            return Nics.OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Ip) || !string.IsNullOrWhiteSpace(Mac))
        {
            return [new DeviceNic { Ip = Ip, Mac = Mac, SortOrder = 0 }];
        }

        return [];
    }

    public IEnumerable<string> NicIps() =>
        OrderedNics().Select(n => n.Ip).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!);

    public IEnumerable<string> NicMacs() =>
        OrderedNics().Select(n => n.Mac).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!);
}

public sealed class DeviceDomain
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string Domain { get; set; } = "";

    public Device Device { get; set; } = null!;
}

public sealed class DeviceNic
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public string? Mac { get; set; }
    public int SortOrder { get; set; }

    public Device Device { get; set; } = null!;
}

public sealed class PortPurpose
{
    public int Id { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
    public string? Service { get; set; }
    public string? Description { get; set; }
}

public sealed class NetworkTopology
{
    public int NetworkEnvironmentId { get; set; }
    public string Json { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }

    public NetworkEnvironment? NetworkEnvironment { get; set; }
}
