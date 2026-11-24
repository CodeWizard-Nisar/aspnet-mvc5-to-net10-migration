using System.Data.Entity;
using System.Text.Json;
using ConfReg.Data;
using ConfReg.Data.Seed;
using ConfReg.Domain.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Xunit;

namespace Jobs.Worker.Tests;

/// <summary>
/// The worker's own behaviour that unit tests of the services cannot see: that the three jobs are really scheduled, with
/// the intended cron expressions in UTC, and that the health endpoint tells the truth about the database.
/// </summary>
public class WorkerWiringTests : IClassFixture<WorkerWiringTests.Db>
{
    public sealed class Db
    {
        public string ConnectionString { get; }
        public Db()
        {
            var baseCs = Environment.GetEnvironmentVariable("CONFREG_TEST_SQL")
                ?? "Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True";
            // Hand-written, not SqlConnectionStringBuilder: EF6 uses System.Data.SqlClient (see docs/03-libraries.md).
            ConnectionString = System.Text.RegularExpressions.Regex.Replace(baseCs, "(Database|Initial Catalog)=[^;]*", "Database=ConfRegWorker",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            Database.SetInitializer<ConfRegContext>(null);
            using (var ctx = new ConfRegContext(ConnectionString)) ctx.Database.Delete();
            Database.SetInitializer(new ConfRegInitializer(new BCryptPasswordHasher()));
            using (var ctx = new ConfRegContext(ConnectionString)) ctx.Database.Initialize(true);
            Database.SetInitializer<ConfRegContext>(null); // from here on, like the real worker: never create anything
        }
    }

    private readonly Db _db;
    public WorkerWiringTests(Db db) { _db = db; }

    private WebApplicationFactory<Program> Worker(string connectionString, Action<IWebHostBuilder>? more = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConfReg:ConnectionString", connectionString);
            more?.Invoke(b);
        });

    [Fact]
    public async Task Liveness_answers_without_touching_the_database()
    {
        using var worker = Worker("Server=127.0.0.1,1;Database=nothing;User Id=x;Password=y;Connect Timeout=1"); // unreachable on purpose
        using var client = worker.CreateClient();
        var res = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Readiness_is_healthy_when_the_database_and_scheduler_are_up()
    {
        using var worker = Worker(_db.ConnectionString);
        using var client = worker.CreateClient();
        var res = await client.GetAsync("/health");
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("Healthy", json.GetProperty("checks").GetProperty("scheduler").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_is_503_when_the_database_cannot_be_reached()
    {
        using var worker = Worker("Server=127.0.0.1,1;Database=nothing;User Id=x;Password=y;Connect Timeout=1;TrustServerCertificate=True");
        using var client = worker.CreateClient();
        var res = await client.GetAsync("/health");
        var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("Unhealthy", json.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
    }

    [Fact]
    public async Task All_three_jobs_are_scheduled_with_the_documented_cron_expressions_in_utc()
    {
        using var worker = Worker(_db.ConnectionString);
        using var client = worker.CreateClient(); // starts the host, and with it Quartz
        var scheduler = await worker.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        async Task<ICronTrigger> TriggerOf(string job) =>
            (ICronTrigger)(await scheduler.GetTriggersOfJob(new JobKey(job))).Single();

        var expire = await TriggerOf("expire");
        var emails = await TriggerOf("emails");
        var report = await TriggerOf("report");

        Assert.Equal("0 * * * * ?", expire.CronExpressionString);        // every minute
        Assert.Equal("0 0/5 * * * ?", emails.CronExpressionString);      // every 5 minutes
        Assert.Equal("0 0 2 * * ?", report.CronExpressionString);        // 02:00 nightly
        Assert.All(new[] { expire, emails, report }, t => Assert.Equal(TimeZoneInfo.Utc, t.TimeZone));

        Assert.Equal(0, report.NextFireTimeUtc!.Value.Minute);
        Assert.Equal(2, report.NextFireTimeUtc!.Value.Hour);
        Assert.True(emails.NextFireTimeUtc!.Value.Minute % 5 == 0);
    }

    [Fact]
    public async Task Schedules_can_be_changed_by_configuration_alone()
    {
        using var worker = Worker(_db.ConnectionString, b => b.UseSetting("Jobs:ExpireCron", "0 0/2 * * * ?"));
        using var client = worker.CreateClient();
        var scheduler = await worker.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var trigger = (ICronTrigger)(await scheduler.GetTriggersOfJob(new JobKey("expire"))).Single();
        Assert.Equal("0 0/2 * * * ?", trigger.CronExpressionString);
    }

    [Fact]
    public async Task A_job_that_runs_through_quartz_records_its_result_and_health_reports_it()
    {
        using var worker = Worker(_db.ConnectionString);
        using var client = worker.CreateClient();
        var scheduler = await worker.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.TriggerJob(new JobKey("expire"));
        for (int i = 0; i < 50; i++)
        {
            var json = JsonDocument.Parse(await client.GetStringAsync("/health")).RootElement;
            if (json.GetProperty("jobs").EnumerateArray().Any(j => j.GetProperty("job").GetString() == "expire" && j.GetProperty("lastResult").ValueKind == JsonValueKind.String))
                return;
            await Task.Delay(200);
        }
        Assert.Fail("The expire job did not report a result through /health within 10 seconds.");
    }
}
