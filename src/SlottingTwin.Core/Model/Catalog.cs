namespace SlottingTwin.Core.Model;

/// <summary>A stock-keeping unit. Volume is the slot space it needs (m³); weight decides which rack levels may hold it.</summary>
public sealed record Sku(string Id, double Volume, double WeightKg);

public sealed record OrderLine(string Sku, int Quantity);

/// <summary>A customer order. <see cref="Day"/> is the zero-based day of the order history it belongs to.</summary>
public sealed record Order(string Id, int Day, IReadOnlyList<OrderLine> Lines);
