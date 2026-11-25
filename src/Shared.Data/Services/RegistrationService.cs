using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;
using ConfReg.Domain.Services;
using ConfReg.Domain.Wizard;

namespace ConfReg.Data.Services
{
    public enum PayStatus { Paid, NoHold, HoldExpired, Declined }

    public class PayOutcome
    {
        public PayStatus Status { get; set; }
        public string Message { get; set; }
        public string ReferenceCode { get; set; }
    }

    /// <summary>
    /// The wizard's business logic: place a seat hold, pay, release.
    ///
    /// THE HOLD (the centrepiece of the migration write-up):
    ///   - Placing a hold inserts a Pending Registration + RegistrationTalk rows. Those rows
    ///     count against talk capacity, so the seats really are held.
    ///   - The wizard remembers the hold in SESSION (WizardSnapshot: the registration id and the attendee's deadline).
    ///   - The expiry job (ExpiryService) cannot see anyone's Session. It only sees the DB row, and
    ///     expires Pending rows older than ISettings.HoldMinutes. Two clocks, two copies of the truth.
    /// </summary>
    public class RegistrationService
    {
        private readonly IConfRegContextFactory _db;
        private readonly ISettings _settings;
        private readonly SettingsService _prices;
        private readonly PaymentGateway _gateway;

        public RegistrationService(IConfRegContextFactory db, ISettings settings, SettingsService prices, PaymentGateway gateway)
        {
            _db = db;
            _settings = settings;
            _prices = prices;
            _gateway = gateway;
        }

        /// <summary>Seats currently held per talk. Pending and Paid hold seats; Cancelled and Expired do not.</summary>
        public Dictionary<int, int> SeatsTaken(ConfRegContext ctx)
        {
            return SeatCounts.For(ctx);
        }

        /// <summary>
        /// Validates the selection and, if valid, writes the Pending registration that holds the seats and points the
        /// snapshot at it. Returns validation errors; an empty list means the hold was placed. The caller saves the snapshot.
        ///
        /// Known problem E1, unchanged: the capacity check and the insert are separate statements with no lock or
        /// transaction, so two people taking the last seat at the same instant can both succeed.
        /// </summary>
        public List<string> PlaceHold(WizardSnapshot state, IEnumerable<int> talkIds)
        {
            using (var ctx = _db.Create())
            {
                // Step back and re-submit: drop the previous hold first so its own seats do not count against this attempt.
                ReleaseHold(ctx, state);

                var ids = (talkIds ?? new int[0]).Distinct().ToList();
                var talks = ctx.Talks.Where(t => ids.Contains(t.Id)).ToList();
                var errors = TalkRules.Validate(talks, SeatsTaken(ctx), _settings.MaxTalksPerRegistration);
                if (errors.Count > 0) return errors;

                // Re-use the Attendee row if this email registered before.
                string email = state.Email;
                var attendee = ctx.Attendees.FirstOrDefault(a => a.Email == email);
                if (attendee == null)
                {
                    attendee = new Attendee { CreatedUtc = DateTime.UtcNow };
                    ctx.Attendees.Add(attendee);
                }
                attendee.FirstName = state.FirstName;
                attendee.LastName = state.LastName;
                attendee.Email = state.Email;
                attendee.Company = state.Company;
                attendee.JobTitle = state.JobTitle;
                attendee.DietaryRequirements = state.DietaryRequirements;

                var reg = new Registration
                {
                    ReferenceCode = ReferenceCodes.Next(),
                    Attendee = attendee,
                    TicketType = state.TicketType.Value,
                    Status = RegistrationStatus.Pending,
                    TotalAmount = _prices.PriceFor(state.TicketType.Value),
                    CreatedUtc = DateTime.UtcNow,
                    RegistrationTalks = talks.Select(t => new RegistrationTalk { Talk = t }).ToList()
                };
                ctx.Registrations.Add(reg);
                ctx.SaveChanges();

                // BASELINE-PROBLEM #2, fixed: ids and primitives, no entities.
                state.SelectedTalkIds = talks.Select(t => t.Id).ToList();
                state.RegistrationId = reg.Id;
                state.ReferenceCode = reg.ReferenceCode;
                state.TotalAmount = reg.TotalAmount;
                state.HoldExpiresUtc = reg.CreatedUtc.AddMinutes(_settings.HoldMinutes);
                return errors;
            }
        }

        public void ReleaseHold(WizardSnapshot state)
        {
            using (var ctx = _db.Create())
            {
                ReleaseHold(ctx, state);
            }
        }

        private static void ReleaseHold(ConfRegContext ctx, WizardSnapshot state)
        {
            if (state.RegistrationId != null)
            {
                int id = state.RegistrationId.Value;
                var row = ctx.Registrations.FirstOrDefault(r => r.Id == id);
                if (row != null && row.Status == RegistrationStatus.Pending)
                {
                    row.Status = RegistrationStatus.Expired;
                    ctx.SaveChanges();
                }
            }
            ForgetHold(state);
        }

        private static void ForgetHold(WizardSnapshot state)
        {
            state.RegistrationId = null;
            state.ReferenceCode = null;
            state.TotalAmount = null;
            state.HoldExpiresUtc = null;
            state.SelectedTalkIds = new List<int>();
        }

        public PayOutcome Pay(WizardSnapshot state, string cardNumber)
        {
            if (state.RegistrationId == null || state.HoldExpiresUtc == null)
                return new PayOutcome { Status = PayStatus.NoHold, Message = "There is no registration in progress." };

            int id = state.RegistrationId.Value;
            decimal amount;
            string code = state.ReferenceCode;

            // The DATABASE ROW is the truth about the hold, not the session. Phase 1 trusted a stale copy of the entity
            // from session and wrote Paid over whatever the row now said (known problem E2): if the expiry job had already
            // expired it and released its seats, paying resurrected a registration whose seats may belong to someone else.
            using (var ctx = _db.Create())
            {
                var row = ctx.Registrations.FirstOrDefault(r => r.Id == id);
                if (row == null || row.Status != RegistrationStatus.Pending)
                    return HoldLost(state);
                amount = row.TotalAmount;
            }

            if (DateTime.UtcNow > state.HoldExpiresUtc.Value)
            {
                ReleaseHold(state);
                return new PayOutcome { Status = PayStatus.HoldExpired, Message = "Your seat hold expired after " + _settings.HoldMinutes + " minutes. Please start again." };
            }

            var result = _gateway.Charge(amount, cardNumber, code);
            if (!result.Success)
                return new PayOutcome { Status = PayStatus.Declined, Message = result.Message };

            using (var ctx = _db.Create())
            {
                // Mark paid ONLY IF it is still pending, in one statement. The job could expire the row between the check
                // above and here; if it did, no row matches and we must give the money back instead of keeping it.
                int paid = ctx.Database.ExecuteSqlCommand(
                    "UPDATE Registrations SET Status = 1, ConfirmedUtc = {0} WHERE Id = {1} AND Status = 0", DateTime.UtcNow, id);
                if (paid == 0)
                {
                    _gateway.Refund(result.ProviderRef, amount);
                    var lost = HoldLost(state);
                    lost.Message = "Your seat hold expired while the payment was being processed. You have not been charged.";
                    return lost;
                }

                ctx.Payments.Add(new Payment
                {
                    RegistrationId = id,
                    Amount = amount,
                    Provider = "StubPay",
                    ProviderRef = result.ProviderRef,
                    Status = PaymentStatus.Succeeded,
                    ProcessedUtc = DateTime.UtcNow
                });
                ctx.SaveChanges();
            }

            return new PayOutcome { Status = PayStatus.Paid, ReferenceCode = code, Message = "Paid" };
        }

        private PayOutcome HoldLost(WizardSnapshot state)
        {
            ForgetHold(state);
            return new PayOutcome { Status = PayStatus.HoldExpired, Message = "Your seat hold expired after " + _settings.HoldMinutes + " minutes. Please start again." };
        }
    }
}
