namespace Qolome.Nets.Core.Data;

public sealed record DeviceTypeColorGroup(string Key, string Name, string Hex, int Order);

public static class DeviceTypeColorGroups
{
    public const string Network = "network";
    public const string Security = "security";
    public const string Wireless = "wireless";
    public const string Storage = "storage";
    public const string Compute = "compute";
    public const string Protect = "protect";
    public const string Office = "office";
    public const string Industrial = "industrial";
    public const string Media = "media";
    public const string Home = "home";
    public const string Other = "other";

    public static IReadOnlyList<DeviceTypeColorGroup> All { get; } =
    [
        new(Network, "网络设备", "#2563EB", 0),
        new(Security, "安全防护", "#DC2626", 1),
        new(Wireless, "无线蜂窝", "#0891B2", 2),
        new(Storage, "存储录像", "#7C3AED", 3),
        new(Compute, "计算终端", "#16A34A", 4),
        new(Protect, "安防门禁", "#C2410C", 5),
        new(Office, "办公通讯", "#D97706", 6),
        new(Industrial, "电力工控", "#0F766E", 7),
        new(Media, "影音显示", "#9333EA", 8),
        new(Home, "智能家居", "#DB2777", 9),
        new(Other, "其他", "#64748B", 10)
    ];

    private static readonly Dictionary<string, string> CodeMap = BuildCodeMap();

    public static string Normalize(string? key, string? code = null)
    {
        var value = key?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(value))
        {
            var byKey = All.FirstOrDefault(g => g.Key == value);
            if (byKey is not null)
            {
                return byKey.Key;
            }

            var byName = All.FirstOrDefault(g => g.Name == key?.Trim());
            if (byName is not null)
            {
                return byName.Key;
            }
        }

        return ForCode(code);
    }

    public static string ForCode(string? code)
    {
        var key = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(key))
        {
            return Other;
        }

        return CodeMap.TryGetValue(key, out var group) ? group : Other;
    }

    public static DeviceTypeColorGroup Resolve(string? key, string? code = null)
    {
        var normalized = Normalize(key, code);
        return All.First(g => g.Key == normalized);
    }

    public static string DisplayName(string? key, string? code = null) => Resolve(key, code).Name;

    public static string Hex(string? key, string? code = null) => Resolve(key, code).Hex;

    public static int SortOrder(string? key, string? code = null) => Resolve(key, code).Order;

    private static Dictionary<string, string> BuildCodeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add(map, Network,
            "router", "gateway", "modem", "ont", "olt", "switch", "core-switch", "access-switch",
            "poe-switch", "load-balancer", "vpn", "proxy", "sdwan", "media-converter", "tap", "powerline");
        Add(map, Security, "firewall", "utm", "waf", "ips");
        Add(map, Wireless,
            "ap", "wireless-controller", "mesh", "extender", "repeater", "bridge", "lte-cpe", "satellite", "bluetooth");
        Add(map, Storage, "nas", "san", "nvr", "dvr");
        Add(map, Compute,
            "server", "hypervisor", "workstation", "desktop", "mini-pc", "laptop", "tablet",
            "mobile", "thin-client", "sbc", "pos", "kiosk");
        Add(map, Protect,
            "camera", "doorbell", "access-control", "smart-lock", "alarm", "intercom", "badge-reader");
        Add(map, Office,
            "printer", "mfp", "scanner", "label-printer", "plotter", "phone", "conference-phone",
            "pbx", "ata", "fax", "conference");
        Add(map, Industrial,
            "ups", "pdu", "inverter", "energy-meter", "plc", "hmi", "iot-gateway", "barcode", "rfid", "env-sensor");
        Add(map, Media,
            "smart-tv", "streaming", "set-top", "projector", "speaker", "soundbar", "smart-speaker",
            "game-console", "av-receiver", "digital-signage");
        Add(map, Home,
            "smart-plug", "smart-bulb", "thermostat", "ac", "heater", "fridge", "washer", "dryer",
            "clothes-rack", "dishwasher", "range-hood", "gas-stove", "vacuum", "air-purifier", "humidifier",
            "dehumidifier", "water-dispenser", "water-purifier", "fan", "lv-box", "garage", "curtain",
            "ir-blaster", "zigbee-hub", "zwave-hub", "matter-hub");
        Add(map, Other, "other");
        return map;
    }

    private static void Add(Dictionary<string, string> map, string group, params string[] codes)
    {
        foreach (var code in codes)
        {
            map[code] = group;
        }
    }
}
