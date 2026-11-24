using System.Data.Common;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

/// <summary>
/// 统一本地 SQLite（<see cref="DevtoyPaths.LocalDbPath"/>）与多方言远程库的网络资产同步。
/// </summary>
public sealed class NetworkAssetSyncService
{
    private readonly DatabaseSyncService _efSync = new();

    public async Task<string> SyncLocalToRemoteAsync(
        DataSourceSettings remote,
        IProgress<ScanProgress>? progress = null)
    {
        try
        {
            remote = Normalize(remote);
            var local = NetsDataSourceBridge.LocalSqliteConfig();
            Report(progress, 2, "准备本地 Schema…");
            await AppDatabase.PrepareSchemaAsync(local).ConfigureAwait(false);

            return remote.DatabaseKind switch
            {
                DatabaseKind.MySql => await SyncMySqlLocalToRemoteAsync(remote, local, progress).ConfigureAwait(false),
                DatabaseKind.PostgreSql => await SyncPostgreSqlLocalToRemoteAsync(remote, local, progress).ConfigureAwait(false),
                DatabaseKind.Oracle or DatabaseKind.Odbc => throw UnsupportedDialect(remote.DatabaseKind),
                DatabaseKind.Sqlite => throw new InvalidOperationException("远程目标不能为本地 SQLite。"),
                _ => throw new NotSupportedException($"不支持的远程方言：{remote.DatabaseKind}"),
            };
        }
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "本地 → 远程"));
        }
    }

    public async Task<string> SyncRemoteToLocalAsync(
        DataSourceSettings remote,
        IProgress<ScanProgress>? progress = null)
    {
        try
        {
            remote = Normalize(remote);
            var local = NetsDataSourceBridge.LocalSqliteConfig();
            Report(progress, 2, "准备本地 Schema…");
            await AppDatabase.PrepareSchemaAsync(local).ConfigureAwait(false);

            return remote.DatabaseKind switch
            {
                DatabaseKind.MySql => await _efSync.SyncRemoteToLocalAsync(remote, progress).ConfigureAwait(false),
                DatabaseKind.PostgreSql => await SyncPostgreSqlRemoteToLocalAsync(remote, local, progress).ConfigureAwait(false),
                DatabaseKind.Oracle or DatabaseKind.Odbc => throw UnsupportedDialect(remote.DatabaseKind),
                DatabaseKind.Sqlite => throw new InvalidOperationException("远程源不能为本地 SQLite。"),
                _ => throw new NotSupportedException($"不支持的远程方言：{remote.DatabaseKind}"),
            };
        }
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "远程 → 本地"));
        }
    }

    private static Task<string> SyncMySqlLocalToRemoteAsync(
        DataSourceSettings remote,
        DatabaseConfig local,
        IProgress<ScanProgress>? progress) =>
        new DatabaseSyncService().SyncAsync(local, NetsDataSourceBridge.ToDatabaseConfig(remote), progress);

    private static async Task<string> SyncPostgreSqlLocalToRemoteAsync(
        DataSourceSettings remote,
        DatabaseConfig local,
        IProgress<ScanProgress>? progress)
    {
        Report(progress, 15, "读取本地库存…");
        await using var from = AppDatabase.CreateContext(local);
        var snapshot = await InventorySnapshot.LoadAsync(from).ConfigureAwait(false);

        Report(progress, 35, "连接远程 PostgreSQL…");
        await using DbConnection conn = DbConnectionFactory.Create(remote);
        await conn.OpenAsync().ConfigureAwait(false);
        if (conn is not Npgsql.NpgsqlConnection npg)
        {
            throw new InvalidOperationException("PostgreSQL 同步需要 Npgsql 连接。");
        }

        Report(progress, 50, "准备远程 Schema…");
        await NetsPostgreSqlInventorySync.EnsureSchemaAsync(npg).ConfigureAwait(false);
        Report(progress, 70, $"写入远程库存（{snapshot.Summary}）…");
        await NetsPostgreSqlInventorySync.ReplaceAsync(npg, snapshot).ConfigureAwait(false);
        Report(progress, 100, "同步完成");
        return $"已同步到 PostgreSQL：{snapshot.Summary}。本地 {AppDatabase.DescribeConnection(local)} → {DescribeRemote(remote)}";
    }

    private static async Task<string> SyncPostgreSqlRemoteToLocalAsync(
        DataSourceSettings remote,
        DatabaseConfig local,
        IProgress<ScanProgress>? progress)
    {
        Report(progress, 15, "连接远程 PostgreSQL…");
        await using DbConnection conn = DbConnectionFactory.Create(remote);
        await conn.OpenAsync().ConfigureAwait(false);
        if (conn is not Npgsql.NpgsqlConnection npg)
        {
            throw new InvalidOperationException("PostgreSQL 同步需要 Npgsql 连接。");
        }

        Report(progress, 35, "准备远程 Schema…");
        await NetsPostgreSqlInventorySync.EnsureSchemaAsync(npg).ConfigureAwait(false);
        Report(progress, 50, "读取远程库存…");
        var snapshot = await NetsPostgreSqlInventorySync.LoadAsync(npg).ConfigureAwait(false);
        Report(progress, 75, $"写入本地库存（{snapshot.Summary}）…");
        await using var to = AppDatabase.CreateContext(local);
        await snapshot.ReplaceAsync(to).ConfigureAwait(false);
        Report(progress, 100, "同步完成");
        return $"已从 PostgreSQL 同步：{snapshot.Summary}。{DescribeRemote(remote)} → 本地 {AppDatabase.DescribeConnection(local)}";
    }

    private static void Report(IProgress<ScanProgress>? progress, int percent, string message) =>
        progress?.Report(new ScanProgress(percent, 100, message));

    private static InvalidOperationException UnsupportedDialect(DatabaseKind kind) =>
        new($"首期网络资产远程同步请使用 MySQL 或 PostgreSQL（当前为 {kind}）；其它方言已支持连接测试与 Schema 准备，资产全量同步将于后续版本提供。");

    private static DataSourceSettings Normalize(DataSourceSettings remote)
    {
        if (remote is null)
        {
            throw new ArgumentNullException(nameof(remote));
        }

        if (string.IsNullOrWhiteSpace(remote.Host) && remote.DatabaseKind != DatabaseKind.Sqlite)
        {
            remote.Host = "127.0.0.1";
        }

        return remote;
    }

    private static string DescribeRemote(DataSourceSettings remote) =>
        $"{remote.DatabaseKind} {remote.Username}@{remote.Host}:{remote.Port}/{remote.Database}";
}
