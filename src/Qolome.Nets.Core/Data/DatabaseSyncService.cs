using Qolome.Devtoy.Core.Models;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

public sealed class DatabaseSyncService
{
    public Task<string> SyncLocalToRemoteAsync(DataSourceSettings remote, IProgress<ScanProgress>? progress = null) =>
        SyncAsync(NetsDataSourceBridge.LocalSqliteConfig(), NetsDataSourceBridge.ToDatabaseConfig(remote), progress);

    public Task<string> SyncRemoteToLocalAsync(DataSourceSettings remote, IProgress<ScanProgress>? progress = null) =>
        SyncAsync(NetsDataSourceBridge.ToDatabaseConfig(remote), NetsDataSourceBridge.LocalSqliteConfig(), progress);

    public async Task<string> SyncSqliteToMySqlAsync(DatabaseConfig config, IProgress<ScanProgress>? progress = null)
    {
        config.Normalize();
        return await SyncAsync(SqliteSide(config), MySqlSide(config), progress).ConfigureAwait(false);
    }

    public async Task<string> SyncMySqlToSqliteAsync(DatabaseConfig config, IProgress<ScanProgress>? progress = null)
    {
        config.Normalize();
        return await SyncAsync(MySqlSide(config), SqliteSide(config), progress).ConfigureAwait(false);
    }

    public async Task<string> SyncAsync(
        DatabaseConfig source,
        DatabaseConfig target,
        IProgress<ScanProgress>? progress = null)
    {
        source.Normalize();
        target.Normalize();
        if (source.IsMySql == target.IsMySql)
        {
            throw new ScanException("同步需要一边为 SQLite、另一边为 MySQL。");
        }

        try
        {
            Report(progress, 5, "准备源库 Schema…");
            await AppDatabase.PrepareSchemaAsync(source).ConfigureAwait(false);
            Report(progress, 15, "准备目标库 Schema…");
            await AppDatabase.PrepareSchemaAsync(target).ConfigureAwait(false);

            await using var from = AppDatabase.CreateContext(source);
            await using var to = AppDatabase.CreateContext(target);

            Report(progress, 30, "读取源库存…");
            var snapshot = await InventorySnapshot.LoadAsync(from).ConfigureAwait(false);

            Report(progress, 55, $"写入目标库存（{snapshot.Summary}）…");
            await snapshot.ReplaceAsync(to).ConfigureAwait(false);

            // OUI 属于本机厂商库，由「从 IEEE 更新」维护，不随网络资产覆盖同步。
            Report(progress, 100, "同步完成");
            return $"已同步 {snapshot.Summary}。{AppDatabase.DescribeConnection(source)} → {AppDatabase.DescribeConnection(target)}";
        }
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "库存同步"));
        }
    }

    private static void Report(IProgress<ScanProgress>? progress, int percent, string message) =>
        progress?.Report(new ScanProgress(percent, 100, message));

    private static DatabaseConfig SqliteSide(DatabaseConfig config)
    {
        config.Normalize();
        return new DatabaseConfig
        {
            Provider = DatabaseProviders.Sqlite,
            Sqlite = new SqliteConfig { Path = config.Sqlite.Path },
            Mysql = CloneMysql(config.Mysql)
        };
    }

    private static DatabaseConfig MySqlSide(DatabaseConfig config)
    {
        config.Normalize();
        return new DatabaseConfig
        {
            Provider = DatabaseProviders.MySql,
            Sqlite = new SqliteConfig { Path = config.Sqlite.Path },
            Mysql = CloneMysql(config.Mysql)
        };
    }

    private static MySqlConfig CloneMysql(MySqlConfig mysql) => new()
    {
        Host = mysql.Host,
        Port = mysql.Port,
        Database = mysql.Database,
        User = mysql.User,
        Password = mysql.Password
    };
}
