using SlottingTwin.Core.Layout;

namespace SlottingTwin.Core.Batching;

/// <summary>An order translated to the pick nodes it needs. <see cref="Lines"/> is what the cart capacity counts.</summary>
public sealed record PickList(string OrderId, int Lines, IReadOnlyList<int> Nodes);

public sealed record PickBatch(IReadOnlyList<PickList> Orders)
{
    public int Lines => Orders.Sum(o => o.Lines);
    public IReadOnlyCollection<int> Nodes => Orders.SelectMany(o => o.Nodes).Distinct().ToArray();
}

/// <summary>
/// Seed batching (de Koster et al. 1999): open a batch with the unassigned order that spans the most aisles, then keep
/// adding the order that adds the fewest new aisles (ties: most shared aisles, then input order) while the cart has room.
/// O(n²·a) for n orders touching up to a aisles each.
/// </summary>
public static class SeedBatcher
{
    public static IReadOnlyList<PickBatch> Build(WarehouseGraph graph, IReadOnlyList<PickList> orders, int cartCapacity)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentOutOfRangeException.ThrowIfLessThan(cartCapacity, 1);
        if (orders.FirstOrDefault(o => o.Lines > cartCapacity) is { } tooBig)
            throw new ArgumentException($"Order {tooBig.OrderId} has {tooBig.Lines} lines, more than the cart capacity {cartCapacity}.", nameof(orders));

        var aisles = orders.Select(o => o.Nodes.Select(graph.AisleOf).ToHashSet()).ToArray();
        var open = Enumerable.Range(0, orders.Count).ToList();
        var batches = new List<PickBatch>();
        while (open.Count > 0)
        {
            var seed = open.MaxBy(i => aisles[i].Count);
            open.Remove(seed);
            var members = new List<int> { seed };
            var batchAisles = new HashSet<int>(aisles[seed]);
            var lines = orders[seed].Lines;

            while (true)
            {
                int best = -1, bestAdded = int.MaxValue, bestShared = -1;
                foreach (var i in open)
                {
                    if (lines + orders[i].Lines > cartCapacity) continue;
                    var shared = aisles[i].Count(batchAisles.Contains);
                    var added = aisles[i].Count - shared;
                    if (added > bestAdded || (added == bestAdded && shared <= bestShared)) continue;
                    (best, bestAdded, bestShared) = (i, added, shared);
                }
                if (best < 0) break;

                open.Remove(best);
                members.Add(best);
                batchAisles.UnionWith(aisles[best]);
                lines += orders[best].Lines;
            }
            batches.Add(new PickBatch(members.Select(i => orders[i]).ToArray()));
        }
        return batches;
    }
}
