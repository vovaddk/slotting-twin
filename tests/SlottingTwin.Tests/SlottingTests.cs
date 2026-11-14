using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Model;
using SlottingTwin.Core.Slotting;

namespace SlottingTwin.Tests;

public class SlottingTests
{
    public static TheoryData<string> StrategyNames => new(SlottingStrategies.All.Select(s => s.Name));

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void Every_sku_gets_its_own_location_that_respects_volume_and_weight(string name)
    {
        var (graph, skus, demand) = Generated();

        var plan = SlottingStrategies.Get(name).Assign(new SlottingContext(graph, skus, demand, Seed: 3));

        plan.Strategy.ShouldBe(name);
        plan.Assignments.Count.ShouldBe(skus.Count);
        plan.Assignments.Values.Select(l => l.Id).ShouldBeUnique();
        foreach (var sku in skus)
        {
            var location = plan.Assignments[sku.Id];
            location.Fits(sku).ShouldBeTrue();
            if (sku.WeightKg > 25 || sku.Volume > 1.0) location.Level.ShouldBe(0, $"{sku.Id} is heavy or bulky");
            if (sku.WeightKg > 10 || sku.Volume > 0.6) location.Level.ShouldBeLessThan(2, $"{sku.Id} is too heavy for the top level");
        }
    }

    [Fact]
    public void The_generated_catalogue_really_exercises_the_constraints()
    {
        var (_, skus, _) = Generated();

        skus.Count(s => s.WeightKg > 25).ShouldBeGreaterThan(0);
        skus.Count(s => s.Volume > 1.0).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Coi_slotting_cuts_expected_travel_against_both_baselines()
    {
        var (graph, skus, demand) = Generated();
        double Travel(string name) => SlottingStrategies.Get(name).Assign(new SlottingContext(graph, skus, demand, Seed: 1))
            .ExpectedTravelPerPick(graph, demand);

        var coi = Travel("coi");

        coi.ShouldBeLessThan(Travel("random") * 0.7);
        coi.ShouldBeLessThan(Travel("alphabetical"));
    }

    [Fact]
    public void Coi_puts_the_lowest_coi_sku_in_one_of_the_closest_fitting_locations()
    {
        var (graph, skus, demand) = Generated();

        var plan = new CoiSlotting().Assign(new SlottingContext(graph, skus, demand));

        var best = skus.MinBy(s => CoiSlotting.Coi(s, demand))!;
        var closest = graph.Locations.Where(l => l.Fits(best)).Min(l => graph.Distance(graph.Depot, l.Node));
        graph.Distance(graph.Depot, plan.Assignments[best.Id].Node).ShouldBe(closest);
    }

    [Fact]
    public void Coi_ranks_by_space_per_pick_not_by_picks_alone()
    {
        var demand = DemandProfile.From([TestData.Order("o1", 0, ("big", 1), ("small", 1)), TestData.Order("o2", 0, ("big", 1))]);

        // big: 1.0 m³ / 2 picks = 0.5; small: 0.1 m³ / 1 pick = 0.1, so small goes first despite fewer picks.
        CoiSlotting.Coi(new Sku("big", 1.0, 1), demand).ShouldBe(0.5);
        CoiSlotting.Coi(new Sku("small", 0.1, 1), demand).ShouldBe(0.1);
        CoiSlotting.Coi(new Sku("never", 0.1, 1), demand).ShouldBe(double.PositiveInfinity);
    }

    [Fact]
    public void Swap_search_never_worsens_its_own_objective()
    {
        var (graph, skus, demand) = Generated();
        var swap = new CoOccurrenceSwapSlotting();
        var context = new SlottingContext(graph, skus, demand);

        var before = CoOccurrenceSwapSlotting.Objective(new CoiSlotting().Assign(context), graph, demand, swap.AffinityWeight);
        var after = CoOccurrenceSwapSlotting.Objective(swap.Assign(context), graph, demand, swap.AffinityWeight);

        after.ShouldBeLessThan(before);
    }

    [Fact]
    public void Swap_search_pulls_co_ordered_skus_together()
    {
        // One level, 3 aisles x 4 bays. "a" and "b" are always ordered together, "c" alone; COI alone would spread them.
        var graph = new WarehouseGraph(TestData.Layout());
        Sku[] skus = [new("a", 0.1, 1), new("b", 0.1, 1), new("c", 0.1, 1)];
        var orders = Enumerable.Range(0, 10).Select(i => TestData.Order($"ab{i}", 0, ("a", 1), ("b", 1)))
            .Concat(Enumerable.Range(0, 11).Select(i => TestData.Order($"c{i}", 0, ("c", 1))));
        var demand = DemandProfile.From(orders);
        var context = new SlottingContext(graph, skus, demand);

        var coi = new CoiSlotting().Assign(context).Assignments;
        var swap = new CoOccurrenceSwapSlotting(affinityWeight: 5).Assign(context).Assignments;

        // COI: c and a share the first bay, b sits one bay further. The swap puts a and b in the same bay.
        graph.Distance(coi["a"].Node, coi["b"].Node).ShouldBe(1);
        graph.Distance(swap["a"].Node, swap["b"].Node).ShouldBe(0);
    }

    [Fact]
    public void Random_slotting_is_reproducible_for_a_seed()
    {
        var (graph, skus, demand) = Generated();
        Dictionary<string, string> Ids(int seed) => new RandomSlotting().Assign(new SlottingContext(graph, skus, demand, seed))
            .Assignments.ToDictionary(a => a.Key, a => a.Value.Id);

        Ids(5).ShouldBe(Ids(5));
        Ids(5).ShouldNotBe(Ids(6));
    }

    [Fact]
    public void Infeasible_instances_fail_loudly()
    {
        var graph = new WarehouseGraph(TestData.Layout(aisles: 1, bays: 1)); // 2 locations, 1 level of 1 m³ / 100 kg
        var demand = DemandProfile.From([]);

        Sku[] tooHeavy = [new("anvil", 0.1, 500)];
        Sku[] tooMany = [new("a", 0.1, 1), new("b", 0.1, 1), new("c", 0.1, 1)];

        foreach (var strategy in SlottingStrategies.All)
        {
            Should.Throw<InvalidOperationException>(() => strategy.Assign(new SlottingContext(graph, tooHeavy, demand)));
            Should.Throw<InvalidOperationException>(() => strategy.Assign(new SlottingContext(graph, tooMany, demand)));
        }
    }

    [Fact]
    public void Unknown_strategy_names_are_rejected()
    {
        SlottingStrategies.Exists("COI").ShouldBeTrue();
        Should.Throw<ArgumentException>(() => SlottingStrategies.Get("magic"));
    }

    private static (WarehouseGraph Graph, IReadOnlyList<Sku> Skus, DemandProfile Demand) Generated()
    {
        var sample = TestData.GeneratedSample();
        return (new WarehouseGraph(sample.Layout), sample.History.Skus, DemandProfile.From(sample.History.Orders));
    }
}
