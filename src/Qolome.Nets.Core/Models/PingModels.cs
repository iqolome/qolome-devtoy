using System.Net.NetworkInformation;

namespace Qolome.Nets.Core.Models;

public enum PingReplyStatus
{
    Success,
    TimedOut,
    DestinationUnreachable,
    TtlExpired,
    HardwareError,
    Cancelled,
    Unknown
}

public sealed class PingReplyInfo
{
    public required string Address { get; init; }
    public required PingReplyStatus Status { get; init; }
    public int Size { get; init; }
    public long RttMs { get; init; }
    public int? Ttl { get; init; }
    public string StatusText { get; init; } = "";

    public string SizeDisplay => Status == PingReplyStatus.Success ? $"{Size} 字节" : "—";

    public string RttDisplay => Status == PingReplyStatus.Success
        ? (RttMs < 1 ? "<1ms" : $"{RttMs}ms")
        : "—";

    public string TtlDisplay => Status == PingReplyStatus.Success && Ttl is int ttl ? ttl.ToString() : "—";

    public string LineText => Status == PingReplyStatus.Success
        ? $"来自 {Address} 的回复: 字节={Size} 时间={RttDisplay} TTL={Ttl}"
        : $"{Address}: {StatusText}";
}

public sealed class PingSummary
{
    public int Sent { get; init; }
    public int Received { get; init; }
    public int Lost => Math.Max(0, Sent - Received);
    public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;
    public long? MinRttMs { get; init; }
    public long? MaxRttMs { get; init; }
    public double? AvgRttMs { get; init; }
    public int? LastSize { get; init; }
    public int? LastTtl { get; init; }
    public bool HasSuccess => Received > 0;

    public string LatencyDisplay =>
        !HasSuccess ? "—" :
        MinRttMs is long min && min < 1 ? "<1ms" :
        AvgRttMs is double avg ? $"{Math.Round(avg)}ms" :
        "—";

    public string TtlDisplay => LastTtl?.ToString() ?? "—";

    public string SizeDisplay => LastSize is int size ? $"{size} 字节" : "—";

    public string StatisticsText
    {
        get
        {
            var loss = Sent == 0 ? "0%" : $"{LossPercent:0.#}%";
            var delay = HasSuccess
                ? $"延迟 min/avg/max = {FormatRtt(MinRttMs)}/{FormatAvg(AvgRttMs)}/{FormatRtt(MaxRttMs)}"
                : "无成功应答";
            return $"已发送 {Sent}，已接收 {Received}，丢失 {Lost} ({loss})，{delay}";
        }
    }

    private static string FormatRtt(long? value) =>
        value is null ? "—" : value.Value < 1 ? "<1ms" : $"{value}ms";

    private static string FormatAvg(double? value) =>
        value is null ? "—" : value.Value < 1 ? "<1ms" : $"{Math.Round(value.Value)}ms";

    public static PingSummary Empty { get; } = new();

    public static PingSummary FromReplies(IReadOnlyList<PingReplyInfo> replies)
    {
        var success = replies.Where(r => r.Status == PingReplyStatus.Success).ToList();
        return new PingSummary
        {
            Sent = replies.Count,
            Received = success.Count,
            MinRttMs = success.Count == 0 ? null : success.Min(r => r.RttMs),
            MaxRttMs = success.Count == 0 ? null : success.Max(r => r.RttMs),
            AvgRttMs = success.Count == 0 ? null : success.Average(r => (double)r.RttMs),
            LastSize = success.LastOrDefault()?.Size,
            LastTtl = success.LastOrDefault()?.Ttl
        };
    }
}

public static class PingStatusMapper
{
    public static PingReplyStatus Map(IPStatus status) => status switch
    {
        IPStatus.Success => PingReplyStatus.Success,
        IPStatus.TimedOut => PingReplyStatus.TimedOut,
        IPStatus.DestinationHostUnreachable
            or IPStatus.DestinationNetworkUnreachable
            or IPStatus.DestinationUnreachable => PingReplyStatus.DestinationUnreachable,
        IPStatus.TtlExpired or IPStatus.TimeExceeded => PingReplyStatus.TtlExpired,
        IPStatus.HardwareError => PingReplyStatus.HardwareError,
        _ => PingReplyStatus.Unknown
    };

    public static string Describe(IPStatus status) => status switch
    {
        IPStatus.Success => "成功",
        IPStatus.TimedOut => "请求超时",
        IPStatus.DestinationHostUnreachable => "目标主机不可达",
        IPStatus.DestinationNetworkUnreachable => "目标网络不可达",
        IPStatus.DestinationUnreachable => "目标不可达",
        IPStatus.TtlExpired or IPStatus.TimeExceeded => "TTL 超时",
        IPStatus.HardwareError => "硬件错误",
        _ => status.ToString()
    };
}
