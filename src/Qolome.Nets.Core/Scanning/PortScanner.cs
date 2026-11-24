using System.Net;
using System.Net.Sockets;
using Qolome.Nets.Core.Data;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public sealed class PortScanner
{
    private readonly PortPurposeStore _purposes;

    public PortScanner(PortPurposeStore? purposes = null)
    {
        _purposes = purposes ?? new PortPurposeStore();
    }

    public async Task<IReadOnlyList<OpenPort>> ScanTcpAsync(
        IPAddress address,
        IReadOnlyList<int> ports,
        int timeoutMs,
        int concurrency,
        CancellationToken cancellationToken)
    {
        if (ports.Count == 0)
        {
            return [];
        }

        var open = new List<OpenPort>();
        var gate = new object();
        await Parallel.ForEachAsync(ports, new ParallelOptions
        {
            MaxDegreeOfParallelism = ClampConcurrency(concurrency),
            CancellationToken = cancellationToken
        }, async (port, token) =>
        {
            if (await IsOpenAsync(address, port, timeoutMs, token).ConfigureAwait(false))
            {
                var item = Create(port, PortPurposeCatalog.Tcp, "open");
                lock (gate)
                {
                    open.Add(item);
                }
            }
        }).ConfigureAwait(false);

        return open.OrderBy(p => p.Port).ToList();
    }

    internal static int ClampConcurrency(int concurrency)
    {
        if (concurrency <= 0)
        {
            concurrency = ScanOptions.DefaultPortScanConcurrency;
        }

        return Math.Clamp(concurrency, 1, 256);
    }

    public OpenPort Create(int port, string protocol, string state)
    {
        var item = new OpenPort
        {
            Port = port,
            Protocol = PortPurposeCatalog.NormalizeProtocol(protocol),
            State = state
        };
        _purposes.Apply(item);
        return item;
    }

    public static async Task<bool> IsOpenAsync(IPAddress address, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(50, timeoutMs));
        try
        {
            await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            return client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }
}
