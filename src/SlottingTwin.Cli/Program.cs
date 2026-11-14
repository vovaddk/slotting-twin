using System.Diagnostics;
using System.Globalization;
using SlottingTwin.Core.Analysis;
using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Routing;
using SlottingTwin.Core.Slotting;
using SlottingTwin.Simulation;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var samplesDir = Option("--samples") ?? "samples";
var seed = int.Parse(Option("--seed") ?? "42", CultureInfo.InvariantCulture);

switch (args.FirstOrDefault())
{
    case "generate":
        Generate(samplesDir);
        return 0;
    case "run":
        return Run(samplesDir, args.Skip(1).FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "compact", seed);
    default:
        Console.WriteLine("""
            Usage:
              generate [--samples dir]                  write the sample layouts and seeded order histories
              run [sample] [--seed n] [--samples dir]   KPI table: every slotting x routing strategy vs random + s-shape
            """);
        return 1;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void Generate(string dir)
{
    LevelSpec[] levels = [new(Volume: 1.2, MaxWeightKg: 1000), new(Volume: 1.0, MaxWeightKg: 25), new(Volume: 0.6, MaxWeightKg: 10)];
    var compact = new WarehouseLayout { Name = "compact", Aisles = 8, BaysPerAisle = 12, Levels = levels, DepotAisle = 0 };
    var wide = new WarehouseLayout { Name = "wide", Aisles = 16, BaysPerAisle = 24, Levels = levels, DepotAisle = 8 };

    SampleCatalog.Save(dir, new Sample(compact, OrderHistoryGenerator.Generate(new GeneratorOptions(SkuCount: 400, OrdersPerDay: 40), seed: 42)));
    SampleCatalog.Save(dir, new Sample(wide, OrderHistoryGenerator.Generate(new GeneratorOptions(SkuCount: 1500, OrdersPerDay: 120), seed: 42)));
    foreach (var sample in SampleCatalog.Load(dir).All)
        Console.WriteLine($"{sample.Name}: {sample.Layout.LocationCount} locations, {sample.History.Skus.Count} SKUs, {sample.History.Orders.Count} orders");
}

static int Run(string dir, string name, int seed)
{
    if (!SampleCatalog.Load(dir).TryGet(name, out var sample))
    {
        Console.Error.WriteLine($"Unknown sample '{name}'.");
        return 1;
    }

    var clock = Stopwatch.StartNew();
    var runner = new ScenarioRunner(sample, seed);
    var history = sample.History;
    Console.WriteLine($"## {sample.Name}, seed {seed}: {sample.Layout.Aisles} aisles x {sample.Layout.BaysPerAisle} bays x {sample.Layout.Levels.Count} levels, " +
                      $"{history.Skus.Count} SKUs, {history.Orders.Count} orders over {history.Days} days");

    var report = AbcXyzAnalyzer.Analyze(history.Skus.Select(s => s.Id), history.Orders, history.Days);
    Console.WriteLine();
    Console.WriteLine("| ABC \\ XYZ | X | Y | Z |");
    Console.WriteLine("|---|--:|--:|--:|");
    foreach (var abc in Enum.GetValues<AbcClass>())
        Console.WriteLine($"| {abc} | {report.Matrix[$"{abc}X"]} | {report.Matrix[$"{abc}Y"]} | {report.Matrix[$"{abc}Z"]} |");

    var baseline = runner.Run(ScenarioRunner.DefaultBaseline);
    Console.WriteLine();
    Console.WriteLine("| Slotting | Routing | Total distance (m) | Avg per order (m) | Picks per m | Exp. travel per pick (m) | vs random + s-shape |");
    Console.WriteLine("|---|---|--:|--:|--:|--:|--:|");
    foreach (var slotting in SlottingStrategies.All)
    foreach (var routing in RoutingStrategies.All)
    {
        var k = runner.Run(new ScenarioConfig(slotting.Name, routing.Name));
        Console.WriteLine($"| {k.Slotting} | {k.Routing} | {k.TotalDistance:F0} | {k.AvgDistancePerOrder:F1} | {k.PicksPerMeter:F4} | " +
                          $"{k.ExpectedTravelPerPick:F1} | {ScenarioRunner.Percent(baseline.TotalDistance, k.TotalDistance):F1} % |");
    }

    var capacity = Math.Max(20, history.MaxLinesPerOrder());
    Console.WriteLine();
    Console.WriteLine($"Order batching, cart capacity {capacity} lines:");
    Console.WriteLine();
    Console.WriteLine("| Slotting | Routing | Single-order (m) | Batched (m) | Batches | Saving |");
    Console.WriteLine("|---|---|--:|--:|--:|--:|");
    foreach (var config in new[] { ScenarioRunner.DefaultBaseline, new ScenarioConfig("coi", "2-opt"), new ScenarioConfig("coi-swap", "2-opt") })
    {
        var k = runner.Run(config with { CartCapacity = capacity });
        Console.WriteLine($"| {k.Slotting} | {k.Routing} | {k.TotalDistance:F0} | {k.Batching!.TotalDistance:F0} | {k.Batching.Batches} | {k.Batching.SavingPercentVsSingleOrder:F1} % |");
    }

    Console.WriteLine();
    Console.WriteLine($"Finished in {clock.Elapsed.TotalSeconds:F1} s.");
    return 0;
}
