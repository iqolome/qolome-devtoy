using System.Net;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public sealed class ScanSession
{
    private readonly IcmpPingService _ping = new();
    private readonly HostDiscoveryService _discovery;
    private readonly ArpMacResolver _mac = new();
    private readonly OuiVendorLookup _oui = new();
    private readonly PortScanner _ports;
    private readonly UdpPortScanner _udp;
    private readonly PortPurposeStore _purposes;

    public ScanSession(PortPurposeStore? purposes = null)
    {
        _purposes = purposes ?? new PortPurposeStore();
        _discovery = new HostDiscoveryService(_ping, _purposes);
        _ports = new PortScanner(_purposes);
        _udp = new UdpPortScanner(_ports);
    }

    public IcmpPingService Ping => _ping;

    public async Task RunAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        Action<HostScanResult> onHost,
        CancellationToken cancellationToken)
    {
        var targets = ScanTargetParser.Parse(request.Target);
        var completed = 0;
        var total = targets.Count;
        progress?.Report(new ScanProgress(0, total, $"准备扫描 {total} 个地址…"));

        var ipConcurrency = Math.Clamp(request.HostConcurrency, 1, 64);
        var batchSize = Math.Clamp(request.IpScanBatchSize <= 0 ? 50 : request.IpScanBatchSize, 1, 4096);
        for (var offset = 0; offset < targets.Count; offset += batchSize)
        {
            var take = Math.Min(batchSize, targets.Count - offset);
            var batch = targets.Skip(offset).Take(take).ToList();
            await Parallel.ForEachAsync(batch, new ParallelOptions
            {
                MaxDegreeOfParallelism = ipConcurrency,
                CancellationToken = cancellationToken
            }, async (address, token) =>
            {
                try
                {
                    var host = await _discovery.ProbeAsync(address, request, token).ConfigureAwait(false);
                    if (!host.IsAlive)
                    {
                        return;
                    }

                    onHost(Clone(host));
                    await EnrichAsync(host, token).ConfigureAwait(false);
                    onHost(Clone(host));

                    host.Status = "扫描端口…";
                    onHost(Clone(host));
                    await ScanPortsAsync(host, address, request, token).ConfigureAwait(false);
                    host.Status = host.OpenPorts.Count == 0
                        ? (host.DiscoveryMethod == "TCP" ? "在线（ICMP 超时）" : "在线")
                        : $"在线 · {host.OpenPorts.Count} 个端口";
                    onHost(Clone(host));
                }
                finally
                {
                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new ScanProgress(done, total, $"已完成 {done}/{total}"));
                }
            }).ConfigureAwait(false);
        }

        progress?.Report(new ScanProgress(total, total, "扫描完成"));
    }

    public async Task<HostScanResult> RefreshHostAsync(
        IPAddress address,
        ScanRequest request,
        bool scanPorts,
        bool enrich,
        CancellationToken cancellationToken)
    {
        var host = await _discovery.ProbeAsync(address, request, cancellationToken).ConfigureAwait(false);
        if (!host.IsAlive)
        {
            return host;
        }

        if (enrich)
        {
            await EnrichAsync(host, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            host.OsType = OsTypeGuess.FromTtl(host.Ping.LastTtl);
            host.DeviceInfo = OsTypeGuess.ComposeDeviceInfo(host.DiscoveryMethod, host.Ping.LastTtl);
        }

        if (scanPorts)
        {
            await ScanPortsAsync(host, address, request, cancellationToken).ConfigureAwait(false);
            host.Status = host.OpenPorts.Count == 0
                ? (host.DiscoveryMethod == "TCP" ? "在线（ICMP 超时）" : "在线")
                : $"在线 · {host.OpenPorts.Count} 个端口";
        }

        return host;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SnapshotArpMacToIps() => _mac.SnapshotMacToIps();

    private async Task ScanPortsAsync(
        HostScanResult host,
        IPAddress address,
        ScanRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TcpPorts.Count > 0)
        {
            var tcp = await _ports.ScanTcpAsync(
                    address,
                    request.TcpPorts,
                    request.ConnectTimeoutMs,
                    request.PortScanConcurrency,
                    cancellationToken)
                .ConfigureAwait(false);
            MergePorts(host, tcp);
        }

        if (request.UdpPorts.Count > 0)
        {
            var udp = await _udp.ScanAsync(
                    address,
                    request.UdpPorts,
                    request.ConnectTimeoutMs,
                    request.TreatUdpTimeoutAsOpen,
                    request.PortScanConcurrency,
                    cancellationToken)
                .ConfigureAwait(false);
            MergePorts(host, udp);
        }

        foreach (var port in host.OpenPorts)
        {
            _purposes.Apply(port);
        }
    }

    private async Task EnrichAsync(HostScanResult host, CancellationToken cancellationToken)
    {
        var dnsTask = ResolveHostnameAsync(host.Address, cancellationToken);
        string? mac = null;
        try
        {
            mac = _mac.Resolve(host.Address);
        }
        catch
        {
            mac = null;
        }

        host.Hostname = await dnsTask.ConfigureAwait(false);
        host.Mac = mac;
        if (IPAddress.IsLoopback(host.Address))
        {
            host.Vendor = "本机回环";
        }
        else if (string.IsNullOrWhiteSpace(mac))
        {
            host.Vendor = IsRemoteSubnetHint(host.Address) ? "MAC 仅本网段可见" : null;
        }
        else
        {
            host.Vendor = _oui.Lookup(mac);
        }

        host.OsType = OsTypeGuess.FromTtl(host.Ping.LastTtl);
        host.DeviceInfo = OsTypeGuess.ComposeDeviceInfo(host.DiscoveryMethod, host.Ping.LastTtl);
    }

    private static async Task<string?> ResolveHostnameAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(1800);
            var entry = await Dns.GetHostEntryAsync(address.ToString(), timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == address.ToString())
            {
                return null;
            }

            return entry.HostName;
        }
        catch
        {
            return null;
        }
    }

    private static void MergePorts(HostScanResult host, IReadOnlyList<OpenPort> ports)
    {
        var existing = host.OpenPorts
            .Select(p => (p.Port, p.ProtocolKey))
            .ToHashSet();
        foreach (var port in ports)
        {
            if (existing.Add((port.Port, port.ProtocolKey)))
            {
                host.OpenPorts.Add(port);
            }
        }

        host.OpenPorts.Sort((a, b) =>
        {
            var byPort = a.Port.CompareTo(b.Port);
            return byPort != 0 ? byPort : string.Compare(a.ProtocolKey, b.ProtocolKey, StringComparison.Ordinal);
        });
    }

    private static HostScanResult Clone(HostScanResult source)
    {
        var copy = new HostScanResult
        {
            Address = source.Address,
            Hostname = source.Hostname,
            Mac = source.Mac,
            Vendor = source.Vendor,
            OsType = source.OsType,
            DeviceInfo = source.DeviceInfo,
            Ping = source.Ping,
            DiscoveryMethod = source.DiscoveryMethod,
            IsAlive = source.IsAlive,
            Status = source.Status
        };
        copy.OpenPorts.AddRange(source.OpenPorts);
        return copy;
    }

    private static bool IsRemoteSubnetHint(IPAddress address)
    {
        foreach (var adapter in NetworkAdapterEnumerator.GetActiveIpv4Adapters())
        {
            var host = ScanTargetParser.ToUint(address);
            var local = ScanTargetParser.ToUint(adapter.Address);
            var mask = adapter.PrefixLength == 0 ? 0 : uint.MaxValue << (32 - adapter.PrefixLength);
            if ((host & mask) == (local & mask))
            {
                return false;
            }
        }

        return true;
    }
}
