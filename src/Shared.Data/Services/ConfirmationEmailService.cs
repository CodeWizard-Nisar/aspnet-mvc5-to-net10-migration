using System;
using System.Collections.Generic;
using System.Linq;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Services
{
    public class ConfirmationRunResult
    {
        public int Sent { get; set; }
        public int Failed { get; set; }
        public int Remaining { get; set; }
    }

    /// <summary>
    /// Sends a confirmation email for each newly paid registration, exactly once.
    ///
    /// Idempotency is the design problem, not sending. The job runs every 5 minutes, may overlap with a manual run,
    /// and a mail provider can fail halfway through a batch. So each registration is CLAIMED first with one atomic
    /// statement:  UPDATE ... SET ConfirmationSentUtc = now WHERE Id = x AND ConfirmationSentUtc IS NULL.
    /// Only the caller whose UPDATE affects a row sends the mail; a second worker gets zero rows and skips it.
    /// If sending then fails, the claim is released so the next run retries. One bad address never blocks the batch.
    /// </summary>
    public class ConfirmationEmailService
    {
        private const int BatchSize = 200;
        private readonly IConfRegContextFactory _db;
        private readonly IEmailSender _mail;

        public ConfirmationEmailService(IConfRegContextFactory db, IEmailSender mail)
        {
            _db = db;
            _mail = mail;
        }

        public ConfirmationRunResult SendPending()
        {
            var result = new ConfirmationRunResult();
            List<int> ids;
            using (var ctx = _db.Create())
            {
                ids = ctx.Registrations
                    .Where(r => r.Status == RegistrationStatus.Paid && r.ConfirmationSentUtc == null)
                    .OrderBy(r => r.ConfirmedUtc).ThenBy(r => r.Id)
                    .Select(r => r.Id).Take(BatchSize).ToList();
            }

            foreach (int id in ids)
            {
                using (var ctx = _db.Create())
                {
                    int claimed = ctx.Database.ExecuteSqlCommand(
                        "UPDATE Registrations SET ConfirmationSentUtc = {0} WHERE Id = {1} AND ConfirmationSentUtc IS NULL AND Status = 1",
                        DateTime.UtcNow, id);
                    if (claimed == 0) continue; // someone else sent it, or it was cancelled in the meantime

                    try
                    {
                        _mail.Send(Compose(ctx, id));
                        result.Sent++;
                    }
                    catch (Exception)
                    {
                        ctx.Database.ExecuteSqlCommand("UPDATE Registrations SET ConfirmationSentUtc = NULL WHERE Id = {0}", id);
                        result.Failed++;
                    }
                }
            }

            using (var ctx = _db.Create())
                result.Remaining = ctx.Registrations.Count(r => r.Status == RegistrationStatus.Paid && r.ConfirmationSentUtc == null);
            return result;
        }

        private static EmailMessage Compose(ConfRegContext ctx, int id)
        {
            var r = ctx.Registrations.Include("Attendee").Include("RegistrationTalks.Talk").Single(x => x.Id == id);
            var talks = r.RegistrationTalks.Select(rt => rt.Talk).OrderBy(t => t.StartsUtc).ThenBy(t => t.Id).ToList();
            var body = new System.Text.StringBuilder();
            body.AppendLine("Hello " + r.Attendee.FirstName + ",");
            body.AppendLine();
            body.AppendLine("You are registered. Your reference code is " + r.ReferenceCode + ".");
            body.AppendLine("Ticket: " + r.TicketType + ", paid " + r.TotalAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            body.AppendLine();
            if (talks.Count == 0) body.AppendLine("You have not selected any talks.");
            else
            {
                body.AppendLine("Your talks:");
                foreach (var t in talks)
                    body.AppendLine("  - " + t.StartsUtc.ToString("ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC, " + t.Room + ": " + t.Title);
            }
            return new EmailMessage { To = r.Attendee.Email, Subject = "Your ConfReg registration " + r.ReferenceCode, Body = body.ToString() };
        }
    }
}
