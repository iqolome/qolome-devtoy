using System.Net;
using System.Net.Sockets;

namespace Qolome.Nets.Core.Scanning;

public static class IpAddressComparer
{
    public static readonly Comparer<string?> TextComparer = Comparer<string?>.Create(Compare);

    public static int Compare(string? left, string? right)
    {
        var leftEmpty = string.IsNullOrWhiteSpace(left);
        var rightEmpty = string.IsNullOrWhiteSpace(right);
        if (leftEmpty || rightEmpty)
        {
            return leftEmpty.CompareTo(rightEmpty);
        }

        var parsedLeft = IPAddress.TryParse(left, out var a);
        var parsedRight = IPAddress.TryParse(right, out var b);
        if (parsedLeft && parsedRight)
        {
            return Compare(a!, b!);
        }

        if (parsedLeft)
        {
            return -1;
        }

        if (parsedRight)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static int Compare(IPAddress left, IPAddress right)
    {
        var family = left.AddressFamily.CompareTo(right.AddressFamily);
        if (family != 0)
        {
            return family;
        }

        if (left.AddressFamily == AddressFamily.InterNetwork)
        {
            return ScanTargetParser.ToUint(left).CompareTo(ScanTargetParser.ToUint(right));
        }

        return left.GetAddressBytes().AsSpan().SequenceCompareTo(right.GetAddressBytes());
    }
}
