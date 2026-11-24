using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>
/// Added in Phase 4 (new file; no earlier test edited). The two jobs that did not exist before the Worker Service:
/// confirmation emails and the nightly capacity report, driven end to end from a real registration.
/// </summary>
[Collection("app")]
public class BackgroundJobTests
{
    private readonly AppFixture _app;
    public BackgroundJobTests(AppFixture app) { _app = app; _app.Reset(); }

    private static async Task<string> RegisterAndPay()
    {
        using var probe = await Wizard.ReachStep3();
        var talk = Wizard.NonOverlapping(await Wizard.Talks(probe), 1)[0];
        var (c, code) = await Wizard.ReachStep4("Standard", talk.Id);
        using (c) await Wizard.Pay(c);
        return code;
    }

    private static bool Confirmed(string code) =>
        Db.Scalar<int>("SELECT CASE WHEN ConfirmationSentUtc IS NULL THEN 0 ELSE 1 END FROM Registrations WHERE ReferenceCode=@c", ("@c", code)) == 1;

    [Fact]
    public async Task A_newly_paid_registration_is_sent_its_confirmation_by_the_next_run_and_only_once()
    {
        string code = await RegisterAndPay();
        Assert.False(Confirmed(code));                        // paying does not send mail; the job does

        // The seed leaves 10 older paid registrations unconfirmed, plus the one just paid.
        Assert.Equal("11", _app.RunJob("emails", "sent"));
        Assert.True(Confirmed(code));

        Assert.Equal("0", _app.RunJob("emails", "sent"));     // nothing is ever sent twice
    }

    [Fact]
    public async Task Registrations_that_are_not_paid_are_never_sent_a_confirmation()
    {
        using var probe = await Wizard.ReachStep3();
        var talk = Wizard.NonOverlapping(await Wizard.Talks(probe), 1)[0];
        var (c, code) = await Wizard.ReachStep4("Standard", talk.Id); // held, not paid
        using (c)
        {
            _app.RunJob("emails", "sent");
            Assert.False(Confirmed(code));
        }
    }

    [Fact]
    public void The_nightly_report_is_written_to_disk_with_one_row_per_talk()
    {
        string path = _app.RunJob("report", "report");
        Assert.True(File.Exists(path), "report file should exist: " + path);
        var lines = File.ReadAllLines(path);
        Assert.Equal(21, lines.Length);                       // header + 20 talks
        Assert.Contains(lines, l => l.StartsWith("13,") && l.EndsWith(",100,YES")); // the full talk is flagged
    }
}
