namespace Qolome_Devtoy_App.Helpers;

/// <summary>为当前可见列表项写入从 1 开始的序号。</summary>
public static class RowIndexStamp
{
    public static void Apply<T>(IEnumerable<T> items, Action<T, int> assign)
    {
        var i = 1;
        foreach (var item in items)
        {
            assign(item, i++);
        }
    }

    public static IList<T> ApplyToList<T>(IList<T> items, Action<T, int> assign)
    {
        Apply(items, assign);
        return items;
    }
}
