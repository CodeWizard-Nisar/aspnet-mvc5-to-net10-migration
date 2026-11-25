using ConfReg.Data;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;
using ConfReg.Domain.Services;
using ConfReg.Domain.Wizard;
using Libraries.Net10;
using Xunit;

namespace Libraries.Net10;

/// <summary>EF6 + the real services on .NET 10 against SQL Server. The same code the legacy app runs on net48.</summary>
[Collection("db")]
public class DatabaseTests
{
    private readonly LibFixture _f;
    public DatabaseTests(LibFixture f) { _f = f; }

    private (RegistrationService Reg, WizardSession Wizard, SettingsService Prices) Services(string ip = "203.0.113.9")
    {
        var prices = new SettingsService(_f.Db, _f.Settings);
        var gateway = new PaymentGateway(_f.Settings, new StaticClientInfo(ip), new DirectoryAppPaths(_f.TempDir));
        return (new RegistrationService(_f.Db, _f.Settings, prices, gateway), new WizardSession(new DictionarySessionStore()), prices);
    }

    private List<Talk> TwoFreeNonOverlappingTalks()
    {
        using var ctx = _f.Db.Create();
        var seats = Services().Reg.SeatsTaken(ctx);
        var picked = new List<Talk>();
        foreach (var t in ctx.Talks.OrderBy(t => t.StartsUtc).ThenBy(t => t.Id).ToList())
            if (TalkRules.Remaining(t, seats) > 5 && picked.All(p => !TalkRules.Overlaps(p, t))) { picked.Add(t); if (picked.Count == 2) break; }
        return picked;
    }

    // Phase 5: the wizard's session value is a flat WizardSnapshot, not entities (BASELINE-PROBLEM #2).
    private static WizardSnapshot Start(string email, TicketType ticket) => new()
    {
        FirstName = "Net", LastName = "Ten", Email = email, Company = "Contoso", TicketType = ticket
    };

    [Fact]
    public void EF6_creates_and_seeds_the_database_on_net10()
    {
        using var ctx = _f.Db.Create();
        // Other tests in this class add rows, so count the SEEDED id range rather than the whole table.
        Assert.Equal(200, ctx.Attendees.Count(a => a.Id <= 200));
        Assert.Equal(20, ctx.Talks.Count());
        Assert.Equal(3, ctx.Talks.Select(t => t.Track).Distinct().Count());
        Assert.Equal(150, ctx.Registrations.Count(r => r.Id <= 150));
        Assert.All(ctx.AdminUsers.Where(u => u.Email.EndsWith("@confreg.example") && u.Id <= 2).ToList(), u => Assert.True(_f.Hasher.IsCurrentFormat(u.PasswordHash))); // new databases start on BCrypt
    }

    [Fact]
    public void A_registration_can_be_held_and_paid_through_the_shared_services()
    {
        var (reg, _, _) = Services();
        var talks = TwoFreeNonOverlappingTalks();
        var state = Start("paid@example.org", TicketType.Workshop);

        Assert.Empty(reg.PlaceHold(state, talks.Select(t => t.Id)));
        Assert.NotNull(state.RegistrationId);
        Assert.Equal(499m, state.TotalAmount);

        var outcome = reg.Pay(state, "4242 4242 4242 4242");
        Assert.Equal(PayStatus.Paid, outcome.Status);

        using var ctx = _f.Db.Create();
        var row = ctx.Registrations.Single(r => r.ReferenceCode == outcome.ReferenceCode);
        Assert.Equal(RegistrationStatus.Paid, row.Status);
        Assert.Single(ctx.Payments.Where(p => p.RegistrationId == row.Id && p.Status == PaymentStatus.Succeeded));
    }

    [Fact]
    public void The_payment_audit_log_uses_the_injected_client_ip_and_app_path()
    {
        var (reg, _, _) = Services(ip: "198.51.100.77");
        var talks = TwoFreeNonOverlappingTalks();
        var state = Start("audit@example.org", TicketType.Standard);
        reg.PlaceHold(state, talks.Select(t => t.Id).Take(1));
        reg.Pay(state, "4242 4242 4242 4242");

        string log = File.ReadAllText(Path.Combine(_f.TempDir, "App_Data", "payments.log"));
        Assert.Contains("ip=198.51.100.77", log);
        Assert.Contains("CHARGE", log);
    }

    [Fact]
    public void Overlap_and_capacity_rules_hold_through_the_services()
    {
        var (reg, _, _) = Services();
        using (var ctx = _f.Db.Create())
        {
            var all = ctx.Talks.ToList();
            var pair = all.SelectMany(a => all.Where(b => a.Id < b.Id && TalkRules.Overlaps(a, b)).Select(b => (a, b))).First();
            var state = Start("rules@example.org", TicketType.Standard);
            Assert.Contains(reg.PlaceHold(state, new[] { pair.a.Id, pair.b.Id }), e => e.Contains("overlap"));

            var seats = reg.SeatsTaken(ctx);
            var full = all.First(t => TalkRules.Remaining(t, seats) <= 0);
            Assert.Contains(reg.PlaceHold(state, new[] { full.Id }), e => e.EndsWith("is full."));
        }
    }

    [Fact]
    public void Expiry_releases_the_seats_of_an_abandoned_hold_and_leaves_fresh_ones_alone()
    {
        var (reg, _, _) = Services();
        var expiry = new ExpiryService(_f.Db, _f.Settings);
        expiry.ExpireUnpaid(); // clear the seed's own overdue pending rows first

        var talk = TwoFreeNonOverlappingTalks()[0];
        var state = Start("abandon@example.org", TicketType.Standard);
        reg.PlaceHold(state, new[] { talk.Id });
        int heldBefore, heldAfter;
        using (var ctx = _f.Db.Create()) heldBefore = reg.SeatsTaken(ctx)[talk.Id];

        Assert.Equal(0, expiry.ExpireUnpaid()); // fresh: untouched

        using (var ctx = _f.Db.Create())
            ctx.Database.ExecuteSqlCommand("UPDATE Registrations SET CreatedUtc = DATEADD(minute,-31,SYSUTCDATETIME()) WHERE Id = {0}", state.RegistrationId.Value);
        Assert.Equal(1, expiry.ExpireUnpaid());

        using (var ctx = _f.Db.Create())
        {
            heldAfter = reg.SeatsTaken(ctx)[talk.Id];
            Assert.Equal(RegistrationStatus.Expired, ctx.Registrations.Find(state.RegistrationId.Value).Status);
        }
        Assert.Equal(heldBefore - 1, heldAfter);
    }

    [Fact]
    public void The_hold_length_comes_from_settings_not_a_constant()
    {
        var short5 = LibFixture.SettingsFrom(_f.ConnectionString, holdMinutes: 5);
        var expiry = new ExpiryService(_f.Db, short5);
        var (reg, _, _) = Services();
        var talk = TwoFreeNonOverlappingTalks()[0];
        var state = Start("short@example.org", TicketType.Standard);
        reg.PlaceHold(state, new[] { talk.Id });
        using (var ctx = _f.Db.Create())
            ctx.Database.ExecuteSqlCommand("UPDATE Registrations SET CreatedUtc = DATEADD(minute,-6,SYSUTCDATETIME()) WHERE Id = {0}", state.RegistrationId.Value);

        Assert.True(expiry.ExpireUnpaid() >= 1); // 6 minutes old is overdue under a 5 minute hold, though not under 30
    }

    [Fact]
    public void A_legacy_password_row_is_upgraded_to_bcrypt_on_next_login_and_keeps_working()
    {
        string email = "legacy-" + Guid.NewGuid().ToString("N")[..8] + "@confreg.example";
        using (var ctx = _f.Db.Create())
        {
            ctx.AdminUsers.Add(new AdminUser { Email = email, PasswordHash = _f.Legacy.Encrypt("Old-Pass-1!"), Role = "Staff" });
            ctx.SaveChanges();
        }
        var auth = new AdminAuthService(_f.Db, _f.Hasher, _f.Legacy);

        Assert.Null(auth.Validate(email, "wrong"));
        Assert.False(_f.Hasher.IsCurrentFormat(Stored(email)));      // a wrong guess upgrades nothing

        Assert.NotNull(auth.Validate(email, "Old-Pass-1!"));         // the right password logs in...
        Assert.True(_f.Hasher.IsCurrentFormat(Stored(email)));       // ...and the row is now BCrypt

        Assert.NotNull(auth.Validate(email, "Old-Pass-1!"));         // still works on the new format
        Assert.Null(auth.Validate(email, "wrong"));
    }

    private string Stored(string email) { using var ctx = _f.Db.Create(); return ctx.AdminUsers.Single(u => u.Email == email).PasswordHash; }

    [Fact]
    public void The_dashboard_query_no_longer_leaks_connections_BASELINE_9()
    {
        // Phase 1's version stranded one pooled connection per call. With a pool of 5 and a 5 second timeout, the
        // 6th call would have thrown "max pool size was reached". 60 calls prove nothing is left open.
        var cs = _f.ConnectionString.TrimEnd(';') + ";Max Pool Size=5;Connect Timeout=5";
        var tight = new ConfReg.Data.Infrastructure.ConfRegContextFactory(LibFixture.SettingsFrom(cs));
        var reports = new ReportQueries(tight);

        for (int i = 0; i < 60; i++) Assert.NotEmpty(reports.RevenueByTicketType());
    }

    [Fact]
    public void A_missing_price_row_falls_back_to_that_ticket_types_own_default()
    {
        // Phase 1 defaulted EVERY ticket type to 299 when its row was missing.
        using (var ctx = _f.Db.Create()) ctx.Database.ExecuteSqlCommand("DELETE FROM Settings WHERE [Key] = 'Price.Student'");
        Assert.Equal(99m, Services().Prices.PriceFor(TicketType.Student));
        using (var ctx = _f.Db.Create()) ctx.Database.ExecuteSqlCommand("INSERT INTO Settings([Key],[Value]) VALUES('Price.Student','99')");
    }
}
