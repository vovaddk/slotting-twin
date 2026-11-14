using SlottingTwin.Core.Layout;

namespace SlottingTwin.Tests;

public class WarehouseGraphTests
{
    // Tiny layout: aisles at x = 0, 3, 6; slot 0 = front junction, slots 1..4 = bays, slot 5 = back junction (1 m apart).
    [Theory]
    [InlineData(0, 0, 0, 3, 3)]  // straight up one aisle
    [InlineData(0, 0, 1, 2, 5)]  // front cross-aisle 3 + up 2
    [InlineData(0, 4, 2, 4, 8)]  // via back: 1 + 6 + 1 (via front would be 14)
    [InlineData(0, 2, 2, 3, 11)] // front 2 + 6 + 3 equals back 3 + 6 + 2
    [InlineData(0, 3, 1, 3, 7)]  // back 2 + 3 + 2 beats front 3 + 3 + 3
    [InlineData(1, 1, 1, 4, 3)]  // same aisle never leaves it
    public void Distance_matches_hand_computed_values(int aisleA, int slotA, int aisleB, int slotB, double expected)
    {
        var graph = TestData.Tiny();
        var (a, b) = (graph.NodeAt(aisleA, slotA), graph.NodeAt(aisleB, slotB));

        graph.Distance(a, b).ShouldBe(expected);
        graph.Distance(b, a).ShouldBe(expected);
    }

    [Fact]
    public void Without_a_back_cross_aisle_every_aisle_change_goes_via_the_front()
    {
        var graph = new WarehouseGraph(TestData.Layout(back: false));

        graph.Distance(graph.NodeAt(0, 4), graph.NodeAt(2, 4)).ShouldBe(14); // 4 + 6 + 4
    }

    [Theory]
    [InlineData(5, 7, 4.0, 1.0, true, 2)]
    [InlineData(4, 9, 2.5, 1.3, false, 3)]
    [InlineData(1, 5, 3.0, 0.8, true, 0)]
    public void Dijkstra_agrees_with_the_closed_form_for_every_pair(int aisles, int bays, double spacing, double bayWidth, bool back, int depot)
    {
        var layout = TestData.Layout(aisles, bays, spacing, bayWidth, back, depot);
        var graph = new WarehouseGraph(layout);

        for (var from = 0; from < graph.NodeCount; from++)
        for (var to = 0; to < graph.NodeCount; to++)
            graph.Distance(from, to).ShouldBe(ClosedForm(graph, from, to), 1e-9);
    }

    [Fact]
    public void ExpandPath_walks_along_edges_and_matches_the_waypoint_length()
    {
        var graph = TestData.Tiny();
        int[] waypoints = [graph.Depot, graph.NodeAt(2, 3), graph.NodeAt(0, 4), graph.Depot];

        var path = graph.ExpandPath(waypoints);

        path[0].ShouldBe(graph.Depot);
        path[^1].ShouldBe(graph.Depot);
        var walked = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var step = graph.Distance(path[i - 1], path[i]);
            step.ShouldBeOneOf(1.0, 3.0); // one bay or one aisle spacing
            walked += step;
        }
        walked.ShouldBe(graph.PathLength(waypoints));
        graph.PathLength(waypoints).ShouldBe(9 + 9 + 4); // (2,3) via front 6+3, then via back 2+6+1, then down 4
    }

    [Fact]
    public void Locations_cover_every_bay_side_and_level_once()
    {
        var graph = new WarehouseGraph(TestData.Layout(levels: TestData.RackLevels));

        graph.Locations.Count.ShouldBe(3 * 4 * 3 * 2);
        graph.Locations.Select(l => l.Id).ShouldBeUnique();
        graph.Locations[0].Id.ShouldBe("A01-B01-L1-L");
        graph.Locations[^1].Id.ShouldBe("A03-B04-L3-R");
        graph.Locations.ShouldAllBe(l => l.Node == graph.NodeAt(l.Aisle, l.Bay + 1) && l.Spec == TestData.RackLevels[l.Level]);
    }

    [Fact]
    public void Depot_and_pick_nodes_follow_the_slot_convention()
    {
        var graph = new WarehouseGraph(TestData.Layout(depot: 1));

        graph.Depot.ShouldBe(graph.Front(1));
        graph.Position(graph.Depot).ShouldBe((3.0, 0.0));
        graph.IsPickNode(graph.Front(0)).ShouldBeFalse();
        graph.IsPickNode(graph.Back(2)).ShouldBeFalse();
        graph.IsPickNode(graph.NodeAt(2, 4)).ShouldBeTrue();
        graph.IsPickNode(-1).ShouldBeFalse();
        graph.IsPickNode(graph.NodeCount).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => graph.Distance(0, graph.NodeCount));
    }

    [Fact]
    public void Invalid_or_oversized_layouts_are_rejected()
    {
        WarehouseLayout[] invalid =
        [
            TestData.Layout() with { Aisles = 0 },
            TestData.Layout() with { Aisles = WarehouseLayout.MaxAisles + 1 },
            TestData.Layout() with { BaysPerAisle = WarehouseLayout.MaxBaysPerAisle + 1 },
            TestData.Layout() with { Levels = [] },
            TestData.Layout() with { Levels = [new LevelSpec(0, 10)] },
            TestData.Layout() with { AisleSpacing = 0 },
            TestData.Layout() with { DepotAisle = 3 },
            TestData.Layout() with { Name = " " },
        ];

        foreach (var layout in invalid)
        {
            layout.Validate().ShouldNotBeEmpty();
            Should.Throw<ArgumentException>(() => new WarehouseGraph(layout));
        }
    }

    private static double ClosedForm(WarehouseGraph graph, int from, int to)
    {
        var layout = graph.Layout;
        int a1 = graph.AisleOf(from), s1 = graph.SlotOf(from), a2 = graph.AisleOf(to), s2 = graph.SlotOf(to);
        if (a1 == a2) return Math.Abs(s1 - s2) * layout.BayWidth;
        var across = Math.Abs(a1 - a2) * layout.AisleSpacing;
        var viaFront = (s1 + s2) * layout.BayWidth + across;
        var viaBack = (2 * graph.BackSlot - s1 - s2) * layout.BayWidth + across;
        return layout.HasBackCrossAisle ? Math.Min(viaFront, viaBack) : viaFront;
    }
}
