using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using ConfReg.Data;
using ConfReg.Data.Services;
using ConfReg.Domain.Entities;
using ConfReg.Legacy.Models;

namespace ConfReg.Legacy.Controllers
{
    // BASELINE-PROBLEM #6: no [Authorize] on any admin controller.
    // The only thing standing between the internet and these actions is a Response.Redirect inside
    // Views/Shared/_AdminLayout.cshtml. A layout only runs when a *view* is rendered, so:
    //   - every POST that redirects (Cancel, Refund, Delete, Save) runs with NO login check at all;
    //   - every ?format=json response skips the layout, so admin data is readable without logging in.
    // The Phase 2 characterization tests record this as current behaviour. Phase 5 fixes it
    // (cookie auth + a global authorize filter) and the change is called out in docs/05-strangler.md.
    public class AdminController : AppController
    {
        // GET /Admin  (dashboard)
        public ActionResult Index()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var reports = new ReportQueries();

            using (var ctx = new ConfRegContext())
            {
                var paid = ctx.Registrations.Where(r => r.Status == RegistrationStatus.Paid);
                var seats = new RegistrationService().SeatsTaken(ctx);

                var model = new DashboardModel
                {
                    PaidTotal = paid.Count(),
                    PaidToday = paid.Count(r => r.CreatedUtc >= today),
                    PendingNow = ctx.Registrations.Count(r => r.Status == RegistrationStatus.Pending),
                    // Leaky query, see ReportQueries (BASELINE-PROBLEM #9).
                    RevenueByTicket = reports.RevenueByTicketType()
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
            var s = new SettingsService();
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

            new SettingsService().Save(form.PriceStandard, form.PriceWorkshop, form.PriceStudent, form.OpensUtc, form.ClosesUtc);
            TempData["Notice"] = "Settings saved.";
            return RedirectToAction("Settings");
        }
    }
}
