namespace Qolome.Devtoy.Core.Services;

/// <summary>避免并发/重复的数据库访问挤满 UI 线程。</summary>
public static class DatabaseGate
{
    private static int _busy;

    public static bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    }

    public static void Exit() => Interlocked.Exchange(ref _busy, 0);
}