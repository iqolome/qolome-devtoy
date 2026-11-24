using System.Net;

namespace Qolome.Nets.Core.Models;

public sealed class NetworkAdapterInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IPAddress Address { get; init; }
    public required int PrefixLength { get; init; }
    public required string Cidr { get; init; }
    public string? Mac { get; init; }

    public string DisplayName => $"{Name}  {Cidr}";
}

public sealed class OpenPort
{
    public required int Port { get; init; }
    public string Protocol { get; init; } = "TCP";
    public string Service { get; set; } = "";
    public string? Description { get; set; }
    public string State { get; init; } = "open";

    public string ProtocolKey => string.Equals(Protocol, "UDP", StringComparison.OrdinalIgnoreCase) ? "UDP" : "TCP";

    public override string ToString()
    {
        var proto = ProtocolKey.ToLowerInvariant();
        var text = $"{Port}/{proto}";
        if (!string.IsNullOrWhiteSpace(Service))
        {
            text += $" {Service}";
        }

        if (!string.IsNullOrWhiteSpace(Description))
        {
            text += $" — {Description}";
        }

        if (State == "open|filtered")
        {
            text += "（开放或过滤）";
        }

        return text;
    }
}

public sealed class HostScanResult
{
    public required IPAddress Address { get; init; }
    public string Ip => Address.ToString();
    public string? Hostname { get; set; }
    public string? Mac { get; set; }
    public string? Vendor { get; set; }
    public string? OsType { get; set; }
    public string? DeviceInfo { get; set; }
    public PingSummary Ping { get; set; } = PingSummary.Empty;
    public string DiscoveryMethod { get; set; } = "";
    public List<OpenPort> OpenPorts { get; } = [];
    public bool IsAlive { get; set; }
    public string Status { get; set; } = "探测中";

    public string PortsDisplay => OpenPorts.Count == 0
        ? "—"
        : string.Join(", ", OpenPorts.Select(p => $"{p.Port}/{p.ProtocolKey.ToLowerInvariant()}"));
}

public sealed class ScanRequest
{
    public required string Target { get; init; }
    public IReadOnlyList<int> TcpPorts { get; init; } = [];
    public IReadOnlyList<int> UdpPorts { get; init; } = [];
    public int PingTimeoutMs { get; init; } = 1000;
    public int PingPayloadSize { get; init; } = 32;
    public int ConnectTimeoutMs { get; init; } = 400;
    public int HostConcurrency { get; init; } = 3;
    public int IpScanBatchSize { get; init; } = 50;
    public int PortScanConcurrency { get; init; }
    public bool TreatUdpTimeoutAsOpen { get; init; }
}

public sealed class DeviceRefreshOptions
{
    public bool Ports { get; init; }
    public bool OsType { get; init; }
    public bool Ip { get; init; }
    public bool Vendor { get; init; }

    public bool Any => Ports || OsType || Ip || Vendor;

    public bool NeedsNetwork => Ports || OsType || Ip;
}

public sealed record DeviceRefreshResult(int Updated, int Unchanged, int Offline);

public sealed class ScanProgress
{
    public ScanProgress(int completed, int total, string message, bool indeterminate = false)
    {
        Completed = completed;
        Total = total;
        Message = message;
        Indeterminate = indeterminate;
        Percent = indeterminate || total <= 0 ? 0 : Math.Clamp(completed * 100.0 / total, 0, 100);
    }

    public int Completed { get; }
    public int Total { get; }
    public string Message { get; }
    public double Percent { get; }
    public bool Indeterminate { get; }
}

public sealed class ScanException : Exception
{
    public ScanException(string message) : base(message)
    {
    }
}

public sealed class PingRequest
{
    public required string Target { get; init; }
    public int PayloadSize { get; init; } = 32;
    public int Count { get; init; } = 4;
    public int TimeoutMs { get; init; } = 1000;
    public int IntervalMs { get; init; } = 1000;
    public int? Ttl { get; init; }
    public bool Continuous => Count <= 0;
}
