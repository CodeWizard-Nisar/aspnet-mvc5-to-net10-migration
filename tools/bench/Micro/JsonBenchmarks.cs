using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ConfReg.Modern.Infrastructure;
using ConfReg.ViewModels;
using Newtonsoft.Json;

namespace ConfReg.Bench.Micro;

/// <summary>
/// What preserving the legacy JSON shape costs.
///
/// Phase 5 deliberately chose Newtonsoft with explicit settings, because it is the only option that reproduces
/// JavaScriptSerializer's bytes (PascalCase, "\/Date(ms)\/", integral decimals with no decimal point). System.Text.Json
/// is the faster default on .NET 10 but writes a DIFFERENT wire format, so switching to it would be a breaking change
/// for every existing caller. This measures the price of that decision instead of guessing at it.
/// </summary>
[MemoryDiagnoser]
public class JsonBenchmarks
{
    private List<TalkRow> _talks;
    private JsonSerializerSettings _newtonsoftDefaults;
    private JsonSerializerOptions _systemTextJson;

    [GlobalSetup]
    public void Setup()
    {
        // The /Talks payload: the realistic shape, 20 rows, which is what the load tests hit.
        _talks = Enumerable.Range(1, 20).Select(i => new TalkRow
        {
            Id = i,
            Title = "Strangler Fig in Practice " + i,
            Speaker = "Ava Okafor",
            Track = i % 3 == 0 ? "Data" : "Architecture",
            StartsUtc = new DateTime(2027, 3, 16, 9, 0, 0, DateTimeKind.Unspecified).AddMinutes(i * 30),
            DurationMinutes = 60,
            Room = "Hall A",
            Capacity = 120,
            Taken = i * 3
        }).ToList();

        _newtonsoftDefaults = new JsonSerializerSettings();
        _systemTextJson = new JsonSerializerOptions();
    }

    /// <summary>What Modern.Web actually does: the legacy wire format, byte for byte.</summary>
    [Benchmark(Baseline = true, Description = "Newtonsoft, legacy shape (shipped)")]
    public string LegacyShape() => LegacyJson.Serialize(_talks);

    /// <summary>Newtonsoft without the explicit settings: camelCase stays off, but dates gain an offset and 299 becomes 299.0.</summary>
    [Benchmark(Description = "Newtonsoft, default settings (wrong bytes)")]
    public string NewtonsoftDefaults() => JsonConvert.SerializeObject(_talks, _newtonsoftDefaults);

    /// <summary>The .NET 10 default. Faster, and a different wire format: camelCase names and ISO 8601 dates.</summary>
    [Benchmark(Description = "System.Text.Json, defaults (wrong bytes)")]
    public string SystemTextJson() => System.Text.Json.JsonSerializer.Serialize(_talks, _systemTextJson);
}
