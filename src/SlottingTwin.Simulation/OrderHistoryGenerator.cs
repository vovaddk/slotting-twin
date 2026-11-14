using SlottingTwin.Core.Model;

namespace SlottingTwin.Simulation;

/// <param name="FamilyOrderShare">Share of orders built around a product family (SKUs usually bought together).</param>
public sealed record GeneratorOptions(
    int SkuCount, int Days = 28, int OrdersPerDay = 50, int MaxLinesPerOrder = 8, double FamilyOrderShare = 0.35);

/// <summary>
/// Synthetic, seeded order history with the properties slotting cares about:
/// Zipf-like popularity (Pareto-shaped ABC curve), steady / weekly-seasonal / sporadic SKUs (XYZ spread),
/// product families that co-occur in orders, and a few heavy or bulky SKUs that only fit low rack levels.
/// </summary>
public static class OrderHistoryGenerator
{
    private const int FamilySize = 4;

    public static OrderHistory Generate(GeneratorOptions options, int seed)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SkuCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.SkuCount, OrderHistory.MaxSkus);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Days, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Days, OrderHistory.MaxDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.OrdersPerDay, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxLinesPerOrder, 1);
        if (options.FamilyOrderShare is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(options), "FamilyOrderShare must be in [0, 1].");

        var random = new Random(seed);
        var n = options.SkuCount;
        var skus = Enumerable.Range(1, n).Select(i => NewSku(i, random)).ToArray();

        // Popularity rank is shuffled so it is unrelated to the SKU id (otherwise alphabetical slotting would cheat).
        var ranks = Enumerable.Range(1, n).ToArray();
        random.Shuffle(ranks);
        var demand = new double[n][];
        for (var i = 0; i < n; i++) demand[i] = DailyMultipliers(options.Days, random).Select(m => m / ranks[i]).ToArray();

        var order = Enumerable.Range(0, n).ToArray();
        random.Shuffle(order);
        var family = new int[n][];
        foreach (var chunk in order.Chunk(FamilySize))
            foreach (var member in chunk) family[member] = chunk;

        var orders = new List<Order>();
        var cumulative = new double[n];
        for (var day = 0; day < options.Days; day++)
        {
            var total = 0.0;
            for (var i = 0; i < n; i++) cumulative[i] = total += demand[i][day];

            var count = (int)Math.Round(options.OrdersPerDay * (0.8 + 0.4 * random.NextDouble()));
            for (var k = 0; k < count; k++)
            {
                var picked = NewOrder(options, random, () => Sample(cumulative, total, random), family);
                var lines = picked.Select(i => new OrderLine(skus[i].Id, 1 + random.Next(4))).ToArray();
                orders.Add(new Order($"ORD-{day + 1:000}-{k + 1:0000}", day, lines));
            }
        }
        return new OrderHistory(options.Days, skus, orders);
    }

    private static Sku NewSku(int number, Random random)
    {
        var volume = random.NextDouble() < 0.05 ? 0.8 + 0.4 * random.NextDouble() : 0.05 + 0.45 * random.NextDouble();
        var weightRoll = random.NextDouble();
        var weight = weightRoll < 0.06 ? 26 + 34 * random.NextDouble()
            : weightRoll < 0.20 ? 10 + 15 * random.NextDouble()
            : 0.2 + 9.8 * random.NextDouble();
        return new Sku($"SKU-{number:0000}", Math.Round(volume, 3), Math.Round(weight, 1));
    }

    /// <summary>Half the SKUs are steady, 30 % weekly-seasonal, 20 % sporadic (ordered on roughly one day in five).</summary>
    private static double[] DailyMultipliers(int days, Random random)
    {
        var kind = random.NextDouble();
        var phase = random.NextDouble() * 2 * Math.PI;
        var multipliers = new double[days];
        for (var day = 0; day < days; day++)
        {
            multipliers[day] = kind < 0.5 ? 1
                : kind < 0.8 ? 1 + 0.9 * Math.Sin(2 * Math.PI * day / 7 + phase)
                : random.NextDouble() < 0.2 ? 5 : 0;
        }
        return multipliers;
    }

    private static List<int> NewOrder(GeneratorOptions options, Random random, Func<int> sample, int[][] family)
    {
        var lines = 1;
        while (lines < options.MaxLinesPerOrder && random.NextDouble() < 0.55) lines++;

        var picked = new List<int> { sample() };
        if (options.MaxLinesPerOrder > 1 && random.NextDouble() < options.FamilyOrderShare)
        {
            var mates = family[picked[0]].Where(m => m != picked[0]).ToArray();
            random.Shuffle(mates);
            picked.AddRange(mates.Take(Math.Max(lines, 2) - 1));
        }
        for (var attempt = 0; picked.Count < lines && attempt < 50; attempt++)
        {
            var next = sample();
            if (!picked.Contains(next)) picked.Add(next);
        }
        return picked;
    }

    /// <summary>Index of the first cumulative weight above a uniform draw (zero-weight SKUs are never chosen).</summary>
    private static int Sample(double[] cumulative, double total, Random random)
    {
        var x = random.NextDouble() * total;
        int lo = 0, hi = cumulative.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (cumulative[mid] > x) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }
}
