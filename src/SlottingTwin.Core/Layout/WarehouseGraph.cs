namespace SlottingTwin.Core.Layout;

/// <summary>
/// Walkable graph of a <see cref="WarehouseLayout"/>. Every aisle is a column of nodes ("slots"):
/// slot 0 joins the front cross-aisle, slots 1..B are the bays, slot B+1 joins the back cross-aisle.
/// Both rack sides and all levels of a bay share the bay's node: walking distance ignores side and height.
/// </summary>
/// <remarks>Shortest-path rows are computed lazily with Dijkstra and cached, so an instance is not thread-safe.</remarks>
public sealed class WarehouseGraph
{
    private readonly List<(int To, double Length)>[] _edges;
    private readonly (double[] Dist, int[] Prev)?[] _rows;
    private readonly int _slotsPerAisle;

    public WarehouseGraph(WarehouseLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var errors = layout.Validate();
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid layout '{layout.Name}': {string.Join(" ", errors)}", nameof(layout));

        Layout = layout;
        _slotsPerAisle = layout.BaysPerAisle + 2;
        NodeCount = layout.Aisles * _slotsPerAisle;
        _edges = new List<(int, double)>[NodeCount];
        for (var i = 0; i < NodeCount; i++) _edges[i] = [];
        _rows = new (double[], int[])?[NodeCount];

        for (var aisle = 0; aisle < layout.Aisles; aisle++)
        {
            for (var slot = 0; slot < BackSlot; slot++) Connect(NodeAt(aisle, slot), NodeAt(aisle, slot + 1), layout.BayWidth);
            if (aisle == 0) continue;
            Connect(Front(aisle - 1), Front(aisle), layout.AisleSpacing);
            if (layout.HasBackCrossAisle) Connect(Back(aisle - 1), Back(aisle), layout.AisleSpacing);
        }

        Depot = Front(layout.DepotAisle);
        Locations = CreateLocations();
    }

    public WarehouseLayout Layout { get; }
    public int NodeCount { get; }
    public int Depot { get; }

    /// <summary>All storage locations, ordered by aisle, bay, level and side (the "address" order).</summary>
    public IReadOnlyList<StorageLocation> Locations { get; }

    public int BackSlot => _slotsPerAisle - 1;

    public int NodeAt(int aisle, int slot) => aisle * _slotsPerAisle + slot;
    public int Front(int aisle) => NodeAt(aisle, 0);
    public int Back(int aisle) => NodeAt(aisle, BackSlot);
    public int AisleOf(int node) => node / _slotsPerAisle;
    public int SlotOf(int node) => node % _slotsPerAisle;

    public bool IsPickNode(int node)
    {
        if ((uint)node >= (uint)NodeCount) return false;
        var slot = SlotOf(node);
        return slot > 0 && slot < BackSlot;
    }

    /// <summary>Position in metres: x across the aisles, y from the front cross-aisle towards the back.</summary>
    public (double X, double Y) Position(int node) => (AisleOf(node) * Layout.AisleSpacing, SlotOf(node) * Layout.BayWidth);

    public double Distance(int from, int to)
    {
        CheckNode(to);
        return Row(from).Dist[to];
    }

    /// <summary>Pairwise shortest walking distances between <paramref name="nodes"/>; one Dijkstra row per node.</summary>
    public double[,] DistanceMatrix(IReadOnlyList<int> nodes)
    {
        var matrix = new double[nodes.Count, nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            var row = Row(nodes[i]).Dist;
            for (var j = 0; j < nodes.Count; j++) matrix[i, j] = row[nodes[j]];
        }
        return matrix;
    }

    /// <summary>Total length when walking the shortest path between each pair of consecutive waypoints.</summary>
    public double PathLength(IReadOnlyList<int> waypoints)
    {
        var total = 0.0;
        for (var i = 1; i < waypoints.Count; i++) total += Distance(waypoints[i - 1], waypoints[i]);
        return total;
    }

    /// <summary>Expands waypoints into the full node-by-node walk (for drawing).</summary>
    public IReadOnlyList<int> ExpandPath(IReadOnlyList<int> waypoints)
    {
        var path = new List<int>();
        if (waypoints.Count == 0) return path;
        CheckNode(waypoints[0]);
        path.Add(waypoints[0]);
        for (var i = 1; i < waypoints.Count; i++)
        {
            var (from, to) = (waypoints[i - 1], waypoints[i]);
            CheckNode(to);
            var prev = Row(from).Prev;
            var segmentStart = path.Count;
            for (var node = to; node != from; node = prev[node]) path.Add(node);
            path.Reverse(segmentStart, path.Count - segmentStart);
        }
        return path;
    }

    private (double[] Dist, int[] Prev) Row(int source)
    {
        CheckNode(source);
        if (_rows[source] is { } cached) return cached;

        var dist = new double[NodeCount];
        var prev = new int[NodeCount];
        Array.Fill(dist, double.PositiveInfinity);
        Array.Fill(prev, -1);
        dist[source] = 0;
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var node, out var d))
        {
            if (d > dist[node]) continue; // stale queue entry
            foreach (var (to, length) in _edges[node])
            {
                var candidate = d + length;
                if (candidate >= dist[to]) continue;
                dist[to] = candidate;
                prev[to] = node;
                queue.Enqueue(to, candidate);
            }
        }

        var row = (dist, prev);
        _rows[source] = row;
        return row;
    }

    private void Connect(int a, int b, double length)
    {
        _edges[a].Add((b, length));
        _edges[b].Add((a, length));
    }

    private void CheckNode(int node)
    {
        if ((uint)node >= (uint)NodeCount)
            throw new ArgumentOutOfRangeException(nameof(node), node, $"Node must be in [0, {NodeCount}).");
    }

    private StorageLocation[] CreateLocations()
    {
        var locations = new List<StorageLocation>(Layout.LocationCount);
        for (var aisle = 0; aisle < Layout.Aisles; aisle++)
        for (var bay = 0; bay < Layout.BaysPerAisle; bay++)
        for (var level = 0; level < Layout.Levels.Count; level++)
        foreach (var side in (RackSide[])[RackSide.Left, RackSide.Right])
        {
            var id = $"A{aisle + 1:00}-B{bay + 1:00}-L{level + 1}-{(side == RackSide.Left ? 'L' : 'R')}";
            locations.Add(new StorageLocation(id, aisle, bay, level, side, NodeAt(aisle, bay + 1), Layout.Levels[level]));
        }
        return [.. locations];
    }
}
