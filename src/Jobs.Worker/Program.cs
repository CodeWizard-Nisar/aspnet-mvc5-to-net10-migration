using System.Text.Json;
using ConfReg.Data;
using ConfReg.Data.Configuration;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Jobs;
using ConfReg.Jobs.Health;
using ConfReg.Jobs.Jobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Data.Entity;

// Two ways to run this one program:
//   dotnet ConfReg.Jobs.Worker.dll                       the service: Quartz runs the three jobs on their schedules
//   dotnet ConfReg.Jobs.Worker.dll --run-job expire      run one job once, print its result on stdout, exit
// (job names: expire | emails | report). The second form is for operators and for the characterization tests,
// which use it to trigger the REAL expiry job the same way they once ran the Windows Service's --once.
string? runJob = null;
int ix = Array.IndexOf(args, "--run-job");
if (ix >= 0) runJob = ix + 1 < args.Length ? args[ix + 1] : throw new ArgumentException("--run-job needs a job name: expire | emails | report");

// Content root = the folder the program lives in, not the shell's current directory, so appsettings.json and
// ~/App_Data resolve the same way under a service manager as they do from a terminal.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

// When started by the Windows Service Control Manager this makes the process behave as a proper Windows service
// (`sc create ConfRegJobs binPath= ...`); everywhere else, including Linux, it does nothing.
builder.Host.UseWindowsService(o => o.ServiceName = "ConfRegJobs");

// ---- logging: structured (Serilog) --------------------------------------------------------------------------
// Production: one compact JSON object per line, easy to ship and query. Development: readable text.
// In --run-job mode every log line goes to STDERR so STDOUT carries only the result line.
builder.Host.UseSerilog((ctx, services, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Service", "ConfReg.Jobs.Worker");

    var toStderr = runJob is not null ? LogEventLevel.Verbose : (LogEventLevel?)null;
    if (ctx.HostingEnvironment.IsDevelopment())
        lc.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Job} {Message:lj} {Properties:j}{NewLine}{Exception}", standardErrorFromLevel: toStderr);
    else
        lc.WriteTo.Console(new RenderedCompactJsonFormatter(), standardErrorFromLevel: toStderr);

    lc.WriteTo.File(new RenderedCompactJsonFormatter(), Path.Combine(AppContext.BaseDirectory, "logs", "worker-.jsonl"),
        rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
});

// ---- configuration: the SAME ConfReg section, names and env vars the legacy app uses (ConfReg__ConnectionString ...)
// Secrets come from user-secrets (development) or environment variables; appsettings.json holds none.
builder.Services.AddOptions<ConfRegOptions>()
    .Bind(builder.Configuration.GetSection(ConfRegOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
        "ConfReg:ConnectionString is not set. Use `dotnet user-secrets set \"ConfReg:ConnectionString\" \"...\"` or the ConfReg__ConnectionString environment variable.")
    .ValidateOnStart();
builder.Services.Configure<JobsOptions>(builder.Configuration.GetSection(JobsOptions.SectionName));

// The worker only ever READS an existing database. The legacy web app creates and seeds it; a worker that did so on
// its own would silently invent an empty database in the wrong place if the connection string were mistyped.
Database.SetInitializer<ConfRegContext>(null);

builder.Services.AddSingleton<ISettings>(sp => new ConfRegSettings(sp.GetRequiredService<IOptions<ConfRegOptions>>().Value));
builder.Services.AddSingleton<IConfRegContextFactory, ConfRegContextFactory>();
builder.Services.AddSingleton<IAppPaths>(_ => new DirectoryAppPaths(AppContext.BaseDirectory));
builder.Services.AddSingleton<IEmailSender, FileEmailSender>();
builder.Services.AddSingleton<ExpiryService>();
builder.Services.AddSingleton<ConfirmationEmailService>();
builder.Services.AddSingleton<CapacityReportService>();
builder.Services.AddSingleton<JobStatusTracker>();

// ---- scheduling: Quartz.NET, cron, in UTC ---------------------------------------------------------------------
var jobs = builder.Configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>() ?? new JobsOptions();
builder.Services.AddQuartz(q =>
{
    q.UseInMemoryStore(); // single instance by design: see docs/04-jobs.md before running more than one worker

    void Schedule<T>(string name, string cron, CronTriggerMisfireInstruction misfire) where T : IJob
    {
        q.AddJob<T>(j => j.WithIdentity(name));
        q.AddTrigger(t => t.ForJob(name).WithIdentity(name + "-trigger")
            .WithCronSchedule(cron, c => c.InTimeZone(TimeZoneInfo.Utc).WithMisfireInstruction(misfire)));
    }

    // If the worker was down when a trigger was due ("misfire"): the frequent jobs just wait for the next tick,
    // while the nightly report runs once as soon as the worker is back, because skipping a night is a gap in the record.
    Schedule<ExpireUnpaidJob>(ExpireUnpaidJob.Name, jobs.ExpireCron, CronTriggerMisfireInstruction.DoNothing);
    Schedule<ConfirmationEmailJob>(ConfirmationEmailJob.Name, jobs.ConfirmationEmailCron, CronTriggerMisfireInstruction.DoNothing);
    Schedule<CapacityReportJob>(CapacityReportJob.Name, jobs.CapacityReportCron, CronTriggerMisfireInstruction.FireAndProceed);
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true); // finish a running job on shutdown

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })
    .AddCheck<SchedulerHealthCheck>("scheduler", tags: new[] { "ready" });

var app = builder.Build();

// ---- one-shot mode ---------------------------------------------------------------------------------------------
if (runJob is not null)
{
    var tracker = app.Services.GetRequiredService<JobStatusTracker>();
    var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ConfReg.Jobs.CLI");
    try
    {
        string result = runJob switch
        {
            ExpireUnpaidJob.Name => JobRunner.Execute(runJob, log, tracker, () => $"expired={app.Services.GetRequiredService<ExpiryService>().ExpireUnpaid()}"),
            ConfirmationEmailJob.Name => JobRunner.Execute(runJob, log, tracker, () => $"sent={app.Services.GetRequiredService<ConfirmationEmailService>().SendPending().Sent}"),
            CapacityReportJob.Name => JobRunner.Execute(runJob, log, tracker, () => $"report={app.Services.GetRequiredService<CapacityReportService>().WriteReport(DateTime.UtcNow)}"),
            _ => throw new ArgumentException($"Unknown job '{runJob}'. Use: expire | emails | report")
        };
        Console.WriteLine(result);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("FAILED: " + ex.Message);
        return 1;
    }
    finally { Log.CloseAndFlush(); }
}

// ---- service mode: health endpoints only; no pages --------------------------------------------------------------
// /health/live  process is up (never touches the database; for a liveness probe)
// /health       database reachable AND scheduler running AND no job's last run failed (for a readiness probe)
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health", new()
{
    ResponseWriter = async (http, report) =>
    {
        http.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(http.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), e.Value.Description }),
            jobs = http.RequestServices.GetRequiredService<JobStatusTracker>().All
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    },
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = 200,
        [HealthStatus.Degraded] = 200, // still serving; surfaced in the body and in the logs
        [HealthStatus.Unhealthy] = 503
    }
});

try
{
    Log.Information("ConfReg.Jobs.Worker starting. Schedules: expire={Expire} emails={Emails} report={Report} (UTC)",
        jobs.ExpireCron, jobs.ConfirmationEmailCron, jobs.CapacityReportCron);
    app.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "ConfReg.Jobs.Worker terminated unexpectedly");
    return 1;
}
finally { Log.CloseAndFlush(); }

/// <summary>Exposed so WebApplicationFactory in the tests can host the worker in-process.</summary>
public partial class Program { }
