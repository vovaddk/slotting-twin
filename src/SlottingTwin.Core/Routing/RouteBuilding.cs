using SlottingTwin.Core.Layout;

namespace SlottingTwin.Core.Routing;

internal static class PickSet
{
    /// <summary>Distinct pick nodes in ascending order, which is aisle by aisle, front to back.</summary>
    public static int[] Normalize(WarehouseGraph graph, IEnumerable<int> pickNodes)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(pickNodes);
        var nodes = pickNodes.Distinct().Order().ToArray();
        foreach (var node in nodes)
            if (!graph.IsPickNode(node)) throw new ArgumentException($"Node {node} is not a pick location.", nameof(pickNodes));
        return nodes;
    }

    /// <summary>Picks grouped per aisle; aisles ascending, picks front to back.</summary>
    public static IReadOnlyList<(int Aisle, int[] Picks)> ByAisle(WarehouseGraph graph, IEnumerable<int> pickNodes) =>
        Normalize(graph, pickNodes).GroupBy(graph.AisleOf).Select(g => (g.Key, g.ToArray())).ToArray();

    public static void RequireBackCrossAisle(WarehouseGraph graph, string strategy)
    {
        if (!graph.Layout.HasBackCrossAisle)
            throw new InvalidOperationException($"The {strategy} heuristic needs a back cross-aisle when picks span several aisles.");
    }
}

/// <summary>Collects waypoints from depot to depot, skipping immediate repeats.</summary>
internal sealed class WaypointBuilder(WarehouseGraph graph)
{
    private readonly List<int> _waypoints = [graph.Depot];

    public WaypointBuilder Visit(int node)
    {
        if (_waypoints[^1] != node) _waypoints.Add(node);
        return this;
    }

    public WaypointBuilder Visit(IEnumerable<int> nodes)
    {
        foreach (var node in nodes) Visit(node);
        return this;
    }

    public PickRoute Build(string strategy)
    {
        Visit(graph.Depot);
        return new PickRoute(strategy, _waypoints, graph.PathLength(_waypoints));
    }
}
