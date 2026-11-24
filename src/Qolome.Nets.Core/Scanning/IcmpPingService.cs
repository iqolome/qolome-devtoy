using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Scanning;

public sealed class IcmpPingService
{
    public async Task<PingReplyInfo> PingOnceAsync(string host, int payloadSize, int timeoutMs, int? ttl, CancellationToken cancellationToken)
    {
        payloadSize = Math.Clamp(payloadSize, 1, 65500);
        timeoutMs = Math.Max(timeoutMs, 1);

        var buffer = CreatePayload(payloadSize);
        PingOptions? options = ttl is int value ? new PingOptions(Math.Clamp(value, 1, 255), true) : null;

        try
        {
            if (!IPAddress.TryParse(host, out var address))
            {
                var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                address = resolved.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                    ?? resolved.FirstOrDefault();
                if (address is null)
                {
                    return new PingReplyInfo
                    {
                        Address = host,
                        Status = PingReplyStatus.Unknown,
                        StatusText = "无法解析主机"
                    };
                }
            }

            using var ping = new Ping();
            using var cancel = cancellationToken.Register(ping.SendAsyncCancel);
            var reply = await ping.SendPingAsync(address, timeoutMs, buffer, options)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Map(reply);
        }
        catch (OperationCanceledException)
        {
            return new PingReplyInfo
            {
                Address = host,
                Status = PingReplyStatus.Cancelled,
                StatusText = "已取消"
            };
        }
        catch (PingException ex)
        {
            return new PingReplyInfo
            {
                Address = host,
                Status = PingReplyStatus.Unknown,
                StatusText = ex.InnerException?.Message ?? ex.Message
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new PingReplyInfo
            {
                Address = host,
                Status = PingReplyStatus.Unknown,
                StatusText = ex.Message
            };
        }
    }

    public async IAsyncEnumerable<PingReplyInfo> PingAsync(PingRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remaining = request.Continuous ? int.MaxValue : Math.Max(1, request.Count);
        for (var i = 0; i < remaining; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await PingOnceAsync(request.Target, request.PayloadSize, request.TimeoutMs, request.Ttl, cancellationToken)
                .ConfigureAwait(false);

            if (i + 1 < remaining)
            {
                await Task.Delay(Math.Max(0, request.IntervalMs), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<PingSummary> PingSummaryAsync(PingRequest request, IProgress<PingReplyInfo>? progress, CancellationToken cancellationToken)
    {
        var replies = new List<PingReplyInfo>();
        await foreach (var reply in PingAsync(request, cancellationToken).ConfigureAwait(false))
        {
            replies.Add(reply);
            progress?.Report(reply);
        }

        return PingSummary.FromReplies(replies);
    }

    private static PingReplyInfo Map(PingReply reply)
    {
        var status = PingStatusMapper.Map(reply.Status);
        return new PingReplyInfo
        {
            Address = reply.Address?.ToString() ?? string.Empty,
            Status = status,
            Size = reply.Buffer?.Length ?? 0,
            RttMs = reply.RoundtripTime,
            Ttl = reply.Options?.Ttl,
            StatusText = PingStatusMapper.Describe(reply.Status)
        };
    }

    private static byte[] CreatePayload(int size)
    {
        var buffer = new byte[size];
        for (var i = 0; i < size; i++)
        {
            buffer[i] = (byte)('a' + (i % 23));
        }

        return buffer;
    }
}
