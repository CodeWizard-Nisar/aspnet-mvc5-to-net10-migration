using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>Snapshot of the JSON the wizard returns for each step and for each way a step can be refused.</summary>
[Collection("app")]
public class WizardValidationSnapshotTests
{
    private readonly AppFixture _app;
    public WizardValidationSnapshotTests(AppFixture app) { _app = app; _app.Reset(); }

    [Fact]
    public async Task Step1_empty_form_is_rejected()
    {
        using var c = new ConfRegClient();
        var r = await c.Post("/Register/Step1", ("FirstName", ""), ("LastName", ""), ("Email", "not-an-email"));
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("wizard_step1_invalid", r.Body);
    }

    [Fact]
    public async Task Step2_form()
    {
        using var c = await Wizard.ReachStep3(); // gets us through 1 and 2; fetch 2 again to see it filled in
        var r = await c.Get("/Register/Step2");
        Snapshot.VerifyJson("wizard_step2_form", r.Body);
    }

    [Fact]
    public async Task Step2_student_without_edu_is_rejected()
    {
        using var c = new ConfRegClient();
        await c.Post("/Register/Step1", Wizard.ValidDetails);
        var r = await c.Post("/Register/Step2", ("ticketType", "Student"));
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("wizard_step2_student_needs_edu", r.Body);
    }

    [Fact]
    public async Task Step2_no_ticket_chosen()
    {
        using var c = new ConfRegClient();
        await c.Post("/Register/Step1", Wizard.ValidDetails);
        var r = await c.Post("/Register/Step2", ("ticketType", ""));
        Snapshot.VerifyJson("wizard_step2_nothing_chosen", r.Body);
    }

    [Fact]
    public async Task Step3_talk_list_for_a_fresh_registration()
    {
        using var c = await Wizard.ReachStep3();
        Snapshot.VerifyJson("wizard_step3_talks", (await c.Get("/Register/Step3")).Body);
    }

    [Fact]
    public async Task Step3_full_talk_is_refused()
    {
        using var c = await Wizard.ReachStep3();
        var full = (await Wizard.Talks(c)).Single(t => t.Full);
        var r = await Wizard.PickTalks(c, full.Id);
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("wizard_step3_full_talk", r.Body);
    }

    [Fact]
    public async Task Step3_overlapping_talks_are_refused()
    {
        using var c = await Wizard.ReachStep3();
        var (a, b) = Wizard.OverlappingPair(await Wizard.Talks(c));
        var r = await Wizard.PickTalks(c, a.Id, b.Id);
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("wizard_step3_overlap", r.Body);
    }

    [Fact]
    public async Task Step3_more_than_five_talks_is_refused()
    {
        using var c = await Wizard.ReachStep3();
        var six = Wizard.NonOverlapping(await Wizard.Talks(c), 6);
        Assert.Equal(6, six.Count);
        var r = await Wizard.PickTalks(c, six.Select(t => t.Id).ToArray());
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("wizard_step3_too_many", r.Body);
    }

    [Fact]
    public async Task Step4_review_with_a_hold()
    {
        var talks = Wizard.NonOverlapping(await TalksOf(), 2);
        var (c, _) = await Wizard.ReachStep4("Workshop", talks.Select(t => t.Id).ToArray());
        using (c) Snapshot.VerifyJson("wizard_step4_review", (await c.Get("/Register/Step4")).Body, Scrub.References | Scrub.Countdown);
    }

    [Fact]
    public async Task Step4_declined_card()
    {
        var (c, _) = await Wizard.ReachStep4("Standard", Wizard.NonOverlapping(await TalksOf(), 1).Select(t => t.Id).ToArray());
        using (c)
        {
            var r = await c.Post("/Register/Step4", ("cardNumber", "4000 0000 0000 0002"));
            Assert.Equal(200, r.Code);
            Snapshot.VerifyJson("wizard_step4_declined", r.Body, Scrub.References | Scrub.Countdown);
        }
    }

    [Fact]
    public async Task Confirmation_after_paying()
    {
        var (c, _) = await Wizard.ReachStep4("Standard", Wizard.NonOverlapping(await TalksOf(), 2).Select(t => t.Id).ToArray());
        using (c)
        {
            var location = await Wizard.Pay(c);
            var r = await c.Get(location);
            Snapshot.VerifyJson("wizard_confirmation", r.Body, Scrub.References);
        }
    }

    [Fact]
    public async Task Registration_closed_page()
    {
        Db.Exec("UPDATE Settings SET Value='2000-01-02T00:00:00.0000000Z' WHERE [Key]='Registration.ClosesUtc'");
        using var c = new ConfRegClient();
        Snapshot.VerifyJson("wizard_closed", (await c.Get("/Register/Step1")).Body);
    }

    private static async Task<List<TalkInfo>> TalksOf()
    {
        using var c = await Wizard.ReachStep3();
        return await Wizard.Talks(c);
    }
}
