namespace Qolome.Nets.Core.Data;

/// <summary>
/// 分层布局：按到根节点的连接距离分级，并用重心法 + 邻接交换减少层间连线交叉。
/// </summary>
public static class TopologyLayerLayout
{
    private const int DummyIdBase = -1_000_000;
    private const int MaxSweeps = 16;
    private const int MaxTransposePasses = 48;

    public static Dictionary<int, int> AssignLayers(
        IReadOnlyList<TopologyNodeDto> nodes,
        IReadOnlyList<TopologyLinkDto> links)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var node in nodes)
        {
            adj[node.Id] = [];
        }

        foreach (var (from, to) in UniqueEdges(nodes, links))
        {
            adj[from].Add(to);
            adj[to].Add(from);
        }

        var layer = new Dictionary<int, int>();
        var queue = new Queue<int>();
        var roots = nodes.Where(n => n.IsIsp).Select(n => n.Id).ToList();
        if (roots.Count == 0)
        {
            roots = nodes
                .OrderByDescending(n => adj.GetValueOrDefault(n.Id)?.Count ?? 0)
                .ThenBy(n => n.Id)
                .Select(n => n.Id)
                .Take(1)
                .ToList();
        }

        void Seed(int id, int depth)
        {
            if (layer.ContainsKey(id))
            {
                return;
            }

            layer[id] = depth;
            queue.Enqueue(id);
        }

        foreach (var root in roots)
        {
            Seed(root, 0);
        }

        Drain(queue, adj, layer);
        foreach (var node in nodes)
        {
            if (layer.ContainsKey(node.Id))
            {
                continue;
            }

            var depth = 0;
            if (layer.Count > 0)
            {
                var nearest = nodes
                    .Where(n => layer.ContainsKey(n.Id))
                    .OrderBy(n => Distance2(n, node))
                    .ThenBy(n => n.Id)
                    .First();
                depth = layer[nearest.Id];
            }

            Seed(node.Id, depth);
            Drain(queue, adj, layer);
        }

        return layer;
    }

    /// <param name="orderAlongX">
    /// 层内主方向：上下分层时按 X 排左右；左右分层时按 Y 排上下。
    /// </param>
    public static List<List<TopologyNodeDto>> OrderLayers(
        IReadOnlyList<TopologyNodeDto> nodes,
        IReadOnlyList<TopologyLinkDto> links,
        IReadOnlyDictionary<int, int> layerById,
        bool orderAlongX)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        var byId = nodes.ToDictionary(n => n.Id);
        var layers = SeedLayerBuckets(nodes, layerById, orderAlongX);
        var adj = BuildLayerAdjacency(nodes, links, layerById, layers);
        MinimizeCrossings(layers, adj);

        return layers
            .Select(layer => layer.Where(byId.ContainsKey).Select(id => byId[id]).ToList())
            .Where(layer => layer.Count > 0)
            .ToList();
    }

    private static List<int>[] SeedLayerBuckets(
        IReadOnlyList<TopologyNodeDto> nodes,
        IReadOnlyDictionary<int, int> layerById,
        bool orderAlongX)
    {
        var maxLayer = layerById.Values.DefaultIfEmpty(0).Max();
        var layers = Enumerable.Range(0, maxLayer + 1).Select(_ => new List<int>()).ToArray();
        foreach (var id in nodes
                     .OrderBy(n => n.IsIsp ? 0 : 1)
                     .ThenBy(n => orderAlongX ? n.X : n.Y)
                     .ThenBy(n => orderAlongX ? n.Y : n.X)
                     .ThenBy(n => n.Id)
                     .Select(node => node.Id)
                     .Where(id => layerById.GetValueOrDefault(id, 0) is var depth && depth >= 0
                                  && depth < layers.Length))
        {
            layers[layerById.GetValueOrDefault(id, 0)].Add(id);
        }

        return layers;
    }

    private static Dictionary<int, List<int>> BuildLayerAdjacency(
        IReadOnlyList<TopologyNodeDto> nodes,
        IReadOnlyList<TopologyLinkDto> links,
        IReadOnlyDictionary<int, int> layerById,
        List<int>[] layers)
    {
        var adj = new Dictionary<int, List<int>>();
        foreach (var node in nodes)
        {
            adj[node.Id] = [];
        }

        var dummyId = DummyIdBase;
        foreach (var (from, to) in UniqueEdges(nodes, links))
        {
            dummyId = ConnectAcrossLayers(adj, layers, layerById, from, to, dummyId);
        }

        return adj;
    }

    private static int ConnectAcrossLayers(
        Dictionary<int, List<int>> adj,
        List<int>[] layers,
        IReadOnlyDictionary<int, int> layerById,
        int from,
        int to,
        int dummyId)
    {
        var fromLayer = layerById.GetValueOrDefault(from, 0);
        var toLayer = layerById.GetValueOrDefault(to, 0);
        if (fromLayer == toLayer)
        {
            Connect(adj, from, to);
            return dummyId;
        }

        var (upper, lower) = fromLayer < toLayer ? (from, to) : (to, from);
        var upperLayer = Math.Min(fromLayer, toLayer);
        var lowerLayer = Math.Max(fromLayer, toLayer);
        var prev = upper;
        for (var depth = upperLayer + 1; depth < lowerLayer; depth++)
        {
            var dummy = dummyId--;
            layers[depth].Add(dummy);
            adj[dummy] = [];
            Connect(adj, prev, dummy);
            prev = dummy;
        }

        Connect(adj, prev, lower);
        return dummyId;
    }

    private static void MinimizeCrossings(List<int>[] layers, Dictionary<int, List<int>> adj)
    {
        if (layers.Length == 0)
        {
            return;
        }

        var best = Clone(layers);
        var bestScore = Score(layers, adj);
        if (bestScore.Crossings == 0 && bestScore.Span == 0)
        {
            return;
        }

        for (var sweep = 0; sweep < MaxSweeps; sweep++)
        {
            RunBarycenterSweep(layers, adj, sweep % 2 == 0);
            Transpose(layers, adj);
            var score = Score(layers, adj);
            if (!IsBetter(score, bestScore))
            {
                continue;
            }

            bestScore = score;
            best = Clone(layers);
            if (bestScore.Crossings == 0 && bestScore.Span == 0)
            {
                break;
            }
        }

        for (var i = 0; i < layers.Length; i++)
        {
            layers[i] = best[i];
        }
    }

    private static void RunBarycenterSweep(List<int>[] layers, Dictionary<int, List<int>> adj, bool topDown)
    {
        if (topDown)
        {
            for (var i = 1; i < layers.Length; i++)
            {
                SortByBarycenter(layers[i], layers[i - 1], adj);
            }

            return;
        }

        for (var i = layers.Length - 2; i >= 0; i--)
        {
            SortByBarycenter(layers[i], layers[i + 1], adj);
        }
    }

    private static void SortByBarycenter(
        List<int> layer,
        IReadOnlyList<int> reference,
        IReadOnlyDictionary<int, List<int>> adj)
    {
        if (layer.Count <= 1 || reference.Count == 0)
        {
            return;
        }

        var refIndex = ToIndex(reference);
        var currentIndex = ToIndex(layer);
        var ordered = layer
            .Select(id =>
            {
                var bary = (double)currentIndex[id];
                if (adj.TryGetValue(id, out var neighbors))
                {
                    var sum = 0.0;
                    var count = 0;
                    foreach (var neighbor in neighbors)
                    {
                        if (!refIndex.TryGetValue(neighbor, out var index))
                        {
                            continue;
                        }

                        sum += index;
                        count++;
                    }

                    if (count > 0)
                    {
                        bary = sum / count;
                    }
                }

                return (Id: id, Bary: bary, Index: currentIndex[id]);
            })
            .OrderBy(item => item.Bary)
            .ThenBy(item => item.Index)
            .Select(item => item.Id)
            .ToList();

        layer.Clear();
        layer.AddRange(ordered);
    }

    private static void Transpose(List<int>[] layers, Dictionary<int, List<int>> adj)
    {
        var moved = true;
        var guard = 0;
        while (moved && guard++ < MaxTransposePasses)
        {
            moved = false;
            for (var layer = 0; layer < layers.Length; layer++)
            {
                var nodes = layers[layer];
                for (var i = 0; i < nodes.Count - 1; i++)
                {
                    var before = LocalScore(layers, layer, adj);
                    (nodes[i], nodes[i + 1]) = (nodes[i + 1], nodes[i]);
                    var after = LocalScore(layers, layer, adj);
                    if (IsBetter(after, before))
                    {
                        moved = true;
                    }
                    else
                    {
                        (nodes[i], nodes[i + 1]) = (nodes[i + 1], nodes[i]);
                    }
                }
            }
        }
    }

    private static (int Crossings, int Span) Score(List<int>[] layers, Dictionary<int, List<int>> adj)
    {
        var crossings = 0;
        var span = 0;
        for (var i = 0; i < layers.Length; i++)
        {
            if (i + 1 < layers.Length)
            {
                crossings += CountCrossings(layers[i], layers[i + 1], adj);
            }

            span += SameLayerSpan(layers[i], adj);
        }

        return (crossings, span);
    }

    private static (int Crossings, int Span) LocalScore(
        List<int>[] layers,
        int layer,
        Dictionary<int, List<int>> adj)
    {
        var crossings = 0;
        if (layer > 0)
        {
            crossings += CountCrossings(layers[layer - 1], layers[layer], adj);
        }

        if (layer + 1 < layers.Length)
        {
            crossings += CountCrossings(layers[layer], layers[layer + 1], adj);
        }

        return (crossings, SameLayerSpan(layers[layer], adj));
    }

    private static int CountCrossings(
        IReadOnlyList<int> upper,
        IReadOnlyList<int> lower,
        IReadOnlyDictionary<int, List<int>> adj)
    {
        var lowerIndex = ToIndex(lower);
        var pairs = new List<(int U, int V)>();
        for (var i = 0; i < upper.Count; i++)
        {
            if (!adj.TryGetValue(upper[i], out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (lowerIndex.TryGetValue(neighbor, out var j))
                {
                    pairs.Add((i, j));
                }
            }
        }

        var crossings = 0;
        for (var a = 0; a < pairs.Count; a++)
        {
            for (var b = a + 1; b < pairs.Count; b++)
            {
                if ((pairs[a].U - pairs[b].U) * (pairs[a].V - pairs[b].V) < 0)
                {
                    crossings++;
                }
            }
        }

        return crossings;
    }

    private static int SameLayerSpan(IReadOnlyList<int> layer, IReadOnlyDictionary<int, List<int>> adj)
    {
        var index = ToIndex(layer);
        var span = 0;
        foreach (var id in layer)
        {
            if (!adj.TryGetValue(id, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (neighbor <= id || !index.TryGetValue(neighbor, out var other))
                {
                    continue;
                }

                span += Math.Abs(index[id] - other) - 1;
            }
        }

        return Math.Max(0, span);
    }

    private static bool IsBetter((int Crossings, int Span) after, (int Crossings, int Span) before) =>
        after.Crossings < before.Crossings
        || (after.Crossings == before.Crossings && after.Span < before.Span);

    private static List<int>[] Clone(List<int>[] source) =>
        source.Select(layer => layer.ToList()).ToArray();

    private static Dictionary<int, int> ToIndex(IReadOnlyList<int> layer)
    {
        var index = new Dictionary<int, int>(layer.Count);
        for (var i = 0; i < layer.Count; i++)
        {
            index[layer[i]] = i;
        }

        return index;
    }

    private static void Connect(Dictionary<int, List<int>> adj, int a, int b)
    {
        if (a == b)
        {
            return;
        }

        AddNeighbor(adj, a, b);
        AddNeighbor(adj, b, a);
    }

    private static void AddNeighbor(Dictionary<int, List<int>> adj, int from, int to)
    {
        if (!adj.TryGetValue(from, out var list))
        {
            list = [];
            adj[from] = list;
        }

        if (!list.Contains(to))
        {
            list.Add(to);
        }
    }

    private static IEnumerable<(int From, int To)> UniqueEdges(
        IReadOnlyList<TopologyNodeDto> nodes,
        IReadOnlyList<TopologyLinkDto> links)
    {
        var known = nodes.Select(n => n.Id).ToHashSet();
        var seen = new HashSet<(int, int)>();
        foreach (var link in links)
        {
            if (!known.Contains(link.FromNodeId) || !known.Contains(link.ToNodeId))
            {
                continue;
            }

            var from = Math.Min(link.FromNodeId, link.ToNodeId);
            var to = Math.Max(link.FromNodeId, link.ToNodeId);
            if (from == to || !seen.Add((from, to)))
            {
                continue;
            }

            yield return (from, to);
        }
    }

    private static double Distance2(TopologyNodeDto a, TopologyNodeDto b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static void Drain(Queue<int> queue, Dictionary<int, List<int>> adj, Dictionary<int, int> layer)
    {
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adj.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (layer.ContainsKey(neighbor))
                {
                    continue;
                }

                layer[neighbor] = layer[current] + 1;
                queue.Enqueue(neighbor);
            }
        }
    }
}
