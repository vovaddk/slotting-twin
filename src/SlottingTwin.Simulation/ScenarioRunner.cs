using SlottingTwin.Core.Batching;
using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Model;
using SlottingTwin.Core.Routing;
using SlottingTwin.Core.Slotting;

namespace SlottingTwin.Simulation;

/// <param name="CartCapacity">When set, orders are also batched (seed algorithm) into carts of this many lines.</param>
public sealed record ScenarioConfig(string Slotting, string Routing, int? CartCapacity = null);

public sealed record BatchingKpis(int CartCapacity, int Batches, double TotalDistance, double SavingPercentVsSingleOrder);

public sealed record ScenarioKpis(
    string Slotting,
    string Routing,
    int Orders,
    int Picks,
    double TotalDistance,
    double AvgDistancePerOrder,
    double PicksPerMeter,
    double ExpectedTravelPerPick,
    BatchingKpis? Batching);

public sealed record ScenarioComparison(ScenarioKpis Baseline, ScenarioKpis Candidate, double ImprovementPercent);

/// <summary>
/// Replays an order history on a layout: slot the SKUs, route every order, aggregate KPIs.
/// Deterministic for a given sample and seed (the seed drives random slotting). Not thread-safe; create one per run.
/// </summary>
public sealed class ScenarioRunner
{
    public static readonly ScenarioConfig DefaultBaseline = new("random", "s-shape");

    private readonly Dictionary<string, SlottingPlan> _plans = new(StringComparer.OrdinalIgnoreCase);

    public ScenarioRunner(Sample sample, int seed)
    {
        ArgumentNullException.ThrowIfNull(sample);
        Sample = sample;
        Seed = seed;
        Graph = new WarehouseGraph(sample.Layout);
        Demand = DemandProfile.From(sample.History.Orders);
    }

    public Sample Sample { get; }
    public int Seed { get; }
    public WarehouseGraph Graph { get; }
    public DemandProfile Demand { get; }

    public SlottingPlan Plan(string slotting)
    {
        if (!_plans.TryGetValue(slotting, out var plan))
        {
            plan = SlottingStrategies.Get(slotting).Assign(new SlottingContext(Graph, Sample.History.Skus, Demand, Seed));
            _plans[slotting] = plan;
        }
        return plan;
    }

    public static PickList PickListOf(Order order, SlottingPlan plan) =>
        new(order.Id, order.Lines.Count, order.Lines.Select(l => plan.Assignments[l.Sku].Node).Distinct().ToArray());

    public ScenarioKpis Run(ScenarioConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var plan = Plan(config.Slotting);
        var router = RoutingStrategies.Get(config.Routing);
        var pickLists = Sample.History.Orders.Select(o => PickListOf(o, plan)).ToArray();
        var total = pickLists.Sum(p => router.Route(Graph, p.Nodes).Distance);
        var picks = pickLists.Sum(p => p.Lines);

        BatchingKpis? batching = null;
        if (config.CartCapacity is { } capacity)
        {
            var batches = SeedBatcher.Build(Graph, pickLists, capacity);
            var batched = batches.Sum(b => router.Route(Graph, b.Nodes).Distance);
            batching = new BatchingKpis(capacity, batches.Count, batched, Percent(total, batched));
        }

        return new ScenarioKpis(
            plan.Strategy,
            router.Name,
            pickLists.Length,
            picks,
            total,
            pickLists.Length == 0 ? 0 : total / pickLists.Length,
            total == 0 ? 0 : picks / total,
            plan.ExpectedTravelPerPick(Graph, Demand),
            batching);
    }

    public ScenarioComparison Compare(ScenarioConfig candidate, ScenarioConfig? baseline = null)
    {
        var before = Run(baseline ?? DefaultBaseline);
        var after = Run(candidate);
        return new ScenarioComparison(before, after, Percent(before.TotalDistance, after.TotalDistance));
    }

    /// <summary>How much smaller <paramref name="after"/> is than <paramref name="before"/>, in percent.</summary>
    public static double Percent(double before, double after) => before == 0 ? 0 : (before - after) / before * 100;
}
