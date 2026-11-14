using Microsoft.AspNetCore.Http.HttpResults;
using SlottingTwin.Core.Analysis;
using SlottingTwin.Core.Routing;
using SlottingTwin.Core.Slotting;
using SlottingTwin.Simulation;

namespace SlottingTwin.Api;

public static class SlottingApi
{
    public const int MaxCartCapacity = 100;

    public static IEndpointRouteBuilder MapSlottingApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/layouts", GetLayouts).WithSummary("Sample layouts with their order histories.");
        api.MapGet("/strategies", GetStrategies).WithSummary("Names of the slotting and routing strategies.");
        api.MapGet("/abc", GetAbc).WithSummary("ABC-XYZ matrix and per-location heat data for one slotting strategy.");
        api.MapPost("/simulate", Simulate).WithSummary("Replay the whole order history and compare KPIs with a baseline.");
        api.MapPost("/route", Route).WithSummary("Pick route for one order, with coordinates for drawing.");
        return app;
    }

    internal static LayoutSummary[] GetLayouts(SampleCatalog catalog) =>
        [.. catalog.All.Select(s => new LayoutSummary(
            s.Name, s.Layout.Aisles, s.Layout.BaysPerAisle, s.Layout.Levels.Count, s.Layout.LocationCount,
            s.Layout.AisleSpacing, s.Layout.BayWidth, s.Layout.HasBackCrossAisle, s.Layout.DepotAisle,
            s.History.Skus.Count, s.History.Orders.Count, s.History.Days, [.. s.History.Orders.Select(o => o.Id)]))];

    internal static StrategiesResponse GetStrategies() =>
        new([.. SlottingStrategies.All.Select(s => s.Name)], [.. RoutingStrategies.All.Select(s => s.Name)]);

    internal static Results<Ok<AbcResponse>, ValidationProblem> GetAbc(
        SampleCatalog catalog, string? layout, string slotting = "coi", int seed = 42)
    {
        var errors = new RequestErrors()
            .Check(catalog.TryGet(layout, out var sample), "layout", $"Unknown layout '{layout}'.")
            .Check(SlottingStrategies.Exists(slotting), "slotting", $"Unknown slotting strategy '{slotting}'.");
        if (errors.Any) return errors.Problem();

        var history = sample!.History;
        var report = AbcXyzAnalyzer.Analyze(history.Skus.Select(s => s.Id), history.Orders, history.Days);
        var classes = report.Items.ToDictionary(i => i.Sku, StringComparer.Ordinal);
        var runner = new ScenarioRunner(sample, seed);
        var plan = runner.Plan(slotting);
        var skuAt = plan.Assignments.ToDictionary(a => a.Value.Id, a => a.Key, StringComparer.Ordinal);

        var locations = runner.Graph.Locations.Select(location =>
        {
            var sku = skuAt.GetValueOrDefault(location.Id);
            var item = sku is null ? null : classes[sku];
            var (x, y) = runner.Graph.Position(location.Node);
            return new LocationHeat(location.Id, location.Aisle, location.Bay, location.Level, location.Side, x, y,
                sku, item?.Picks ?? 0, item?.Abc, item?.Xyz);
        }).ToArray();

        return TypedResults.Ok(new AbcResponse(sample.Name, plan.Strategy, report.Matrix,
            plan.ExpectedTravelPerPick(runner.Graph, runner.Demand), locations));
    }

    internal static Results<Ok<ScenarioComparison>, ValidationProblem> Simulate(SimulateRequest request, SampleCatalog catalog)
    {
        var errors = new RequestErrors()
            .Check(catalog.TryGet(request.Layout, out var sample), "layout", $"Unknown layout '{request.Layout}'.")
            .CheckStrategies(request.Slotting, request.Routing)
            .Check(SlottingStrategies.Exists(request.BaselineSlotting), "baselineSlotting", $"Unknown slotting strategy '{request.BaselineSlotting}'.")
            .Check(RoutingStrategies.Exists(request.BaselineRouting), "baselineRouting", $"Unknown routing strategy '{request.BaselineRouting}'.")
            .Check(request.CartCapacity is null or (>= 1 and <= MaxCartCapacity), "cartCapacity", $"Cart capacity must be between 1 and {MaxCartCapacity} lines.");
        if (!errors.Any && request.CartCapacity is { } capacity && capacity < sample!.History.MaxLinesPerOrder())
            errors.Check(false, "cartCapacity", $"Cart capacity must hold the largest order ({sample.History.MaxLinesPerOrder()} lines).");
        if (!errors.Any)
            errors.CheckLayoutSupports(sample!, "routing", request.Routing!).CheckLayoutSupports(sample!, "baselineRouting", request.BaselineRouting);
        if (errors.Any) return errors.Problem();

        var runner = new ScenarioRunner(sample!, request.Seed);
        return TypedResults.Ok(runner.Compare(
            new ScenarioConfig(request.Slotting!, request.Routing!, request.CartCapacity),
            new ScenarioConfig(request.BaselineSlotting, request.BaselineRouting, request.CartCapacity)));
    }

    internal static Results<Ok<RouteResponse>, ValidationProblem> Route(RouteRequest request, SampleCatalog catalog)
    {
        var errors = new RequestErrors()
            .Check(catalog.TryGet(request.Layout, out var sample), "layout", $"Unknown layout '{request.Layout}'.")
            .CheckStrategies(request.Slotting, request.Routing);
        var orders = sample?.History.Orders ?? [];
        var order = request.OrderId is null
            ? orders.Count == 0 ? null : orders[new Random(request.Seed).Next(orders.Count)]
            : orders.FirstOrDefault(o => o.Id == request.OrderId);
        errors.Check(sample is null || order is not null, "orderId", $"Unknown order '{request.OrderId}'.");
        if (!errors.Any) errors.CheckLayoutSupports(sample!, "routing", request.Routing!);
        if (errors.Any) return errors.Problem();

        var runner = new ScenarioRunner(sample!, request.Seed);
        var graph = runner.Graph;
        var plan = runner.Plan(request.Slotting!);
        var route = RoutingStrategies.Get(request.Routing!).Route(graph, ScenarioRunner.PickListOf(order!, plan).Nodes);
        var stops = route.Waypoints.Where(graph.IsPickNode).Select((node, i) => (node, i)).ToDictionary(s => s.node, s => s.i + 1);

        Point PointOf(int node)
        {
            var (x, y) = graph.Position(node);
            return new Point(x, y);
        }

        var picks = order!.Lines.Select(line =>
        {
            var location = plan.Assignments[line.Sku];
            var point = PointOf(location.Node);
            return new RoutePick(line.Sku, location.Id, location.Aisle, location.Side, point.X, point.Y, stops[location.Node]);
        }).ToArray();

        return TypedResults.Ok(new RouteResponse(order.Id, plan.Strategy, route.Strategy, route.Distance, PointOf(graph.Depot), picks,
            [.. graph.ExpandPath(route.Waypoints).Select(PointOf)]));
    }

    private static RequestErrors CheckStrategies(this RequestErrors errors, string? slotting, string? routing) => errors
        .Check(SlottingStrategies.Exists(slotting), "slotting", $"Unknown slotting strategy '{slotting}'.")
        .Check(RoutingStrategies.Exists(routing), "routing", $"Unknown routing strategy '{routing}'.");

    private static RequestErrors CheckLayoutSupports(this RequestErrors errors, Sample sample, string field, string routing) => errors
        .Check(RoutingStrategies.Supports(routing, sample.Layout), field, $"Layout '{sample.Name}' has no back cross-aisle, which '{routing}' needs.");

    /// <summary>Collects field errors for an RFC 9457 validation problem response.</summary>
    private sealed class RequestErrors
    {
        private readonly Dictionary<string, string[]> _errors = [];

        public bool Any => _errors.Count > 0;

        public RequestErrors Check(bool valid, string field, string message)
        {
            if (!valid) _errors[field] = [message];
            return this;
        }

        public ValidationProblem Problem() => TypedResults.ValidationProblem(_errors);
    }
}
