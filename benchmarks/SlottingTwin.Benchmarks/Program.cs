using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Routing;

BenchmarkSwitcher.FromAssembly(typeof(RoutingBenchmarks).Assembly).Run(args);

/// <summary>Routing heuristics on one pick list in a 20 x 30 warehouse (600 pick nodes), depot mid-front.</summary>
[MemoryDiagnoser]
public class RoutingBenchmarks
{
    private WarehouseGraph _graph = null!;
    private int[] _picks = [];
    private IRoutingStrategy _strategy = null!;

    [Params(10, 50, 200)]
    public int Picks { get; set; }

    [ParamsSource(nameof(StrategyNames))]
    public string Strategy { get; set; } = "";

    public static IEnumerable<string> StrategyNames => RoutingStrategies.All.Select(s => s.Name);

    [GlobalSetup]
    public void Setup()
    {
        _graph = new WarehouseGraph(new WarehouseLayout
        {
            Name = "bench", Aisles = 20, BaysPerAisle = 30, Levels = [new LevelSpec(1, 100)], DepotAisle = 10,
        });
        var all = Enumerable.Range(0, _graph.NodeCount).Where(_graph.IsPickNode).ToArray();
        new Random(42).Shuffle(all);
        _picks = all[..Picks];
        _strategy = RoutingStrategies.Get(Strategy);
        _graph.DistanceMatrix([_graph.Depot, .. _picks]); // warm the Dijkstra cache: measure routing, not shortest paths
    }

    [Benchmark]
    public double Route() => _strategy.Route(_graph, _picks).Distance;
}
