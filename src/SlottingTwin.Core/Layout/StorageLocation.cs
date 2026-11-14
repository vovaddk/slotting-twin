using SlottingTwin.Core.Model;

namespace SlottingTwin.Core.Layout;

public enum RackSide
{
    Left,
    Right,
}

/// <summary>
/// One pick slot. <see cref="Aisle"/>, <see cref="Bay"/> and <see cref="Level"/> are zero-based;
/// the human-readable <see cref="Id"/> is one-based, e.g. <c>A01-B03-L1-R</c>.
/// </summary>
public sealed record StorageLocation(string Id, int Aisle, int Bay, int Level, RackSide Side, int Node, LevelSpec Spec)
{
    public bool Fits(Sku sku) => sku.Volume <= Spec.Volume && sku.WeightKg <= Spec.MaxWeightKg;
}
