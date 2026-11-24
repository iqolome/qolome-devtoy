using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Qolome.Nets.Core.Data;

namespace Qolome.Nets.Core.Scanning;

public static class ArpMacResolver
{
    public static string? Resolve(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        var local = NetworkAdapterEnumerator.FindLocalMac(address);
        if (!string.IsNullOrWhiteSpace(local))
        {
            return local;
        }

        var fromTable = TryGetFromNeighborTable(address);
        if (!string.IsNullOrWhiteSpace(fromTable))
        {
            return fromTable;
        }

        return TrySendArp(address);
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotMacToIps()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ip, mac) in EnumerateNeighbors())
        {
            if (string.IsNullOrWhiteSpace(mac) || string.IsNullOrWhiteSpace(ip))
            {
                continue;
            }

            if (!map.TryGetValue(mac, out var ips))
            {
                ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[mac] = ips;
            }

            ips.Add(ip);
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, string> SnapshotMacToIp()
    {
        return SnapshotMacToIps().ToDictionary(
            kv => kv.Key,
            kv => kv.Value[0],
            StringComparer.OrdinalIgnoreCase);
    }

    public static string? FindIpByMac(string? mac)
    {
        var key = InventoryService.NormalizeMac(mac);
        if (key is null)
        {
            return null;
        }

        return SnapshotMacToIp().TryGetValue(key, out var ip) ? ip : null;
    }

    private static string? TrySendArp(IPAddress address)
    {
        var mac = new byte[6];
        var length = mac.Length;
        var dest = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        var result = SendARP(dest, 0, mac, ref length);
        if (result != 0 || length < 6)
        {
            return null;
        }

        return FormatMac(mac, length);
    }

    private static string? TryGetFromNeighborTable(IPAddress address)
    {
        var target = BitConverter.ToUInt32(address.GetAddressBytes(), 0);
        foreach (var (_, mac, addr) in EnumerateNeighborRows())
        {
            if (addr == target)
            {
                return mac;
            }
        }

        return null;
    }

    private static IEnumerable<(string Ip, string Mac)> EnumerateNeighbors()
    {
        foreach (var (ip, mac, _) in EnumerateNeighborRows())
        {
            yield return (ip, mac);
        }
    }

    private static IEnumerable<(string Ip, string Mac, uint Addr)> EnumerateNeighborRows()
    {
        var size = 0;
        _ = GetIpNetTable(IntPtr.Zero, ref size, false);
        if (size <= 0)
        {
            yield break;
        }

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var error = GetIpNetTable(buffer, ref size, false);
            if (error != 0)
            {
                yield break;
            }

            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibIpNetRow>();
            var ptr = IntPtr.Add(buffer, 4);
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibIpNetRow>(ptr);
                ptr = IntPtr.Add(ptr, rowSize);
                if (row.DwType == 2)
                {
                    continue;
                }

                var mac = FormatMac(row.BPhysAddr, row.DwPhysAddrLen);
                if (mac is null)
                {
                    continue;
                }

                var normalized = InventoryService.NormalizeMac(mac) ?? mac;
                var bytes = BitConverter.GetBytes(unchecked((uint)row.DwAddr));
                var ip = new IPAddress(bytes).ToString();
                yield return (ip, normalized, unchecked((uint)row.DwAddr));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? FormatMac(byte[]? bytes, int length)
    {
        if (bytes is null || length < 6)
        {
            return null;
        }

        if (bytes.Take(6).All(b => b == 0))
        {
            return null;
        }

        return string.Join(":", bytes.Take(6).Select(b => b.ToString("X2")));
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref int phyAddrLen);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIpNetRow
    {
        public int DwIndex;
        public int DwPhysAddrLen;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] BPhysAddr;
        public int DwAddr;
        public int DwType;
    }
}
