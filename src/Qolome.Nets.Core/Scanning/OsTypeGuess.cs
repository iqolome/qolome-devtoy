namespace Qolome.Nets.Core.Scanning;

public static class OsTypeGuess
{
    public static string FromTtl(int? ttl)
    {
        if (ttl is null or <= 0)
        {
            return "";
        }

        if (ttl <= 64)
        {
            return "Linux/Unix";
        }

        if (ttl <= 128)
        {
            return "Windows";
        }

        return "网络设备";
    }

    public static string ComposeDeviceInfo(string discoveryMethod, int? ttl)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(discoveryMethod))
        {
            parts.Add($"发现={discoveryMethod}");
        }

        if (ttl is int value)
        {
            parts.Add($"TTL={value}");
        }

        return string.Join("；", parts);
    }

    public static string? MeaningfulVendor(string? vendor)
    {
        if (string.IsNullOrWhiteSpace(vendor) || vendor is "MAC 仅本网段可见")
        {
            return null;
        }

        return vendor.Trim();
    }

    public static bool TrySplitVendor(string? deviceInfo, out string? vendor, out string? remainder)
    {
        vendor = null;
        remainder = string.IsNullOrWhiteSpace(deviceInfo) ? null : deviceInfo.Trim();
        if (remainder is null)
        {
            return false;
        }

        var parts = remainder.Split(['；', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || IsMetaPart(parts[0]) || !parts.Skip(1).Any(IsMetaPart))
        {
            return false;
        }

        vendor = parts[0];
        remainder = string.Join("；", parts.Skip(1));
        return true;
    }

    private static bool IsMetaPart(string part) =>
        part.StartsWith("发现=", StringComparison.Ordinal)
        || part.StartsWith("TTL=", StringComparison.OrdinalIgnoreCase);
}
