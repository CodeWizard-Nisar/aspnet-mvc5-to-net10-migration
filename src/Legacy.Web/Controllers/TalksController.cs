using System;
using System.Linq;
using System.Web.Mvc;
using ConfReg.Data;
using ConfReg.Data.Services;
using ConfReg.Domain.Entities;
using ConfReg.Legacy.Models;

namespace ConfReg.Legacy.Controllers
{
    // No [Authorize]: see AdminController (BASELINE-PROBLEM #6).
    public class TalksController : AppController
    {
        public ActionResult Index()
        {
            using (var ctx = new ConfRegContext())
            {
                var seats = new RegistrationService().SeatsTaken(ctx);
                var rows = ctx.Talks.OrderBy(t => t.StartsUtc).ThenBy(t => t.Id).ToList()
                    .Select(t => new TalkRow
                    {
                        Id = t.Id, Title = t.Title, Speaker = t.Speaker, Track = t.Track,
                        StartsUtc = t.StartsUtc, DurationMinutes = t.DurationMinutes, Room = t.Room,
                        Capacity = t.Capacity, Taken = seats.ContainsKey(t.Id) ? seats[t.Id] : 0
                    }).ToList();
                return Respond(rows);
            }
        }

        [HttpGet]
        public ActionResult Create()
        {
            return Respond(new TalkForm { StartsUtc = DateTime.UtcNow.Date.AddDays(30).AddHours(9), DurationMinutes = 60, Capacity = 50 });
        }

        [HttpPost]
        public ActionResult Create(TalkForm form)
        {
            Validate(form, null);
            if (form.Errors.Count > 0) return Respond(form);

            using (var ctx = new ConfRegContext())
            {
                ctx.Talks.Add(new Talk
                {
                    Title = form.Title, Speaker = form.Speaker, Track = form.Track, StartsUtc = form.StartsUtc,
                    DurationMinutes = form.DurationMinutes, Room = form.Room, Capacity = form.Capacity
                });
                ctx.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        public ActionResult Edit(int id)
        {
            using (var ctx = new ConfRegContext())
            {
                var t = ctx.Talks.Find(id);
                if (t == null) return HttpNotFound();
                return Respond(new TalkForm
                {
                    Id = t.Id, Title = t.Title, Speaker = t.Speaker, Track = t.Track, StartsUtc = t.StartsUtc,
                    DurationMinutes = t.DurationMinutes, Room = t.Room, Capacity = t.Capacity
                });
            }
        }

        [HttpPost]
        public ActionResult Edit(TalkForm form)
        {
            Validate(form, form.Id);
            if (form.Errors.Count > 0) return Respond(form);

            using (var ctx = new ConfRegContext())
            {
                var t = ctx.Talks.Find(form.Id);
                if (t == null) return HttpNotFound();
                t.Title = form.Title; t.Speaker = form.Speaker; t.Track = form.Track; t.StartsUtc = form.StartsUtc;
                t.DurationMinutes = form.DurationMinutes; t.Room = form.Room; t.Capacity = form.Capacity;
                ctx.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public ActionResult Delete(int id)
        {
            using (var ctx = new ConfRegContext())
            {
                var t = ctx.Talks.Find(id);
                if (t == null) return HttpNotFound();

                var seats = new RegistrationService().SeatsTaken(ctx);
                int holders = seats.ContainsKey(id) ? seats[id] : 0;
                if (holders > 0)
                {
                    TempData["Notice"] = "Cannot delete \"" + t.Title + "\": " + holders + " seat(s) are held or paid. Cancel those registrations first.";
                    return RedirectToAction("Index");
                }

                // Historical (cancelled/expired) join rows would block the delete through the FK; clear them.
                ctx.RegistrationTalks.RemoveRange(ctx.RegistrationTalks.Where(rt => rt.TalkId == id));
                ctx.Talks.Remove(t);
                ctx.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // Capacity management lives here: capacity may not drop below the seats already held or paid.
        private void Validate(TalkForm form, int? id)
        {
            if (!ModelState.IsValid)
                foreach (var e in ModelState.Values.SelectMany(v => v.Errors))
                    form.Errors.Add(string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage);

            if (id.HasValue && form.Errors.Count == 0)
            {
                using (var ctx = new ConfRegContext())
                {
                    var seats = new RegistrationService().SeatsTaken(ctx);
                    int taken = seats.ContainsKey(id.Value) ? seats[id.Value] : 0;
                    if (form.Capacity < taken)
                        form.Errors.Add("Capacity cannot be lower than the " + taken + " seat(s) already held or paid.");
                }
            }
        }
    }
}
