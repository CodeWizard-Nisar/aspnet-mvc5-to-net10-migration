using BenchmarkDotNet.Attributes;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;

namespace ConfReg.Bench.Micro;

/// <summary>
/// The pure talk-selection rules, which run identically on both runtimes because they are the multi-targeted
/// Shared.Domain code. Included to show where request time does NOT go: these are nanoseconds against the
/// milliseconds the database costs, so the migration's performance story is about the host and the data layer,
/// not about the business rules.
/// </summary>
[MemoryDiagnoser]
public class DomainBenchmarks
{
    private List<Talk> _schedule;
    private List<Talk> _selection;
    private Dictionary<int, int> _seats;

    [GlobalSetup]
    public void Setup()
    {
        _schedule = Enumerable.Range(1, 20).Select(i => new Talk
        {
            Id = i, Title = "Talk " + i, Capacity = 50,
            StartsUtc = new DateTime(2027, 3, 16, 9, 0, 0, DateTimeKind.Utc).AddMinutes(i * 45),
            DurationMinutes = 60
        }).ToList();
        _selection = _schedule.Where(t => t.Id % 4 == 0).ToList();   // 5 talks, the maximum
        _seats = _schedule.ToDictionary(t => t.Id, t => t.Id * 2);
    }

    [Benchmark(Description = "TalkRules.Validate, 5 selected of 20")]
    public List<string> ValidateSelection() => TalkRules.Validate(_selection, _seats, maxTalks: 5);

    [Benchmark(Description = "TalkRules.Overlaps")]
    public bool Overlaps() => TalkRules.Overlaps(_schedule[0], _schedule[1]);

    [Benchmark(Description = "TicketRules.IsEduEmail")]
    public bool EduEmail() => TicketRules.IsEduEmail("student@uni3.edu");

    [Benchmark(Description = "ReferenceCodes.Next")]
    public string ReferenceCode() => ReferenceCodes.Next();
}
