using SlottingTwin.Core.Batching;
using SlottingTwin.Simulation;

namespace SlottingTwin.Tests;

public class BatchingTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(30)]
    public void Batches_respect_cart_capacity_and_contain_every_order_once(int capacity)
    {
        var runner = new ScenarioRunner(TestData.GeneratedSample(), seed: 1);
        var plan = runner.Plan("coi");
        var pickLists = runner.Sample.History.Orders.Select(o => ScenarioRunner.PickListOf(o, plan)).ToArray();

        var batches = SeedBatcher.Build(runner.Graph, pickLists, capacity);

        batches.ShouldAllBe(b => b.Lines <= capacity && b.Orders.Count > 0);
        batches.SelectMany(b => b.Orders).Select(o => o.OrderId).Order().ShouldBe(pickLists.Select(p => p.OrderId).Order());
    }

    [Fact]
    public void Seed_batching_groups_orders_that_share_aisles()
    {
        var graph = TestData.Tiny();
        PickList[] orders =
        [
            new("o1", 1, [graph.NodeAt(0, 1)]),
            new("o2", 1, [graph.NodeAt(2, 2)]),
            new("o3", 1, [graph.NodeAt(0, 3)]),
        ];

        var batches = SeedBatcher.Build(graph, orders, cartCapacity: 2);

        batches.Select(b => b.Orders.Select(o => o.OrderId)).ShouldBe([["o1", "o3"], ["o2"]]);
        batches[0].Nodes.Order().ShouldBe([graph.NodeAt(0, 1), graph.NodeAt(0, 3)]);
    }

    [Fact]
    public void An_order_larger_than_the_cart_is_rejected()
    {
        var graph = TestData.Tiny();
        PickList[] orders = [new("huge", 5, [graph.NodeAt(0, 1)])];

        Should.Throw<ArgumentException>(() => SeedBatcher.Build(graph, orders, cartCapacity: 4));
        Should.Throw<ArgumentOutOfRangeException>(() => SeedBatcher.Build(graph, orders, cartCapacity: 0));
    }

    [Theory]
    [InlineData("s-shape")]
    [InlineData("2-opt")]
    public void Batched_picking_walks_less_than_picking_order_by_order(string routing)
    {
        var runner = new ScenarioRunner(TestData.GeneratedSample(), seed: 1);

        var kpis = runner.Run(new ScenarioConfig("coi", routing, CartCapacity: 16));

        kpis.Batching.ShouldNotBeNull();
        kpis.Batching.TotalDistance.ShouldBeLessThan(kpis.TotalDistance);
        kpis.Batching.Batches.ShouldBeLessThan(kpis.Orders);
    }
}
