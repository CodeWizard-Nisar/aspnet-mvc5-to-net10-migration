using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Entities;
using ConfReg.ViewModels;

namespace ConfReg.Modern.Controllers
{
    // Protected by the global AuthorizeFilter registered in Program.cs.
    public class TalksController : AppController
    {
        private readonly IConfRegContextFactory _db;
        private readonly RegistrationService _registrations;
        public TalksController(IConfRegContextFactory db, RegistrationService registrations) { _db = db; _registrations = registrations; }

        public IActionResult Index()
        {
            using (var ctx = _db.Create())
            {
                var seats = _registrations.SeatsTaken(ctx);
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
        public IActionResult Create()
        {
            return Respond(new TalkForm { StartsUtc = DateTime.UtcNow.Date.AddDays(30).AddHours(9), DurationMinutes = 60, Capacity = 50 });
        }

        [HttpPost]
        public IActionResult Create(TalkForm form)
        {
            Validate(form, null);
            if (form.Errors.Count > 0) return Respond(form);

            using (var ctx = _db.Create())
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
        public IActionResult Edit(int id)
        {
            using (var ctx = _db.Create())
            {
                var t = ctx.Talks.Find(id);
                if (t == null) return NotFound();
                return Respond(new TalkForm
                {
                    Id = t.Id, Title = t.Title, Speaker = t.Speaker, Track = t.Track, StartsUtc = t.StartsUtc,
                    DurationMinutes = t.DurationMinutes, Room = t.Room, Capacity = t.Capacity
                });
            }
        }

        [HttpPost]
        public IActionResult Edit(TalkForm form)
        {
            Validate(form, form.Id);
            if (form.Errors.Count > 0) return Respond(form);

            using (var ctx = _db.Create())
            {
                var t = ctx.Talks.Find(form.Id);
                if (t == null) return NotFound();
                t.Title = form.Title; t.Speaker = form.Speaker; t.Track = form.Track; t.StartsUtc = form.StartsUtc;
                t.DurationMinutes = form.DurationMinutes; t.Room = form.Room; t.Capacity = form.Capacity;
                ctx.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult Delete(int id)
        {
            using (var ctx = _db.Create())
            {
                var t = ctx.Talks.Find(id);
                if (t == null) return NotFound();

                var seats = _registrations.SeatsTaken(ctx);
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

        private static IEnumerable<string> ModelStateInDeclaredOrder(ModelStateDictionary modelState, Type formType)
    {
        var order = formType.GetProperties().Select((p, i) => (p.Name, i)).ToDictionary(x => x.Name, x => x.i);
        return modelState
            .OrderBy(entry => order.TryGetValue(entry.Key, out int i) ? i : int.MaxValue)
            .SelectMany(entry => entry.Value.Errors.Select(e => e.ErrorMessage));
    }

    // Capacity management lives here: capacity may not drop below the seats already held or paid.
        private void Validate(TalkForm form, int? id)
        {
            if (!ModelState.IsValid)
                // In DECLARED PROPERTY ORDER, deliberately. MVC 5 happened to yield validation messages in that order;
                // ASP.NET Core's ModelState is ordered by how the binder visited the fields, so the same invalid form
                // produced the same messages in a different sequence. Nothing fails because of that except a client that
                // shows "the first error", or a test that compares the list - and the Phase 2 snapshot did exactly that,
                // which is how this was found. Ordering by the form's own property order makes it deterministic and
                // identical in both apps. See docs/05-strangler.md.
                foreach (var e in ModelStateInDeclaredOrder(ModelState, typeof(TalkForm)))
                    form.Errors.Add(string.IsNullOrEmpty(e) ? "Invalid value." : e);

            if (id.HasValue && form.Errors.Count == 0)
            {
                using (var ctx = _db.Create())
                {
                    var seats = _registrations.SeatsTaken(ctx);
                    int taken = seats.ContainsKey(id.Value) ? seats[id.Value] : 0;
                    if (form.Capacity < taken)
                        form.Errors.Add("Capacity cannot be lower than the " + taken + " seat(s) already held or paid.");
                }
            }
        }
    }
}
