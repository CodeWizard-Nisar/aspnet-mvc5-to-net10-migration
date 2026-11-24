using System.Text.RegularExpressions;
using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>End-to-end behaviour of the public wizard, including every business rule in the brief.</summary>
[Collection("app")]
public class WizardRulesTests
{
    private readonly AppFixture _app;
    public WizardRulesTests(AppFixture app) { _app = app; _app.Reset(); }

    // ---- the whole journey ----

    [Theory]
    [InlineData("Standard", 299, "test.person@example.org")]
    [InlineData("Workshop", 499, "test.person@example.org")]
    [InlineData("Student", 99, "student@uni.edu")]
    public async Task A_visitor_can_register_end_to_end_and_gets_a_reference_code(string ticket, int price, string email)
    {
        using var c = await Wizard.ReachStep3(ticket, email);
        var picks = Wizard.NonOverlapping(await Wizard.Talks(c), 2);
        Assert.Equal(302, (await Wizard.PickTalks(c, picks.Select(t => t.Id).ToArray())).Code);

        var review = (await c.Get("/Register/Step4")).Root;
        Assert.Equal(price, review.GetProperty("TotalAmount").GetDecimal());

        var location = await Wizard.Pay(c);
        var code = Regex.Match(location, "code=(CR-[A-HJ-NP-Z2-9]{8})").Groups[1].Value;
        Assert.NotEmpty(code); // a reference code was issued

        var confirmation = (await c.Get(location)).Root;
        Assert.Equal("Paid", confirmation.GetProperty("Status").GetString());
        Assert.Equal(ticket, confirmation.GetProperty("TicketType").GetString());
        Assert.Equal(2, confirmation.GetProperty("TalkTitles").GetArrayLength());

        Assert.Equal("Paid", Db.StatusOf(code));
        Assert.Equal(1, Db.Count("Payments p JOIN Registrations r ON r.Id=p.RegistrationId", $"r.ReferenceCode='{code}' AND p.Status=0"));
    }

    [Fact]
    public async Task Details_entered_in_step_1_are_stored_against_the_attendee()
    {
        using var c = await Wizard.ReachStep3();
        var talk = Wizard.NonOverlapping(await Wizard.Talks(c), 1)[0];
        await Wizard.PickTalks(c, talk.Id);
        Assert.Equal("Acme", Db.Scalar<string>("SELECT Company FROM Attendees WHERE Email='test.person@example.org'"));
        Assert.Equal("None", Db.Scalar<string>("SELECT DietaryRequirements FROM Attendees WHERE Email='test.person@example.org'"));
    }

    [Fact]
    public async Task Registering_again_with_the_same_email_reuses_the_attendee_row()
    {
        int before = Db.Count("Attendees");
        for (int i = 0; i < 2; i++)
        {
            using var c = await Wizard.ReachStep3();
            await Wizard.PickTalks(c, Wizard.NonOverlapping(await Wizard.Talks(c), 1)[0].Id);
        }
        Assert.Equal(before + 1, Db.Count("Attendees"));
    }

    // ---- step ordering ----

    [Theory]
    [InlineData("/Register/Step2", "/Register/Step1")]
    [InlineData("/Register/Step3", "/Register/Step1")]
    [InlineData("/Register/Step4", "/Register/Step3")]
    public async Task Steps_cannot_be_skipped(string path, string redirectsTo)
    {
        using var c = new ConfRegClient();
        var r = await c.Get(path);
        Assert.Equal(302, r.Code);
        Assert.EndsWith(redirectsTo, r.Location);
    }

    // ---- ticket rules ----

    [Theory]
    [InlineData("student@uni.edu", 302)]
    [InlineData("student@UNI.EDU", 302)]
    [InlineData("student@ox.ac.uk", 200)]   // only .edu counts
    [InlineData("student@example.org", 200)]
    public async Task Student_tickets_require_an_edu_email(string email, int expectedStatus)
    {
        using var c = new ConfRegClient();
        await c.Post("/Register/Step1", Wizard.ValidDetails.Select(d => d.Item1 == "Email" ? ("Email", email) : d).ToArray());
        var r = await c.Post("/Register/Step2", ("ticketType", "Student"));
        Assert.Equal(expectedStatus, r.Code);
        if (expectedStatus == 200) Assert.Contains("Student tickets require an .edu email address.", r.Errors);
    }

    // ---- talk rules ----

    [Fact]
    public async Task Up_to_five_talks_are_allowed()
    {
        using var c = await Wizard.ReachStep3();
        var five = Wizard.NonOverlapping(await Wizard.Talks(c), 5);
        Assert.Equal(5, five.Count);
        Assert.Equal(302, (await Wizard.PickTalks(c, five.Select(t => t.Id).ToArray())).Code);
    }

    [Fact]
    public async Task A_sixth_talk_is_refused()
    {
        using var c = await Wizard.ReachStep3();
        var six = Wizard.NonOverlapping(await Wizard.Talks(c), 6);
        var r = await Wizard.PickTalks(c, six.Select(t => t.Id).ToArray());
        Assert.Equal(200, r.Code);
        Assert.Contains("You can select at most 5 talks.", r.Errors);
    }

    [Fact]
    public async Task A_talk_at_capacity_cannot_be_selected()
    {
        using var c = await Wizard.ReachStep3();
        var full = (await Wizard.Talks(c)).Single(t => t.Full);
        Assert.Equal(0, full.Remaining);
        var r = await Wizard.PickTalks(c, full.Id);
        Assert.Equal(200, r.Code);
        Assert.Contains(r.Errors, e => e.EndsWith("is full."));
    }

    [Fact]
    public async Task Overlapping_talks_cannot_both_be_selected()
    {
        using var c = await Wizard.ReachStep3();
        var (a, b) = Wizard.OverlappingPair(await Wizard.Talks(c));
        var r = await Wizard.PickTalks(c, a.Id, b.Id);
        Assert.Equal(200, r.Code);
        Assert.Contains(r.Errors, e => e.EndsWith("overlap in time."));
        // ...and the refusal did not leave a hold behind.
        Assert.Equal(0, Db.Count("Registrations", "Status=0 AND CreatedUtc > DATEADD(minute,-5,SYSUTCDATETIME())"));
    }

    [Fact]
    public async Task Talks_that_merely_touch_are_not_an_overlap()
    {
        // 10:30-ends / 10:30-starts boundary: seed has back-to-back talks; pick any touching pair.
        using var c = await Wizard.ReachStep3();
        var talks = (await Wizard.Talks(c)).Where(t => !t.Full).ToList();
        var touching = talks.SelectMany(a => talks.Where(b => a.EndMs == b.StartMs).Select(b => (a, b))).FirstOrDefault();
        Assert.NotNull(touching.a); // the seed has back-to-back talks (one ends 10:00, another starts 10:00)
        Assert.Equal(302, (await Wizard.PickTalks(c, touching.a.Id, touching.b.Id)).Code);
    }

    // ---- the hold ----

    [Fact]
    public async Task Choosing_talks_holds_the_seats_for_everyone_else()
    {
        using var a = await Wizard.ReachStep3();
        using var b = new ConfRegClient();
        var before = (await Wizard.Talks(a)).Where(t => !t.Full).OrderBy(t => t.Id).First();

        await Wizard.PickTalks(a, before.Id);

        var seenByB = (await Wizard.ParseTalksAsync(b)).Single(t => t.Id == before.Id);
        Assert.Equal(before.Remaining - 1, seenByB.Remaining);
    }

    [Fact]
    public async Task The_last_seat_goes_to_whoever_holds_it_first()
    {
        using var a = await Wizard.ReachStep3();
        var last = (await Wizard.Talks(a)).Where(t => t.Remaining == 1).OrderBy(t => t.Id).FirstOrDefault();
        if (last == null) // make one: shrink a talk to exactly the seats currently taken, plus one
        {
            var t = (await Wizard.Talks(a)).Where(t => !t.Full).OrderBy(t => t.Id).First();
            Db.Exec("UPDATE Talks SET Capacity = Capacity - @n + 1 WHERE Id=@id", ("@n", t.Remaining), ("@id", t.Id));
            last = t with { Remaining = 1 };
        }

        Assert.Equal(302, (await Wizard.PickTalks(a, last.Id)).Code);

        using var b = await Wizard.ReachStep3(email: "second@example.org");
        var r = await Wizard.PickTalks(b, last.Id);
        Assert.Equal(200, r.Code);
        Assert.Contains(r.Errors, e => e.EndsWith("is full."));
    }

    [Fact]
    public async Task Changing_your_talks_releases_the_previous_hold()
    {
        using var c = await Wizard.ReachStep3();
        var two = Wizard.NonOverlapping(await Wizard.Talks(c), 2);
        await Wizard.PickTalks(c, two[0].Id);
        await Wizard.PickTalks(c, two[1].Id);

        using var other = new ConfRegClient();
        var seen = await Wizard.ParseTalksAsync(other);
        Assert.Equal(two[0].Remaining, seen.Single(t => t.Id == two[0].Id).Remaining); // released
        Assert.Equal(two[1].Remaining - 1, seen.Single(t => t.Id == two[1].Id).Remaining); // held
    }

    // ---- payment ----

    [Fact]
    public async Task A_declined_card_leaves_the_registration_pending_and_unpaid()
    {
        var (c, code) = await Wizard.ReachStep4("Standard", Wizard.NonOverlapping(await TalksOf(), 1).Select(t => t.Id).ToArray());
        using (c)
        {
            var r = await c.Post("/Register/Step4", ("cardNumber", "4000 0000 0000 0002"));
            Assert.Equal(200, r.Code);
            Assert.Contains("Your card was declined.", r.Errors);
            Assert.Equal("Pending", Db.StatusOf(code));
            Assert.Equal(0, Db.Count("Payments p JOIN Registrations r ON r.Id=p.RegistrationId", $"r.ReferenceCode='{code}'"));

            // ...and they can try again with a good card.
            Assert.Contains("code=" + code, await Wizard.Pay(c));
        }
    }

    private static async Task<List<TalkInfo>> TalksOf() { using var c = await Wizard.ReachStep3(); return await Wizard.Talks(c); }
}
