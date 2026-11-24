using System.Data.Entity;
using ConfReg.Domain.Entities;

namespace ConfReg.Data
{
    public class ConfRegContext : DbContext
    {
        // "ConfReg" is the connection string name in Web.config / App.config.
        public ConfRegContext() : base("name=ConfReg") { }

        public DbSet<Attendee> Attendees { get; set; }
        public DbSet<Talk> Talks { get; set; }
        public DbSet<Registration> Registrations { get; set; }
        public DbSet<RegistrationTalk> RegistrationTalks { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<AdminUser> AdminUsers { get; set; }
        public DbSet<Setting> Settings { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RegistrationTalk>().HasKey(x => new { x.RegistrationId, x.TalkId });
            modelBuilder.Entity<Registration>().Property(x => x.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<Payment>().Property(x => x.Amount).HasPrecision(18, 2);
        }
    }
}
