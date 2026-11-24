using Qolome.Devtoy.Core.Models;

namespace Qolome.Devtoy.Core.Services;

/// <summary>当前进程生效的数据源连接（来自 datasource.dat）。</summary>
public static class DataSourceRuntime
{
    private static readonly object Gate = new();
    private static DataSourceSettings _current = new();

    public static DataSourceSettings Current
    {
        get
        {
            lock (Gate)
            {
                return Clone(_current);
            }
        }
    }

    public static string ResolvedSqlitePath
    {
        get
        {
            lock (Gate)
            {
                return ResolveSqlitePath(_current);
            }
        }
    }

    public static string ResolveSqlitePath(DataSourceSettings ds)
    {
        if (!string.IsNullOrWhiteSpace(ds.SqlitePath))
        {
            return Path.GetFullPath(ds.SqlitePath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(ds.Database)
            && (ds.Database.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
                || ds.Database.Contains('\\', StringComparison.Ordinal)
                || ds.Database.Contains('/', StringComparison.Ordinal)))
        {
            return Path.GetFullPath(ds.Database.Trim());
        }

        return DevtoyPaths.EnsureMigratedDefaultSqlitePath();
    }

    public static void Normalize(DataSourceSettings ds)
    {
        if (ds.DatabaseKind != DatabaseKind.Sqlite)
        {
            return;
        }

        ds.InputMode = JdbcInputMode.Fields;
        ds.SqlitePath = ResolveSqlitePath(ds);
        ds.Database = ds.SqlitePath;
        ds.Host = "";
        ds.Port = "";
        ds.ConnectionParams = "";
        ds.JdbcUrl = "";
        DataSourceConnectionStore.GenerateSqliteCredentialsIfNeeded(ds);
    }

    public static bool IsSqliteMode
    {
        get
        {
            lock (Gate)
            {
                return _current.IsSqlite;
            }
        }
    }

    public static bool IsRemoteMySql
    {
        get
        {
            lock (Gate)
            {
                return _current.IsRemoteBusiness;
            }
        }
    }

    /// <summary>启动时：优先读 .dat；若无则保持当前（由 SettingsStore 再灌入遗留 DataSource）。</summary>
    public static DataSourceSettings Bootstrap()
    {
        var fromDat = DataSourceConnectionStore.TryLoad();
        if (fromDat is not null)
        {
            Normalize(fromDat);
            SetCurrent(fromDat);
            return Clone(fromDat);
        }

        lock (Gate)
        {
            // 尚无 .dat：先不强制改成 Sqlite，等 SettingsStore 从遗留库迁移
            return Clone(_current);
        }
    }

    public static void UseDefaultSqliteIfUnset()
    {
        lock (Gate)
        {
            if (DataSourceConnectionStore.TryLoad() is not null)
            {
                return;
            }
        }

        var def = CreateDefault();
        SetCurrent(def);
        DataSourceConnectionStore.Save(def);
    }

    public static void SetCurrent(DataSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Normalize(settings);
        lock (Gate)
        {
            _current = Clone(settings);
        }
    }

    public static void PersistCurrent(DataSourceSettings settings)
    {
        SetCurrent(settings);
        DataSourceConnectionStore.Save(settings);
    }

    private static DataSourceSettings CreateDefault()
    {
        var path = DevtoyPaths.EnsureMigratedDefaultSqlitePath();
        return new DataSourceSettings
        {
            DatabaseKind = DatabaseKind.Sqlite,
            InputMode = JdbcInputMode.Fields,
            SqlitePath = path,
            Database = path,
            Username = "devtoy",
            Password = "",
        };
    }

    private static DataSourceSettings Clone(DataSourceSettings s) => new()
    {
        InputMode = s.InputMode,
        DatabaseKind = s.DatabaseKind,
        Host = s.Host,
        Port = s.Port,
        Database = s.Database,
        SqlitePath = s.SqlitePath,
        ConnectionParams = s.ConnectionParams,
        JdbcUrl = s.JdbcUrl,
        Username = s.Username,
        Password = s.Password,
    };
}
