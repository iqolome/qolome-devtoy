namespace Qolome.Nets.Core.Data;

public sealed class InventoryBackupFile
{
    public string App { get; set; } = "qolome-nets";
    public string FormatVersion { get; set; } = "1";
    public string? AppVersion { get; set; }
    public DateTime ExportedAtUtc { get; set; }
    public List<string> Modules { get; set; } = [];
    public List<EnvironmentBackupDto>? Environments { get; set; }
    public List<GroupBackupDto>? Groups { get; set; }
    public List<DeviceTypeBackupDto>? DeviceTypes { get; set; }
    public List<DeviceBackupDto>? Devices { get; set; }
    public List<TopologyBackupDto>? Topologies { get; set; }
}

public sealed class InventoryBackupModules
{
    public bool Devices { get; set; } = true;
    public bool Environments { get; set; } = true;
    public bool Groups { get; set; } = true;
    public bool DeviceTypes { get; set; } = true;
    public bool Topology { get; set; } = true;

    public bool Any => Devices || Environments || Groups || DeviceTypes || Topology;
}

public sealed class InventoryBackupResult
{
    public int Environments { get; set; }
    public int Groups { get; set; }
    public int DeviceTypes { get; set; }
    public int Devices { get; set; }
    public int CustomIcons { get; set; }
    public int Topologies { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; } = [];

    public string ToStatus(string action)
    {
        var parts = new List<string>();
        if (Environments > 0) parts.Add($"网络环境 {Environments}");
        if (Groups > 0) parts.Add($"网络分组 {Groups}");
        if (DeviceTypes > 0) parts.Add($"设备类型 {DeviceTypes}");
        if (Devices > 0) parts.Add($"设备 {Devices}");
        if (CustomIcons > 0) parts.Add($"自定义图标 {CustomIcons}");
        if (Topologies > 0) parts.Add($"拓扑图 {Topologies}");
        if (Skipped > 0) parts.Add($"跳过 {Skipped}");
        var summary = parts.Count == 0 ? "没有数据" : string.Join("，", parts);
        var text = $"{action}完成：{summary}。";
        if (Warnings.Count > 0)
        {
            text += " " + string.Join(" ", Warnings);
        }

        return text;
    }
}

public sealed class EnvironmentBackupDto
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? DefaultCidr { get; set; }
}

public sealed class GroupBackupDto
{
    public string EnvironmentName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public sealed class InventoryBackupPackage
{
    public InventoryBackupFile File { get; init; } = new();
    public IReadOnlyDictionary<string, byte[]> CustomIcons { get; init; } =
        new Dictionary<string, byte[]>();
}

public sealed class DeviceTypeBackupDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public string? IconFile { get; set; }
    public bool IsCustomIcon { get; set; }
    public string? ColorGroup { get; set; }
}

public sealed class DeviceBackupDto
{
    public string Code { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public string? GroupName { get; set; }
    public string? DeviceTypeCode { get; set; }
    public string? DeviceTypeName { get; set; }
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
    public string Source { get; set; } = "Import";
    public List<string> Domains { get; set; } = [];
    public List<DeviceNicBackupDto> Nics { get; set; } = [];
}

public sealed class DeviceNicBackupDto
{
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public string? Mac { get; set; }
    public int SortOrder { get; set; }
}

public sealed class TopologyBackupDto
{
    public string EnvironmentName { get; set; } = "";
    public List<TopologyNodeBackupDto> Nodes { get; set; } = [];
    public List<TopologyLinkBackupDto> Links { get; set; } = [];
}

public sealed class TopologyNodeBackupDto
{
    public int Id { get; set; }
    public string Kind { get; set; } = TopologyNodeKinds.Device;
    public string? DeviceCode { get; set; }
    public string? IspKey { get; set; }
    public string? Label { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int? ContainerId { get; set; }
}

public sealed class TopologyLinkBackupDto
{
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public string Kind { get; set; } = TopologyLinkKinds.Ethernet;
    public string? Color { get; set; }
    public bool? Dashed { get; set; }
    public string? Label { get; set; }
    public double Curve { get; set; }
}
