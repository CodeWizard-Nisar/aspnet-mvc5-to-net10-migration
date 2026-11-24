using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
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
    ///   - The wizard remembers the hold in SESSION (WizardState.Registration / HoldExpiresUtc).
    ///   - The expiry job (ExpiryService) cannot see anyone's Session. It only sees the DB row, and
    ///     expires Pending rows older than HoldPolicy.Minutes. Two clocks, two copies of the truth.
    /// </summary>
    public class RegistrationService
    {
        /// <summary>Seats currently held per talk. Pending and Paid hold seats; Cancelled and Expired do not.</summary>
        public Dictionary<int, int> SeatsTaken(ConfRegContext ctx)
        {
            return ctx.RegistrationTalks
                .Where(rt => rt.Registration.Status == RegistrationStatus.Pending
                          || rt.Registration.Status == RegistrationStatus.Paid)
                .GroupBy(rt => rt.TalkId)
                .Select(g => new { TalkId = g.Key, Count = g.Count() })
                .ToDictionary(x => x.TalkId, x => x.Count);
        }

        /// <summary>
        /// Validates the selection and, if valid, writes the Pending registration that holds the seats.
        /// Returns validation errors; an empty list means the hold was placed.
        ///
        /// Extra problem (not one of the ten planned ones, see docs "Unplanned problems"): the capacity
        /// check and the insert are separate statements with no lock or transaction, so two people
        /// taking the last seat at the same moment can both succeed.
        /// </summary>
        public List<string> PlaceHold(WizardState state, IEnumerable<int> talkIds)
        {
            using (var ctx = new ConfRegContext())
            {
                // Step back and re-submit: drop the previous hold first so its own seats
                // do not count against this attempt.
                ReleaseHold(ctx, state);

                var ids = (talkIds ?? new int[0]).Distinct().ToList();
                var talks = ctx.Talks.Where(t => ids.Contains(t.Id)).ToList();
                var errors = TalkRules.Validate(talks, SeatsTaken(ctx), HoldPolicy.MaxTalks);
                if (errors.Count > 0) return errors;

                // Re-use the Attendee row if this email registered before.
                string email = state.Attendee.Email;
                var attendee = ctx.Attendees.FirstOrDefault(a => a.Email == email);
                if (attendee == null)
                {
                    attendee = new Attendee { CreatedUtc = DateTime.UtcNow };
                    ctx.Attendees.Add(attendee);
                }
                attendee.FirstName = state.Attendee.FirstName;
                attendee.LastName = state.Attendee.LastName;
                attendee.Email = state.Attendee.Email;
                attendee.Company = state.Attendee.Company;
                attendee.JobTitle = state.Attendee.JobTitle;
                attendee.DietaryRequirements = state.Attendee.DietaryRequirements;

                var reg = new Registration
                {
                    ReferenceCode = ReferenceCodes.Next(),
                    Attendee = attendee,
                    TicketType = state.TicketType.Value,
                    Status = RegistrationStatus.Pending,
                    TotalAmount = new SettingsService().PriceFor(state.TicketType.Value),
                    CreatedUtc = DateTime.UtcNow,
                    RegistrationTalks = talks.Select(t => new RegistrationTalk { Talk = t }).ToList()
                };
                ctx.Registrations.Add(reg);
                ctx.SaveChanges();

                // BASELINE-PROBLEM #2: whole entities go into Session.
                state.Attendee = attendee;
                state.SelectedTalks = talks;
                state.Registration = reg;
                state.HoldExpiresUtc = reg.CreatedUtc.AddMinutes(HoldPolicy.Minutes);
                return errors;
            }
        }

        public void ReleaseHold(WizardState state)
        {
            using (var ctx = new ConfRegContext())
            {
                ReleaseHold(ctx, state);
            }
        }

        private static void ReleaseHold(ConfRegContext ctx, WizardState state)
        {
            if (state.Registration == null) return;
            int id = state.Registration.Id;
            var row = ctx.Registrations.FirstOrDefault(r => r.Id == id);
            if (row != null && row.Status == RegistrationStatus.Pending)
            {
                row.Status = RegistrationStatus.Expired;
                ctx.SaveChanges();
            }
            state.Registration = null;
            state.HoldExpiresUtc = null;
            state.SelectedTalks = new List<Talk>();
        }

        public PayOutcome Pay(WizardState state, string cardNumber)
        {
            if (state.Registration == null || state.HoldExpiresUtc == null)
                return new PayOutcome { Status = PayStatus.NoHold, Message = "There is no registration in progress." };

            // The session's clock decides whether the hold is still good.
            if (DateTime.UtcNow > state.HoldExpiresUtc.Value)
            {
                ReleaseHold(state);
                return new PayOutcome { Status = PayStatus.HoldExpired, Message = "Your seat hold expired after " + HoldPolicy.Minutes + " minutes. Please start again." };
            }

            var reg = state.Registration;
            var result = new PaymentGateway().Charge(reg.TotalAmount, cardNumber, reg.ReferenceCode);
            if (!result.Success)
                return new PayOutcome { Status = PayStatus.Declined, Message = result.Message };

            using (var ctx = new ConfRegContext())
            {
                // Extra problem: the entity in Session is a stale, detached copy. Attach() trusts it, and
                // the UPDATE below writes Status=Paid over whatever the DB now says. If the expiry job has
                // flipped this row to Expired in the meantime (its clock and the session's clock are not the
                // same clock) we resurrect a registration whose seats were already released and may have
                // been given to someone else. The Phase 2 tests pin down the happy path; this race is
                // documented, not fixed, until the session state is replaced in Phase 5.
                ctx.Registrations.Attach(reg);
                reg.Status = RegistrationStatus.Paid;
                reg.ConfirmedUtc = DateTime.UtcNow;
                ctx.Payments.Add(new Payment
                {
                    RegistrationId = reg.Id,
                    Amount = reg.TotalAmount,
                    Provider = "StubPay",
                    ProviderRef = result.ProviderRef,
                    Status = PaymentStatus.Succeeded,
                    ProcessedUtc = DateTime.UtcNow
                });
                ctx.SaveChanges();
            }

            return new PayOutcome { Status = PayStatus.Paid, ReferenceCode = reg.ReferenceCode, Message = "Paid" };
        }
    }
}
