using System;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using ConfReg.Data;
using ConfReg.Domain.Entities;
using ConfReg.Legacy.Models;

namespace ConfReg.Legacy.Controllers
{
    // No [Authorize]: see AdminController (BASELINE-PROBLEM #6).
    public class AttendeesController : AppController
    {
        private const int PageSize = 25;

        public ActionResult Index(string q, string ticket, string status, int page = 1)
        {
            using (var ctx = new ConfRegContext())
            {
                var query = Filter(ctx, q, ticket, status);
                int total = query.Count();
                var rows = query.OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ThenBy(r => r.Id)
                    .Skip((Math.Max(page, 1) - 1) * PageSize).Take(PageSize).ToList();

                return Respond(new AttendeeList
                {
                    Q = q, Ticket = ticket, Status = status, Page = Math.Max(page, 1),
                    PageSize = PageSize, Total = total, Rows = rows
                });
            }
        }

        // GET /Attendees/Export?q=...  -> CSV of the same filtered set, unpaged.
        public ActionResult Export(string q, string ticket, string status)
        {
            using (var ctx = new ConfRegContext())
            {
                var rows = Filter(ctx, q, ticket, status).OrderBy(r => r.LastName).ThenBy(r => r.FirstName).ThenBy(r => r.Id).ToList();

                var sb = new StringBuilder();
                sb.AppendLine("Id,FirstName,LastName,Email,Company,JobTitle,DietaryRequirements,Ticket,Status,ReferenceCode");
                foreach (var r in rows)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        r.Id.ToString(), Csv(r.FirstName), Csv(r.LastName), Csv(r.Email), Csv(r.Company), Csv(r.JobTitle),
                        Csv(r.DietaryRequirements), Csv(r.Ticket.HasValue ? r.Ticket.ToString() : ""),
                        Csv(r.Status.HasValue ? r.Status.ToString() : ""), Csv(r.ReferenceCode)
                    }));
                }
                return File(new UTF8Encoding(true).GetBytes(sb.ToString()), "text/csv", "attendees.csv");
            }
        }

        private static IQueryable<AttendeeRow> Filter(ConfRegContext ctx, string q, string ticket, string status)
        {
            // One row per attendee, showing their most recent registration (if any).
            var query =
                from a in ctx.Attendees
                let r = a.Registrations.OrderByDescending(x => x.CreatedUtc).FirstOrDefault()
                select new AttendeeRow
                {
                    Id = a.Id, FirstName = a.FirstName, LastName = a.LastName, Email = a.Email,
                    Company = a.Company, JobTitle = a.JobTitle, DietaryRequirements = a.DietaryRequirements,
                    Ticket = r == null ? (TicketType?)null : r.TicketType,
                    Status = r == null ? (RegistrationStatus?)null : r.Status,
                    ReferenceCode = r == null ? null : r.ReferenceCode
                };

            if (!string.IsNullOrWhiteSpace(q))
            {
                string term = q.Trim();
                query = query.Where(x => x.FirstName.Contains(term) || x.LastName.Contains(term)
                                      || x.Email.Contains(term) || x.Company.Contains(term));
            }

            TicketType t;
            if (Enum.TryParse(ticket, true, out t) && Enum.IsDefined(typeof(TicketType), t))
                query = query.Where(x => x.Ticket == t);

            RegistrationStatus s;
            if (Enum.TryParse(status, true, out s) && Enum.IsDefined(typeof(RegistrationStatus), s))
                query = query.Where(x => x.Status == s);

            return query;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            // Spreadsheet formula injection: a cell starting with = + - @ is executed by Excel.
            if ("=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
