using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Model;
using SlottingTwin.Simulation;

namespace SlottingTwin.Tests;

internal static class TestData
{
    public static readonly LevelSpec[] RackLevels = [new(1.2, 1000), new(1.0, 25), new(0.6, 10)];

    /// <summary>Defaults give the hand-computed layout: 3 aisles 3 m apart, 4 bays of 1 m, depot at the head of aisle 0.</summary>
    public static WarehouseLayout Layout(
        int aisles = 3, int bays = 4, double spacing = 3, double bayWidth = 1, bool back = true, int depot = 0, LevelSpec[]? levels = null) =>
        new()
        {
            Name = "test",
            Aisles = aisles,
            BaysPerAisle = bays,
            Levels = levels ?? [new LevelSpec(1, 100)],
            AisleSpacing = spacing,
            BayWidth = bayWidth,
            HasBackCrossAisle = back,
            DepotAisle = depot,
        };

    public static WarehouseGraph Tiny() => new(Layout());

    public static int[] RandomPicks(WarehouseGraph graph, int count, int seed)
    {
        var all = Enumerable.Range(0, graph.NodeCount).Where(graph.IsPickNode).ToArray();
        new Random(seed).Shuffle(all);
        return all[..count];
    }

    /// <summary>6 aisles x 10 bays x 3 levels (360 slots) with a seeded 200-SKU, 14-day history.</summary>
    public static Sample GeneratedSample(int seed = 7) =>
        new(Layout(aisles: 6, bays: 10, spacing: 4, levels: RackLevels) with { Name = "generated" },
            OrderHistoryGenerator.Generate(new GeneratorOptions(SkuCount: 200, Days: 14, OrdersPerDay: 30), seed));

    public static Order Order(string id, int day, params (string Sku, int Quantity)[] lines) =>
        new(id, day, [.. lines.Select(l => new OrderLine(l.Sku, l.Quantity))]);
}
