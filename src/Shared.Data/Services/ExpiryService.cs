using System;
using System.Linq;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Services;

namespace ConfReg.Data.Services
{
    /// <summary>
    /// Expires unpaid registrations. "Freeing the seats" is simply flipping Status to Expired:
    /// seat counting ignores everything but Pending and Paid.
    ///
    /// This is the one business-logic method the legacy Windows Service wraps
    /// (BASELINE-PROBLEM #8). Replaced by a Quartz job in Phase 4.
    /// </summary>
    public class ExpiryService
    {
        public int ExpireUnpaid()
        {
            // Untestable clock (extra problem): DateTime.UtcNow inline. Tests have to
            // back-date CreatedUtc in the database instead of moving time.
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-HoldPolicy.Minutes);

            using (var ctx = new ConfRegContext())
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
