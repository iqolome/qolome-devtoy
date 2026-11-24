using Qolome.Devtoy.Core.Models;

namespace Qolome.Nets.Core.Data;

/// <summary>Devtoy 宿主注入的运行时钩子（远程数据源等）。</summary>
public static class NetsRuntime
{
    /// <summary>当前远程数据源（MySQL / PgSQL / Oracle / ODBC / 或指向其它 SQLite）。</summary>
    public static Func<DataSourceSettings>? GetRemoteDataSource { get; set; }

    public static DataSourceSettings RequireRemoteDataSource()
    {
        var ds = GetRemoteDataSource?.Invoke()
            ?? throw new InvalidOperationException("未注入远程数据源，请先在「数据源设置」中配置。");
        return ds;
    }
}
