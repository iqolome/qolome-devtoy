using Qolome.Devtoy.Core.Models;

namespace Qolome.Nets.Core.Data;

/// <summary>Devtoy 宿主注入的运行时钩子（业务数据源等）。</summary>
public static class NetsRuntime
{
    /// <summary>当前业务数据源（「数据源设置」；业务表只写这里）。</summary>
    public static Func<DataSourceSettings>? GetRemoteDataSource { get; set; }

    public static DataSourceSettings RequireRemoteDataSource()
    {
        var ds = GetRemoteDataSource?.Invoke()
            ?? throw new InvalidOperationException("未配置数据源，请先在「配置管理 → 数据源设置」中配置远程库。");
        if (string.IsNullOrWhiteSpace(ds.Host) && ds.DatabaseKind != DatabaseKind.Sqlite)
        {
            // 允许空 host 时 Normalize 会补 127.0.0.1；这里不强制。
        }

        return ds;
    }

    public static bool TryGetRemoteDataSource(out DataSourceSettings settings, out string? error)
    {
        try
        {
            var ds = GetRemoteDataSource?.Invoke();
            if (ds is null)
            {
                settings = null!;
                error = "未配置数据源，请先在「配置管理 → 数据源设置」中配置远程库。";
                return false;
            }

            settings = ds;
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            settings = null!;
            error = ex.Message;
            return false;
        }
    }
}
