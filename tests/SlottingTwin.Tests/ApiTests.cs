using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SlottingTwin.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Layouts_lists_the_samples_with_drawing_dimensions()
    {
        var layouts = await _client.GetFromJsonAsync<JsonElement>("/api/layouts", Ct);

        layouts.EnumerateArray().Select(l => l.GetProperty("name").GetString()).ShouldBe(["compact", "wide"]);
        var compact = layouts[0];
        compact.GetProperty("aisles").GetInt32().ShouldBe(8);
        compact.GetProperty("locations").GetInt32().ShouldBe(576);
        compact.GetProperty("orderIds").GetArrayLength().ShouldBe(compact.GetProperty("orders").GetInt32());
    }

    [Fact]
    public async Task Strategies_lists_every_strategy()
    {
        var strategies = await _client.GetFromJsonAsync<JsonElement>("/api/strategies", Ct);

        strategies.GetProperty("slotting").GetArrayLength().ShouldBe(4);
        strategies.GetProperty("routing").GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task Simulate_compares_kpis_with_the_baseline()
    {
        var response = await Post("/api/simulate", """{"layout":"compact","slotting":"coi","routing":"2-opt","seed":42,"cartCapacity":20}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("improvementPercent").GetDouble().ShouldBeGreaterThan(0);
        body.GetProperty("baseline").GetProperty("routing").GetString().ShouldBe("s-shape");
        body.GetProperty("candidate").GetProperty("batching").GetProperty("batches").GetInt32().ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData("""{"layout":"nope","slotting":"coi","routing":"2-opt"}""", "layout")]
    [InlineData("""{"layout":"compact","slotting":"coi","routing":"teleport"}""", "routing")]
    [InlineData("""{"layout":"compact","routing":"2-opt"}""", "slotting")]
    [InlineData("""{"layout":"compact","slotting":"coi","routing":"2-opt","cartCapacity":0}""", "cartCapacity")]
    [InlineData("""{"layout":"compact","slotting":"coi","routing":"2-opt","cartCapacity":1000}""", "cartCapacity")]
    [InlineData("""{"layout":"compact","slotting":"coi","routing":"2-opt","cartCapacity":2}""", "cartCapacity")]
    [InlineData("""{"layout":"compact","slotting":"coi","routing":"2-opt","baselineRouting":"x"}""", "baselineRouting")]
    public async Task Simulate_rejects_invalid_input_with_a_validation_problem(string json, string field)
    {
        var response = await Post("/api/simulate", json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Route_returns_a_drawable_path_from_depot_to_depot()
    {
        var response = await Post("/api/route", """{"layout":"compact","slotting":"coi-swap","routing":"2-opt","seed":1,"orderId":"ORD-001-0001"}""");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var route = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        route.GetProperty("orderId").GetString().ShouldBe("ORD-001-0001");
        var depot = route.GetProperty("depot");
        var path = route.GetProperty("path");
        path[0].GetRawText().ShouldBe(depot.GetRawText());
        path[path.GetArrayLength() - 1].GetRawText().ShouldBe(depot.GetRawText());
        route.GetProperty("picks").EnumerateArray().ShouldAllBe(p => p.GetProperty("stop").GetInt32() >= 1);
    }

    [Fact]
    public async Task Route_picks_a_random_order_when_none_is_given_and_rejects_unknown_ones()
    {
        (await Post("/api/route", """{"layout":"compact","slotting":"coi","routing":"s-shape"}""")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await Post("/api/route", """{"layout":"compact","slotting":"coi","routing":"s-shape","orderId":"ORD-404"}""");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Abc_returns_the_matrix_and_heat_for_every_location()
    {
        var abc = await _client.GetFromJsonAsync<JsonElement>("/api/abc?layout=compact&slotting=coi", Ct);

        abc.GetProperty("matrix").EnumerateObject().Count().ShouldBe(9);
        abc.GetProperty("locations").GetArrayLength().ShouldBe(576);
        abc.GetProperty("locations").EnumerateArray().Count(l => l.GetProperty("sku").ValueKind == JsonValueKind.String).ShouldBe(400);
    }

    [Theory]
    [InlineData("/api/abc?layout=nope")]
    [InlineData("/api/abc?layout=compact&slotting=magic")]
    public async Task Abc_rejects_unknown_names(string url)
    {
        (await _client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/map.js", "text/javascript")]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/openapi/v1.json", "application/json")]
    [InlineData("/scalar", "text/html")]
    public async Task Ui_and_api_docs_are_served(string url, string mediaType)
    {
        var response = await _client.GetAsync(url, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(mediaType);
    }

    private Task<HttpResponseMessage> Post(string url, string json) =>
        _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), Ct);
}
