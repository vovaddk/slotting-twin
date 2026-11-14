using SlottingTwin.Core.Analysis;
using SlottingTwin.Core.Layout;

namespace SlottingTwin.Api;

public sealed record LayoutSummary(
    string Name,
    int Aisles,
    int BaysPerAisle,
    int Levels,
    int Locations,
    double AisleSpacing,
    double BayWidth,
    bool HasBackCrossAisle,
    int DepotAisle,
    int Skus,
    int Orders,
    int Days,
    IReadOnlyList<string> OrderIds);

public sealed record StrategiesResponse(IReadOnlyList<string> Slotting, IReadOnlyList<string> Routing);

/// <param name="CartCapacity">Optional: also batch orders into carts of this many lines (1–100).</param>
public sealed record SimulateRequest(
    string? Layout,
    string? Slotting,
    string? Routing,
    int Seed = 42,
    int? CartCapacity = null,
    string BaselineSlotting = "random",
    string BaselineRouting = "s-shape");

/// <param name="OrderId">Order to route; when omitted a random order is chosen with <paramref name="Seed"/>.</param>
public sealed record RouteRequest(string? Layout, string? Slotting, string? Routing, int Seed = 42, string? OrderId = null);

public sealed record Point(double X, double Y);

/// <param name="Stop">1-based position of this pick's bay in the route; lines in the same bay share a stop.</param>
public sealed record RoutePick(string Sku, string Location, int Aisle, RackSide Side, double X, double Y, int Stop);

public sealed record RouteResponse(
    string OrderId,
    string Slotting,
    string Routing,
    double Distance,
    Point Depot,
    IReadOnlyList<RoutePick> Picks,
    IReadOnlyList<Point> Path);

public sealed record LocationHeat(
    string Id, int Aisle, int Bay, int Level, RackSide Side, double X, double Y, string? Sku, int Picks, AbcClass? Abc, XyzClass? Xyz);

public sealed record AbcResponse(
    string Layout,
    string Slotting,
    IReadOnlyDictionary<string, int> Matrix,
    double ExpectedTravelPerPick,
    IReadOnlyList<LocationHeat> Locations);
