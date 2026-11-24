using System;
using System.Configuration;
using System.Globalization;
using System.Linq;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Services
{
    /// <summary>
    /// Ticket prices and registration window. DB value wins, Web.config AppSettings is the fallback.
    /// BASELINE-PROBLEM #3: two config sources, resolved inline, with no single owner.
    /// </summary>
    public class SettingsService
    {
        public decimal PriceFor(TicketType type)
        {
            return decimal.Parse(Get("Price." + type, "299"), CultureInfo.InvariantCulture);
        }

        public DateTime OpensUtc  { get { return ParseDate(Get("Registration.OpensUtc",  null), DateTime.MinValue); } }
        public DateTime ClosesUtc { get { return ParseDate(Get("Registration.ClosesUtc", null), DateTime.MaxValue); } }

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

        private string Get(string key, string fallback)
        {
            using (var ctx = new ConfRegContext())
            {
                var row = ctx.Settings.FirstOrDefault(s => s.Key == key);
                if (row != null) return row.Value;
            }
            return ConfigurationManager.AppSettings[key] ?? fallback;
        }

        private void Set(string key, string value)
        {
            using (var ctx = new ConfRegContext())
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
