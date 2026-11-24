using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public static class NetworkAdapterEnumerator
{
    public static IReadOnlyList<NetworkAdapterInfo> GetActiveIpv4Adapters()
    {
        var adapters = new List<NetworkAdapterInfo>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            CollectActiveIpv4Adapters(nic, adapters);
        }

        return adapters
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectActiveIpv4Adapters(NetworkInterface nic, List<NetworkAdapterInfo> adapters)
    {
        if (nic.OperationalStatus != OperationalStatus.Up
            || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
        {
            return;
        }

        var mac = FormatMac(nic.GetPhysicalAddress());
        foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
        {
            if (!TryCreateAdapterInfo(nic, unicast, mac, out var info))
            {
                continue;
            }

            adapters.Add(info);
        }
    }

    private static bool TryCreateAdapterInfo(
        NetworkInterface nic,
        UnicastIPAddressInformation unicast,
        string? mac,
        out NetworkAdapterInfo info)
    {
        info = null!;
        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (IPAddress.IsLoopback(unicast.Address) || unicast.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        {
            return false;
        }

        var prefix = unicast.PrefixLength;
        if (prefix is < 8 or > 32)
        {
            return false;
        }

        var network = ScanTargetParser.FromUint(
            ScanTargetParser.ToUint(unicast.Address) & (prefix == 0 ? 0 : uint.MaxValue << (32 - prefix)));
        info = new NetworkAdapterInfo
        {
            Id = nic.Id,
            Name = nic.Name,
            Description = nic.Description,
            Address = unicast.Address,
            PrefixLength = prefix,
            Cidr = $"{network}/{prefix}",
            Mac = mac
        };
        return true;
    }

    public static string? FindLocalMac(IPAddress address)
    {
        var matched = GetActiveIpv4Adapters().FirstOrDefault(a => a.Address.Equals(address));
        if (matched is not null)
        {
            return matched.Mac;
        }

        try
        {
            var loopback = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.NetworkInterfaceType == NetworkInterfaceType.Loopback);
            if (IPAddress.IsLoopback(address))
            {
                return FormatMac(loopback?.GetPhysicalAddress());
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string? FormatMac(PhysicalAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length < 6 || bytes.All(b => b == 0))
        {
            return null;
        }

        return string.Join(":", bytes.Take(6).Select(b => b.ToString("X2")));
    }
}
