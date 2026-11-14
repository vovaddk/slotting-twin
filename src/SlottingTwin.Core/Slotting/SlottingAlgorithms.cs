using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Model;

namespace SlottingTwin.Core.Slotting;

/// <summary>Baseline: SKUs in random order into random free locations (seeded).</summary>
public sealed class RandomSlotting : ISlottingStrategy
{
    public string Name => "random";

    public SlottingPlan Assign(SlottingContext context)
    {
        var random = new Random(context.Seed);
        var skus = context.Skus.ToArray();
        var locations = context.Graph.Locations.ToArray();
        random.Shuffle(skus);
        random.Shuffle(locations);
        return new SlottingPlan(Name, SlotFiller.Fill(skus, locations));
    }
}

/// <summary>Baseline: SKU ids in ordinal order into locations in address order (aisle, bay, level, side).</summary>
public sealed class AlphabeticalSlotting : ISlottingStrategy
{
    public string Name => "alphabetical";

    public SlottingPlan Assign(SlottingContext context) =>
        new(Name, SlotFiller.Fill(context.Skus.OrderBy(s => s.Id, StringComparer.Ordinal), context.Graph.Locations));
}

/// <summary>
/// Cube-per-order index (Heskett 1963): SKUs with the least storage space per pick go to the locations closest to the
/// depot. Optimal for single-command picking with dedicated storage when capacity constraints are slack. O(n log n + n·m).
/// </summary>
public sealed class CoiSlotting : ISlottingStrategy
{
    public string Name => "coi";

    public SlottingPlan Assign(SlottingContext context) => new(Name, Fill(context));

    public static double Coi(Sku sku, DemandProfile demand)
    {
        var picks = demand.PicksOf(sku.Id);
        return picks == 0 ? double.PositiveInfinity : sku.Volume / picks;
    }

    internal static Dictionary<string, StorageLocation> Fill(SlottingContext context)
    {
        var graph = context.Graph;
        var locations = graph.Locations
            .OrderBy(l => graph.Distance(graph.Depot, l.Node))
            .ThenBy(l => l.Level)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToArray();
        var skus = context.Skus
            .OrderBy(s => Coi(s, context.Demand))
            .ThenBy(s => s.Id, StringComparer.Ordinal);
        return SlotFiller.Fill(skus, locations);
    }
}

/// <summary>
/// Starts from <see cref="CoiSlotting"/> and runs a pairwise-swap local search on
/// J = Σ picks(s)·d(depot, s) + w·Σ together(s, t)·d(s, t):
/// fast movers stay close to the depot while SKUs that are often ordered together are pulled towards each other.
/// Only the most-picked <c>candidates</c> SKUs are swapped (they dominate J). Each pass is O(c²·deg).
/// </summary>
public sealed class CoOccurrenceSwapSlotting : ISlottingStrategy
{
    private const double Epsilon = 1e-9;
    private readonly double _affinityWeight;
    private readonly int _candidates;
    private readonly int _maxPasses;

    public CoOccurrenceSwapSlotting(double affinityWeight = 0.5, int candidates = 200, int maxPasses = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(affinityWeight);
        ArgumentOutOfRangeException.ThrowIfLessThan(candidates, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPasses, 1);
        (_affinityWeight, _candidates, _maxPasses) = (affinityWeight, candidates, maxPasses);
    }

    public string Name => "coi-swap";
    public double AffinityWeight => _affinityWeight;

    public SlottingPlan Assign(SlottingContext context)
    {
        var graph = context.Graph;
        var demand = context.Demand;
        var start = CoiSlotting.Fill(context);
        var skus = context.Skus.ToArray();
        var location = skus.Select(s => start[s.Id]).ToArray();
        var picks = skus.Select(s => demand.PicksOf(s.Id)).ToArray();
        var neighbours = Neighbours(skus, demand);
        var candidates = Enumerable.Range(0, skus.Length)
            .Where(i => picks[i] > 0)
            .OrderByDescending(i => picks[i])
            .ThenBy(i => skus[i].Id, StringComparer.Ordinal)
            .Take(_candidates)
            .ToArray();

        double SwapDelta(int s, int t)
        {
            int ls = location[s].Node, lt = location[t].Node;
            if (ls == lt) return 0; // same bay: every distance stays the same
            var delta = (picks[s] - picks[t]) * (graph.Distance(graph.Depot, lt) - graph.Distance(graph.Depot, ls));
            foreach (var (u, together) in neighbours[s])
                if (u != t) delta += _affinityWeight * together * (graph.Distance(lt, location[u].Node) - graph.Distance(ls, location[u].Node));
            foreach (var (u, together) in neighbours[t])
                if (u != s) delta += _affinityWeight * together * (graph.Distance(ls, location[u].Node) - graph.Distance(lt, location[u].Node));
            return delta;
        }

        for (var pass = 0; pass < _maxPasses; pass++)
        {
            var improved = false;
            for (var i = 0; i < candidates.Length; i++)
            for (var j = i + 1; j < candidates.Length; j++)
            {
                var (s, t) = (candidates[i], candidates[j]);
                if (!location[s].Fits(skus[t]) || !location[t].Fits(skus[s])) continue;
                if (SwapDelta(s, t) >= -Epsilon) continue;
                (location[s], location[t]) = (location[t], location[s]);
                improved = true;
            }
            if (!improved) break;
        }

        return new SlottingPlan(Name, Enumerable.Range(0, skus.Length).ToDictionary(i => skus[i].Id, i => location[i], StringComparer.Ordinal));
    }

    /// <summary>The objective J the local search minimises (lower is better).</summary>
    public static double Objective(SlottingPlan plan, WarehouseGraph graph, DemandProfile demand, double affinityWeight)
    {
        var travel = plan.Assignments.Sum(a => demand.PicksOf(a.Key) * graph.Distance(graph.Depot, a.Value.Node));
        var affinity = demand.Pairs.Sum(p =>
            plan.Assignments.TryGetValue(p.Key.A, out var a) && plan.Assignments.TryGetValue(p.Key.B, out var b)
                ? p.Value * graph.Distance(a.Node, b.Node)
                : 0);
        return travel + affinityWeight * affinity;
    }

    private static List<(int Other, int Together)>[] Neighbours(Sku[] skus, DemandProfile demand)
    {
        var index = Enumerable.Range(0, skus.Length).ToDictionary(i => skus[i].Id, StringComparer.Ordinal);
        var neighbours = skus.Select(_ => new List<(int, int)>()).ToArray();
        foreach (var ((a, b), together) in demand.Pairs)
        {
            if (!index.TryGetValue(a, out var ia) || !index.TryGetValue(b, out var ib)) continue;
            neighbours[ia].Add((ib, together));
            neighbours[ib].Add((ia, together));
        }
        return neighbours;
    }
}
