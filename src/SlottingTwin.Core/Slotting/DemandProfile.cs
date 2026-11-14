using SlottingTwin.Core.Model;

namespace SlottingTwin.Core.Slotting;

/// <summary>Pick frequency per SKU (order lines) and how often pairs of SKUs share an order.</summary>
public sealed class DemandProfile
{
    private readonly Dictionary<string, int> _picks;

    private DemandProfile(Dictionary<string, int> picks, Dictionary<(string A, string B), int> pairs)
    {
        _picks = picks;
        Pairs = pairs;
    }

    public IReadOnlyDictionary<string, int> Picks => _picks;

    /// <summary>Number of orders containing both SKUs; key items are in ordinal order (A &lt; B).</summary>
    public IReadOnlyDictionary<(string A, string B), int> Pairs { get; }

    public int PicksOf(string sku) => _picks.GetValueOrDefault(sku);

    public static DemandProfile From(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var picks = new Dictionary<string, int>(StringComparer.Ordinal);
        var pairs = new Dictionary<(string, string), int>();
        foreach (var order in orders)
        {
            foreach (var line in order.Lines) picks[line.Sku] = picks.GetValueOrDefault(line.Sku) + 1;

            var skus = order.Lines.Select(l => l.Sku).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            for (var i = 0; i < skus.Length; i++)
            for (var j = i + 1; j < skus.Length; j++)
                pairs[(skus[i], skus[j])] = pairs.GetValueOrDefault((skus[i], skus[j])) + 1;
        }
        return new DemandProfile(picks, pairs);
    }
}
