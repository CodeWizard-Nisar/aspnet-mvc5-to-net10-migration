using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;
using Xunit;

namespace Libraries.Net10;

/// <summary>The logic behind the Phase 4 jobs, on .NET 10 against SQL Server. The Quartz wiring is tested in Jobs.Worker.Tests.</summary>
[Collection("db")]
public class JobServiceTests
{
    private readonly LibFixture _f;
    public JobServiceTests(LibFixture f) { _f = f; }

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Func<EmailMessage, bool> Fails { get; set; } = _ => false;
        public void Send(EmailMessage m)
        {
            if (Fails(m)) throw new InvalidOperationException("mailbox unavailable");
            lock (Sent) Sent.Add(m);
        }
    }

    private int UnsentPaid() { using var c = _f.Db.Create(); return c.Registrations.Count(r => r.Status == RegistrationStatus.Paid && r.ConfirmationSentUtc == null); }

    /// <summary>Make sure there is something to send: a fresh paid registration nobody has emailed.</summary>
    private int NewPaidRegistration(string email)
    {
        using var c = _f.Db.Create();
        var a = new Attendee { FirstName = "Job", LastName = "Test", Email = email, CreatedUtc = DateTime.UtcNow };
        var r = new Registration
        {
            ReferenceCode = ConfReg.Domain.Rules.ReferenceCodes.Next(), Attendee = a, TicketType = TicketType.Standard,
            Status = RegistrationStatus.Paid, TotalAmount = 299m, CreatedUtc = DateTime.UtcNow, ConfirmedUtc = DateTime.UtcNow
        };
        c.Registrations.Add(r);
        c.SaveChanges();
        return r.Id;
    }

    [Fact]
    public void Confirmation_emails_go_to_newly_paid_registrations_exactly_once()
    {
        NewPaidRegistration("once@example.org");
        int waiting = UnsentPaid();
        Assert.True(waiting >= 1);

        var mail = new RecordingSender();
        var svc = new ConfirmationEmailService(_f.Db, mail);

        var first = svc.SendPending();
        Assert.Equal(waiting, first.Sent);
        Assert.Equal(0, first.Remaining);
        Assert.Contains(mail.Sent, m => m.To == "once@example.org" && m.Subject.StartsWith("Your ConfReg registration CR-"));

        var second = svc.SendPending();   // the job runs again five minutes later
        Assert.Equal(0, second.Sent);
        Assert.Equal(waiting, mail.Sent.Count);
    }

    [Fact]
    public void Only_paid_registrations_get_a_confirmation()
    {
        using (var c = _f.Db.Create())
        {
            Assert.True(c.Registrations.Any(r => r.Status == RegistrationStatus.Pending), "seed has pending rows");
            Assert.True(c.Registrations.Any(r => r.Status == RegistrationStatus.Cancelled), "seed has cancelled rows");
        }
        var mail = new RecordingSender();
        new ConfirmationEmailService(_f.Db, mail).SendPending();
        using var ctx = _f.Db.Create();
        // An attendee can hold both a paid and a non-paid registration, so check by reference code, not by address.
        Assert.DoesNotContain(mail.Sent, m => ctx.Registrations.Any(r => r.Status != RegistrationStatus.Paid && m.Subject.EndsWith(r.ReferenceCode)));
    }

    [Fact]
    public void A_failed_send_is_released_for_retry_and_does_not_block_the_rest_of_the_batch()
    {
        int a = NewPaidRegistration("bad@example.org"), b = NewPaidRegistration("good@example.org");
        var svc = new ConfirmationEmailService(_f.Db, new RecordingSender { Fails = m => m.To == "bad@example.org" });

        var run1 = svc.SendPending();
        Assert.Equal(1, run1.Failed);
        Assert.True(run1.Sent >= 1);                       // the good ones still went
        using (var c = _f.Db.Create())
        {
            Assert.Null(c.Registrations.Find(a).ConfirmationSentUtc);      // failed: claim released
            Assert.NotNull(c.Registrations.Find(b).ConfirmationSentUtc);   // sent: stays claimed
        }

        var healthy = new RecordingSender();
        var run2 = new ConfirmationEmailService(_f.Db, healthy).SendPending();   // the mailbox recovers
        Assert.Equal(1, run2.Sent);
        Assert.Equal("bad@example.org", healthy.Sent.Single().To);
    }

    [Fact]
    public async Task Two_workers_running_at_once_never_send_the_same_confirmation_twice()
    {
        for (int i = 0; i < 25; i++) NewPaidRegistration($"race{i}@example.org");
        int waiting = UnsentPaid();

        var mail = new RecordingSender();
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new ConfirmationEmailService(_f.Db, mail).SendPending())).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(waiting, mail.Sent.Count);                                   // everything sent...
        Assert.Equal(waiting, mail.Sent.Select(m => m.Subject).Distinct().Count()); // ...once each
        Assert.Equal(0, UnsentPaid());
    }

    [Fact]
    public void Mail_headers_cannot_be_injected_through_an_attendee_field()
    {
        string dir = Path.Combine(Path.GetTempPath(), "confreg-mail-" + Guid.NewGuid().ToString("N"));
        new FileEmailSender(new DirectoryAppPaths(dir)).Send(new EmailMessage { To = "a@example.org\r\nBcc: attacker@example.org", Subject = "s", Body = "b" });
        string text = File.ReadAllText(Directory.GetFiles(Path.Combine(dir, "App_Data", "outbox")).Single());
        Assert.DoesNotContain("\nBcc:", text);
    }

    [Fact]
    public void The_capacity_report_lists_every_talk_and_flags_those_over_80_percent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "confreg-report-" + Guid.NewGuid().ToString("N"));
        var svc = new CapacityReportService(_f.Db, new DirectoryAppPaths(dir));

        string path = svc.WriteReport(new DateTime(2026, 11, 24, 2, 0, 0, DateTimeKind.Utc));
        Assert.Equal("capacity-20261124.csv", Path.GetFileName(path));
        var lines = File.ReadAllLines(path);
        Assert.StartsWith("TalkId,Title,Track,Room,StartsUtc,Capacity,Taken,Remaining,PercentFull,Warning", lines[0]);

        using var ctx = _f.Db.Create();
        Assert.Equal(ctx.Talks.Count(), lines.Length - 1);
        var full = lines.Single(l => l.StartsWith("13,")).Split(',');            // the seed's 12-seat talk
        Assert.Equal("YES", full[^1]);
        Assert.Equal("100", full[^2]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));   // no half-written leftovers

        // Re-running the same night replaces the file rather than failing or appending.
        svc.WriteReport(new DateTime(2026, 11, 24, 2, 5, 0, DateTimeKind.Utc));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.csv"));
    }
}
