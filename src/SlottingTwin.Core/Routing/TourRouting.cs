using SlottingTwin.Core.Layout;

namespace SlottingTwin.Core.Routing;

/// <summary>Nearest neighbour: always walk to the closest unvisited pick (shortest-path distance). O(k²) plus k Dijkstra rows.</summary>
public sealed class NearestNeighborRouting : IRoutingStrategy
{
    public string Name => "nearest-neighbor";

    public PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes)
    {
        var picks = PickSet.Normalize(graph, pickNodes);
        int[] nodes = [graph.Depot, .. picks];
        return Tour.ToRoute(graph, Name, nodes, Tour.NearestNeighbor(graph.DistanceMatrix(nodes)));
    }
}

/// <summary>
/// Nearest neighbour followed by 2-opt: reverse any tour segment whose reversal shortens the tour, until none does.
/// Never longer than its nearest-neighbour start. O(k²) per improvement pass.
/// </summary>
public sealed class TwoOptRouting : IRoutingStrategy
{
    public string Name => "2-opt";

    public PickRoute Route(WarehouseGraph graph, IReadOnlyCollection<int> pickNodes)
    {
        var picks = PickSet.Normalize(graph, pickNodes);
        int[] nodes = [graph.Depot, .. picks];
        var distances = graph.DistanceMatrix(nodes);
        return Tour.ToRoute(graph, Name, nodes, Tour.TwoOpt(Tour.NearestNeighbor(distances), distances));
    }
}

/// <summary>Tour heuristics over a distance matrix whose index 0 is the depot. Tours start at 0; the return to 0 is implied.</summary>
internal static class Tour
{
    private const double Epsilon = 1e-9;

    public static int[] NearestNeighbor(double[,] distances)
    {
        var n = distances.GetLength(0);
        var tour = new int[n];
        var visited = new bool[n];
        visited[0] = true;
        for (var step = 1; step < n; step++)
        {
            var from = tour[step - 1];
            var next = -1;
            for (var candidate = 1; candidate < n; candidate++)
            {
                if (!visited[candidate] && (next < 0 || distances[from, candidate] < distances[from, next])) next = candidate;
            }
            tour[step] = next;
            visited[next] = true;
        }
        return tour;
    }

    /// <summary>First-improvement 2-opt; each accepted move strictly shortens the tour, so the loop terminates.</summary>
    public static int[] TwoOpt(int[] start, double[,] d)
    {
        int[] t = [.. start, 0];
        bool improved;
        do
        {
            improved = false;
            for (var i = 1; i < t.Length - 2; i++)
            for (var j = i + 1; j < t.Length - 1; j++)
            {
                var delta = d[t[i - 1], t[j]] + d[t[i], t[j + 1]] - d[t[i - 1], t[i]] - d[t[j], t[j + 1]];
                if (delta >= -Epsilon) continue;
                Array.Reverse(t, i, j - i + 1);
                improved = true;
            }
        } while (improved);
        return t[..^1];
    }

    public static PickRoute ToRoute(WarehouseGraph graph, string strategy, int[] nodes, int[] tour) =>
        new WaypointBuilder(graph).Visit(tour.Skip(1).Select(i => nodes[i])).Build(strategy);
}
