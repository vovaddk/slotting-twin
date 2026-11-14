using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlottingTwin.Core.Layout;

namespace SlottingTwin.Simulation;

/// <summary>A layout and the order history that runs on it.</summary>
public sealed record Sample(WarehouseLayout Layout, OrderHistory History)
{
    public string Name => Layout.Name;
}

/// <summary>Samples stored as <c>{name}.layout.json</c> + <c>{name}.orders.json</c> pairs in one directory.</summary>
public sealed class SampleCatalog
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private const string LayoutSuffix = ".layout.json";
    private const string OrdersSuffix = ".orders.json";
    private readonly Dictionary<string, Sample> _samples;

    public SampleCatalog(IEnumerable<Sample> samples) =>
        _samples = samples.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Sample> All => [.. _samples.Values.OrderBy(s => s.Name, StringComparer.Ordinal)];

    public bool TryGet(string? name, [NotNullWhen(true)] out Sample? sample)
    {
        sample = null;
        return name is not null && _samples.TryGetValue(name, out sample);
    }

    public static SampleCatalog Load(string directory)
    {
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Samples directory not found: {directory}");

        var samples = new List<Sample>();
        foreach (var layoutFile in Directory.EnumerateFiles(directory, "*" + LayoutSuffix).Order(StringComparer.Ordinal))
        {
            var ordersFile = layoutFile[..^LayoutSuffix.Length] + OrdersSuffix;
            var sample = new Sample(Read<WarehouseLayout>(layoutFile), Read<OrderHistory>(ordersFile));
            var errors = Validate(sample);
            if (errors.Count > 0) throw new InvalidDataException($"Sample '{layoutFile}' is invalid: {string.Join(" ", errors)}");
            samples.Add(sample);
        }
        return new SampleCatalog(samples);
    }

    public static IReadOnlyList<string> Validate(Sample sample)
    {
        var errors = sample.Layout.Validate().Concat(sample.History.Validate()).ToList();
        if (errors.Count == 0 && sample.History.Skus.Count > sample.Layout.LocationCount)
            errors.Add($"{sample.History.Skus.Count} SKUs do not fit into {sample.Layout.LocationCount} locations.");
        return errors;
    }

    public static void Save(string directory, Sample sample)
    {
        Directory.CreateDirectory(directory);
        var indented = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, sample.Name + LayoutSuffix), JsonSerializer.Serialize(sample.Layout, indented));
        File.WriteAllText(Path.Combine(directory, sample.Name + OrdersSuffix), JsonSerializer.Serialize(sample.History, JsonOptions));
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} is empty.");
}
