using System;
using System.Collections.Generic;
using System.Data.Entity;
using ConfReg.Domain.Entities;
using ConfReg.Domain.Services;

namespace ConfReg.Data.Seed
{
    /// <summary>
    /// Creates the database on first run and fills it with the demo data from SeedPlan.
    /// Wired up in Global.asax (BASELINE-PROBLEM #5).
    /// </summary>
    public class ConfRegInitializer : CreateDatabaseIfNotExists<ConfRegContext>
    {
        protected override void Seed(ConfRegContext ctx)
        {
            List<AdminUser> admins;
            List<Setting> settings;
            var plan = SeedPlan.Build(DateTime.UtcNow, PasswordCrypto.Encrypt, out admins, out settings);

            // Explicit Ids in the plan are ignored by the identity columns; insertion order
            // preserves them (1..n) on a fresh database, which is all the seed relies on.
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
