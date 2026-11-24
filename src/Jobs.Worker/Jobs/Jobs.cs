using ConfReg.Data.Services;
using Quartz;
using Serilog.Context;

namespace ConfReg.Jobs.Jobs;

/// <summary>
/// The three jobs. Each is a thin Quartz wrapper over a service in Shared.Data, so the same logic runs from the
/// scheduler, from the command line (--run-job) and from tests.
///
/// [DisallowConcurrentExecution] is the overlap guard the Windows Service never had: if a run is still going when the
/// next trigger fires, Quartz skips it instead of starting a second copy.
/// </summary>
[DisallowConcurrentExecution]
public sealed class ExpireUnpaidJob(ExpiryService service, JobStatusTracker tracker, ILogger<ExpireUnpaidJob> log) : IJob
{
    public const string Name = "expire";
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        JobRunner.Run(Name, log, tracker, () =>
        {
            int expired = service.ExpireUnpaid();
            log.LogInformation("Expired {ExpiredCount} unpaid registration(s)", expired);
            return $"expired={expired}";
        });
}

[DisallowConcurrentExecution]
public sealed class ConfirmationEmailJob(ConfirmationEmailService service, JobStatusTracker tracker, ILogger<ConfirmationEmailJob> log) : IJob
{
    public const string Name = "emails";
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        JobRunner.Run(Name, log, tracker, () =>
        {
            var r = service.SendPending();
            log.LogInformation("Confirmation emails: {SentCount} sent, {FailedCount} failed, {RemainingCount} still waiting", r.Sent, r.Failed, r.Remaining);
            // A failed send is retried on the next run, but a run that sent nothing and failed everything is an incident.
            if (r.Failed > 0 && r.Sent == 0) throw new InvalidOperationException($"All {r.Failed} confirmation emails failed to send.");
            return $"sent={r.Sent}";
        });
}

[DisallowConcurrentExecution]
public sealed class CapacityReportJob(CapacityReportService service, JobStatusTracker tracker, ILogger<CapacityReportJob> log) : IJob
{
    public const string Name = "report";
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        JobRunner.Run(Name, log, tracker, () =>
        {
            string path = service.WriteReport(DateTime.UtcNow);
            log.LogInformation("Capacity report written to {ReportPath}", path);
            return $"report={path}";
        });
}

/// <summary>Adds the job name and duration to every log line of a run and records the outcome for the health check.</summary>
public static class JobRunner
{
    /// <summary>Runs the body, returns its one-line result (also printed by --run-job).</summary>
    public static ValueTask Run(string job, ILogger log, JobStatusTracker tracker, Func<string> body)
    {
        Execute(job, log, tracker, body);
        return ValueTask.CompletedTask;
    }

    public static string Execute(string job, ILogger log, JobStatusTracker tracker, Func<string> body)
    {
        using (LogContext.PushProperty("Job", job))
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string result = body();
                tracker.Success(job, result);
                log.LogInformation("Job {Job} finished in {ElapsedMs} ms: {Result}", job, started.ElapsedMilliseconds, result);
                return result;
            }
            catch (Exception ex)
            {
                tracker.Failure(job, ex);
                log.LogError(ex, "Job {Job} failed after {ElapsedMs} ms", job, started.ElapsedMilliseconds);
                // Do not rethrow to Quartz as a crash: the scheduler keeps its schedule and tries again at the next tick.
                throw new JobExecutionException(ex);
            }
        }
    }
}
