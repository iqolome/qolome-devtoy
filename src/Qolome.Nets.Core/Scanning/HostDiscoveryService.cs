using System.Net;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public sealed class HostDiscoveryService
{
    private static readonly int[] TcpFallbackPorts = [80, 443, 22, 445, 3389];

    private readonly IcmpPingService _ping;
    private readonly PortPurposeStore _purposes;

    public HostDiscoveryService(IcmpPingService ping, PortPurposeStore? purposes = null)
    {
        _ping = ping;
        _purposes = purposes ?? new PortPurposeStore();
    }

    public async Task<HostScanResult> ProbeAsync(IPAddress address, ScanRequest request, CancellationToken cancellationToken)
    {
        var reply = await _ping.PingOnceAsync(
            address.ToString(),
            request.PingPayloadSize,
            request.PingTimeoutMs,
            ttl: null,
            cancellationToken).ConfigureAwait(false);

        var result = new HostScanResult { Address = address };
        cancellationToken.ThrowIfCancellationRequested();
        if (reply.Status == PingReplyStatus.Success)
        {
            result.IsAlive = true;
            result.DiscoveryMethod = "ICMP";
            result.Ping = PingSummary.FromReplies([reply]);
            result.Status = "在线";
            return result;
        }

        foreach (var port in TcpFallbackPorts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await PortScanner.IsOpenAsync(address, port, Math.Min(request.ConnectTimeoutMs, 400), cancellationToken)
                    .ConfigureAwait(false))
            {
                result.IsAlive = true;
                result.DiscoveryMethod = "TCP";
                result.Ping = PingSummary.FromReplies([reply]);
                result.Status = "在线（ICMP 超时）";
                var found = new OpenPort
                {
                    Port = port,
                    Protocol = PortPurposeCatalog.Tcp,
                    State = "open"
                };
                _purposes.Apply(found);
                result.OpenPorts.Add(found);
                return result;
            }
        }

        result.IsAlive = false;
        result.Ping = PingSummary.FromReplies([reply]);
        result.Status = reply.StatusText;
        return result;
    }
}
