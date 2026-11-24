namespace Qolome.Nets.Core.Data;

public readonly record struct IspPreset(string Key, string Label);

public static class IspPresets
{
    public const string Cmcc = "cmcc";
    public const string Unicom = "unicom";
    public const string Telecom = "telecom";
    public const string Custom = "custom";

    public static IReadOnlyList<IspPreset> All { get; } =
    [
        new(Cmcc, "中国移动"),
        new(Unicom, "中国联通"),
        new(Telecom, "中国电信"),
        new(Custom, "自定义")
    ];

    public static string Normalize(string? key, string? label)
    {
        var value = key?.Trim().ToLowerInvariant();
        if (value is Cmcc or Unicom or Telecom or Custom)
        {
            return value;
        }

        return (label?.Trim() ?? "") switch
        {
            "中国移动" or "移动" => Cmcc,
            "中国联通" or "联通" => Unicom,
            "中国电信" or "电信" => Telecom,
            _ => Custom
        };
    }

    public static bool IsCarrier(string? key) =>
        key is Cmcc or Unicom or Telecom;

    public static string DefaultLabel(string? key) => Normalize(key, null) switch
    {
        Cmcc => "中国移动",
        Unicom => "中国联通",
        Telecom => "中国电信",
        _ => "互联网"
    };

    public static string IconFileName(string? key, string? label = null) => Normalize(key, label) switch
    {
        Cmcc => "cmcc.svg",
        Unicom => "unicom.svg",
        Telecom => "telecom.svg",
        _ => "globe.svg"
    };
}
