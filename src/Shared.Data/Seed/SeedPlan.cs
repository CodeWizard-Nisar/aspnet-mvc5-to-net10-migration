using System;
using System.Collections.Generic;
using System.Linq;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Rules;

namespace ConfReg.Data.Seed
{
    /// <summary>
    /// Builds the demo data as plain objects, with no database involved, so the rules it must
    /// respect (capacity, overlap, student/.edu) can be checked without SQL Server.
    /// Deterministic: Random(42), so every machine gets the same people, talks and bookings.
    /// Dates in the past are relative to "now" so the dashboard's "today" is never empty.
    ///
    /// Shape: 200 attendees, 20 talks over 3 tracks, 150 registrations.
    /// </summary>
    public class SeedPlan
    {
        public const int AttendeeCount = 200;
        public const int RegistrationCount = 150;

        public List<Attendee> Attendees = new List<Attendee>();
        public List<Talk> Talks = new List<Talk>();
        public List<Registration> Registrations = new List<Registration>();
        public List<Payment> Payments = new List<Payment>();

        private static readonly string[] First = { "Ava", "Liam", "Noor", "Mateo", "Priya", "Chen", "Sofia", "Omar", "Elena", "Kofi", "Hana", "Lucas", "Mei", "Ivan", "Zara", "Tomas", "Aisha", "Jon", "Rosa", "Ben" };
        private static readonly string[] Last = { "Okafor", "Lindqvist", "Haddad", "Moreau", "Sharma", "Wei", "Rossi", "Nasser", "Petrova", "Mensah", "Sato", "Silva", "Tanaka", "Novak", "Khan", "Berg", "Bello", "Hughes", "Ortega", "Fischer" };
        private static readonly string[] Companies = { "Northwind Labs", "Contoso Ltd", "Fabrikam", "Tailspin Software", "Adatum", "Litware", "Proseware", "Wingtip Systems" };
        private static readonly string[] Titles = { "Software Engineer", "Architect", "Engineering Manager", "QA Lead", "DevOps Engineer", "CTO", "Developer Advocate", "Data Engineer" };
        private static readonly string[] Dietary = { null, null, null, null, "Vegetarian", "Vegan", "Gluten free", "No nuts", "Halal" };

        public static SeedPlan Build(DateTime utcNow, Func<string, string> encryptPassword, out List<AdminUser> admins, out List<Setting> settings)
        {
            var rng = new Random(42);
            var plan = new SeedPlan();

            plan.BuildTalks(rng, utcNow);
            plan.BuildAttendees(rng, utcNow);
            plan.BuildRegistrations(rng, utcNow);

            admins = new List<AdminUser>
            {
                new AdminUser { Email = "admin@confreg.example", PasswordHash = encryptPassword("ConfReg-Demo-1!"), Role = "Admin" },
                new AdminUser { Email = "staff@confreg.example", PasswordHash = encryptPassword("ConfReg-Demo-2!"), Role = "Staff" }
            };

            settings = new List<Setting>
            {
                new Setting { Key = "Price.Standard", Value = "299" },
                new Setting { Key = "Price.Workshop", Value = "499" },
                new Setting { Key = "Price.Student",  Value = "99"  },
                new Setting { Key = "Registration.OpensUtc",  Value = utcNow.AddDays(-60).ToString("o") },
                new Setting { Key = "Registration.ClosesUtc", Value = utcNow.AddDays(60).ToString("o") }
            };

            return plan;
        }

        private void BuildTalks(Random rng, DateTime utcNow)
        {
            // Fixed schedule on two days. Slots deliberately include OVERLAPPING pairs
            // (10:00/90min vs 10:30/60min) so the overlap rule has something to refuse.
            DateTime day1 = new DateTime(utcNow.Year + 1, 3, 16, 0, 0, 0, DateTimeKind.Utc);
            DateTime day2 = day1.AddDays(1);
            string[] tracks = { "Architecture", "Web", "Data" };
            string[] rooms = { "Hall A", "Hall B", "Room 101" };
            string[] titles =
            {
                "Strangler Fig in Practice", "Modular Monoliths", "Event Sourcing Without Tears", "Designing for Failure",
                "Cutting Over Without Downtime", "Razor Pages vs MVC", "Minimal APIs at Scale", "Front Ends That Age Well",
                "Caching Done Right", "Auth Beyond Cookies", "Observability for Small Teams", "EF Core Query Tuning",
                "SQL Server Indexing Myths", "Zero-Downtime Schema Changes", "Streaming ETL Basics", "Data Contracts",
                "Testing Legacy Code", "Feature Flags in Anger", "Reading Production Telemetry", "Retiring Dead Code"
            };
            // (day, hour, minute, minutes) - index i maps to track i % 3.
            int[][] slots =
            {
                new[]{1,9,0,60},  new[]{1,9,0,60},  new[]{1,9,30,60},
                new[]{1,10,0,90}, new[]{1,10,30,60},new[]{1,11,45,60},
                new[]{1,13,0,60}, new[]{1,13,0,60}, new[]{1,14,15,45},
                new[]{2,9,0,60},  new[]{2,9,30,60}, new[]{2,10,45,60},
                new[]{2,10,45,60},new[]{2,12,0,60}, new[]{2,13,30,60},
                new[]{2,13,30,90},new[]{2,14,0,60}, new[]{2,15,15,45},
                new[]{2,15,30,60},new[]{2,16,30,30}
            };
            // Capacities chosen so that: one talk is tiny (fills during seeding), several land
            // above the 80% dashboard warning, and most have plenty of room.
            int[] caps = { 120, 80, 60, 40, 100, 30, 90, 70, 25, 110, 60, 45, 12, 50, 80, 35, 90, 28, 60, 20 };

            for (int i = 0; i < 20; i++)
            {
                var s = slots[i];
                Talks.Add(new Talk
                {
                    Id = i + 1,
                    Title = titles[i],
                    Speaker = First[(i * 3) % First.Length] + " " + Last[(i * 7) % Last.Length],
                    Track = tracks[i % 3],
                    StartsUtc = (s[0] == 1 ? day1 : day2).AddHours(s[1]).AddMinutes(s[2]),
                    DurationMinutes = s[3],
                    Room = rooms[i % 3],
                    Capacity = caps[i]
                });
            }
        }

        private void BuildAttendees(Random rng, DateTime utcNow)
        {
            for (int i = 0; i < AttendeeCount; i++)
            {
                string first = First[rng.Next(First.Length)];
                string last = Last[rng.Next(Last.Length)];
                // The first 25 attendees are students with .edu addresses.
                string domain = i < 25 ? "uni" + (i % 5) + ".edu" : "example.org";
                Attendees.Add(new Attendee
                {
                    Id = i + 1,
                    FirstName = first,
                    LastName = last,
                    Email = (first + "." + last + (i + 1)).ToLowerInvariant() + "@" + domain,
                    Company = i < 25 ? null : Companies[rng.Next(Companies.Length)],
                    JobTitle = i < 25 ? "Student" : Titles[rng.Next(Titles.Length)],
                    DietaryRequirements = Dietary[rng.Next(Dietary.Length)],
                    CreatedUtc = utcNow.AddDays(-rng.Next(0, 30)).AddMinutes(-rng.Next(0, 1440))
                });
            }
        }

        private void BuildRegistrations(Random rng, DateTime utcNow)
        {
            var seats = new Dictionary<int, int>();
            var used = new HashSet<string>();
            // Registrations 1..150 go to the first 150 attendees, so 50 attendees never registered
            // (the admin Attendees list needs those people to exist).
            for (int i = 0; i < RegistrationCount; i++)
            {
                var attendee = Attendees[i];
                bool student = i < 25;
                TicketType ticket = student ? TicketType.Student : (rng.Next(100) < 30 ? TicketType.Workshop : TicketType.Standard);
                decimal price = ticket == TicketType.Student ? 99m : ticket == TicketType.Workshop ? 499m : 299m;

                // Status mix: 120 Paid, 10 Pending (fresh, they expire during a demo), 10 Cancelled, 10 Expired.
                RegistrationStatus status =
                    i < 120 ? RegistrationStatus.Paid :
                    i < 130 ? RegistrationStatus.Pending :
                    i < 140 ? RegistrationStatus.Cancelled : RegistrationStatus.Expired;

                // Pending ones are 2..20 minutes old (still inside the 30 minute window).
                DateTime created = status == RegistrationStatus.Pending
                    ? utcNow.AddMinutes(-(2 + (i - 120) * 2))
                    : (i % 12 == 0 ? utcNow.AddMinutes(-rng.Next(5, 600)) : utcNow.AddDays(-rng.Next(1, 30)).AddMinutes(-rng.Next(0, 1440)));
                if (created < attendee.CreatedUtc && status != RegistrationStatus.Pending) created = attendee.CreatedUtc;

                string code;
                do { code = ReferenceCodes.Build(rng); } while (!used.Add(code));

                var reg = new Registration
                {
                    Id = i + 1,
                    AttendeeId = attendee.Id,
                    ReferenceCode = code,
                    TicketType = ticket,
                    Status = status,
                    TotalAmount = price,
                    CreatedUtc = created,
                    ConfirmedUtc = status == RegistrationStatus.Paid || status == RegistrationStatus.Cancelled ? created.AddMinutes(3) : (DateTime?)null,
                    ConfirmationSentUtc = status == RegistrationStatus.Paid && i < 110 ? created.AddMinutes(8) : (DateTime?)null
                };
                Registrations.Add(reg);

                // Pick 1-4 talks that obey the same rules the live wizard enforces.
                // Only Pending/Paid registrations hold seats.
                bool holdsSeats = status == RegistrationStatus.Paid || status == RegistrationStatus.Pending;
                var chosen = new List<Talk>();
                int want = 1 + rng.Next(4);
                for (int attempt = 0; attempt < 12 && chosen.Count < want; attempt++)
                {
                    // Weight early talk ids so a few fill up and cross 80%.
                    var talk = Talks[Math.Min(rng.Next(Talks.Count), rng.Next(Talks.Count))];
                    if (chosen.Contains(talk)) continue;
                    if (chosen.Any(c => TalkRules.Overlaps(c, talk))) continue;
                    if (holdsSeats && TalkRules.Remaining(talk, seats) <= 0) continue;
                    chosen.Add(talk);
                    if (holdsSeats) seats[talk.Id] = (seats.ContainsKey(talk.Id) ? seats[talk.Id] : 0) + 1;
                }
                reg.RegistrationTalks = chosen.Select(t => new RegistrationTalk { RegistrationId = reg.Id, TalkId = t.Id }).ToList();

                if (status == RegistrationStatus.Paid || (status == RegistrationStatus.Cancelled && i < 135))
                {
                    Payments.Add(new Payment
                    {
                        Id = Payments.Count + 1,
                        RegistrationId = reg.Id,
                        Amount = price,
                        Provider = "StubPay",
                        ProviderRef = "STUB-SEED" + (i + 1).ToString("D5"),
                        // Cancelled-after-payment ones were refunded.
                        Status = status == RegistrationStatus.Paid ? PaymentStatus.Succeeded : PaymentStatus.Refunded,
                        ProcessedUtc = created.AddMinutes(3)
                    });
                }
            }
        }
    }
}
