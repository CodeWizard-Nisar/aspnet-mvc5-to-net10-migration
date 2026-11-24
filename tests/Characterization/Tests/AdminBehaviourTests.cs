using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>Admin portal behaviour: login, every mutation, and the security holes as they exist TODAY.</summary>
[Collection("app")]
public class AdminBehaviourTests
{
    private readonly AppFixture _app;
    public AdminBehaviourTests(AppFixture app) { _app = app; _app.Reset(); }

    private static async Task<ConfRegClient> LoggedIn(string email = "admin@confreg.example", string password = "ConfReg-Demo-1!")
    {
        var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", email), ("Password", password));
        Assert.Equal(302, r.Code);
        return c;
    }

    // ---- login ----

    [Theory]
    [InlineData("admin@confreg.example", "ConfReg-Demo-1!")]
    [InlineData("staff@confreg.example", "ConfReg-Demo-2!")]
    public async Task Seeded_admins_can_log_in_and_are_sent_to_the_dashboard(string email, string password)
    {
        using var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", email), ("Password", password));
        Assert.Equal(302, r.Code);
        Assert.EndsWith("/Admin", r.Location);
    }

    [Theory]
    [InlineData("wrong_password", "admin@confreg.example", "wrong")]
    [InlineData("unknown_user", "nobody@confreg.example", "ConfReg-Demo-1!")]
    [InlineData("empty", "", "")]
    public async Task Bad_credentials_are_refused(string label, string email, string password)
    {
        using var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", email), ("Password", password));
        Assert.Equal(200, r.Code);
        Assert.Contains("Invalid email or password.", r.Errors);
        Snapshot.VerifyJson("account_login_invalid_" + label, r.Body);
    }

    [Fact]
    public async Task The_admin_html_pages_redirect_to_login_when_logged_out()
    {
        using var c = new ConfRegClient();
        var r = await c.Get("/Admin", json: false);
        Assert.Equal(302, r.Code);
        Assert.Contains("/Account/Login?returnUrl=", r.Location);
    }

    [Fact]
    public async Task The_admin_html_pages_render_when_logged_in()
    {
        using var c = await LoggedIn();
        var r = await c.Get("/Admin", json: false);
        Assert.Equal(200, r.Code);
        Assert.Contains("Dashboard", r.Body);
        Assert.Contains("admin@confreg.example", r.Body);
    }

    [Fact]
    public async Task Logging_out_returns_to_the_login_page_and_ends_access()
    {
        using var c = await LoggedIn();
        var r = await c.Get("/Account/Logout", json: false);
        Assert.Equal(302, r.Code);
        Assert.EndsWith("/Account/Login", r.Location);
        Assert.Equal(302, (await c.Get("/Admin", json: false)).Code);
    }

    // ---- known security holes, pinned as current behaviour (BASELINE-PROBLEM #6 and the E-list) ----

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Settings")]
    [InlineData("/Attendees")]
    [InlineData("/Talks")]
    [InlineData("/Registrations")]
    [InlineData("/Registrations/Details/1")]
    public async Task Admin_data_is_readable_without_login_KNOWN_BUG(string path)
    {
        using var c = new ConfRegClient(); // never logs in
        Assert.Equal(200, (await c.Get(path)).Code);
    }

    [Fact]
    public async Task The_csv_export_is_downloadable_without_login_KNOWN_BUG()
    {
        using var c = new ConfRegClient();
        var r = await c.Get("/Attendees/Export", json: false);
        Assert.Equal(200, r.Code);
        Assert.Contains("Id,FirstName,LastName,Email", r.Body);
    }

    [Fact]
    public async Task Cancelling_a_registration_works_without_login_KNOWN_BUG()
    {
        using var c = new ConfRegClient();
        var r = await c.Post("/Registrations/Cancel/1");
        Assert.Equal(302, r.Code);
        Assert.Equal("Cancelled", Db.Scalar<string>("SELECT CASE Status WHEN 2 THEN 'Cancelled' ELSE 'other' END FROM Registrations WHERE Id=1"));
    }

    [Fact]
    public async Task Login_follows_an_unchecked_returnUrl_KNOWN_BUG()
    {
        using var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", "admin@confreg.example"), ("Password", "ConfReg-Demo-1!"), ("ReturnUrl", "https://evil.example/"));
        Assert.Equal("https://evil.example/", r.Location);
    }

    [Fact]
    public async Task Staff_have_the_same_power_as_admins_KNOWN_BUG()
    {
        using var c = await LoggedIn("staff@confreg.example", "ConfReg-Demo-2!");
        Assert.Equal(302, (await c.Post("/Registrations/Cancel/2")).Code);
        Assert.Equal(2, Db.Scalar<int>("SELECT Status FROM Registrations WHERE Id=2"));
    }

    // ---- registrations: cancel and refund ----

    [Fact]
    public async Task An_admin_can_cancel_a_registration_and_its_seats_are_released()
    {
        using var admin = await LoggedIn();
        int regId = 1;
        var talkIds = Db.Scalar<int>("SELECT COUNT(*) FROM RegistrationTalks WHERE RegistrationId=@r", ("@r", regId));
        Assert.True(talkIds > 0);
        int firstTalk = Db.Scalar<int>("SELECT TOP 1 TalkId FROM RegistrationTalks WHERE RegistrationId=@r ORDER BY TalkId", ("@r", regId));

        int takenBefore = TakenFor(firstTalk);
        var r = await admin.Post("/Registrations/Cancel/" + regId);
        Assert.Equal(302, r.Code);
        Assert.EndsWith("/Registrations/Details/" + regId, r.Location);

        var details = (await admin.Get("/Registrations/Details/" + regId)).Root;
        Assert.Equal("Cancelled", details.GetProperty("Status").GetString());
        Assert.False(details.GetProperty("CanCancel").GetBoolean());
        Assert.Equal("Registration cancelled. Seats released.", details.GetProperty("Notice").GetString());
        Assert.Equal(takenBefore - 1, TakenFor(firstTalk));
        Assert.Equal(1, Db.Scalar<int>("SELECT COUNT(*) FROM Payments WHERE RegistrationId=@r AND Status=0", ("@r", regId))); // money did NOT move
    }

    [Fact]
    public async Task Cancelling_an_already_cancelled_registration_changes_nothing()
    {
        using var admin = await LoggedIn();
        await admin.Post("/Registrations/Cancel/131");
        var details = (await admin.Get("/Registrations/Details/131")).Root;
        Assert.Equal("Only pending or paid registrations can be cancelled.", details.GetProperty("Notice").GetString());
    }

    [Fact]
    public async Task Refunding_returns_the_money_and_cancels_the_registration()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Registrations/Refund/1");
        Assert.Equal(302, r.Code);
        Assert.Equal(1, Db.Scalar<int>("SELECT Status FROM Payments WHERE RegistrationId=1"));      // Refunded
        Assert.Equal(2, Db.Scalar<int>("SELECT Status FROM Registrations WHERE Id=1"));              // Cancelled
        var notice = (await admin.Get("/Registrations/Details/1")).Root.GetProperty("Notice").GetString();
        Assert.StartsWith("Refunded ", notice);
    }

    [Fact]
    public async Task Refunding_a_registration_with_no_payment_is_refused()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Registrations/Refund/121"); // pending, never paid
        Assert.Equal(302, r.Code);
        Assert.Equal(0, Db.Scalar<int>("SELECT Status FROM Registrations WHERE Id=121"));
        Assert.Equal("Nothing to refund: no successful payment on this registration.",
            (await admin.Get("/Registrations/Details/121")).Root.GetProperty("Notice").GetString());
    }

    // ---- talks ----

    [Fact]
    public async Task Talks_can_be_created()
    {
        using var admin = await LoggedIn();
        int before = Db.Count("Talks");
        var r = await admin.Post("/Talks/Create", ("Title", "New talk"), ("Speaker", "Someone"), ("Track", "Web"),
            ("StartsUtc", "2027-03-18 09:00"), ("DurationMinutes", "45"), ("Room", "Hall A"), ("Capacity", "40"));
        Assert.Equal(302, r.Code);
        Assert.Equal(before + 1, Db.Count("Talks"));
        Assert.Equal(40, Db.Scalar<int>("SELECT Capacity FROM Talks WHERE Title='New talk'"));
    }

    [Fact]
    public async Task Invalid_talks_are_refused_with_messages()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Talks/Create", ("Title", ""), ("Speaker", ""), ("Track", ""), ("StartsUtc", "2027-03-18 09:00"),
            ("DurationMinutes", "1"), ("Room", ""), ("Capacity", "0"));
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("talks_create_invalid", r.Body);
    }

    [Fact]
    public async Task A_talks_capacity_and_details_can_be_edited()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Talks/Edit", ("Id", "1"), ("Title", "Renamed"), ("Speaker", "S"), ("Track", "Web"),
            ("StartsUtc", "2027-03-16 09:00"), ("DurationMinutes", "60"), ("Room", "Hall A"), ("Capacity", "500"));
        Assert.Equal(302, r.Code);
        Assert.Equal(500, Db.Scalar<int>("SELECT Capacity FROM Talks WHERE Id=1"));
        Assert.Equal("Renamed", Db.Scalar<string>("SELECT Title FROM Talks WHERE Id=1"));
    }

    [Fact]
    public async Task Capacity_cannot_be_lowered_below_the_seats_already_held()
    {
        using var admin = await LoggedIn();
        int taken = TakenFor(13); // seeded as full: 12 of 12
        var r = await admin.Post("/Talks/Edit", ("Id", "13"), ("Title", "SQL Server Indexing Myths"), ("Speaker", "S"), ("Track", "Data"),
            ("StartsUtc", "2027-03-17 10:45"), ("DurationMinutes", "60"), ("Room", "Room 101"), ("Capacity", (taken - 1).ToString()));
        Assert.Equal(200, r.Code);
        Assert.Contains($"Capacity cannot be lower than the {taken} seat(s) already held or paid.", r.Errors);
        Assert.Equal(12, Db.Scalar<int>("SELECT Capacity FROM Talks WHERE Id=13"));
    }

    [Fact]
    public async Task A_talk_with_held_or_paid_seats_cannot_be_deleted()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Talks/Delete/13");
        Assert.Equal(302, r.Code);
        Assert.Equal(1, Db.Count("Talks", "Id=13"));
    }

    [Fact]
    public async Task A_talk_with_no_live_seats_can_be_deleted()
    {
        using var admin = await LoggedIn();
        await admin.Post("/Talks/Create", ("Title", "Disposable"), ("Speaker", "S"), ("Track", "Web"), ("StartsUtc", "2027-03-18 09:00"),
            ("DurationMinutes", "30"), ("Room", "R"), ("Capacity", "10"));
        int id = Db.Scalar<int>("SELECT Id FROM Talks WHERE Title='Disposable'");
        Assert.Equal(302, (await admin.Post("/Talks/Delete/" + id)).Code);
        Assert.Equal(0, Db.Count("Talks", "Title='Disposable'"));
    }

    // ---- settings ----

    [Fact]
    public async Task Changing_a_price_changes_what_the_wizard_charges()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Admin/Settings", ("PriceStandard", "350.00"), ("PriceWorkshop", "499.00"), ("PriceStudent", "99.00"),
            ("OpensUtc", "2000-01-01 00:00"), ("ClosesUtc", "2100-01-01 00:00"));
        Assert.Equal(302, r.Code);

        var talk = Wizard.NonOverlapping(await TalksOf(), 1)[0];
        var (c, _) = await Wizard.ReachStep4("Standard", talk.Id);
        using (c) Assert.Equal(350m, (await c.Get("/Register/Step4")).Root.GetProperty("TotalAmount").GetDecimal());
    }

    [Fact]
    public async Task Closing_registration_stops_new_registrations()
    {
        using var admin = await LoggedIn();
        await admin.Post("/Admin/Settings", ("PriceStandard", "299"), ("PriceWorkshop", "499"), ("PriceStudent", "99"),
            ("OpensUtc", "2000-01-01 00:00"), ("ClosesUtc", "2000-06-01 00:00"));
        using var visitor = new ConfRegClient();
        var r = await visitor.Get("/Register/Step1");
        Assert.True(r.Root.TryGetProperty("OpensUtc", out _)); // the "closed" model, not the form
        var post = await visitor.Post("/Register/Step1", Wizard.ValidDetails);
        Assert.Equal(302, post.Code);                       // bounced back to step 1, which shows "closed"
        Assert.EndsWith("/Register/Step1", post.Location);
    }

    [Fact]
    public async Task Invalid_settings_are_refused()
    {
        using var admin = await LoggedIn();
        var r = await admin.Post("/Admin/Settings", ("PriceStandard", "-1"), ("PriceWorkshop", "499"), ("PriceStudent", "99"),
            ("OpensUtc", "2030-01-01 00:00"), ("ClosesUtc", "2029-01-01 00:00"));
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson("admin_settings_invalid", r.Body);
    }

    // ---- attendees and dashboard ----

    [Fact]
    public async Task Attendee_search_and_filters_narrow_the_list()
    {
        using var c = new ConfRegClient();
        int all = (await c.Get("/Attendees")).Root.GetProperty("Total").GetInt32();
        int students = (await c.Get("/Attendees?ticket=Student")).Root.GetProperty("Total").GetInt32();
        int okafor = (await c.Get("/Attendees?q=Okafor")).Root.GetProperty("Total").GetInt32();
        Assert.Equal(200, all);
        Assert.Equal(25, students);
        Assert.InRange(okafor, 1, 199);
    }

    [Fact]
    public async Task The_csv_export_honours_the_filter_and_neutralises_formulas()
    {
        using var c = new ConfRegClient();
        Db.Exec("UPDATE Attendees SET Company = '=HYPERLINK(\"http://x\")' WHERE Id = 1");
        var all = await c.Get("/Attendees/Export", json: false);
        Assert.Equal(201, all.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length); // header + 200
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\")\"", all.Body);
        var students = await c.Get("/Attendees/Export?ticket=Student", json: false);
        Assert.Equal(26, students.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task The_dashboard_follows_what_happens()
    {
        using var admin = await LoggedIn();
        var before = (await admin.Get("/Admin")).Root;
        Assert.Equal(120, before.GetProperty("PaidTotal").GetInt32());
        Assert.Equal(0, before.GetProperty("PaidToday").GetInt32());

        var talk = Wizard.NonOverlapping(await TalksOf(), 1)[0];
        var (c, _) = await Wizard.ReachStep4("Workshop", talk.Id);
        using (c) await Wizard.Pay(c);

        var after = (await admin.Get("/Admin")).Root;
        Assert.Equal(121, after.GetProperty("PaidTotal").GetInt32());
        Assert.Equal(1, after.GetProperty("PaidToday").GetInt32());
        Assert.Equal(before.GetProperty("TotalRevenue").GetDecimal() + 499m, after.GetProperty("TotalRevenue").GetDecimal());
    }

    [Fact]
    public async Task The_dashboard_warns_about_talks_over_80_percent_full()
    {
        using var c = new ConfRegClient();
        var warnings = (await c.Get("/Admin")).Root.GetProperty("CapacityWarnings").EnumerateArray().ToList();
        Assert.Equal(new[] { 13, 6, 9 }, warnings.Select(w => w.GetProperty("TalkId").GetInt32()).ToArray()); // 100%, 93%, 88%
    }

    // ---- helpers ----

    private static int TakenFor(int talkId) => Db.Scalar<int>(
        "SELECT COUNT(*) FROM RegistrationTalks rt JOIN Registrations r ON r.Id=rt.RegistrationId WHERE rt.TalkId=@t AND r.Status IN (0,1)", ("@t", talkId));

    private static async Task<List<TalkInfo>> TalksOf() { using var c = await Wizard.ReachStep3(); return await Wizard.Talks(c); }
}
