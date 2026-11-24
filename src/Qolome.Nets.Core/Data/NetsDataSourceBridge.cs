using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Nets.Core.Data;

/// <summary>Devtoy <see cref="DataSourceSettings"/> 与 Nets <see cref="DatabaseConfig"/> 互转。</summary>
public static class NetsDataSourceBridge
{
    /// <summary>本地 SQLite 仅用于少量配置（非业务库存）。</summary>
    public static DatabaseConfig LocalSqliteConfig() => new()
    {
        Provider = DatabaseProviders.Sqlite,
        Sqlite = new SqliteConfig { Path = DevtoyPaths.LocalDbPath },
    };

    /// <summary>业务库连接：日常 CRUD / Schema 使用（当前 EF 路径支持 MySQL）。</summary>
    public static DatabaseConfig ToBusinessDatabaseConfig(DataSourceSettings ds)
    {
        if (ds.DatabaseKind == DatabaseKind.MySql)
        {
            return ToDatabaseConfig(ds);
        }

        if (ds.DatabaseKind == DatabaseKind.Sqlite)
        {
            throw new InvalidOperationException(
                "业务数据不能使用本地 SQLite。请在「数据源设置」中配置 MySQL（或受支持的远程库）。");
        }

        throw new InvalidOperationException(
            $"网络业务库日常读写目前仅支持 MySQL（当前为 {ds.DatabaseKind}）。字典等 ADO 路径仍可按数据源类型使用。");
    }

    public static DatabaseConfig ToDatabaseConfig(DataSourceSettings ds)
    {
        if (ds.DatabaseKind == DatabaseKind.Sqlite)
        {
            return LocalSqliteConfig();
        }

        if (ds.DatabaseKind != DatabaseKind.MySql)
        {
            throw new InvalidOperationException(
                $"Nets EF 连接仅支持 MySQL，当前为 {ds.DatabaseKind}。");
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
            Sqlite = new SqliteConfig { Path = DevtoyPaths.LocalDbPath },
            Mysql = mysql,
        };
    }

    public static DataSourceSettings FromDatabaseConfig(DatabaseConfig config)
    {
        config.Normalize();
        if (config.IsSqlite)
        {
            return new DataSourceSettings
            {
                DatabaseKind = DatabaseKind.Sqlite,
                Database = DevtoyPaths.LocalDbPath,
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
