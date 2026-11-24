namespace Qolome.Nets.Core.Data;

/// <summary>本地 SQLite / MySQL / PostgreSQL 同步用 Nets 表名（均带 <c>devtoy_</c> 前缀）。</summary>
public static class NetsTableNames
{
    public const string NetworkEnvironment = "devtoy_network_environment";
    public const string NetworkTopology = "devtoy_network_topology";
    public const string DeviceDomain = "devtoy_device_domain";
    public const string DeviceGroup = "devtoy_device_group";
    public const string DeviceType = "devtoy_device_type";
    public const string DeviceNic = "devtoy_device_nic";
    public const string PortPurpose = "devtoy_port_purpose";
    public const string OuiEntry = "devtoy_oui_entry";
    public const string OuiMeta = "devtoy_oui_meta";
    public const string Device = "devtoy_device";

    public static readonly string[] IdentityResetTables =
    [
        NetworkEnvironment,
        DeviceType,
        DeviceGroup,
        Device,
        DeviceDomain,
        DeviceNic,
        PortPurpose,
    ];

    public static readonly string[] PgInventoryTables =
    [
        NetworkEnvironment,
        DeviceType,
        DeviceGroup,
        Device,
        DeviceDomain,
        DeviceNic,
        PortPurpose,
    ];
}