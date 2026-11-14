using SlottingTwin.Core.Model;

namespace SlottingTwin.Simulation;

/// <summary>SKU catalogue plus the orders of <see cref="Days"/> consecutive days.</summary>
public sealed record OrderHistory(int Days, IReadOnlyList<Sku> Skus, IReadOnlyList<Order> Orders)
{
    public const int MaxDays = 366;
    public const int MaxSkus = 20_000;
    public const int MaxOrders = 100_000;

    public int MaxLinesPerOrder() => Orders.Count == 0 ? 0 : Orders.Max(o => o.Lines.Count);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Days is < 1 or > MaxDays) errors.Add($"Days must be between 1 and {MaxDays}.");
        if (Skus is null || Orders is null) return [.. errors, "Skus and Orders are required."];
        if (Skus.Count is < 1 or > MaxSkus) errors.Add($"Skus must contain between 1 and {MaxSkus} entries.");
        if (Orders.Count > MaxOrders) errors.Add($"Orders must contain at most {MaxOrders} entries.");
        if (errors.Count > 0) return errors;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sku in Skus)
        {
            if (string.IsNullOrWhiteSpace(sku.Id) || !ids.Add(sku.Id)) errors.Add($"SKU id '{sku.Id}' is empty or duplicated.");
            if (!(sku.Volume > 0) || !(sku.WeightKg > 0)) errors.Add($"SKU '{sku.Id}' needs positive volume and weight.");
        }
        foreach (var order in Orders)
        {
            if (order.Day < 0 || order.Day >= Days) errors.Add($"Order {order.Id} has day {order.Day} outside 0..{Days - 1}.");
            if (order.Lines is null || order.Lines.Count == 0) errors.Add($"Order {order.Id} has no lines.");
            else if (order.Lines.Any(l => !ids.Contains(l.Sku) || l.Quantity < 1)) errors.Add($"Order {order.Id} has an unknown SKU or a non-positive quantity.");
        }
        return errors;
    }
}
