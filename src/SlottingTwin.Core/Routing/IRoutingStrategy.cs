using SlottingTwin.Core.Layout;

namespace SlottingTwin.Core.Routing;

/// <summary>
/// A picker tour. <see cref="Waypoints"/> start and end at the depot and contain every pick node exactly once;
/// they may also contain cross-aisle junctions that force the walk through a specific aisle end.
/// <see cref="Distance"/> is the walk along shortest paths between consecutive waypoints.
/// </summary>
public sealed record PickRoute(string Strategy, IReadOnlyList<int> Waypoints, double Distance);

/// <summary>Strategy pattern: one heuristic for ordering the picks of a single pick list.</summary>
public interface IRoutingStrategy
{
    string Name { get; }

    PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes);
}

public static class RoutingStrategies
{
    public static IReadOnlyList<IRoutingStrategy> All { get; } =
        [new SShapeRouting(), new ReturnRouting(), new LargestGapRouting(), new NearestNeighborRouting(), new TwoOptRouting()];

    public static bool Exists(string? name) => All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the strategy can route multi-aisle pick lists on <paramref name="layout"/> (traversal needs a back cross-aisle).</summary>
    public static bool Supports(string name, Layout.WarehouseLayout layout) =>
        layout.HasBackCrossAisle || !(string.Equals(name, "s-shape", StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(name, "largest-gap", StringComparison.OrdinalIgnoreCase));

    public static IRoutingStrategy Get(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown routing strategy '{name}'.", nameof(name));
}
