using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>
/// Snapshot of the JSON of every GET action, against the fixed seed.
///
/// PHASE 5 CHANGE: until Phase 4 none of these logged in, and they all succeeded anyway, because the admin "login check"
/// lived in a layout that JSON never rendered (BASELINE-PROBLEM #6). Phase 5 secured every admin action, so the admin
/// ones now sign in first. THE SNAPSHOT FILES ARE UNCHANGED: the JSON bytes are exactly what Phase 2 recorded, which is
/// the proof that the migration preserved the wire format. See AdminBehaviourTests.Admin_data_is_not_readable_without_login.
/// </summary>
[Collection("app")]
public class ReadOnlySnapshotTests
{
    private readonly AppFixture _app;
    public ReadOnlySnapshotTests(AppFixture app) { _app = app; _app.Reset(); }

    private async Task Json(string name, string path, Scrub scrub = Scrub.None)
    {
        using var c = AdminSession.IsAdminPath(path) ? await AdminSession.Login() : new ConfRegClient();
        var r = await c.Get(path);
        Assert.Equal(200, r.Code);
        Snapshot.VerifyJson(name, r.Body, scrub);
    }

    [Fact] public Task Home() => Json("home", "/Home/Index");
    [Fact] public Task Register_step1_empty() => Json("register_step1_empty", "/Register/Step1");
    [Fact] public Task Account_login() => Json("account_login", "/Account/Login");
    [Fact] public Task Admin_dashboard() => Json("admin_dashboard", "/Admin");
    [Fact] public Task Admin_settings() => Json("admin_settings", "/Admin/Settings");
    [Fact] public Task Attendees_default() => Json("attendees_default", "/Attendees");
    [Fact] public Task Attendees_page_2() => Json("attendees_page_2", "/Attendees?page=2");
    [Fact] public Task Attendees_search() => Json("attendees_search_okafor", "/Attendees?q=Okafor");
    [Fact] public Task Attendees_filter_ticket_and_status() => Json("attendees_student_paid", "/Attendees?ticket=Student&status=Paid");
    [Fact] public Task Talks_index() => Json("talks_index", "/Talks");
    [Fact] public Task Talks_create_form() => Json("talks_create_form", "/Talks/Create", Scrub.FormDefaultDate);
    [Fact] public Task Talks_edit_form() => Json("talks_edit_1", "/Talks/Edit/1");
    [Fact] public Task Registrations_all() => Json("registrations_all", "/Registrations");
    [Fact] public Task Registrations_pending() => Json("registrations_pending", "/Registrations?status=Pending");
    [Fact] public Task Registration_details_paid() => Json("registration_details_paid_1", "/Registrations/Details/1");
    [Fact] public Task Registration_details_pending() => Json("registration_details_pending_121", "/Registrations/Details/121");
    [Fact] public Task Registration_details_cancelled_refunded() => Json("registration_details_cancelled_131", "/Registrations/Details/131");

    [Fact]
    public async Task Confirmation_for_a_seeded_registration()
    {
        var code = Db.Scalar<string>("SELECT ReferenceCode FROM Registrations WHERE Id = 1")!;
        await Json("register_confirmation_1", "/Register/Confirmation?code=" + code);
    }

    [Fact]
    public async Task Attendees_csv_export()
    {
        using var c = await AdminSession.Login();
        var r = await c.Get("/Attendees/Export?ticket=Student", json: false);
        Assert.Equal(200, r.Code);
        Assert.Equal("text/csv", r.ContentType);
        Snapshot.VerifyText("attendees_export_student", "csv", r.Body);
    }

    [Theory]
    [InlineData("/Registrations/Details/99999")]
    [InlineData("/Talks/Edit/99999")]
    [InlineData("/Register/Confirmation?code=NOPE")]
    public async Task Unknown_ids_are_404(string path)
    {
        using var c = AdminSession.IsAdminPath(path) ? await AdminSession.Login() : new ConfRegClient();
        Assert.Equal(404, (await c.Get(path)).Code);
    }
}
