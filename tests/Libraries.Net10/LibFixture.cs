using System.Data.Entity;
using ConfReg.Data;
using ConfReg.Data.Configuration;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Seed;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Security;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Libraries.Net10;

[CollectionDefinition("db")]
public class DbCollection : ICollectionFixture<LibFixture> { }

/// <summary>
/// Wires the libraries together the way a modern host will: configuration bound into ConfRegOptions, wrapped in
/// ConfRegSettings. Uses its OWN database (ConfRegNet10) so it never disturbs the characterization tests' database.
/// </summary>
public sealed class LibFixture
{
    // Demo values, same as config/secrets.config.example. Not secrets.
    public const string DemoKey = "Y29uZnJlZy1kZW1vLWtleS1OT1QtQS1TRUNSRVQtMDA=";
    public const string DemoIv = "Y29uZnJlZy1kZW1vLWl2IQ==";

    public ISettings Settings { get; }
    public IConfRegContextFactory Db { get; }
    public IPasswordHasher Hasher { get; } = new BCryptPasswordHasher();
    public LegacyPasswordCipher Legacy { get; }
    public string ConnectionString { get; }
    public string TempDir { get; } = Path.Combine(Path.GetTempPath(), "confreg-net10-" + Guid.NewGuid().ToString("N"));

    public LibFixture()
    {
        var baseCs = Environment.GetEnvironmentVariable("CONFREG_TEST_SQL")
            ?? "Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True";
        // Plain string surgery on purpose. EF6 talks to SQL through System.Data.SqlClient, which rejects the spaced
        // "Trust Server Certificate" that Microsoft.Data.SqlClient's connection-string builder writes. Hand-written
        // strings (TrustServerCertificate=True) are accepted by both.
        ConnectionString = System.Text.RegularExpressions.Regex.Replace(baseCs, "(Database|Initial Catalog)=[^;]*", "Database=ConfRegNet10", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Settings = SettingsFrom(ConnectionString);
        Db = new ConfRegContextFactory(Settings);
        Legacy = new LegacyPasswordCipher(Settings);

        // Fresh database each run, created and seeded by EF6 running on .NET 10.
        Database.SetInitializer<ConfRegContext>(null);
        using (var ctx = Db.Create()) ctx.Database.Delete();
        Database.SetInitializer(new ConfRegInitializer(Hasher));
        using (var ctx = Db.Create()) ctx.Database.Initialize(force: true);
    }

    /// <summary>Exactly the modern binding path: IConfiguration section "ConfReg" into ConfRegOptions.</summary>
    public static ISettings SettingsFrom(string connectionString, int holdMinutes = 30)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["ConfReg:ConnectionString"] = connectionString,
            ["ConfReg:PaymentApiKey"] = "sk_test_DEMO_not_a_real_key",
            ["ConfReg:LegacyCryptoKey"] = DemoKey,
            ["ConfReg:LegacyCryptoIv"] = DemoIv,
            ["ConfReg:HoldMinutes"] = holdMinutes.ToString(),
        }).Build();
        var options = config.GetSection(ConfRegOptions.SectionName).Get<ConfRegOptions>();
        return new ConfRegSettings(options);
    }
}
