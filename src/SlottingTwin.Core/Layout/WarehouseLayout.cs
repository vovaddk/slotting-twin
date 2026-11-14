using System.Text.Json.Serialization;

namespace SlottingTwin.Core.Layout;

/// <summary>Slot volume (m³) and weight limit (kg) of one rack level. Low weight limits on high levels keep heavy items low.</summary>
public sealed record LevelSpec(double Volume, double MaxWeightKg);

/// <summary>
/// Parallel-aisle warehouse: aisles run front to back with racks on both sides, joined by a front cross-aisle
/// and optionally a back cross-aisle. The depot sits on the front cross-aisle at the head of <see cref="DepotAisle"/>.
/// </summary>
public sealed record WarehouseLayout
{
    public const int MaxAisles = 40;
    public const int MaxBaysPerAisle = 60;
    public const int MaxLevels = 8;

    public required string Name { get; init; }
    public required int Aisles { get; init; }
    public required int BaysPerAisle { get; init; }

    /// <summary>Rack levels from the floor (index 0) upwards.</summary>
    public required IReadOnlyList<LevelSpec> Levels { get; init; }

    /// <summary>Distance between the centre lines of neighbouring aisles, in metres.</summary>
    public double AisleSpacing { get; init; } = 4;

    /// <summary>Length of one bay along the aisle, in metres.</summary>
    public double BayWidth { get; init; } = 1;

    public bool HasBackCrossAisle { get; init; } = true;
    public int DepotAisle { get; init; }

    /// <summary>Two rack sides per aisle, one slot per bay, side and level.</summary>
    [JsonIgnore]
    public int LocationCount => Aisles * BaysPerAisle * Levels.Count * 2;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Name is required.");
        if (Aisles is < 1 or > MaxAisles) errors.Add($"Aisles must be between 1 and {MaxAisles}.");
        if (BaysPerAisle is < 1 or > MaxBaysPerAisle) errors.Add($"BaysPerAisle must be between 1 and {MaxBaysPerAisle}.");
        if (Levels is null || Levels.Count is < 1 or > MaxLevels) errors.Add($"Levels must contain between 1 and {MaxLevels} entries.");
        else if (Levels.Any(l => l is null || !(l.Volume > 0) || !(l.MaxWeightKg > 0))) errors.Add("Every level needs a positive volume and weight limit.");
        if (AisleSpacing is not (> 0 and <= 20)) errors.Add("AisleSpacing must be in (0, 20] metres.");
        if (BayWidth is not (> 0 and <= 10)) errors.Add("BayWidth must be in (0, 10] metres.");
        if (DepotAisle < 0 || DepotAisle >= Aisles) errors.Add("DepotAisle must be an existing aisle index.");
        return errors;
    }
}
