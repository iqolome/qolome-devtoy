namespace Qolome.Nets.Core.Scanning;

/// <summary>公网出口（WAN）查询结果。</summary>
public sealed class EgressNetworkInfo
{
    public string? Ipv4 { get; init; }
    public string? Ipv6 { get; init; }
    public string? Asn { get; init; }
    public string? Isp { get; init; }
    public string? Organization { get; init; }
    public string? TimeZone { get; init; }
    public string? Location { get; init; }
    public string? Error { get; init; }

    public static EgressNetworkInfo Failed(string message) => new() { Error = message };
}

/// <summary>网卡分类：物理 / 虚拟 / NDIS 筛选器绑定。</summary>
public enum LocalNicKind
{
    Physical,
    Virtual,
    NdisFilter
}

/// <summary>本机网卡一行展示数据。</summary>
public sealed class LocalNicInfo
{
    public required string InterfaceId { get; init; }
    public string? PnpDeviceId { get; init; }
    /// <summary>连接名（如 WLAN、以太网）；启停操作用此名。</summary>
    public required string Name { get; init; }
    /// <summary>界面「连接名」列。</summary>
    public required string NetworkName { get; init; }
    /// <summary>界面「网卡名称」列（驱动/设备描述）。</summary>
    public required string AdapterName { get; init; }
    public required string Description { get; init; }
    public required string ConnectionType { get; init; }
    public string? WirelessSsid { get; init; }
    public string? WirelessSignal { get; init; }
    public required string SpeedDisplay { get; init; }
    public required string Ipv4Info { get; init; }
    public required string Ipv6Info { get; init; }
    public required string Gateway { get; init; }
    public required string Mac { get; init; }
    public required string Duration { get; init; }
    public bool IsUp { get; init; }
    public required LocalNicKind Kind { get; init; }

    public bool IsPhysical => Kind == LocalNicKind.Physical;
    public bool IsNdisFilter => Kind == LocalNicKind.NdisFilter;

    public string KindLabel => Kind switch
    {
        LocalNicKind.Physical => "物理",
        LocalNicKind.NdisFilter => "NDIS筛选器",
        _ => "虚拟"
    };

    public string ConnectionDisplay
    {
        get
        {
            if (!string.Equals(ConnectionType, "无线", StringComparison.Ordinal))
            {
                return ConnectionType;
            }

            var parts = new List<string> { "无线" };
            if (!string.IsNullOrWhiteSpace(WirelessSsid))
            {
                parts.Add($"SSID {WirelessSsid}");
            }

            if (!string.IsNullOrWhiteSpace(WirelessSignal))
            {
                parts.Add($"信号 {WirelessSignal}");
            }

            return string.Join(Environment.NewLine, parts);
        }
    }
}

/// <summary>禁用 / 删除网卡的操作结果。</summary>
public sealed class LocalNicActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";

    public static LocalNicActionResult Ok(string message) => new() { Success = true, Message = message };
    public static LocalNicActionResult Fail(string message) => new() { Success = false, Message = message };
}
