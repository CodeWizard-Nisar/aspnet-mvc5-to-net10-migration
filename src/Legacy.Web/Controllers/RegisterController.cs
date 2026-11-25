using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Web.Mvc;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;
using ConfReg.Domain.Wizard;
using ConfReg.ViewModels;

namespace ConfReg.Legacy.Controllers
{
    /// <summary>
    /// The 4-step public wizard. State is carried between requests in Session via WizardSession.
    ///
    /// Note what is NOT here: no DI (services are `new`ed), no [Authorize] (it is public),
    /// no abstraction over Session, no clock. All deliberate, see docs/01-baseline.md.
    /// </summary>
    [AllowAnonymous] // public: the registration wizard, the home page, and the login page itself
    public class RegisterController : AppController
    {
        private readonly RegistrationService _registrations;
        private readonly SettingsService _settings;
        private readonly WizardSession _wizard;
        private readonly IConfRegContextFactory _db;
        private readonly ISettings _appSettings;

        public RegisterController(RegistrationService registrations, SettingsService settings, WizardSession wizard, IConfRegContextFactory db, ISettings appSettings)
        {
            _registrations = registrations;
            _settings = settings;
            _wizard = wizard;
            _db = db;
            _appSettings = appSettings;
        }

        // ---------------- Step 1: attendee ----------------

        [HttpGet]
        public ActionResult Step1()
        {
            if (!_settings.IsOpen())
                return Respond(new ClosedModel { OpensUtc = _settings.OpensUtc, ClosesUtc = _settings.ClosesUtc }, "Closed");

            var a = _wizard.Load();
            var form = new Step1Form { Notice = TempData["Notice"] as string };
            if (a.Email != null)
            {
                form.FirstName = a.FirstName; form.LastName = a.LastName; form.Email = a.Email;
                form.Company = a.Company; form.JobTitle = a.JobTitle; form.DietaryRequirements = a.DietaryRequirements;
            }
            return Respond(form);
        }

        [HttpPost]
        public ActionResult Step1(Step1Form form)
        {
            if (!_settings.IsOpen()) return RedirectToAction("Step1");

            if (string.IsNullOrWhiteSpace(form.FirstName)) form.Errors.Add("First name is required.");
            if (string.IsNullOrWhiteSpace(form.LastName)) form.Errors.Add("Last name is required.");
            if (!IsValidEmail(form.Email)) form.Errors.Add("A valid email address is required.");
            if (form.Errors.Count > 0) return Respond(form);

            var state = _wizard.Load();
            // Changing details invalidates any hold made for the old ones.
            _registrations.ReleaseHold(state);
            state.TicketType = null;
            state.FirstName = form.FirstName.Trim();
            state.LastName = form.LastName.Trim();
            state.Email = form.Email.Trim();
            state.Company = form.Company;
            state.JobTitle = form.JobTitle;
            state.DietaryRequirements = form.DietaryRequirements;
            _wizard.Save(state);
            return RedirectToAction("Step2");
        }

        // ---------------- Step 2: ticket ----------------

        [HttpGet]
        public ActionResult Step2()
        {
            var state = _wizard.Load();
            if (state.Email == null) return RedirectToAction("Step1");
            return Respond(BuildStep2(state.TicketType));
        }

        [HttpPost]
        public ActionResult Step2(string ticketType)
        {
            var state = _wizard.Load();
            if (state.Email == null) return RedirectToAction("Step1");

            TicketType parsed;
            var form = BuildStep2(null);
            form.TicketType = ticketType;

            if (!Enum.TryParse(ticketType, true, out parsed) || !Enum.IsDefined(typeof(TicketType), parsed))
                form.Errors.Add("Please choose a ticket type.");
            else if (parsed == TicketType.Student && !TicketRules.IsEduEmail(state.Email))
                form.Errors.Add("Student tickets require an .edu email address.");

            if (form.Errors.Count > 0) return Respond(form);

            // A different ticket means a different price: any existing hold is now wrong.
            _registrations.ReleaseHold(state);
            state.TicketType = parsed;
            _wizard.Save(state);
            return RedirectToAction("Step3");
        }

        private Step2Form BuildStep2(TicketType? selected)
        {
            return new Step2Form
            {
                TicketType = selected.HasValue ? selected.Value.ToString() : null,
                Options = new[] { TicketType.Standard, TicketType.Workshop, TicketType.Student }
                    .Select(t => new TicketOption { Type = t.ToString(), Name = t + " ticket", Price = _settings.PriceFor(t) })
                    .ToList()
            };
        }

        // ---------------- Step 3: talks ----------------

        [HttpGet]
        public ActionResult Step3()
        {
            var state = _wizard.Load();
            if (state.Email == null) return RedirectToAction("Step1");
            if (state.TicketType == null) return RedirectToAction("Step2");
            return Respond(BuildStep3(state, state.SelectedTalkIds));
        }

        [HttpPost]
        public ActionResult Step3(int[] talkIds)
        {
            var state = _wizard.Load();
            if (state.Email == null) return RedirectToAction("Step1");
            if (state.TicketType == null) return RedirectToAction("Step2");

            var errors = _registrations.PlaceHold(state, talkIds);
            _wizard.Save(state); // PlaceHold released any earlier hold even when the new selection is refused
            if (errors.Count > 0)
            {
                var form = BuildStep3(state, talkIds ?? new int[0]);
                form.Errors.AddRange(errors);
                return Respond(form);
            }
            return RedirectToAction("Step4");
        }

        private Step3Form BuildStep3(WizardSnapshot state, IEnumerable<int> selectedIds)
        {
            var selected = new HashSet<int>(selectedIds);
            using (var ctx = _db.Create())
            {
                var seats = _registrations.SeatsTaken(ctx);
                var talks = ctx.Talks.OrderBy(t => t.StartsUtc).ThenBy(t => t.Id).ToList();
                return new Step3Form
                {
                    MaxTalks = _appSettings.MaxTalksPerRegistration,
                    Talks = talks.Select(t =>
                    {
                        int remaining = TalkRules.Remaining(t, seats);
                        return new TalkOption
                        {
                            Id = t.Id, Title = t.Title, Speaker = t.Speaker, Track = t.Track,
                            StartsUtc = t.StartsUtc, DurationMinutes = t.DurationMinutes, Room = t.Room,
                            Capacity = t.Capacity, Remaining = remaining,
                            Selected = selected.Contains(t.Id),
                            // A full talk cannot be picked, unless it is already ours (our own hold is inside "taken").
                            Full = remaining <= 0 && !selected.Contains(t.Id)
                        };
                    }).ToList()
                };
            }
        }

        // ---------------- Step 4: review and pay ----------------

        [HttpGet]
        public ActionResult Step4()
        {
            var state = _wizard.Load();
            if (state.RegistrationId == null) return RedirectToAction("Step3");
            return Respond(BuildStep4(state));
        }

        [HttpPost]
        public ActionResult Step4(string cardNumber)
        {
            var state = _wizard.Load();
            if (state.RegistrationId == null) return RedirectToAction("Step3");

            var outcome = _registrations.Pay(state, cardNumber);
            switch (outcome.Status)
            {
                case PayStatus.Paid:
                    _wizard.Clear();
                    return RedirectToAction("Confirmation", new { code = outcome.ReferenceCode });

                case PayStatus.HoldExpired:
                case PayStatus.NoHold:
                    _wizard.Save(state); // Pay cleared the lost hold from the snapshot
                    TempData["Notice"] = outcome.Message;
                    return RedirectToAction("Step1");

                default:
                    var form = BuildStep4(state);
                    form.Errors.Add(outcome.Message);
                    return Respond(form);
            }
        }

        private Step4Form BuildStep4(WizardSnapshot state)
        {
            List<string> titles;
            using (var ctx = _db.Create())
            {
                var ids = state.SelectedTalkIds;
                titles = ctx.Talks.Where(t => ids.Contains(t.Id)).OrderBy(t => t.Id).Select(t => t.Title).ToList();
            }
            return new Step4Form
            {
                Name = state.FirstName + " " + state.LastName,
                Email = state.Email,
                TicketType = state.TicketType.Value.ToString(),
                TotalAmount = state.TotalAmount.Value,
                TalkTitles = titles,
                SecondsRemaining = Math.Max(0, (int)(state.HoldExpiresUtc.Value - DateTime.UtcNow).TotalSeconds),
                ReferenceCode = state.ReferenceCode
            };
        }

        // ---------------- Confirmation ----------------

        [HttpGet]
        public ActionResult Confirmation(string code)
        {
            using (var ctx = _db.Create())
            {
                var reg = ctx.Registrations.Include("Attendee").Include("RegistrationTalks.Talk")
                    .FirstOrDefault(r => r.ReferenceCode == code);
                if (reg == null) return HttpNotFound();

                return Respond(new ConfirmationModel
                {
                    ReferenceCode = reg.ReferenceCode,
                    Name = reg.Attendee.FirstName + " " + reg.Attendee.LastName,
                    Email = reg.Attendee.Email,
                    TicketType = reg.TicketType.ToString(),
                    Status = reg.Status.ToString(),
                    TotalAmount = reg.TotalAmount,
                    TalkTitles = reg.RegistrationTalks.Select(rt => rt.Talk.Title).OrderBy(t => t).ToList()
                });
            }
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try { return new MailAddress(email.Trim()).Address == email.Trim(); }
            catch (FormatException) { return false; }
        }
    }
}
