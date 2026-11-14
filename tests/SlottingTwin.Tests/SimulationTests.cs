using System.Text.Json;
using SlottingTwin.Core.Analysis;
using SlottingTwin.Simulation;

namespace SlottingTwin.Tests;

public class SimulationTests
{
    [Fact]
    public void Generator_is_deterministic_for_a_seed()
    {
        var options = new GeneratorOptions(SkuCount: 100, Days: 7, OrdersPerDay: 20);
        string Json(int seed) => JsonSerializer.Serialize(OrderHistoryGenerator.Generate(options, seed), SampleCatalog.JsonOptions);

        Json(11).ShouldBe(Json(11));
        Json(11).ShouldNotBe(Json(12));
    }

    [Fact]
    public void Generated_history_is_valid_and_bounded()
    {
        var options = new GeneratorOptions(SkuCount: 150, Days: 10, OrdersPerDay: 25, MaxLinesPerOrder: 6);

        var history = OrderHistoryGenerator.Generate(options, seed: 3);

        history.Validate().ShouldBeEmpty();
        history.Days.ShouldBe(10);
        history.Skus.Count.ShouldBe(150);
        history.MaxLinesPerOrder().ShouldBeLessThanOrEqualTo(6);
        history.Orders.ShouldAllBe(o => o.Lines.Select(l => l.Sku).Distinct().Count() == o.Lines.Count);
    }

    [Fact]
    public void Generated_demand_is_pareto_shaped()
    {
        var history = TestData.GeneratedSample().History;

        var report = AbcXyzAnalyzer.Analyze(history.Skus.Select(s => s.Id), history.Orders, history.Days);

        var aShare = report.Items.Count(i => i.Abc == AbcClass.A) / (double)report.Items.Count;
        aShare.ShouldBeLessThan(0.4); // 80 % of picks come from well under half of the SKUs
    }

    [Fact]
    public void Samples_round_trip_through_json_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotting-twin-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var sample = TestData.GeneratedSample();
            SampleCatalog.Save(directory, sample);

            SampleCatalog.Load(directory).TryGet("GENERATED", out var loaded).ShouldBeTrue();

            loaded!.Layout.ShouldBe(sample.Layout with { Levels = loaded.Layout.Levels });
            loaded.Layout.Levels.ShouldBe(sample.Layout.Levels);
            loaded.History.Orders.Count.ShouldBe(sample.History.Orders.Count);
            loaded.History.Orders[0].Lines.ShouldBe(sample.History.Orders[0].Lines);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Shipped_samples_load_and_validate()
    {
        var catalog = SampleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "samples"));

        catalog.All.Select(s => s.Name).ShouldBe(["compact", "wide"]);
        catalog.All.ShouldAllBe(s => SampleCatalog.Validate(s).Count == 0);
    }

    [Fact]
    public void Scenario_kpis_are_deterministic_and_consistent()
    {
        var sample = TestData.GeneratedSample();
        var config = new ScenarioConfig("random", "largest-gap");

        var first = new ScenarioRunner(sample, seed: 9).Run(config);
        var second = new ScenarioRunner(sample, seed: 9).Run(config);

        second.ShouldBe(first);
        first.Orders.ShouldBe(sample.History.Orders.Count);
        first.Picks.ShouldBe(sample.History.Orders.Sum(o => o.Lines.Count));
        first.AvgDistancePerOrder.ShouldBe(first.TotalDistance / first.Orders, 1e-9);
        first.PicksPerMeter.ShouldBe(first.Picks / first.TotalDistance, 1e-12);
    }

    [Fact]
    public void Optimized_slotting_and_routing_beat_the_random_s_shape_baseline()
    {
        var runner = new ScenarioRunner(TestData.GeneratedSample(), seed: 42);

        var comparison = runner.Compare(new ScenarioConfig("coi", "2-opt"));

        comparison.Baseline.Slotting.ShouldBe("random");
        comparison.Baseline.Routing.ShouldBe("s-shape");
        comparison.ImprovementPercent.ShouldBeGreaterThan(20);
        comparison.ImprovementPercent.ShouldBe(ScenarioRunner.Percent(comparison.Baseline.TotalDistance, comparison.Candidate.TotalDistance));
    }
}
