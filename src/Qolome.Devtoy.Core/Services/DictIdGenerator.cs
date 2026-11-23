namespace Qolome.Devtoy.Core.Services;

/// <summary>与库表 ASSIGN_ID 兼容的简易 ID（对齐 Java DictIdGenerator）。</summary>
public static class DictIdGenerator
{
    private static long _sequence;

    public static long NextId()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seq = Interlocked.Increment(ref _sequence) % 10_000;
        return now * 10_000 + seq;
    }
}