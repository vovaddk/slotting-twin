using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Routing;

namespace SlottingTwin.Tests;

public class RoutingTests
{
    public static TheoryData<string> StrategyNames => new(RoutingStrategies.All.Select(s => s.Name));

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void Route_runs_depot_to_depot_and_visits_every_pick_exactly_once(string name)
    {
        var graph = new WarehouseGraph(TestData.Layout(aisles: 6, bays: 10, spacing: 4, depot: 2));
        var strategy = RoutingStrategies.Get(name);

        for (var seed = 1; seed <= 40; seed++)
        {
            var picks = TestData.RandomPicks(graph, 1 + seed % 25, seed);

            var route = strategy.Route(graph, picks);

            route.Strategy.ShouldBe(name);
            route.Waypoints[0].ShouldBe(graph.Depot);
            route.Waypoints[^1].ShouldBe(graph.Depot);
            route.Waypoints.Where(graph.IsPickNode).Order().ShouldBe(picks.Order());
            route.Distance.ShouldBe(graph.PathLength(route.Waypoints), 1e-9);
        }
    }

    [Fact]
    public void Aisle_heuristics_match_the_hand_computed_example()
    {
        // Picks in aisle 0 bay-slot 2, aisle 1 slot 1, aisle 2 slot 3; aisle length 5 m, aisles 3 m apart.
        var graph = TestData.Tiny();
        int[] picks = [graph.NodeAt(0, 2), graph.NodeAt(1, 1), graph.NodeAt(2, 3)];

        // S-shape: up aisle 0 (5), back to aisle 1 (3), down aisle 1 (5), front to aisle 2 (3),
        // odd last aisle in and out (3 + 3), front back to the depot (6).
        new SShapeRouting().Route(graph, picks).Distance.ShouldBe(28);
        // Return: 2·2 + 3 + 2·1 + 3 + 2·3 + 6.
        new ReturnRouting().Route(graph, picks).Distance.ShouldBe(24);
        // Largest gap: through aisle 0 (5), back 3 + 3, through aisle 2 (5); aisle 1's largest gap is behind its pick,
        // so it is picked from the front on the way home: 3 + 2 + 3.
        new LargestGapRouting().Route(graph, picks).Distance.ShouldBe(24);
    }

    [Fact]
    public void Nearest_neighbor_matches_the_hand_computed_example()
    {
        var graph = TestData.Tiny();
        int[] picks = [graph.NodeAt(0, 3), graph.NodeAt(1, 1), graph.NodeAt(2, 2)];

        var route = new NearestNeighborRouting().Route(graph, picks);

        // depot -> (0,3): 3, -> (1,1): 7 via front, -> (2,2): 6, -> depot: 8.
        route.Waypoints.ShouldBe([graph.Depot, graph.NodeAt(0, 3), graph.NodeAt(1, 1), graph.NodeAt(2, 2), graph.Depot]);
        route.Distance.ShouldBe(24);
        new SShapeRouting().Route(graph, picks).Distance.ShouldBe(26);
    }

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void A_single_pick_costs_exactly_one_round_trip(string name)
    {
        var graph = new WarehouseGraph(TestData.Layout(aisles: 5, bays: 8, depot: 2));
        foreach (var pick in TestData.RandomPicks(graph, 10, seed: 3))
        {
            RoutingStrategies.Get(name).Route(graph, [pick]).Distance.ShouldBe(2 * graph.Distance(graph.Depot, pick));
        }
    }

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void An_empty_pick_list_stays_at_the_depot(string name)
    {
        var graph = TestData.Tiny();

        var route = RoutingStrategies.Get(name).Route(graph, []);

        route.Waypoints.ShouldBe([graph.Depot]);
        route.Distance.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void No_heuristic_beats_the_brute_force_optimum(string name)
    {
        var graph = new WarehouseGraph(TestData.Layout(aisles: 5, bays: 8, spacing: 4, depot: 1));
        for (var seed = 1; seed <= 30; seed++)
        {
            var picks = TestData.RandomPicks(graph, 1 + seed % 6, seed);

            RoutingStrategies.Get(name).Route(graph, picks).Distance.ShouldBeGreaterThanOrEqualTo(OptimalTour(graph, picks) - 1e-9);
        }
    }

    [Fact]
    public void Two_opt_is_never_worse_than_its_nearest_neighbor_start()
    {
        var graph = new WarehouseGraph(TestData.Layout(aisles: 10, bays: 20, spacing: 4, depot: 5));
        var strictlyBetter = 0;
        for (var seed = 1; seed <= 100; seed++)
        {
            var picks = TestData.RandomPicks(graph, 5 + seed % 36, seed);
            var nearest = new NearestNeighborRouting().Route(graph, picks).Distance;
            var twoOpt = new TwoOptRouting().Route(graph, picks).Distance;

            twoOpt.ShouldBeLessThanOrEqualTo(nearest + 1e-9);
            if (twoOpt < nearest - 1e-9) strictlyBetter++;
        }
        strictlyBetter.ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void Duplicate_picks_are_visited_once(string name)
    {
        var graph = TestData.Tiny();
        var pick = graph.NodeAt(1, 2);

        var route = RoutingStrategies.Get(name).Route(graph, [pick, pick, pick]);

        route.Waypoints.Count(n => n == pick).ShouldBe(1);
        route.Distance.ShouldBe(2 * graph.Distance(graph.Depot, pick));
    }

    [Theory]
    [MemberData(nameof(StrategyNames))]
    public void Non_pick_nodes_are_rejected(string name)
    {
        var graph = TestData.Tiny();
        var strategy = RoutingStrategies.Get(name);

        foreach (var node in new[] { graph.Depot, graph.Back(1), -1, graph.NodeCount })
            Should.Throw<ArgumentException>(() => strategy.Route(graph, [node]));
    }

    [Fact]
    public void Traversal_heuristics_need_a_back_cross_aisle_once_picks_span_aisles()
    {
        var graph = new WarehouseGraph(TestData.Layout(back: false));
        int[] twoAisles = [graph.NodeAt(0, 2), graph.NodeAt(2, 2)];

        Should.Throw<InvalidOperationException>(() => new SShapeRouting().Route(graph, twoAisles));
        Should.Throw<InvalidOperationException>(() => new LargestGapRouting().Route(graph, twoAisles));
        new ReturnRouting().Route(graph, twoAisles).Distance.ShouldBe(4 + 6 + 4 + 6); // in/out aisle 0, across, in/out aisle 2, home
        new SShapeRouting().Route(graph, [graph.NodeAt(1, 3)]).Distance.ShouldBe(2 * 6);

        RoutingStrategies.All.Where(s => !RoutingStrategies.Supports(s.Name, graph.Layout)).Select(s => s.Name)
            .ShouldBe(["s-shape", "largest-gap"]);
        var withBackAisle = TestData.Layout();
        RoutingStrategies.All.ShouldAllBe(s => RoutingStrategies.Supports(s.Name, withBackAisle));
    }

    [Theory]
    [InlineData(new[] { 1, 4 }, new[] { 1 }, new[] { 4 })] // gaps 1, 3, 1: the middle one is skipped
    [InlineData(new[] { 2 }, new[] { 2 }, new int[0])]     // gaps 2, 3: everything from the front
    [InlineData(new[] { 4 }, new int[0], new[] { 4 })]     // gaps 4, 1: everything from the back
    [InlineData(new[] { 1, 3 }, new[] { 1 }, new[] { 3 })] // gaps 1, 2, 2: tie goes to the gap nearest the front
    public void Largest_gap_splits_an_aisle_at_its_widest_empty_stretch(int[] slots, int[] fromFront, int[] fromBack)
    {
        var graph = TestData.Tiny();
        var picks = slots.Select(s => graph.NodeAt(1, s)).ToArray();

        var (front, back) = LargestGapRouting.SplitAtLargestGap(graph, picks);

        front.Select(graph.SlotOf).ShouldBe(fromFront);
        back.Select(graph.SlotOf).ShouldBe(fromBack);
    }

    [Fact]
    public void Unknown_strategy_names_are_rejected()
    {
        RoutingStrategies.Exists("S-SHAPE").ShouldBeTrue();
        RoutingStrategies.Exists("teleport").ShouldBeFalse();
        Should.Throw<ArgumentException>(() => RoutingStrategies.Get("teleport"));
    }

    /// <summary>Exact TSP over shortest-path distances by depth-first search with pruning (small k only).</summary>
    private static double OptimalTour(WarehouseGraph graph, int[] picks)
    {
        int[] nodes = [graph.Depot, .. picks.Distinct()];
        var d = graph.DistanceMatrix(nodes);
        var all = (1 << nodes.Length) - 1;
        var best = double.MaxValue;

        void Search(int at, int visited, double length)
        {
            if (length >= best) return;
            if (visited == all)
            {
                best = Math.Min(best, length + d[at, 0]);
                return;
            }
            for (var next = 1; next < nodes.Length; next++)
                if ((visited & (1 << next)) == 0) Search(next, visited | (1 << next), length + d[at, next]);
        }

        Search(0, 1, 0);
        return best;
    }
}
