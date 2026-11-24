using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qolome.Nets.Core.Models;

namespace Qolome.Nets.Core.Data;

/// <summary>
/// 拓扑存储：权威数据在远程业务库 <c>devtoy_network_topology</c>（经 <see cref="AppDatabase.CreateContext"/>）。
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

    public async Task<TopologyDocument> LoadAsync(int environmentId, IReadOnlySet<int> validDeviceIds)
    {
        var side = await ReadDbAsync(environmentId).ConfigureAwait(false);
        var document = side.Exists
            ? side.Document!
            : new TopologyDocument { EnvironmentId = environmentId };
        document.EnvironmentId = environmentId;
        Normalize(document, validDeviceIds);
        return document;
    }

    /// <summary>保存到远程业务库。</summary>
    public async Task SaveAsync(TopologyDocument document)
    {
        if (document.EnvironmentId <= 0)
        {
            throw new ScanException("请先选择网络环境。");
        }

        Normalize(document, null);
        document.UpdatedAtUtc = DateTime.UtcNow;
        await WriteDbAsync(document).ConfigureAwait(false);
    }

    /// <summary>远程库中的拓扑是否不早于给定时间戳。</summary>
    public async Task<bool> IsRemoteCurrentAsync(int environmentId, DateTime? localUpdatedAt)
    {
        if (environmentId <= 0 || !IsUsable(localUpdatedAt))
        {
            return false;
        }

        var remote = await ReadDbAsync(environmentId).ConfigureAwait(false);
        if (!remote.Exists)
        {
            return false;
        }

        return remote.Timestamp >= AsUtc(localUpdatedAt!.Value).AddSeconds(-2);
    }

    public static void DeleteLocal(int environmentId)
    {
        // 兼容旧调用：业务拓扑已不落本地文件；删除远程行由 InventoryService 处理。
        _ = environmentId;
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

    private static async Task<TopologySide> ReadDbAsync(int environmentId)
    {
        try
        {
            await using var db = AppDatabase.CreateContext();
            var row = await db.Topologies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.NetworkEnvironmentId == environmentId)
                .ConfigureAwait(false);
            return ParseRow(environmentId, row);
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "读取拓扑"));
        }
    }

    private static async Task WriteDbAsync(TopologyDocument document)
    {
        try
        {
            await AppDatabase.EnsureTopologyStorageAsync().ConfigureAwait(false);
            await using var db = AppDatabase.CreateContext();
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
        catch (ScanException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScanException(DbExceptionFormatter.Format(ex, "保存拓扑"));
        }
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

    private readonly record struct TopologySide(bool Exists, DateTime Timestamp, TopologyDocument? Document)
    {
        public static TopologySide Missing => new(false, DateTime.MinValue, null);
    }
}
