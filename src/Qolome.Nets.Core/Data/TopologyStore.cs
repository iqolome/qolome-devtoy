using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qolome.Devtoy.Core.Models;
using Qolome.Devtoy.Core.Services;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

/// <summary>
/// 拓扑存储：
/// <list type="bullet">
/// <item>打开：本地 SQLite（及 JSON 缓存）→ 没有则拉远程 → 都没有则空白</item>
/// <item>保存：只写本地 SQLite + JSON 缓存</item>
/// <item>云同步：把当前环境拓扑从本地推到「数据源设置」远程库</item>
/// </list>
/// </summary>
public sealed class TopologyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DirectoryPath => Path.Combine(
        AppDatabase.DataDirectory,
        "topology");

    public async Task<TopologyDocument> LoadAsync(int environmentId, IReadOnlySet<int> validDeviceIds)
    {
        var local = await LoadLocalAsync(environmentId).ConfigureAwait(false);
        if (local.Exists)
        {
            var document = local.Document!;
            document.EnvironmentId = environmentId;
            Normalize(document, validDeviceIds);
            return document;
        }

        var remote = await TryReadRemoteAsync(environmentId).ConfigureAwait(false);
        if (remote.Exists)
        {
            var document = remote.Document!;
            document.EnvironmentId = environmentId;
            Normalize(document, validDeviceIds);
            await SaveLocalAsync(document).ConfigureAwait(false);
            return document;
        }

        var blank = new TopologyDocument { EnvironmentId = environmentId };
        Normalize(blank, validDeviceIds);
        return blank;
    }

    /// <summary>仅保存到本地 SQLite + JSON 缓存，不推远程。</summary>
    public Task SaveAsync(TopologyDocument document) => SaveLocalAsync(document);

    /// <summary>将本地已保存的拓扑推送到远程数据源；成功后可用于「云端已同步」状态。</summary>
    public async Task PushToRemoteAsync(TopologyDocument document)
    {
        if (document.EnvironmentId <= 0)
        {
            throw new ScanException("请先选择网络环境。");
        }

        Normalize(document, null);
        if (!IsUsable(document.UpdatedAtUtc))
        {
            document.UpdatedAtUtc = DateTime.UtcNow;
        }

        await SaveLocalAsync(document).ConfigureAwait(false);
        await WriteRemoteAsync(document).ConfigureAwait(false);
    }

    public async Task<bool> IsRemoteCurrentAsync(int environmentId, DateTime? localUpdatedAt)
    {
        if (environmentId <= 0 || !IsUsable(localUpdatedAt))
        {
            return false;
        }

        var remote = await TryReadRemoteAsync(environmentId).ConfigureAwait(false);
        if (!remote.Exists)
        {
            return false;
        }

        return remote.Timestamp >= AsUtc(localUpdatedAt!.Value).AddSeconds(-2);
    }

    public static void DeleteLocal(int environmentId)
    {
        var path = FilePath(environmentId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static void Normalize(TopologyDocument document, IReadOnlySet<int>? validDeviceIds)
    {
        TopologyMetrics.Apply(document.CanvasWidth, document.CanvasHeight);
        var kept = new List<TopologyNodeDto>();
        var usedIds = new HashSet<int>();
        foreach (var node in document.Nodes)
        {
            node.Kind = TopologyNodeKinds.Normalize(node.Kind, node.DeviceId);
            if (node.IsDevice)
            {
                if (node.DeviceId <= 0)
                {
                    continue;
                }

                if (validDeviceIds is not null && !validDeviceIds.Contains(node.DeviceId))
                {
                    continue;
                }

                node.Id = node.DeviceId;
                node.Kind = TopologyNodeKinds.Device;
                if (!usedIds.Add(node.Id))
                {
                    continue;
                }

                kept.Add(node);
                continue;
            }

            node.DeviceId = 0;
            node.IspKey = node.IsContainer ? null : IspPresets.Normalize(node.IspKey, node.Label);
            if (node.IsContainer)
            {
                node.Kind = TopologyNodeKinds.Container;
                if (string.IsNullOrWhiteSpace(node.Label))
                {
                    node.Label = "容器";
                }
                else
                {
                    node.Label = node.Label.Trim();
                }

                node.Width = TopologyMetrics.WidthOf(node);
                node.Height = TopologyMetrics.HeightOf(node);
            }
            else
            {
                node.Kind = TopologyNodeKinds.Isp;
                node.IspKey = IspPresets.Normalize(node.IspKey, node.Label);
                if (IspPresets.IsCarrier(node.IspKey))
                {
                    node.Label = IspPresets.DefaultLabel(node.IspKey);
                }
                else if (string.IsNullOrWhiteSpace(node.Label))
                {
                    node.Label = IspPresets.DefaultLabel(IspPresets.Custom);
                }

                node.Width = 0;
                node.Height = 0;
            }

            if (node.Id >= 0 || !usedIds.Add(node.Id))
            {
                node.Id = NextNegativeId(usedIds);
                usedIds.Add(node.Id);
            }

            kept.Add(node);
        }

        foreach (var node in kept)
        {
            TopologyMetrics.EnsureFits(node, node.X, node.Y);
            var (x, y) = TopologyMetrics.ClampPosition(node, node.X, node.Y);
            node.X = x;
            node.Y = y;
        }

        document.Nodes = kept;
        TopologyMetrics.CopyTo(document);
        var validNodeIds = kept.Select(n => n.Id).ToHashSet();
        var containerIds = kept.Where(n => n.IsContainer).Select(n => n.Id).ToHashSet();
        foreach (var node in kept)
        {
            if (node.IsContainer || node.ContainerId is not int parent || !containerIds.Contains(parent))
            {
                node.ContainerId = null;
            }
        }
        var links = document.Links
            .Select(link =>
            {
                if (link.FromNodeId == 0 && link.FromDeviceId != 0)
                {
                    link.FromNodeId = link.FromDeviceId;
                }

                if (link.ToNodeId == 0 && link.ToDeviceId != 0)
                {
                    link.ToNodeId = link.ToDeviceId;
                }

                if (link.FromNodeId > link.ToNodeId)
                {
                    (link.FromNodeId, link.ToNodeId) = (link.ToNodeId, link.FromNodeId);
                    link.Curve = -link.Curve;
                }

                if (double.IsNaN(link.Curve) || double.IsInfinity(link.Curve))
                {
                    link.Curve = 0;
                }

                link.Curve = Math.Clamp(link.Curve, -480, 480);

                link.FromDeviceId = link.FromNodeId;
                link.ToDeviceId = link.ToNodeId;
                link.Kind = TopologyLinkKinds.Normalize(link.Kind);
                if (string.IsNullOrWhiteSpace(link.Color))
                {
                    link.Color = null;
                }

                if (string.IsNullOrWhiteSpace(link.Label))
                {
                    link.Label = null;
                }

                return link;
            })
            .Where(l => validNodeIds.Contains(l.FromNodeId)
                        && validNodeIds.Contains(l.ToNodeId)
                        && l.FromNodeId != l.ToNodeId)
            .ToList();
        AssignLinkIds(links);
        document.Links = links;
    }

    private static async Task<TopologySide> LoadLocalAsync(int environmentId)
    {
        var file = await ReadFileAsync(environmentId).ConfigureAwait(false);
        var db = await ReadLocalDbAsync(environmentId).ConfigureAwait(false);
        if (!db.Exists && !file.Exists)
        {
            return TopologySide.Missing;
        }

        if (!db.Exists)
        {
            await WriteLocalDbAsync(file.Document!).ConfigureAwait(false);
            return file;
        }

        if (!file.Exists)
        {
            await WriteFileAsync(db.Document!).ConfigureAwait(false);
            return db;
        }

        if (db.Timestamp >= file.Timestamp)
        {
            if (db.Timestamp > file.Timestamp)
            {
                await WriteFileAsync(db.Document!).ConfigureAwait(false);
            }

            return db;
        }

        await WriteLocalDbAsync(file.Document!).ConfigureAwait(false);
        return file;
    }

    private async Task SaveLocalAsync(TopologyDocument document)
    {
        if (document.EnvironmentId <= 0)
        {
            throw new ScanException("请先选择网络环境。");
        }

        Normalize(document, null);
        if (!IsUsable(document.UpdatedAtUtc))
        {
            document.UpdatedAtUtc = DateTime.UtcNow;
        }

        await WriteLocalDbAsync(document).ConfigureAwait(false);
        await WriteFileAsync(document).ConfigureAwait(false);
    }

    private static async Task<TopologySide> ReadFileAsync(int environmentId)
    {
        var path = FilePath(environmentId);
        if (!File.Exists(path))
        {
            return TopologySide.Missing;
        }

        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<TopologyDocument>(json, JsonOptions)
            ?? new TopologyDocument { EnvironmentId = environmentId };
        document.EnvironmentId = environmentId;
        var timestamp = ResolveTimestamp(document.UpdatedAtUtc, File.GetLastWriteTimeUtc(path));
        document.UpdatedAtUtc = timestamp;
        return new TopologySide(true, timestamp, document);
    }

    private static async Task<TopologySide> ReadLocalDbAsync(int environmentId)
    {
        try
        {
            await using var db = AppDatabase.CreateContext();
            var row = await db.Topologies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.NetworkEnvironmentId == environmentId)
                .ConfigureAwait(false);
            return ParseRow(environmentId, row);
        }
        catch
        {
            return TopologySide.Missing;
        }
    }

    private static async Task WriteLocalDbAsync(TopologyDocument document)
    {
        await AppDatabase.EnsureTopologyStorageAsync().ConfigureAwait(false);
        await using var db = AppDatabase.CreateContext();
        await UpsertDbRowAsync(db, document).ConfigureAwait(false);
    }

    private static async Task WriteFileAsync(TopologyDocument document)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(FilePath(document.EnvironmentId), json).ConfigureAwait(false);
    }

    private static async Task<TopologySide> TryReadRemoteAsync(int environmentId)
    {
        DataSourceSettings? remote;
        try
        {
            remote = NetsRuntime.GetRemoteDataSource?.Invoke();
        }
        catch
        {
            return TopologySide.Missing;
        }

        if (remote is null)
        {
            return TopologySide.Missing;
        }

        try
        {
            return remote.DatabaseKind switch
            {
                DatabaseKind.MySql => await ReadRemoteMySqlAsync(remote, environmentId).ConfigureAwait(false),
                DatabaseKind.PostgreSql => await ReadRemotePostgreSqlAsync(remote, environmentId).ConfigureAwait(false),
                _ => TopologySide.Missing,
            };
        }
        catch
        {
            return TopologySide.Missing;
        }
    }

    private static async Task WriteRemoteAsync(TopologyDocument document)
    {
        var remote = NetsRuntime.GetRemoteDataSource?.Invoke()
            ?? throw new ScanException("未配置远程数据源，请先在「配置管理 → 数据源设置」中配置。");

        try
        {
            switch (remote.DatabaseKind)
            {
                case DatabaseKind.MySql:
                    await WriteRemoteMySqlAsync(remote, document).ConfigureAwait(false);
                    return;
                case DatabaseKind.PostgreSql:
                    await WriteRemotePostgreSqlAsync(remote, document).ConfigureAwait(false);
                    return;
                case DatabaseKind.Sqlite:
                    throw new ScanException("远程目标不能为本地 SQLite。");
                default:
                    throw new ScanException($"拓扑云同步暂不支持 {remote.DatabaseKind}，请使用 MySQL 或 PostgreSQL。");
            }
        }
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "拓扑云同步"));
        }
    }

    private static async Task<TopologySide> ReadRemoteMySqlAsync(DataSourceSettings remote, int environmentId)
    {
        var config = NetsDataSourceBridge.ToDatabaseConfig(remote);
        await AppDatabase.PrepareSchemaAsync(config).ConfigureAwait(false);
        await using var db = AppDatabase.CreateContext(config);
        var row = await db.Topologies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.NetworkEnvironmentId == environmentId)
            .ConfigureAwait(false);
        return ParseRow(environmentId, row);
    }

    private static async Task WriteRemoteMySqlAsync(DataSourceSettings remote, TopologyDocument document)
    {
        var config = NetsDataSourceBridge.ToDatabaseConfig(remote);
        await AppDatabase.PrepareSchemaAsync(config).ConfigureAwait(false);
        await using var db = AppDatabase.CreateContext(config);
        await UpsertDbRowAsync(db, document).ConfigureAwait(false);
    }

    private static async Task<TopologySide> ReadRemotePostgreSqlAsync(DataSourceSettings remote, int environmentId)
    {
        await using DbConnection conn = DbConnectionFactory.Create(remote);
        await conn.OpenAsync().ConfigureAwait(false);
        if (conn is not Npgsql.NpgsqlConnection npg)
        {
            throw new InvalidOperationException("PostgreSQL 需要 Npgsql 连接。");
        }

        await NetsPostgreSqlInventorySync.EnsureSchemaAsync(npg).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Json", "UpdatedAtUtc" FROM devtoy_network_topology
            WHERE "NetworkEnvironmentId" = @id
            """;
        AddParam(cmd, "@id", environmentId);
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return TopologySide.Missing;
        }

        var json = reader.IsDBNull(0) ? "" : reader.GetString(0);
        if (string.IsNullOrWhiteSpace(json))
        {
            return TopologySide.Missing;
        }

        var updated = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1);
        var document = JsonSerializer.Deserialize<TopologyDocument>(json, JsonOptions)
            ?? new TopologyDocument { EnvironmentId = environmentId };
        document.EnvironmentId = environmentId;
        var timestamp = ResolveTimestamp(updated, document.UpdatedAtUtc);
        document.UpdatedAtUtc = timestamp;
        return new TopologySide(true, timestamp, document);
    }

    private static async Task WriteRemotePostgreSqlAsync(DataSourceSettings remote, TopologyDocument document)
    {
        await using DbConnection conn = DbConnectionFactory.Create(remote);
        await conn.OpenAsync().ConfigureAwait(false);
        if (conn is not Npgsql.NpgsqlConnection npg)
        {
            throw new InvalidOperationException("PostgreSQL 需要 Npgsql 连接。");
        }

        await NetsPostgreSqlInventorySync.EnsureSchemaAsync(npg).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var timestamp = ResolveTimestamp(document.UpdatedAtUtc, DateTime.UtcNow);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devtoy_network_topology ("NetworkEnvironmentId", "Json", "UpdatedAtUtc")
            VALUES (@id, @json, @updated)
            ON CONFLICT ("NetworkEnvironmentId") DO UPDATE
            SET "Json" = EXCLUDED."Json", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """;
        AddParam(cmd, "@id", document.EnvironmentId);
        AddParam(cmd, "@json", json);
        AddParam(cmd, "@updated", timestamp);
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static TopologySide ParseRow(int environmentId, NetworkTopology? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Json))
        {
            return TopologySide.Missing;
        }

        var document = JsonSerializer.Deserialize<TopologyDocument>(row.Json, JsonOptions)
            ?? new TopologyDocument { EnvironmentId = environmentId };
        document.EnvironmentId = environmentId;
        var timestamp = ResolveTimestamp(row.UpdatedAtUtc, document.UpdatedAtUtc);
        document.UpdatedAtUtc = timestamp;
        return new TopologySide(true, timestamp, document);
    }

    private static async Task UpsertDbRowAsync(NetsDbContext db, TopologyDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var timestamp = ResolveTimestamp(document.UpdatedAtUtc, DateTime.UtcNow);
        var row = await db.Topologies
            .FirstOrDefaultAsync(x => x.NetworkEnvironmentId == document.EnvironmentId)
            .ConfigureAwait(false);
        if (row is null)
        {
            db.Topologies.Add(new NetworkTopology
            {
                NetworkEnvironmentId = document.EnvironmentId,
                Json = json,
                UpdatedAtUtc = timestamp
            });
        }
        else
        {
            row.Json = json;
            row.UpdatedAtUtc = timestamp;
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static DateTime ResolveTimestamp(DateTime? preferred, DateTime? fallback)
    {
        if (IsUsable(preferred))
        {
            return AsUtc(preferred!.Value);
        }

        if (IsUsable(fallback))
        {
            return AsUtc(fallback!.Value);
        }

        return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
    }

    private static bool IsUsable(DateTime? value) =>
        value is { Year: >= 2000 };

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void AssignLinkIds(List<TopologyLinkDto> links)
    {
        var used = new HashSet<int>();
        var next = 1;
        foreach (var link in links)
        {
            if (link.Id > 0)
            {
                next = Math.Max(next, link.Id + 1);
            }
        }

        foreach (var link in links)
        {
            if (link.Id <= 0 || !used.Add(link.Id))
            {
                link.Id = next++;
                used.Add(link.Id);
            }
        }
    }

    private static int NextNegativeId(HashSet<int> used)
    {
        var id = -1;
        while (used.Contains(id))
        {
            id--;
        }

        return id;
    }

    private static string FilePath(int environmentId) =>
        Path.Combine(DirectoryPath, $"env-{environmentId}.json");

    private readonly record struct TopologySide(bool Exists, DateTime Timestamp, TopologyDocument? Document)
    {
        public static TopologySide Missing => new(false, DateTime.MinValue, null);
    }
}
