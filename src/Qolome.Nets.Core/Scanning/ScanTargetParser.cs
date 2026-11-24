using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public static class ScanTargetParser
{
    public const int MaxHosts = 2048;

    private static readonly Regex RangeRegex = new(
        @"^(?<start>\d{1,3}(?:\.\d{1,3}){3})\s*-\s*(?<end>(?:\d{1,3}(?:\.\d{1,3}){3})|\d{1,3})$",
        RegexOptions.Compiled);

    public static IReadOnlyList<IPAddress> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ScanException("请输入扫描目标（CIDR、IP 段或单 IP）。");
        }

        var text = input.Trim();
        if (text.Contains('/'))
        {
            return EnumerateCidr(text);
        }

        var rangeMatch = RangeRegex.Match(text);
        if (rangeMatch.Success)
        {
            return EnumerateRange(rangeMatch.Groups["start"].Value, rangeMatch.Groups["end"].Value);
        }

        if (IPAddress.TryParse(text, out var single))
        {
            if (single.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ScanException("当前版本仅支持 IPv4 目标。");
            }

            return [single];
        }

        return ResolveHost(text);
    }

    public static IReadOnlyList<IPAddress> EnumerateCidr(string cidr)
    {
        var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var network)
            || network.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out var prefix)
            || prefix is < 0 or > 32)
        {
            throw new ScanException($"无法解析 CIDR：{cidr}");
        }

        var start = ToUint(network) & PrefixMask(prefix);
        var end = start | ~PrefixMask(prefix);
        if (prefix <= 30)
        {
            start++;
            end--;
        }

        return EnumerateInclusive(start, end);
    }

    private static IReadOnlyList<IPAddress> EnumerateRange(string startText, string endText)
    {
        if (!IPAddress.TryParse(startText, out var startIp) || startIp.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ScanException($"无法解析起始 IP：{startText}");
        }

        IPAddress endIp;
        if (endText.Contains('.'))
        {
            if (!IPAddress.TryParse(endText, out endIp!) || endIp.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ScanException($"无法解析结束 IP：{endText}");
            }
        }
        else
        {
            if (!int.TryParse(endText, out var lastOctet) || lastOctet is < 0 or > 255)
            {
                throw new ScanException($"无法解析结束地址：{endText}");
            }

            var bytes = startIp.GetAddressBytes();
            bytes[3] = (byte)lastOctet;
            endIp = new IPAddress(bytes);
        }

        var start = ToUint(startIp);
        var end = ToUint(endIp);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        return EnumerateInclusive(start, end);
    }

    private static IReadOnlyList<IPAddress> ResolveHost(string host)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(host)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .ToList();
            if (addresses.Count == 0)
            {
                throw new ScanException($"主机 {host} 没有 IPv4 地址。");
            }

            return addresses;
        }
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException($"无法解析主机名 {host}：{ex.Message}");
        }
    }

    private static IReadOnlyList<IPAddress> EnumerateInclusive(uint start, uint end)
    {
        if (end < start)
        {
            throw new ScanException("扫描范围无效。");
        }

        var count = (int)(end - start + 1);
        if (count > MaxHosts)
        {
            throw new ScanException($"目标数量 {count} 超过上限 {MaxHosts}，请缩小 CIDR 或 IP 段。");
        }

        var list = new List<IPAddress>(count);
        for (var value = start; value <= end; value++)
        {
            list.Add(FromUint(value));
            if (value == uint.MaxValue)
            {
                break;
            }
        }

        return list;
    }

    public static uint ToUint(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    public static IPAddress FromUint(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static uint PrefixMask(int prefix) =>
        prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
}
