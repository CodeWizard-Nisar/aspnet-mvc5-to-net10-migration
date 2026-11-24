using ConfReg.Data.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace ConfReg.Jobs.Health;

/// <summary>Can we actually reach the database? A scheduler that is "up" with a dead database is not healthy.</summary>
public sealed class DatabaseHealthCheck(IConfRegContextFactory db) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var ctx = db.Create();
            ctx.Database.SqlQuery<int>("SELECT 1").Single();
            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Database unreachable", ex));
        }
    }
}

/// <summary>Is Quartz running, and did any job's most recent run fail?</summary>
public sealed class SchedulerHealthCheck(ISchedulerFactory factory, JobStatusTracker tracker) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        var meta = await scheduler.GetMetadata(cancellationToken);
        if (meta.Status != SchedulerStatus.Running)
            return HealthCheckResult.Unhealthy("Scheduler is not running (status: " + meta.Status + ")");

        var failing = tracker.All.Where(s => s.LastFailureUtc is not null && (s.LastSuccessUtc is null || s.LastFailureUtc > s.LastSuccessUtc)).ToList();
        var data = tracker.All.ToDictionary(s => s.Job, s => (object)(s.LastResult ?? s.LastFailure ?? "not run yet"));
        return failing.Count == 0
            ? HealthCheckResult.Healthy("Scheduler running", data)
            : HealthCheckResult.Degraded("Last run failed: " + string.Join(", ", failing.Select(f => f.Job)), data: data);
    }
}
