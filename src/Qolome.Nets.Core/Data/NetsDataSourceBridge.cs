using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;

namespace Qolome.Nets.Core.Data;

/// <summary>Devtoy <see cref="DataSourceSettings"/> 与 Nets <see cref="DatabaseConfig"/> 互转。</summary>
public static class NetsDataSourceBridge
{
    public static DatabaseConfig LocalSqliteConfig() => new()
    {
        Provider = DatabaseProviders.Sqlite,
        Sqlite = new SqliteConfig { Path = DevtoyPaths.LocalDbPath },
    };

    public static DatabaseConfig ToDatabaseConfig(DataSourceSettings ds)
    {
        if (ds.DatabaseKind == DatabaseKind.Sqlite)
        {
            return LocalSqliteConfig();
        }

        if (ds.DatabaseKind != DatabaseKind.MySql)
        {
            throw new InvalidOperationException(
                $"Nets EF 连接仅支持 MySQL / 本地 SQLite，当前为 {ds.DatabaseKind}。");
        }

        var mysql = new MySqlConfig
        {
            Host = string.IsNullOrWhiteSpace(ds.Host) ? "127.0.0.1" : ds.Host.Trim(),
            Port = int.TryParse(ds.Port, out var port) && port > 0 ? port : 3306,
            Database = string.IsNullOrWhiteSpace(ds.Database) ? "qolome_nets" : ds.Database.Trim(),
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