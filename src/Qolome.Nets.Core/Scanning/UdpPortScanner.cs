using System.Net;
using System.Net.Sockets;
using System.Text;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public sealed class UdpPortScanner
{
    private readonly PortScanner _tcp;

    public UdpPortScanner(PortScanner tcp)
    {
        _tcp = tcp;
    }

    public async Task<IReadOnlyList<OpenPort>> ScanAsync(
        IPAddress address,
        IReadOnlyList<int> ports,
        int timeoutMs,
        bool treatTimeoutAsOpen,
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
            MaxDegreeOfParallelism = PortScanner.ClampConcurrency(concurrency),
            CancellationToken = cancellationToken
        }, async (port, token) =>
        {
            var state = await ProbeAsync(address, port, timeoutMs, treatTimeoutAsOpen, token).ConfigureAwait(false);
            if (state is null)
            {
                return;
            }

            var item = _tcp.Create(port, PortPurposeCatalog.Udp, state);
            lock (gate)
            {
                open.Add(item);
            }
        }).ConfigureAwait(false);

        return open.OrderBy(p => p.Port).ToList();
    }

    private static async Task<string?> ProbeAsync(
        IPAddress address,
        int port,
        int timeoutMs,
        bool treatTimeoutAsOpen,
        CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        try
        {
            client.Connect(new IPEndPoint(address, port));
            var payload = UdpProbes.For(port);
            await client.SendAsync(payload, cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Math.Max(80, timeoutMs));
            _ = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            return "open";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return treatTimeoutAsOpen ? "open|filtered" : null;
        }
        catch (SocketException ex) when (
            ex.SocketErrorCode is SocketError.ConnectionReset
                or SocketError.TimedOut
                or SocketError.NetworkUnreachable
                or SocketError.HostUnreachable
                or SocketError.MessageSize)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}

internal static class UdpProbes
{
    public static byte[] For(int port) => port switch
    {
        53 or 5353 or 5355 => DnsQuery(),
        123 => NtpQuery(),
        161 => SnmpGet(),
        137 => NetBiosQuery(),
        1900 => Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n"),
        5060 => Encoding.ASCII.GetBytes("OPTIONS sip:scan@invalid SIP/2.0\r\nVia: SIP/2.0/UDP 0.0.0.0\r\n\r\n"),
        3702 => Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body/></soap:Envelope>"),
        _ => [0x00, 0x00, 0x00, 0x00]
    };

    private static byte[] DnsQuery()
    {
        var name = EncodeDnsName("example.com");
        var packet = new byte[12 + name.Length + 4];
        packet[0] = 0x12;
        packet[1] = 0x34;
        packet[2] = 0x01;
        packet[5] = 0x01;
        Buffer.BlockCopy(name, 0, packet, 12, name.Length);
        var offset = 12 + name.Length;
        packet[offset + 1] = 0x01;
        packet[offset + 3] = 0x01;
        return packet;
    }

    private static byte[] EncodeDnsName(string host)
    {
        var labels = host.Split('.');
        var bytes = new List<byte>();
        foreach (var label in labels)
        {
            var raw = Encoding.ASCII.GetBytes(label);
            bytes.Add((byte)raw.Length);
            bytes.AddRange(raw);
        }

        bytes.Add(0);
        return [.. bytes];
    }

    private static byte[] NtpQuery()
    {
        var packet = new byte[48];
        packet[0] = 0x23;
        return packet;
    }

    private static byte[] SnmpGet()
    {
        return
        [
            0x30, 0x26, 0x02, 0x01, 0x00, 0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63,
            0xA0, 0x19, 0x02, 0x01, 0x01, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00, 0x30, 0x0E,
            0x30, 0x0C, 0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x01, 0x00, 0x05, 0x00
        ];
    }

    private static byte[] NetBiosQuery()
    {
        var packet = new byte[50];
        packet[0] = 0x12;
        packet[1] = 0x34;
        packet[2] = 0x01;
        packet[5] = 0x01;
        packet[12] = 32;
        var name = Encoding.ASCII.GetBytes("CKAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
        Buffer.BlockCopy(name, 0, packet, 13, 32);
        packet[46] = 0x00;
        packet[47] = 0x20;
        packet[49] = 0x01;
        return packet;
    }
}
