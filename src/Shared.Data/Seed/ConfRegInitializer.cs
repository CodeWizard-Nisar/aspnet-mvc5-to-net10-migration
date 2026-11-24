using System;
using System.Collections.Generic;
using System.Data.Entity;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Security;

namespace ConfReg.Data.Seed
{
    /// <summary>
    /// Creates the database on first run and fills it with the demo data from SeedPlan.
    /// New databases get BCrypt admin passwords from day one; only databases created before Phase 3 have legacy ones.
    /// </summary>
    public class ConfRegInitializer : CreateDatabaseIfNotExists<ConfRegContext>
    {
        private readonly IPasswordHasher _hasher;
        public ConfRegInitializer(IPasswordHasher hasher) { _hasher = hasher; }

        protected override void Seed(ConfRegContext ctx)
        {
            List<AdminUser> admins;
            List<Setting> settings;
            var plan = SeedPlan.Build(DateTime.UtcNow, _hasher.Hash, out admins, out settings);

            ctx.Talks.AddRange(plan.Talks);
            ctx.Attendees.AddRange(plan.Attendees);
            ctx.SaveChanges();

            ctx.Registrations.AddRange(plan.Registrations);
            ctx.SaveChanges();

            ctx.Payments.AddRange(plan.Payments);
            ctx.AdminUsers.AddRange(admins);
            ctx.Settings.AddRange(settings);
            ctx.SaveChanges();
        }
    }
}
