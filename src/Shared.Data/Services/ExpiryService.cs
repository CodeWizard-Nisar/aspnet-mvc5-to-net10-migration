using System;
using System.Linq;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Entities;

namespace ConfReg.Data.Services
{
    /// <summary>
    /// Expires unpaid registrations. "Freeing the seats" is simply flipping Status to Expired:
    /// seat counting ignores everything but Pending and Paid.
    ///
    /// Still wrapped by the legacy Windows Service (BASELINE-PROBLEM #8) until Phase 4 replaces that with a
    /// Quartz job. Phase 3 changed only how this class gets its connection and its 30 minutes: both injected.
    /// </summary>
    public class ExpiryService
    {
        private readonly IConfRegContextFactory _db;
        private readonly ISettings _settings;

        public ExpiryService(IConfRegContextFactory db, ISettings settings)
        {
            _db = db;
            _settings = settings;
        }

        public int ExpireUnpaid()
        {
            // Untestable clock (extra problem E5, unchanged): DateTime.UtcNow inline.
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-_settings.HoldMinutes);

            using (var ctx = _db.Create())
            {
                var stale = ctx.Registrations
                    .Where(r => r.Status == RegistrationStatus.Pending && r.CreatedUtc < cutoff)
                    .ToList();
                foreach (var r in stale) r.Status = RegistrationStatus.Expired;
                ctx.SaveChanges();
                return stale.Count;
            }
        }
    }
}
