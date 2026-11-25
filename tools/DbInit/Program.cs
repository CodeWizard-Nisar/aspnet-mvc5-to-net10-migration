using System.Data.Entity;
using ConfReg.Data;
using ConfReg.Data.Seed;
using ConfReg.Domain.Security;

// ConfReg.DbInit                 create and seed the database if it does not exist
// ConfReg.DbInit --recreate      drop it first, then create and seed (destructive, local development only)
//
// The connection string comes from ConfReg__ConnectionString, or --connection, or the local-dev default below.
// Admin passwords are seeded as BCrypt hashes, exactly as a database created by Legacy.Web after Phase 3 would be.

string connectionString =
    ArgValue("--connection")
    ?? Environment.GetEnvironmentVariable("ConfReg__ConnectionString")
    ?? "Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True;";

bool recreate = args.Contains("--recreate", StringComparer.OrdinalIgnoreCase);

// Show where we are going, with the password masked: this is the setting people get wrong, so it should be visible.
Console.WriteLine($"ConfReg database init -> {Mask(connectionString)}");

try
{
    Database.SetInitializer<ConfRegContext>(null);
    using (var probe = new ConfRegContext(connectionString))
    {
        bool exists = probe.Database.Exists();
        if (exists && !recreate)
        {
            Console.WriteLine("Database already exists; nothing to do. Pass --recreate to drop and reseed it.");
            Report(probe);
            return 0;
        }
        if (exists)
        {
            Console.WriteLine("Dropping the existing database (--recreate)...");
            probe.Database.Delete();
        }
    }

    // The same initializer and the same seed data the legacy app uses, so either route produces an identical database.
    Database.SetInitializer(new ConfRegInitializer(new BCryptPasswordHasher()));
    using (var ctx = new ConfRegContext(connectionString))
    {
        Console.WriteLine("Creating schema and seeding...");
        ctx.Database.Initialize(force: true);
        Report(ctx);
    }

    Console.WriteLine();
    Console.WriteLine("Done. Admin logins (demo values): admin@confreg.example / ConfReg-Demo-1!");
    Console.WriteLine("                                  staff@confreg.example / ConfReg-Demo-2!");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("FAILED: " + ex.Message);
    if (ex.InnerException is not null) Console.Error.WriteLine("  caused by: " + ex.InnerException.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Is SQL Server running and reachable? For the local container:  docker compose up -d");
    Console.Error.WriteLine("Note EF6 uses System.Data.SqlClient, which rejects a connection string written by the");
    Console.Error.WriteLine("Microsoft.Data.SqlClient builder (\"Trust Server Certificate\" with spaces). Write");
    Console.Error.WriteLine("TrustServerCertificate=True instead.");
    return 1;
}

static void Report(ConfRegContext ctx) => Console.WriteLine(
    $"  attendees={ctx.Attendees.Count()} talks={ctx.Talks.Count()} registrations={ctx.Registrations.Count()} " +
    $"payments={ctx.Payments.Count()} admins={ctx.AdminUsers.Count()}");

static string ArgValue(string name)
{
    var all = Environment.GetCommandLineArgs();
    int i = Array.FindIndex(all, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < all.Length ? all[i + 1] : null;
}

static string Mask(string connectionString) =>
    System.Text.RegularExpressions.Regex.Replace(connectionString, @"(?i)(password\s*=)[^;]*", "$1***");
