using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Entities;
using ConfReg.ViewModels;

namespace ConfReg.Legacy.Controllers
{
    // BASELINE-PROBLEM #6, fixed in Phase 5: every action here is protected by the global AdminAuthorizeFilter
    // (Infrastructure/AdminAuthorizeFilter.cs). Phase 1 relied on a Response.Redirect in the layout, which JSON, file
    // downloads and redirecting POSTs all bypassed.
    public class AdminController : AppController
    {
        private readonly IConfRegContextFactory _db;
        private readonly RegistrationService _registrations;
        private readonly ReportQueries _reports;
        private readonly SettingsService _settings;

        public AdminController(IConfRegContextFactory db, RegistrationService registrations, ReportQueries reports, SettingsService settings)
        {
            _db = db;
            _registrations = registrations;
            _reports = reports;
            _settings = settings;
        }

        // GET /Admin  (dashboard)
        public ActionResult Index()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            using (var ctx = _db.Create())
            {
                var paid = ctx.Registrations.Where(r => r.Status == RegistrationStatus.Paid);
                var seats = _registrations.SeatsTaken(ctx);

                var model = new DashboardModel
                {
                    PaidTotal = paid.Count(),
                    PaidToday = paid.Count(r => r.CreatedUtc >= today),
                    PendingNow = ctx.Registrations.Count(r => r.Status == RegistrationStatus.Pending),
                    // Was the leaky query (BASELINE-PROBLEM #9); fixed in ReportQueries.
                    RevenueByTicket = _reports.RevenueByTicketType()
                        .Select(r => new RevenueLine { TicketType = ((TicketType)r.TicketType).ToString(), Registrations = r.Registrations, Revenue = r.Revenue })
                        .ToList(),
                    CapacityWarnings = ctx.Talks.ToList()
                        .Select(t => new CapacityWarning
                        {
                            TalkId = t.Id, Title = t.Title, Capacity = t.Capacity,
                            Taken = seats.ContainsKey(t.Id) ? seats[t.Id] : 0,
                            PercentFull = t.Capacity == 0 ? 100 : (int)Math.Round(100.0 * (seats.ContainsKey(t.Id) ? seats[t.Id] : 0) / t.Capacity)
                        })
                        .Where(w => w.PercentFull > 80)
                        .OrderByDescending(w => w.PercentFull).ThenBy(w => w.TalkId)
                        .ToList()
                };
                model.TotalRevenue = model.RevenueByTicket.Sum(r => r.Revenue);
                return Respond(model);
            }
        }

        [HttpGet]
        public ActionResult Settings()
        {
            var s = _settings;
            return Respond(new SettingsForm
            {
                PriceStandard = s.PriceFor(TicketType.Standard),
                PriceWorkshop = s.PriceFor(TicketType.Workshop),
                PriceStudent = s.PriceFor(TicketType.Student),
                OpensUtc = s.OpensUtc,
                ClosesUtc = s.ClosesUtc,
                Notice = TempData["Notice"] as string
            });
        }

        [HttpPost]
        public ActionResult Settings(SettingsForm form)
        {
            if (form.PriceStandard < 0 || form.PriceWorkshop < 0 || form.PriceStudent < 0)
                form.Errors.Add("Prices cannot be negative.");
            if (form.ClosesUtc <= form.OpensUtc)
                form.Errors.Add("Registration must close after it opens.");
            if (form.Errors.Count > 0) return Respond(form);

            _settings.Save(form.PriceStandard, form.PriceWorkshop, form.PriceStudent, form.OpensUtc, form.ClosesUtc);
            TempData["Notice"] = "Settings saved.";
            return RedirectToAction("Settings");
        }
    }
}
