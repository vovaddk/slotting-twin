using SlottingTwin.Core.Model;

namespace SlottingTwin.Core.Analysis;

public enum AbcClass
{
    A,
    B,
    C,
}

public enum XyzClass
{
    X,
    Y,
    Z,
}

/// <param name="AShare">Cumulative pick share covered by class A (Pareto 80 %).</param>
/// <param name="BShare">Cumulative pick share covered by classes A and B (80 + 15 %).</param>
/// <param name="XMaxCv">Highest coefficient of variation of daily demand that still counts as X (steady).</param>
/// <param name="YMaxCv">Highest coefficient of variation that counts as Y (fluctuating); above it is Z (erratic).</param>
/// <param name="PeriodDays">Demand is bucketed into periods of this many days before the CV is taken (7 = weekly demand).</param>
public sealed record AbcXyzOptions(double AShare = 0.80, double BShare = 0.95, double XMaxCv = 0.5, double YMaxCv = 1.0, int PeriodDays = 7);

/// <param name="CumulativeShare">Pick share of this SKU and all SKUs ranked above it.</param>
/// <param name="CoefficientOfVariation">Std. deviation / mean of per-period demand rate; null when the SKU has no demand.</param>
public sealed record SkuClassification(
    string Sku, int Picks, double CumulativeShare, double? CoefficientOfVariation, AbcClass Abc, XyzClass Xyz);

/// <param name="Matrix">SKU count per cell, keyed "AX" … "CZ"; all nine keys are present.</param>
public sealed record AbcXyzReport(IReadOnlyList<SkuClassification> Items, IReadOnlyDictionary<string, int> Matrix);

/// <summary>
/// ABC by pick frequency (order lines) and XYZ by variability of demanded quantity per period.
/// A SKU is A while the share of picks ranked above it is still below <see cref="AbcXyzOptions.AShare"/>, so the
/// SKU that crosses the threshold still belongs to A. SKUs without picks are always C / Z.
/// </summary>
public static class AbcXyzAnalyzer
{
    public static AbcXyzReport Analyze(IEnumerable<string> skus, IEnumerable<Order> orders, int days, AbcXyzOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(skus);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        options ??= new AbcXyzOptions();
        if (options.AShare is not (> 0 and < 1) || options.BShare <= options.AShare || options.BShare > 1)
            throw new ArgumentException("Expected 0 < AShare < BShare <= 1.", nameof(options));
        if (options.XMaxCv < 0 || options.YMaxCv <= options.XMaxCv)
            throw new ArgumentException("Expected 0 <= XMaxCv < YMaxCv.", nameof(options));
        if (options.PeriodDays < 1 || options.PeriodDays > days)
            throw new ArgumentException($"PeriodDays must be between 1 and the {days}-day history length.", nameof(options));

        var ids = skus.Distinct(StringComparer.Ordinal).ToArray();
        var periods = (days + options.PeriodDays - 1) / options.PeriodDays;
        var demand = ids.ToDictionary(id => id, _ => new double[periods], StringComparer.Ordinal);
        var picks = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (order.Day < 0 || order.Day >= days)
                throw new ArgumentException($"Order {order.Id} is on day {order.Day}, outside the {days}-day period.", nameof(orders));
            foreach (var line in order.Lines)
            {
                if (!demand.TryGetValue(line.Sku, out var series))
                    throw new ArgumentException($"Order {order.Id} references unknown SKU '{line.Sku}'.", nameof(orders));
                series[order.Day / options.PeriodDays] += line.Quantity;
                picks[line.Sku]++;
            }
        }

        // A shorter last period is compared by demand per day, not by its raw total.
        var lastPeriodDays = days - (periods - 1) * options.PeriodDays;
        foreach (var series in demand.Values) series[^1] *= (double)options.PeriodDays / lastPeriodDays;

        var total = (double)picks.Values.Sum();
        var picksAbove = 0;
        var items = new List<SkuClassification>(ids.Length);
        foreach (var id in ids.OrderByDescending(id => picks[id]).ThenBy(id => id, StringComparer.Ordinal))
        {
            var abc = picks[id] == 0 ? AbcClass.C
                : picksAbove < options.AShare * total ? AbcClass.A
                : picksAbove < options.BShare * total ? AbcClass.B
                : AbcClass.C;
            picksAbove += picks[id];

            var cv = CoefficientOfVariation(demand[id]);
            var xyz = cv is null ? XyzClass.Z : cv <= options.XMaxCv ? XyzClass.X : cv <= options.YMaxCv ? XyzClass.Y : XyzClass.Z;
            items.Add(new SkuClassification(id, picks[id], total == 0 ? 0 : picksAbove / total, cv, abc, xyz));
        }

        var matrix = (from a in Enum.GetValues<AbcClass>()
                      from x in Enum.GetValues<XyzClass>()
                      select $"{a}{x}").ToDictionary(key => key, key => items.Count(i => $"{i.Abc}{i.Xyz}" == key));
        return new AbcXyzReport(items, matrix);
    }

    /// <summary>Population coefficient of variation; null for an all-zero series.</summary>
    public static double? CoefficientOfVariation(IReadOnlyList<double> series)
    {
        ArgumentOutOfRangeException.ThrowIfZero(series.Count);
        var mean = series.Average();
        if (mean == 0) return null;
        var variance = series.Sum(v => (v - mean) * (v - mean)) / series.Count;
        return Math.Sqrt(variance) / mean;
    }
}
