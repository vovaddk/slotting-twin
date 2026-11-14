using SlottingTwin.Core.Layout;
using SlottingTwin.Core.Model;

namespace SlottingTwin.Core.Slotting;

public sealed record SlottingContext(WarehouseGraph Graph, IReadOnlyList<Sku> Skus, DemandProfile Demand, int Seed = 0);

/// <summary>Which storage location holds each SKU (one SKU per location).</summary>
public sealed record SlottingPlan(string Strategy, IReadOnlyDictionary<string, StorageLocation> Assignments)
{
    /// <summary>Pick-weighted mean round trip depot → SKU → depot, the classic single-command slotting objective.</summary>
    public double ExpectedTravelPerPick(WarehouseGraph graph, DemandProfile demand)
    {
        double weighted = 0;
        long picks = 0;
        foreach (var (sku, location) in Assignments)
        {
            var frequency = demand.PicksOf(sku);
            weighted += frequency * 2 * graph.Distance(graph.Depot, location.Node);
            picks += frequency;
        }
        return picks == 0 ? 0 : weighted / picks;
    }
}

/// <summary>Strategy pattern: one way of assigning SKUs to storage locations.</summary>
public interface ISlottingStrategy
{
    string Name { get; }

    SlottingPlan Assign(SlottingContext context);
}

public static class SlottingStrategies
{
    public static IReadOnlyList<ISlottingStrategy> All { get; } =
        [new RandomSlotting(), new AlphabeticalSlotting(), new CoiSlotting(), new CoOccurrenceSwapSlotting()];

    public static bool Exists(string? name) => All.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ISlottingStrategy Get(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown slotting strategy '{name}'.", nameof(name));
}

internal static class SlotFiller
{
    /// <summary>
    /// Gives each SKU, in priority order, the first free location (in preference order) that fits it.
    /// shortcut: greedy first-fit can fail on tight instances a matching algorithm would solve; fine while slots are plentiful.
    /// </summary>
    public static Dictionary<string, StorageLocation> Fill(IEnumerable<Sku> skus, IReadOnlyList<StorageLocation> locations)
    {
        var used = new bool[locations.Count];
        var plan = new Dictionary<string, StorageLocation>(StringComparer.Ordinal);
        foreach (var sku in skus)
        {
            var index = -1;
            for (var i = 0; i < locations.Count && index < 0; i++)
                if (!used[i] && locations[i].Fits(sku)) index = i;
            if (index < 0)
                throw new InvalidOperationException(
                    $"No free location fits SKU '{sku.Id}' (volume {sku.Volume} m³, {sku.WeightKg} kg).");
            used[index] = true;
            plan.Add(sku.Id, locations[index]);
        }
        return plan;
    }
}
