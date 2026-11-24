using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>
/// The 30-minute hold. The app has no clock to move, so "30 minutes later" is done by back-dating the row
/// in the database and then running the REAL expiry job (see TestEnvironment.ExpiryCommand).
/// </summary>
[Collection("app")]
public class ExpiryTests
{
    private readonly AppFixture _app;
    public ExpiryTests(AppFixture app) { _app = app; _app.Reset(); }

    /// <summary>
    /// The seed contains 10 Pending rows dated long ago, so they are overdue and the job would also expire them,
    /// releasing their seats mid-test. Run the job once up front so every test starts from "nothing overdue".
    /// </summary>
    private async Task<(ConfRegClient Client, string Code, TalkInfo Talk)> Hold()
    {
        _app.RunExpiryJob();
        using var probe = await Wizard.ReachStep3();
        var talk = Wizard.NonOverlapping(await Wizard.Talks(probe), 1)[0];
        var (c, code) = await Wizard.ReachStep4("Standard", talk.Id);
        return (c, code, talk);
    }

    private static async Task<int> RemainingSeenByOthers(int talkId)
    {
        using var other = new ConfRegClient();
        return (await Wizard.ParseTalksAsync(other)).Single(t => t.Id == talkId).Remaining;
    }

    [Fact]
    public async Task An_abandoned_registration_is_expired_and_its_seats_are_released()
    {
        var (c, code, talk) = await Hold();
        using (c)
        {
            Assert.Equal(talk.Remaining - 1, await RemainingSeenByOthers(talk.Id)); // held

            Db.BackDate(code, 31);
            _app.RunExpiryJob();

            Assert.Equal("Expired", Db.StatusOf(code));
            Assert.Equal(talk.Remaining, await RemainingSeenByOthers(talk.Id));     // released
        }
    }

    [Fact]
    public async Task A_registration_inside_the_30_minute_window_is_left_alone()
    {
        var (c, code, talk) = await Hold();
        using (c)
        {
            Db.BackDate(code, 29);
            _app.RunExpiryJob();
            Assert.Equal("Pending", Db.StatusOf(code));
            Assert.Equal(talk.Remaining - 1, await RemainingSeenByOthers(talk.Id));
        }
    }

    [Fact]
    public async Task The_job_only_touches_pending_registrations()
    {
        int paid = Db.Count("Registrations", "Status=1"), cancelled = Db.Count("Registrations", "Status=2");

        // Seed: 10 Pending rows dated at the fixed anchor (long ago), so all 10 are overdue.
        Assert.Equal(10, _app.RunExpiryJob());
        Assert.Equal(paid, Db.Count("Registrations", "Status=1"));
        Assert.Equal(cancelled, Db.Count("Registrations", "Status=2"));
        Assert.Equal(20, Db.Count("Registrations", "Status=3")); // 10 seeded Expired + the 10 just expired

        // A fresh hold is not overdue, so a second run does nothing, and it is idempotent.
        var (c, code, _) = await Hold();
        using (c)
        {
            Assert.Equal(0, _app.RunExpiryJob());
            Assert.Equal("Pending", Db.StatusOf(code));
        }
    }

    [Fact]
    public async Task Paying_before_the_job_runs_keeps_the_registration()
    {
        var (c, code, _) = await Hold();
        using (c)
        {
            await Wizard.Pay(c);
            Db.BackDate(code, 60); // would be overdue if it were still pending
            _app.RunExpiryJob();
            Assert.Equal("Paid", Db.StatusOf(code));
        }
    }

    /// <summary>
    /// KNOWN BUG, pinned on purpose (docs/01-baseline.md, E2). The job expires the row and frees the seats, but
    /// the attendee's SESSION still believes the hold is good, and Pay() re-attaches the stale entity and writes
    /// Paid over Expired. A paid registration now exists whose seats may already belong to someone else.
    /// If a later phase fixes this, this test must be changed deliberately, in a commit that says so.
    /// </summary>
    [Fact]
    public async Task Paying_after_the_job_expired_the_hold_resurrects_the_registration_KNOWN_BUG()
    {
        var (c, code, talk) = await Hold();
        using (c)
        {
            Db.BackDate(code, 31);
            _app.RunExpiryJob();
            Assert.Equal("Expired", Db.StatusOf(code));
            Assert.Equal(talk.Remaining, await RemainingSeenByOthers(talk.Id));

            var location = await Wizard.Pay(c); // the session clock still says "valid"
            Assert.Contains("code=" + code, location);
            Assert.Equal("Paid", Db.StatusOf(code)); // resurrected
        }
    }
}
