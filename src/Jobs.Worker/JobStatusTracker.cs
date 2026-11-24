using System.Collections.Concurrent;

namespace ConfReg.Jobs;

public record JobStatus(string Job, DateTime? LastSuccessUtc, DateTime? LastFailureUtc, string? LastFailure, string? LastResult);

/// <summary>What each job last did. Feeds the health endpoint, so "the service is running" can no longer hide "the job keeps failing".</summary>
public class JobStatusTracker
{
    private readonly ConcurrentDictionary<string, JobStatus> _status = new();

    public void Success(string job, string result) =>
        _status.AddOrUpdate(job, _ => new JobStatus(job, DateTime.UtcNow, null, null, result),
            (_, old) => old with { LastSuccessUtc = DateTime.UtcNow, LastResult = result, LastFailure = null });

    public void Failure(string job, Exception ex) =>
        _status.AddOrUpdate(job, _ => new JobStatus(job, null, DateTime.UtcNow, ex.Message, null),
            (_, old) => old with { LastFailureUtc = DateTime.UtcNow, LastFailure = ex.Message });

    public IReadOnlyCollection<JobStatus> All => _status.Values.OrderBy(s => s.Job).ToList();
}
