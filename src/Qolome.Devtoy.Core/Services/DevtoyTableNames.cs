namespace Qolome.Devtoy.Core.Services;

/// <summary>
/// Devtoy 表名。
/// 远程业务：字典 <c>qolome_dict_*</c> + 网络 <c>devtoy_*</c>；
/// 应用配置/模块：<c>devtoy_doc</c> / <c>devtoy_workspace</c> / <c>devtoy_module</c>（Sqlite 或远程）。
/// </summary>
public static class DevtoyTableNames
{
    public const string DictItem = "qolome_dict_item";
    public const string DictVal = "qolome_dict_val";
    public const string Doc = "devtoy_doc";
    public const string Workspace = "devtoy_workspace";
    public const string Module = "devtoy_module";

    public const string NetworkEnvironment = "devtoy_network_environment";
    public const string DeviceType = "devtoy_device_type";
    public const string DeviceGroup = "devtoy_device_group";
    public const string Device = "devtoy_device";
    public const string DeviceDomain = "devtoy_device_domain";
    public const string DeviceNic = "devtoy_device_nic";
    public const string PortPurpose = "devtoy_port_purpose";
    public const string NetworkTopology = "devtoy_network_topology";
    public const string OuiEntry = "devtoy_oui_entry";
    public const string OuiMeta = "devtoy_oui_meta";

    public static readonly IReadOnlyList<string> RequiredDictTables = [DictItem, DictVal];

    /// <summary>远程网络业务表（与 <c>sql/qolome_devtoy_mysql_init.sql</c> / Nets EF 对齐）。</summary>
    public static readonly IReadOnlyList<string> RequiredNetsTables =
    [
        NetworkEnvironment,
        DeviceType,
        DeviceGroup,
        Device,
        DeviceDomain,
        DeviceNic,
        PortPurpose,
        NetworkTopology,
        OuiEntry,
        OuiMeta,
    ];

    /// <summary>「数据源设置 → 初始化」须一并创建的远程表。</summary>
    /// <summary>远程落库的应用配置/模块表。</summary>
    public static readonly IReadOnlyList<string> RequiredConfigTables = [Doc, Workspace, Module];

    public static readonly IReadOnlyList<string> RequiredInitTables =
        [..RequiredDictTables, ..RequiredNetsTables, ..RequiredConfigTables];

    public static readonly IReadOnlyList<string> LocalConfigTables = [Doc, Workspace, Module];
}
