using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Nets.Core.Data;

/// <summary>Devtoy <see cref="DataSourceSettings"/> 与 Nets <see cref="DatabaseConfig"/> 互转。</summary>
public static class NetsDataSourceBridge
{
    /// <summary>当前 Sqlite 业务/配置库路径。</summary>
    public static DatabaseConfig LocalSqliteConfig() => new()
    {
        Provider = DatabaseProviders.Sqlite,
        Sqlite = new SqliteConfig { Path = DataSourceRuntime.ResolvedSqlitePath },
    };

    /// <summary>业务库连接：Sqlite 或 MySQL。</summary>
    public static DatabaseConfig ToBusinessDatabaseConfig(DataSourceSettings ds)
    {
        if (ds.DatabaseKind == DatabaseKind.Sqlite || ds.DatabaseKind == DatabaseKind.MySql)
        {
            return ToDatabaseConfig(ds);
        }

        throw new InvalidOperationException(
            $"网络业务库日常读写目前仅支持 Sqlite 或 MySQL（当前为 {ds.DatabaseKind}）。字典等 ADO 路径仍可按数据源类型使用。");
    }

    public static DatabaseConfig ToDatabaseConfig(DataSourceSettings ds)
    {
        DataSourceRuntime.Normalize(ds);
        if (ds.DatabaseKind == DatabaseKind.Sqlite)
        {
            var path = DataSourceRuntime.ResolveSqlitePath(ds);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            return new DatabaseConfig
            {
                Provider = DatabaseProviders.Sqlite,
                Sqlite = new SqliteConfig { Path = path },
            };
        }

        if (ds.DatabaseKind != DatabaseKind.MySql)
        {
            throw new InvalidOperationException(
                $"Nets EF 连接仅支持 MySQL / Sqlite，当前为 {ds.DatabaseKind}。");
        }

        var mysql = new MySqlConfig
        {
            Host = string.IsNullOrWhiteSpace(ds.Host) ? "127.0.0.1" : ds.Host.Trim(),
            Port = int.TryParse(ds.Port, out var port) && port > 0 ? port : 3306,
            Database = string.IsNullOrWhiteSpace(ds.Database) ? "qolome" : ds.Database.Trim(),
            User = string.IsNullOrWhiteSpace(ds.Username) ? "root" : ds.Username.Trim(),
        };
        mysql.SetPasswordFromUser(ds.Password);

        return new DatabaseConfig
        {
            Provider = DatabaseProviders.MySql,
            Sqlite = new SqliteConfig { Path = DataSourceRuntime.ResolvedSqlitePath },
            Mysql = mysql,
        };
    }

    public static DataSourceSettings FromDatabaseConfig(DatabaseConfig config)
    {
        config.Normalize();
        if (config.IsSqlite)
        {
            var path = config.Sqlite.Path;
            return new DataSourceSettings
            {
                DatabaseKind = DatabaseKind.Sqlite,
                SqlitePath = path,
                Database = path,
            };
        }

        var m = config.Mysql;
        return new DataSourceSettings
        {
            DatabaseKind = DatabaseKind.MySql,
            Host = m.Host,
            Port = m.Port.ToString(),
            Database = m.Database,
            Username = m.User,
            Password = m.Password,
        };
    }
}
