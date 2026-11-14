using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using SlottingTwin.Api;
using SlottingTwin.Simulation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(sp => SampleCatalog.Load(
    sp.GetRequiredService<IConfiguration>()["Samples:Path"] ?? Path.Combine(AppContext.BaseDirectory, "samples")));

var app = builder.Build();
app.Services.GetRequiredService<SampleCatalog>(); // fail at startup, not on the first request, if samples are missing or invalid

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapSlottingApi();

app.Run();
