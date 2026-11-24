using System;
using System.Globalization;
using System.Linq;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Services
{
    /// <summary>
    /// Ticket prices and the registration window, edited at runtime from the admin Settings page.
    /// The database row wins; ISettings supplies the default when a row does not exist.
    /// (Phase 1 read Web.config inline as the fallback and defaulted EVERY ticket type to 299 if a row was
    /// missing: BASELINE-PROBLEM #3. Defaults are now explicit per ticket type.)
    /// </summary>
    public class SettingsService
    {
        private readonly IConfRegContextFactory _db;
        private readonly ISettings _settings;

        public SettingsService(IConfRegContextFactory db, ISettings settings)
        {
            _db = db;
            _settings = settings;
        }

        public decimal PriceFor(TicketType type)
        {
            string stored = Get("Price." + type);
            return stored == null ? _settings.DefaultPrice(type) : decimal.Parse(stored, CultureInfo.InvariantCulture);
        }

        public DateTime OpensUtc  { get { return ParseDate(Get("Registration.OpensUtc"), DateTime.MinValue); } }
        public DateTime ClosesUtc { get { return ParseDate(Get("Registration.ClosesUtc"), DateTime.MaxValue); } }

        public bool IsOpen()
        {
            var now = DateTime.UtcNow;
            return now >= OpensUtc && now <= ClosesUtc;
        }

        public void Save(decimal standard, decimal workshop, decimal student, DateTime opensUtc, DateTime closesUtc)
        {
            Set("Price.Standard", standard.ToString(CultureInfo.InvariantCulture));
            Set("Price.Workshop", workshop.ToString(CultureInfo.InvariantCulture));
            Set("Price.Student", student.ToString(CultureInfo.InvariantCulture));
            Set("Registration.OpensUtc", opensUtc.ToUniversalTime().ToString("o"));
            Set("Registration.ClosesUtc", closesUtc.ToUniversalTime().ToString("o"));
        }

        private string Get(string key)
        {
            using (var ctx = _db.Create())
            {
                var row = ctx.Settings.FirstOrDefault(s => s.Key == key);
                return row == null ? null : row.Value;
            }
        }

        private void Set(string key, string value)
        {
            using (var ctx = _db.Create())
            {
                var row = ctx.Settings.FirstOrDefault(s => s.Key == key);
                if (row == null) ctx.Settings.Add(new Setting { Key = key, Value = value });
                else row.Value = value;
                ctx.SaveChanges();
            }
        }

        private static DateTime ParseDate(string s, DateTime fallback)
        {
            DateTime d;
            return s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d)
                ? d.ToUniversalTime() : fallback;
        }
    }
}
