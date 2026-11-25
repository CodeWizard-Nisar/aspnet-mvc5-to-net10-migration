using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

// DbTool reset <seed.sql>   wipe and reload the fixed seed
// DbTool fingerprint        one hash over every table's contents, to prove a benchmark run did not change the data
var connectionString = Environment.GetEnvironmentVariable("CONFREG_TEST_SQL")
    ?? "Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True";

if (args.Length == 0) { Console.Error.WriteLine("usage: DbTool reset <seed.sql> | fingerprint"); return 2; }

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

switch (args[0])
{
    case "reset":
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = await File.ReadAllTextAsync(args[1]);
            cmd.CommandTimeout = 300;
            await cmd.ExecuteNonQueryAsync();
        }
        Console.WriteLine("seed reloaded");
        return 0;

    case "fingerprint":
    {
        // Row counts plus a checksum per table. CHECKSUM_AGG is cheap and order-independent, which is what we want:
        // the question is "are these the same rows", not "in what order did the engine return them".
        var sql = """
            SELECT
              (SELECT COUNT(*) FROM Attendees),         (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM Attendees),
              (SELECT COUNT(*) FROM Talks),             (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM Talks),
              (SELECT COUNT(*) FROM Registrations),     (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM Registrations),
              (SELECT COUNT(*) FROM RegistrationTalks), (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM RegistrationTalks),
              (SELECT COUNT(*) FROM Payments),          (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM Payments),
              (SELECT COUNT(*) FROM Settings),          (SELECT ISNULL(CHECKSUM_AGG(CHECKSUM(*)),0) FROM Settings)
            """;
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var parts = new StringBuilder();
        for (int i = 0; i < reader.FieldCount; i++) parts.Append(reader.GetValue(i)).Append('|');
        // NOTE: AdminUsers is deliberately excluded. Logging in upgrades a legacy password hash to BCrypt, which is a
        // real write; including it would make the fingerprint differ for a reason that has nothing to do with load.
        Console.WriteLine(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString())))[..16]
            + " rows=" + parts.ToString().Replace("|", ","));
        return 0;
    }

    default:
        Console.Error.WriteLine("unknown command: " + args[0]);
        return 2;
}
