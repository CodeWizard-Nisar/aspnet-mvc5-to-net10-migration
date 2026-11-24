using System;
using System.Linq;
using System.Web.Mvc;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Services;
using ConfReg.Legacy.Models;

namespace ConfReg.Legacy.Controllers
{
    // No [Authorize]: see AdminController (BASELINE-PROBLEM #6). Cancel and Refund are POSTs that
    // redirect, so they never reach a view, so the layout's login check never runs for them.
    public class RegistrationsController : AppController
    {
        private readonly IConfRegContextFactory _db;
        private readonly PaymentGateway _gateway;
        public RegistrationsController(IConfRegContextFactory db, PaymentGateway gateway) { _db = db; _gateway = gateway; }

        public ActionResult Index(string status)
        {
            using (var ctx = _db.Create())
            {
                var query = ctx.Registrations.AsQueryable();
                RegistrationStatus s;
                if (Enum.TryParse(status, true, out s) && Enum.IsDefined(typeof(RegistrationStatus), s))
                    query = query.Where(r => r.Status == s);

                var rows = query.OrderByDescending(r => r.CreatedUtc).ThenByDescending(r => r.Id).Take(200).ToList()
                    .Select(r => new RegistrationRow
                    {
                        Id = r.Id, ReferenceCode = r.ReferenceCode,
                        Attendee = r.Attendee.FirstName + " " + r.Attendee.LastName, Email = r.Attendee.Email,
                        TicketType = r.TicketType.ToString(), Status = r.Status.ToString(),
                        TotalAmount = r.TotalAmount, CreatedUtc = r.CreatedUtc
                    }).ToList();
                return Respond(new RegistrationList { Status = status, Rows = rows });
            }
        }

        public ActionResult Details(int id)
        {
            using (var ctx = _db.Create())
            {
                var r = ctx.Registrations.Include("Attendee").Include("RegistrationTalks.Talk").Include("Payments")
                    .FirstOrDefault(x => x.Id == id);
                if (r == null) return HttpNotFound();

                bool paidOk = r.Payments.Any(p => p.Status == PaymentStatus.Succeeded);
                return Respond(new RegistrationDetail
                {
                    Id = r.Id, ReferenceCode = r.ReferenceCode,
                    Attendee = r.Attendee.FirstName + " " + r.Attendee.LastName, Email = r.Attendee.Email, Company = r.Attendee.Company,
                    TicketType = r.TicketType.ToString(), Status = r.Status.ToString(), TotalAmount = r.TotalAmount,
                    CreatedUtc = r.CreatedUtc, ConfirmedUtc = r.ConfirmedUtc,
                    TalkTitles = r.RegistrationTalks.Select(rt => rt.Talk.Title).OrderBy(t => t).ToList(),
                    Payments = r.Payments.OrderBy(p => p.Id).Select(p => new PaymentRow
                    {
                        Id = p.Id, Amount = p.Amount, Provider = p.Provider, ProviderRef = p.ProviderRef,
                        Status = p.Status.ToString(), ProcessedUtc = p.ProcessedUtc
                    }).ToList(),
                    CanCancel = r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Paid,
                    CanRefund = paidOk,
                    Notice = TempData["Notice"] as string
                });
            }
        }

        // Cancelling frees the seats (seat counting ignores Cancelled) but does NOT move money.
        [HttpPost]
        public ActionResult Cancel(int id)
        {
            using (var ctx = _db.Create())
            {
                var r = ctx.Registrations.Find(id);
                if (r == null) return HttpNotFound();
                if (r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Paid)
                {
                    r.Status = RegistrationStatus.Cancelled;
                    ctx.SaveChanges();
                    TempData["Notice"] = "Registration cancelled. Seats released.";
                }
                else TempData["Notice"] = "Only pending or paid registrations can be cancelled.";
            }
            return RedirectToAction("Details", new { id });
        }

        // Refunding returns the money AND cancels, so a refunded attendee does not keep their seats.
        [HttpPost]
        public ActionResult Refund(int id)
        {
            using (var ctx = _db.Create())
            {
                var r = ctx.Registrations.Include("Payments").FirstOrDefault(x => x.Id == id);
                if (r == null) return HttpNotFound();

                var payment = r.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Succeeded);
                if (payment == null)
                {
                    TempData["Notice"] = "Nothing to refund: no successful payment on this registration.";
                    return RedirectToAction("Details", new { id });
                }

                // The gateway no longer needs a web request (BASELINE-PROBLEM #1, fixed): IClientInfo and IAppPaths are injected.
                _gateway.Refund(payment.ProviderRef, payment.Amount);
                payment.Status = PaymentStatus.Refunded;
                if (r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Paid)
                    r.Status = RegistrationStatus.Cancelled;
                ctx.SaveChanges();
                TempData["Notice"] = "Refunded " + payment.Amount.ToString("0.00") + " and cancelled.";
            }
            return RedirectToAction("Details", new { id });
        }
    }
}
