using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Qolome.Nets.Core.Scanning;

/// <summary>枚举本机网卡：连接方式、速率、IPv4/IPv6、网关、MAC、连接时长等。</summary>
public static class LocalNicInventory
{
    private static readonly Regex WlanSignalRegex = new(
        @"^\s*(?:Signal|信号)\s*:\s*(\d+)\s*%",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex WlanSsidRegex = new(
        @"^\s*SSID\s*:\s*(.+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex WlanNameRegex = new(
        @"^\s*(?:Name|名称)\s*:\s*(.+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>采集本机网卡；<paramref name="parallel"/> 为 true 时并行读取 WMI/netsh 元数据。</summary>
    public static IReadOnlyList<LocalNicInfo> GetAdapters(bool parallel = true)
    {
        var (wifiStatus, dhcpObtained, msft, pnpIds) = LoadSideChannels(parallel);

        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic => nic.NetworkInterfaceType is not (
                NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToArray();

        LocalNicInfo? MapOne(NetworkInterface nic)
        {
            var mac = FormatMac(nic.GetPhysicalAddress());
            var isNdisFilter = IsNdisFilterBinding(nic);
            if (!isNdisFilter
                && string.IsNullOrEmpty(mac)
                && nic.OperationalStatus != OperationalStatus.Up
                && nic.NetworkInterfaceType == NetworkInterfaceType.Unknown)
            {
                return null;
            }

            var props = nic.GetIPProperties();
            var isWifi = IsWireless(nic);
            var connectionType = isWifi ? "无线" : "有线";
            string? signal = null;
            string? ssid = null;
            if (isWifi && nic.OperationalStatus == OperationalStatus.Up && !isNdisFilter)
            {
                var wifi = ResolveWifiStatus(nic, wifiStatus);
                signal = wifi?.Signal;
                ssid = wifi?.Ssid;
            }

            var meta = ResolveMsftMeta(nic, msft);
            var speedDisplay = FormatSpeed(meta?.ReceiveBps, meta?.TransmitBps, nic.Speed);
            var kind = ResolveNicKind(nic, mac, meta, isNdisFilter);

            var ipv4 = FormatIpv4Info(props, out var v4Gateway);
            var ipv6 = FormatIpv6Info(props, out var v6Gateway);
            var gateway = FirstNonEmpty(v4Gateway, v6Gateway) ?? "—";

            DateTime? leaseUtc = null;
            if (dhcpObtained.TryGetValue(NormalizeKey(nic.Id), out var byId))
            {
                leaseUtc = byId;
            }
            else if (dhcpObtained.TryGetValue(NormalizeKey(nic.Description), out var byDesc))
            {
                leaseUtc = byDesc;
            }

            string? pnpId = null;
            foreach (var key in new[] { NormalizeKey(nic.Id), NormalizeKey(nic.Name), NormalizeKey(nic.Description) })
            {
                if (pnpIds.TryGetValue(key, out pnpId))
                {
                    break;
                }
            }

            var (networkName, adapterName) = SplitNetworkAndAdapterNames(nic, isNdisFilter);

            return new LocalNicInfo
            {
                InterfaceId = nic.Id,
                PnpDeviceId = pnpId,
                Name = nic.Name,
                NetworkName = networkName,
                AdapterName = adapterName,
                Description = nic.Description,
                ConnectionType = connectionType,
                WirelessSsid = ssid,
                WirelessSignal = signal,
                SpeedDisplay = speedDisplay,
                Ipv4Info = ipv4,
                Ipv6Info = ipv6,
                Gateway = gateway,
                Mac = mac ?? "—",
                Duration = FormatDuration(leaseUtc, nic.OperationalStatus == OperationalStatus.Up),
                IsUp = nic.OperationalStatus == OperationalStatus.Up,
                Kind = kind
            };
        }

        List<LocalNicInfo> list;
        if (parallel && candidates.Length > 4)
        {
            var mapped = new LocalNicInfo?[candidates.Length];
            Parallel.For(0, candidates.Length, i => mapped[i] = MapOne(candidates[i]));
            list = mapped.Where(static x => x is not null).Select(static x => x!).ToList();
        }
        else
        {
            list = new List<LocalNicInfo>(candidates.Length);
            foreach (var nic in candidates)
            {
                var row = MapOne(nic);
                if (row is not null)
                {
                    list.Add(row);
                }
            }
        }

        return list
            .OrderBy(a => a.Kind)
            .ThenByDescending(a => a.IsUp)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Task<IReadOnlyList<LocalNicInfo>> GetAdaptersAsync(
        bool parallel = true,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetAdapters(parallel);
        }, cancellationToken);

    private static (
        Dictionary<string, WifiStatusInfo> Wifi,
        Dictionary<string, DateTime> Dhcp,
        Dictionary<string, MsftAdapterMeta> Msft,
        Dictionary<string, string> Pnp)
        LoadSideChannels(bool parallel)
    {
        if (!parallel)
        {
            return (TryReadWifiStatus(), TryReadDhcpLeaseObtained(), TryReadMsftAdapterMeta(), TryReadPnpDeviceIds());
        }

        var wifiTask = Task.Run(TryReadWifiStatus);
        var dhcpTask = Task.Run(TryReadDhcpLeaseObtained);
        var msftTask = Task.Run(TryReadMsftAdapterMeta);
        var pnpTask = Task.Run(TryReadPnpDeviceIds);
        Task.WaitAll(wifiTask, dhcpTask, msftTask, pnpTask);
        return (wifiTask.Result, dhcpTask.Result, msftTask.Result, pnpTask.Result);
    }

    /// <summary>
    /// 拆分连接名与网卡名称。
    /// 普通网卡：Name=连接名，Description=设备描述；
    /// NDIS 绑定（父名-组件-0000）：网络名=父连接，网卡名=组件或 Description。
    /// </summary>
    private static (string NetworkName, string AdapterName) SplitNetworkAndAdapterNames(
        NetworkInterface nic,
        bool isNdisFilter)
    {
        var connectionName = nic.Name?.Trim() ?? "";
        var description = nic.Description?.Trim() ?? "";

        if (isNdisFilter)
        {
            var match = Regex.Match(connectionName, @"^(?<net>.+)-(?<comp>.+)-\d{4}$", RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var network = match.Groups["net"].Value.Trim();
                var component = match.Groups["comp"].Value.Trim();
                var adapter = !string.IsNullOrWhiteSpace(description) && description != connectionName
                    ? description
                    : component;
                return (string.IsNullOrWhiteSpace(network) ? connectionName : network, adapter);
            }
        }

        var adapterName = !string.IsNullOrWhiteSpace(description) ? description : connectionName;
        return (connectionName, adapterName);
    }

    private static LocalNicKind ResolveNicKind(
        NetworkInterface nic,
        string? mac,
        MsftAdapterMeta? meta,
        bool isNdisFilter)
    {
        if (isNdisFilter)
        {
            return LocalNicKind.NdisFilter;
        }

        var text = $"{nic.Name} {nic.Description}";
        if (LooksVirtualByName(text))
        {
            return LocalNicKind.Virtual;
        }

        var physical = (meta?.IsPhysical ?? GuessPhysical(nic)) && IsLikelyRealAdapter(nic, mac);
        return physical ? LocalNicKind.Physical : LocalNicKind.Virtual;
    }

    /// <summary>启用网卡（物理 / 软网卡均可；需足够权限）。</summary>
    public static LocalNicActionResult EnableAdapter(string interfaceId, string? name = null) =>
        SetAdapterAdminState(interfaceId, name, enable: true);

    /// <summary>禁用网卡（物理 / 软网卡均可；需足够权限）。</summary>
    public static LocalNicActionResult DisableAdapter(string interfaceId, string? name = null) =>
        SetAdapterAdminState(interfaceId, name, enable: false);

    /// <summary>删除虚拟网卡或 NDIS 筛选器绑定（卸载设备）。物理网卡拒绝删除。</summary>
    public static LocalNicActionResult RemoveVirtualAdapter(
        string interfaceId,
        string? name = null,
        string? pnpDeviceId = null,
        bool isPhysical = false)
    {
        if (isPhysical)
        {
            return LocalNicActionResult.Fail("物理网卡不支持删除，仅可禁用 / 启用。");
        }

        if (string.IsNullOrWhiteSpace(interfaceId)
            && string.IsNullOrWhiteSpace(name)
            && string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return LocalNicActionResult.Fail("未指定网卡。");
        }

        try
        {
            if (TryDeleteMsftNetAdapter(interfaceId, name))
            {
                return LocalNicActionResult.Ok($"已删除软网卡「{name ?? interfaceId}」。");
            }

            var pnputil = TryRemoveViaPnputil(pnpDeviceId);
            if (pnputil.Success)
            {
                return pnputil;
            }

            return LocalNicActionResult.Fail(
                string.IsNullOrWhiteSpace(pnputil.Message)
                    ? "删除失败。请以管理员身份运行后再试。"
                    : pnputil.Message + " 请以管理员身份运行后再试。");
        }
        catch (Exception ex)
        {
            return LocalNicActionResult.Fail(ex.Message);
        }
    }

    private static LocalNicActionResult SetAdapterAdminState(string interfaceId, string? name, bool enable)
    {
        if (string.IsNullOrWhiteSpace(interfaceId) && string.IsNullOrWhiteSpace(name))
        {
            return LocalNicActionResult.Fail("未指定网卡。");
        }

        var action = enable ? "启用" : "禁用";
        try
        {
            if (TrySetEnabledViaWin32(interfaceId, name, enable))
            {
                return LocalNicActionResult.Ok($"已{action}网卡「{name ?? interfaceId}」。");
            }

            var viaNetsh = TrySetAdminViaNetsh(name, enable);
            if (viaNetsh.Success)
            {
                return viaNetsh;
            }

            return LocalNicActionResult.Fail(
                viaNetsh.Message + " 请以管理员身份运行本程序后再试。");
        }
        catch (Exception ex)
        {
            return LocalNicActionResult.Fail(ex.Message);
        }
    }

    private static bool TrySetEnabledViaWin32(string interfaceId, string? name, bool enable)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionStatus IS NOT NULL OR GUID IS NOT NULL");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                if (!MatchesWin32Adapter(obj, interfaceId, name))
                {
                    continue;
                }

                var result = obj.InvokeMethod(enable ? "Enable" : "Disable", null);
                if (result is uint code)
                {
                    return code is 0 or 1;
                }

                return true;
            }
        }

        return false;
    }

    private static bool MatchesWin32Adapter(ManagementObject obj, string interfaceId, string? name)
    {
        var guid = obj["GUID"]?.ToString();
        var connId = obj["NetConnectionID"]?.ToString();
        var matchGuid = !string.IsNullOrWhiteSpace(interfaceId)
            && string.Equals(NormalizeKey(guid), NormalizeKey(interfaceId), StringComparison.Ordinal);
        var matchName = !string.IsNullOrWhiteSpace(name)
            && string.Equals(connId, name, StringComparison.OrdinalIgnoreCase);
        return matchGuid || matchName;
    }

    private static LocalNicActionResult TrySetAdminViaNetsh(string? name, bool enable)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return LocalNicActionResult.Fail("无法通过 netsh 操作（缺少连接名称）。");
        }

        var admin = enable ? "enabled" : "disabled";
        var action = enable ? "启用" : "禁用";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface set interface name=\"{name.Replace("\"", "")}\" admin={admin}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return LocalNicActionResult.Fail("无法启动 netsh。");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            if (process.ExitCode == 0)
            {
                return LocalNicActionResult.Ok($"已{action}网卡「{name}」。");
            }

            var detail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            return LocalNicActionResult.Fail(string.IsNullOrEmpty(detail)
                ? $"netsh {action}失败（exit={process.ExitCode}）。"
                : detail);
        }
        catch (Exception ex)
        {
            return LocalNicActionResult.Fail(ex.Message);
        }
    }

    private static bool TryDeleteMsftNetAdapter(string interfaceId, string? name)
    {
        using var searcher = new ManagementObjectSearcher(
            @"\\.\root\StandardCimv2",
            "SELECT * FROM MSFT_NetAdapter");
        foreach (ManagementObject obj in searcher.Get())
        {
            using (obj)
            {
                var deviceId = obj["DeviceID"]?.ToString();
                var adapterName = obj["Name"]?.ToString();
                var matchId = !string.IsNullOrWhiteSpace(interfaceId)
                    && string.Equals(NormalizeKey(deviceId), NormalizeKey(interfaceId), StringComparison.Ordinal);
                var matchName = !string.IsNullOrWhiteSpace(name)
                    && string.Equals(adapterName, name, StringComparison.OrdinalIgnoreCase);
                if (!matchId && !matchName)
                {
                    continue;
                }

                obj.Delete();
                return true;
            }
        }

        return false;
    }

    private static LocalNicActionResult TryRemoveViaPnputil(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return LocalNicActionResult.Fail("缺少 PNP 设备 ID，无法卸载。");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil",
                Arguments = $"/remove-device \"{pnpDeviceId.Replace("\"", "")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return LocalNicActionResult.Fail("无法启动 pnputil。");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode == 0)
            {
                return LocalNicActionResult.Ok("已通过 pnputil 卸载设备。");
            }

            var detail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            return LocalNicActionResult.Fail(string.IsNullOrEmpty(detail)
                ? $"pnputil 卸载失败（exit={process.ExitCode}）。"
                : detail);
        }
        catch (Exception ex)
        {
            return LocalNicActionResult.Fail(ex.Message);
        }
    }

    private static bool GuessPhysical(NetworkInterface nic)
    {
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
        {
            var text = $"{nic.Name} {nic.Description}";
            if (LooksVirtualByName(text) || IsNdisFilterBinding(nic))
            {
                return false;
            }

            return !string.IsNullOrEmpty(FormatMac(nic.GetPhysicalAddress()));
        }

        return false;
    }

    /// <summary>真实可管理网卡：有 MAC，且不是「父网卡-组件-0000」这类绑定项。</summary>
    private static bool IsLikelyRealAdapter(NetworkInterface nic, string? mac)
    {
        if (string.IsNullOrEmpty(mac))
        {
            return false;
        }

        // 绑定名常见形态：WLAN-QoS Packet Scheduler-0000
        if (Regex.IsMatch(nic.Name, @"-.+-\d{4}$", RegexOptions.CultureInvariant))
        {
            return false;
        }

        return nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT;
    }

    private static bool IsNdisFilterBinding(NetworkInterface nic)
    {
        var text = $"{nic.Name} {nic.Description}";
        if (LooksLikeNdisFilter(text))
        {
            return true;
        }

        // 父接口名-筛选器显示名-实例号
        return Regex.IsMatch(nic.Name, @"-.+-\d{4}$", RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeNdisFilter(string text) =>
        text.Contains("Filter Driver", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Packet Scheduler", StringComparison.OrdinalIgnoreCase)
        || text.Contains("LightWeight Filter", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Light-Weight Filter", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Lightweight Filter", StringComparison.OrdinalIgnoreCase)
        || text.Contains("NPCAP", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Npcap", StringComparison.OrdinalIgnoreCase)
        || text.Contains("WFP ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("WFP Native", StringComparison.OrdinalIgnoreCase)
        || text.Contains("MAC Layer", StringComparison.OrdinalIgnoreCase)
        || text.Contains("NDIS", StringComparison.OrdinalIgnoreCase)
           && text.Contains("Filter", StringComparison.OrdinalIgnoreCase);

    private static bool LooksVirtualByName(string text) =>
        text.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VMware", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
        || text.Contains("VPN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("TAP-", StringComparison.OrdinalIgnoreCase)
        || text.Contains("TUN", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
        || text.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
        || text.Contains("WSL", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Npc", StringComparison.OrdinalIgnoreCase)
        || text.Contains("虚拟", StringComparison.OrdinalIgnoreCase)
        || LooksLikeNdisFilter(text);

    private static bool IsWireless(NetworkInterface nic) =>
        nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211
            || nic.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
            || nic.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
            || nic.Description.Contains("WLAN", StringComparison.OrdinalIgnoreCase)
            || nic.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase);

    private static string FormatIpv4Info(IPInterfaceProperties props, out string? gateway)
    {
        gateway = null;
        var unicast = props.UnicastAddresses
            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(u => !IPAddress.IsLoopback(u.Address))
            .Where(u => !u.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .ToList();
        if (unicast.Count == 0)
        {
            return "未连接";
        }

        gateway = props.GatewayAddresses
            .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(g => g.Address.ToString())
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && a is not "0.0.0.0");

        var dns = props.DnsAddresses
            .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
            .Select(d => d.ToString())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = unicast[0];
        var mask = primary.IPv4Mask?.ToString()
                   ?? PrefixToIpv4Mask(primary.PrefixLength);
        var extraIps = unicast.Skip(1).Select(u => u.Address.ToString()).ToList();

        var sb = new StringBuilder();
        sb.Append("IP: ").Append(primary.Address);
        if (extraIps.Count > 0)
        {
            sb.Append(", ").Append(string.Join(", ", extraIps));
        }

        sb.AppendLine();
        sb.Append("子网掩码: ").Append(string.IsNullOrWhiteSpace(mask) ? "—" : mask);
        sb.AppendLine();
        sb.Append("网关: ").Append(string.IsNullOrWhiteSpace(gateway) ? "—" : gateway);
        sb.AppendLine();
        sb.Append("DNS: ").Append(dns.Count == 0 ? "—" : string.Join(", ", dns));
        return sb.ToString();
    }

    private static string FormatIpv6Info(IPInterfaceProperties props, out string? gateway)
    {
        gateway = null;
        var addresses = props.UnicastAddresses
            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetworkV6)
            .Where(u => !IPAddress.IsLoopback(u.Address))
            .Where(u => !u.Address.IsIPv6LinkLocal)
            .Select(u => u.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (addresses.Count == 0)
        {
            return "未连接";
        }

        gateway = props.GatewayAddresses
            .Where(g => g.Address.AddressFamily == AddressFamily.InterNetworkV6)
            .Select(g => g.Address.ToString())
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && a is not "::");

        var sb = new StringBuilder();
        sb.AppendLine("IP:");
        foreach (var ip in addresses)
        {
            sb.AppendLine(ip);
        }

        sb.Append("网关: ").Append(string.IsNullOrWhiteSpace(gateway) ? "—" : gateway);
        return sb.ToString().TrimEnd();
    }

    private static string? PrefixToIpv4Mask(int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
        {
            return null;
        }

        var bits = prefixLength == 0
            ? 0u
            : prefixLength == 32
                ? uint.MaxValue
                : uint.MaxValue << (32 - prefixLength);
        var bytes = BitConverter.GetBytes(bits);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return new IPAddress(bytes).ToString();
    }

    private static string FormatSpeed(ulong? receiveBps, ulong? transmitBps, long nicSpeed)
    {
        var rx = receiveBps is > 0 ? receiveBps.Value : (nicSpeed > 0 ? (ulong)nicSpeed : 0UL);
        var tx = transmitBps is > 0 ? transmitBps.Value : (nicSpeed > 0 ? (ulong)nicSpeed : 0UL);
        if (rx == 0 && tx == 0)
        {
            return "—";
        }

        if (rx == 0)
        {
            return $"↕:{FormatBps(tx)}";
        }

        if (tx == 0 || AlmostSame(rx, tx))
        {
            return $"↕:{FormatBps(rx == 0 ? tx : rx)}";
        }

        // tx = 上行(发送), rx = 下行(接收)；换行展示时不加分号
        return $"↑:{FormatBps(tx)}{Environment.NewLine}↓:{FormatBps(rx)}";
    }

    private static bool AlmostSame(ulong a, ulong b)
    {
        var max = Math.Max(a, b);
        var min = Math.Min(a, b);
        if (max == 0)
        {
            return true;
        }

        return (double)(max - min) / max < 0.02;
    }

    private static string FormatBps(ulong bps)
    {
        if (bps >= 1_000_000_000UL)
        {
            return $"{bps / 1_000_000_000.0:0.##} Gbps";
        }

        if (bps >= 1_000_000UL)
        {
            return $"{bps / 1_000_000.0:0.##} Mbps";
        }

        if (bps >= 1_000UL)
        {
            return $"{bps / 1_000.0:0.##} Kbps";
        }

        return $"{bps} bps";
    }

    private static string FormatDuration(DateTime? leaseUtc, bool isUp)
    {
        if (!isUp)
        {
            return "—";
        }

        if (leaseUtc is null)
        {
            return "—";
        }

        var elapsed = DateTime.UtcNow - leaseUtc.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)elapsed.TotalDays} 天 {elapsed.Hours} 小时";
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours} 小时 {elapsed.Minutes} 分";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return $"{(int)elapsed.TotalMinutes} 分 {elapsed.Seconds} 秒";
        }

        return $"{Math.Max(0, (int)elapsed.TotalSeconds)} 秒";
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

    private static string NormalizeKey(string? value) =>
        (value ?? string.Empty).Trim().Trim('{', '}').ToLowerInvariant();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private sealed record MsftAdapterMeta(ulong ReceiveBps, ulong TransmitBps, bool IsPhysical);

    private static MsftAdapterMeta? ResolveMsftMeta(
        NetworkInterface nic,
        Dictionary<string, MsftAdapterMeta> map)
    {
        foreach (var key in new[] { NormalizeKey(nic.Id), NormalizeKey(nic.Name), NormalizeKey(nic.Description) })
        {
            if (map.TryGetValue(key, out var meta))
            {
                return meta;
            }
        }

        foreach (var (key, meta) in map)
        {
            if (key.Length > 3
                && (NormalizeKey(nic.Description).Contains(key, StringComparison.Ordinal)
                    || key.Contains(NormalizeKey(nic.Name), StringComparison.Ordinal)))
            {
                return meta;
            }
        }

        return null;
    }

    private sealed record WifiStatusInfo(string? Ssid, string? Signal);

    private static WifiStatusInfo? ResolveWifiStatus(
        NetworkInterface nic,
        Dictionary<string, WifiStatusInfo> statusMap)
    {
        if (statusMap.TryGetValue(NormalizeKey(nic.Name), out var byName))
        {
            return byName;
        }

        foreach (var (key, value) in statusMap)
        {
            if (NormalizeKey(nic.Description).Contains(key, StringComparison.Ordinal)
                || key.Contains(NormalizeKey(nic.Name), StringComparison.Ordinal))
            {
                return value;
            }
        }

        return statusMap.Count == 1 ? statusMap.Values.First() : null;
    }

    private static Dictionary<string, MsftAdapterMeta> TryReadMsftAdapterMeta()
    {
        var map = new Dictionary<string, MsftAdapterMeta>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\StandardCimv2",
                "SELECT Name, InterfaceDescription, DeviceID, ReceiveLinkSpeed, TransmitLinkSpeed, HardwareInterface, Virtual FROM MSFT_NetAdapter");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var rx = ToUInt64(obj["ReceiveLinkSpeed"]);
                    var tx = ToUInt64(obj["TransmitLinkSpeed"]);
                    var hardware = obj["HardwareInterface"] is bool hb && hb;
                    var virtualFlag = obj["Virtual"] is bool vb && vb;
                    var isPhysical = hardware && !virtualFlag;
                    if (!isPhysical)
                    {
                        var desc = $"{obj["Name"]} {obj["InterfaceDescription"]}";
                        if (!LooksVirtualByName(desc) && hardware)
                        {
                            isPhysical = true;
                        }
                    }

                    var meta = new MsftAdapterMeta(rx, tx, isPhysical);

                    void Add(string? key)
                    {
                        var n = NormalizeKey(key);
                        if (!string.IsNullOrEmpty(n))
                        {
                            map[n] = meta;
                        }
                    }

                    Add(obj["Name"]?.ToString());
                    Add(obj["InterfaceDescription"]?.ToString());
                    Add(obj["DeviceID"]?.ToString());
                }
            }
        }
        catch
        {
            // WMI 不可用时回退启发式
        }

        return map;
    }

    private static ulong ToUInt64(object? value) => value switch
    {
        ulong u => u,
        long l when l > 0 => (ulong)l,
        uint ui => ui,
        int i when i > 0 => (uint)i,
        string s when ulong.TryParse(s, out var parsed) => parsed,
        _ => 0UL
    };

    private static Dictionary<string, string> TryReadPnpDeviceIds()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT GUID, NetConnectionID, Name, Description, PNPDeviceID FROM Win32_NetworkAdapter");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var pnp = obj["PNPDeviceID"]?.ToString();
                    if (string.IsNullOrWhiteSpace(pnp))
                    {
                        continue;
                    }

                    void Add(string? key)
                    {
                        var n = NormalizeKey(key);
                        if (!string.IsNullOrEmpty(n))
                        {
                            map[n] = pnp;
                        }
                    }

                    Add(obj["GUID"]?.ToString());
                    Add(obj["NetConnectionID"]?.ToString());
                    Add(obj["Name"]?.ToString());
                    Add(obj["Description"]?.ToString());
                }
            }
        }
        catch
        {
            // ignore
        }

        return map;
    }

    private static Dictionary<string, DateTime> TryReadDhcpLeaseObtained()
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SettingID, Description, DHCPLeaseObtained FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var raw = obj["DHCPLeaseObtained"]?.ToString();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    DateTime lease;
                    try
                    {
                        lease = ManagementDateTimeConverter.ToDateTime(raw).ToUniversalTime();
                    }
                    catch
                    {
                        continue;
                    }

                    void Add(string? key)
                    {
                        var n = NormalizeKey(key);
                        if (!string.IsNullOrEmpty(n))
                        {
                            map[n] = lease;
                        }
                    }

                    Add(obj["SettingID"]?.ToString());
                    Add(obj["Description"]?.ToString());
                }
            }
        }
        catch
        {
            // ignore
        }

        return map;
    }

    private static Dictionary<string, WifiStatusInfo> TryReadWifiStatus()
    {
        var map = new Dictionary<string, WifiStatusInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var process = Process.Start(psi);
            if (process is null)
            {
                return map;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // 多接口时按块解析：Name / SSID / Signal（兼容中英文 netsh）
            var blocks = Regex.Split(
                output,
                @"(?=^\s*(?:Name|名称)\s*:)",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            foreach (var block in blocks)
            {
                var nameMatch = WlanNameRegex.Match(block);
                if (!nameMatch.Success)
                {
                    continue;
                }

                var name = nameMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                string? ssid = null;
                var ssidMatch = WlanSsidRegex.Match(block);
                if (ssidMatch.Success)
                {
                    ssid = ssidMatch.Groups[1].Value.Trim();
                    if (string.Equals(ssid, "无", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(ssid))
                    {
                        ssid = null;
                    }
                }

                string? signal = null;
                var signalMatch = WlanSignalRegex.Match(block);
                if (signalMatch.Success)
                {
                    signal = $"{signalMatch.Groups[1].Value.Trim()}%";
                }

                map[NormalizeKey(name)] = new WifiStatusInfo(ssid, signal);
            }
        }
        catch
        {
            // ignore
        }

        return map;
    }
}
