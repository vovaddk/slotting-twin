using SlottingTwin.Core.Analysis;
using SlottingTwin.Core.Model;

namespace SlottingTwin.Tests;

public class AbcXyzAnalyzerTests
{
    private static readonly AbcXyzOptions Daily = new(PeriodDays: 1);

    [Fact]
    public void Pareto_thresholds_split_a_known_dataset()
    {
        // 100 picks: S1 50, S2 30 (A = first 80 %), S3 10, S4 5 (B = next 15 %), S5 3, S6 2 (C).
        var orders = PicksPerSku(("S1", 50), ("S2", 30), ("S3", 10), ("S4", 5), ("S5", 3), ("S6", 2));

        var report = AbcXyzAnalyzer.Analyze(["S1", "S2", "S3", "S4", "S5", "S6"], orders, days: 1, Daily);

        report.Items.Select(i => (i.Sku, i.Abc)).ShouldBe(
        [
            ("S1", AbcClass.A), ("S2", AbcClass.A), ("S3", AbcClass.B), ("S4", AbcClass.B), ("S5", AbcClass.C), ("S6", AbcClass.C),
        ]);
        report.Items[1].CumulativeShare.ShouldBe(0.8, 1e-12);
        report.Items[^1].CumulativeShare.ShouldBe(1.0, 1e-12);
    }

    [Fact]
    public void The_sku_that_crosses_the_threshold_is_still_class_A()
    {
        var orders = PicksPerSku(("S1", 90), ("S2", 10));

        var report = AbcXyzAnalyzer.Analyze(["S1", "S2"], orders, days: 1, Daily);

        report.Items.Select(i => i.Abc).ShouldBe([AbcClass.A, AbcClass.B]);
    }

    [Fact]
    public void Custom_thresholds_are_respected()
    {
        var orders = PicksPerSku(("S1", 50), ("S2", 30), ("S3", 20));

        var report = AbcXyzAnalyzer.Analyze(["S1", "S2", "S3"], orders, days: 1, new AbcXyzOptions(AShare: 0.5, BShare: 0.8, PeriodDays: 1));

        report.Items.Select(i => i.Abc).ShouldBe([AbcClass.A, AbcClass.B, AbcClass.C]);
    }

    [Fact]
    public void Xyz_uses_the_coefficient_of_variation_of_demand_per_period()
    {
        // Daily quantities over 4 days: steady 10,10,10,10 (CV 0), wavy 2,18,2,18 (CV 0.8), spike 40,0,0,0 (CV √3).
        int[][] quantities = [[10, 10, 10, 10], [2, 18, 2, 18], [40, 0, 0, 0]];
        string[] skus = ["steady", "wavy", "spike"];
        var orders = new List<Order>();
        for (var s = 0; s < skus.Length; s++)
        for (var day = 0; day < 4; day++)
            if (quantities[s][day] > 0) orders.Add(TestData.Order($"{skus[s]}-{day}", day, (skus[s], quantities[s][day])));

        var items = AbcXyzAnalyzer.Analyze(skus, orders, days: 4, Daily).Items.ToDictionary(i => i.Sku);

        items["steady"].CoefficientOfVariation.ShouldBe(0);
        items["steady"].Xyz.ShouldBe(XyzClass.X);
        items["wavy"].CoefficientOfVariation!.Value.ShouldBe(0.8, 1e-12);
        items["wavy"].Xyz.ShouldBe(XyzClass.Y);
        items["spike"].CoefficientOfVariation!.Value.ShouldBe(Math.Sqrt(3), 1e-12);
        items["spike"].Xyz.ShouldBe(XyzClass.Z);
    }

    [Fact]
    public void Weekly_periods_smooth_out_a_weekday_pattern()
    {
        // Ordered every Monday only: erratic per day, perfectly steady per week.
        Order[] orders = [TestData.Order("w1", 0, ("mon", 7)), TestData.Order("w2", 7, ("mon", 7))];

        AbcXyzAnalyzer.Analyze(["mon"], orders, days: 14, Daily).Items[0].Xyz.ShouldBe(XyzClass.Z);
        AbcXyzAnalyzer.Analyze(["mon"], orders, days: 14, new AbcXyzOptions(PeriodDays: 7)).Items[0].Xyz.ShouldBe(XyzClass.X);
    }

    [Fact]
    public void A_shorter_last_period_is_compared_by_rate_not_total()
    {
        // 10 days of 1 unit per day = weeks of 7 and 3 days; per-day rate is identical, so CV must be 0.
        var orders = Enumerable.Range(0, 10).Select(day => TestData.Order($"o{day}", day, ("sku", 1)));

        AbcXyzAnalyzer.Analyze(["sku"], orders, days: 10).Items[0].CoefficientOfVariation!.Value.ShouldBe(0, 1e-12);
    }

    [Fact]
    public void Sku_without_demand_is_C_and_Z_without_a_cv()
    {
        var report = AbcXyzAnalyzer.Analyze(["busy", "idle"], PicksPerSku(("busy", 3)), days: 1, Daily);

        var idle = report.Items.Single(i => i.Sku == "idle");
        idle.Picks.ShouldBe(0);
        idle.Abc.ShouldBe(AbcClass.C);
        idle.Xyz.ShouldBe(XyzClass.Z);
        idle.CoefficientOfVariation.ShouldBeNull();
    }

    [Fact]
    public void Matrix_has_all_nine_cells_and_counts_every_sku_once()
    {
        var report = AbcXyzAnalyzer.Analyze(["S1", "S2", "S3", "S4"], PicksPerSku(("S1", 8), ("S2", 1), ("S3", 1)), days: 1, Daily);

        report.Matrix.Keys.Order().ShouldBe(["AX", "AY", "AZ", "BX", "BY", "BZ", "CX", "CY", "CZ"]);
        report.Matrix.Values.Sum().ShouldBe(4);
        report.Matrix["AX"].ShouldBe(1);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Should.Throw<ArgumentException>(() => AbcXyzAnalyzer.Analyze(["S1"], [TestData.Order("o", 0, ("ghost", 1))], days: 1, Daily));
        Should.Throw<ArgumentException>(() => AbcXyzAnalyzer.Analyze(["S1"], [TestData.Order("o", 5, ("S1", 1))], days: 5, Daily));
        Should.Throw<ArgumentException>(() => AbcXyzAnalyzer.Analyze(["S1"], [], days: 3, new AbcXyzOptions(AShare: 0.9, BShare: 0.8, PeriodDays: 1)));
        Should.Throw<ArgumentException>(() => AbcXyzAnalyzer.Analyze(["S1"], [], days: 3, new AbcXyzOptions(PeriodDays: 4)));
    }

    private static List<Order> PicksPerSku(params (string Sku, int Picks)[] skus) =>
        [.. skus.SelectMany(s => Enumerable.Range(0, s.Picks).Select(i => TestData.Order($"{s.Sku}-{i}", 0, (s.Sku, 1))))];
}
