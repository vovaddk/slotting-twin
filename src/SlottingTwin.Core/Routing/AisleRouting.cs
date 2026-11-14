using SlottingTwin.Core.Layout;

namespace SlottingTwin.Core.Routing;

/// <summary>Return: enter each aisle with picks from the front, walk to its farthest pick, come back. O(k log k).</summary>
public sealed class ReturnRouting : IRoutingStrategy
{
    public string Name => "return";

    public PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes)
    {
        var route = new WaypointBuilder(graph);
        foreach (var (aisle, picks) in PickSet.ByAisle(graph, pickNodes))
            route.Visit(graph.Front(aisle)).Visit(picks).Visit(graph.Front(aisle));
        return route.Build(Name);
    }
}

/// <summary>
/// S-shape (traversal): every aisle with picks is walked end to end, alternating direction.
/// With an odd number of such aisles the last one is entered from the front and left the same way. O(k log k).
/// </summary>
public sealed class SShapeRouting : IRoutingStrategy
{
    public string Name => "s-shape";

    public PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes)
    {
        var aisles = PickSet.ByAisle(graph, pickNodes);
        if (aisles.Count > 1) PickSet.RequireBackCrossAisle(graph, Name);

        var route = new WaypointBuilder(graph);
        var atFront = true;
        for (var i = 0; i < aisles.Count; i++)
        {
            var (aisle, picks) = aisles[i];
            if (!atFront)
            {
                route.Visit(graph.Back(aisle)).Visit(Enumerable.Reverse(picks)).Visit(graph.Front(aisle));
                atFront = true;
            }
            else if (i == aisles.Count - 1)
            {
                route.Visit(graph.Front(aisle)).Visit(picks).Visit(graph.Front(aisle));
            }
            else
            {
                route.Visit(graph.Front(aisle)).Visit(picks).Visit(graph.Back(aisle));
                atFront = false;
            }
        }
        return route.Build(Name);
    }
}

/// <summary>
/// Largest gap (Hall 1993): the leftmost and rightmost aisles with picks are traversed completely; every aisle in between
/// is entered from the back and from the front, each time up to the largest gap between neighbouring stops,
/// so that gap is never walked. O(k log k).
/// </summary>
public sealed class LargestGapRouting : IRoutingStrategy
{
    public string Name => "largest-gap";

    public PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes)
    {
        var aisles = PickSet.ByAisle(graph, pickNodes);
        if (aisles.Count <= 1) return new ReturnRouting().Route(graph, pickNodes) with { Strategy = Name };
        PickSet.RequireBackCrossAisle(graph, Name);

        var (first, firstPicks) = aisles[0];
        var (last, lastPicks) = aisles[^1];
        var middle = aisles.Skip(1).Take(aisles.Count - 2)
            .Select(a => (a.Aisle, Split: SplitAtLargestGap(graph, a.Picks)))
            .ToArray();

        var route = new WaypointBuilder(graph);
        route.Visit(graph.Front(first)).Visit(firstPicks).Visit(graph.Back(first));
        foreach (var (aisle, (_, fromBack)) in middle)
        {
            if (fromBack.Length > 0)
                route.Visit(graph.Back(aisle)).Visit(Enumerable.Reverse(fromBack)).Visit(graph.Back(aisle));
        }
        route.Visit(graph.Back(last)).Visit(Enumerable.Reverse(lastPicks)).Visit(graph.Front(last));
        foreach (var (aisle, (fromFront, _)) in Enumerable.Reverse(middle))
        {
            if (fromFront.Length > 0)
                route.Visit(graph.Front(aisle)).Visit(fromFront).Visit(graph.Front(aisle));
        }
        return route.Build(Name);
    }

    /// <summary>
    /// Splits one aisle's picks (sorted front to back) at the largest gap between consecutive stops,
    /// counting the front and back junctions as stops. Ties go to the gap nearest the front.
    /// </summary>
    internal static (int[] FromFront, int[] FromBack) SplitAtLargestGap(WarehouseGraph graph, int[] picks)
    {
        var bestIndex = 0;
        var bestGap = -1;
        for (var i = 0; i <= picks.Length; i++)
        {
            var lower = i == 0 ? 0 : graph.SlotOf(picks[i - 1]);
            var upper = i == picks.Length ? graph.BackSlot : graph.SlotOf(picks[i]);
            if (upper - lower <= bestGap) continue;
            bestGap = upper - lower;
            bestIndex = i;
        }
        return (picks[..bestIndex], picks[bestIndex..]);
    }
}
