using System.Diagnostics;
using Xunit;

namespace Characterization.Harness;

[CollectionDefinition("app")]
public class AppCollection : ICollectionFixture<AppFixture> { }

/// <summary>
/// Shared once per run. Checks the app is up, makes sure the database schema exists (the legacy app creates
/// it on first use), and offers Reset() to put the database back to the fixed seed before each test.
/// </summary>
public sealed class AppFixture
{
    private readonly string _seed = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "seed.sql"));

    public AppFixture()
    {
        using var c = new ConfRegClient();
        Reply? last = null;
        for (int i = 0; i < 60; i++)
        {
            try { last = c.Get("/Home/Index").GetAwaiter().GetResult(); if (last.Code == 200) break; }
            catch (HttpRequestException) { }
            Thread.Sleep(1000);
        }
        if (last is not { Code: 200 })
            throw new InvalidOperationException(
                $"ConfReg is not answering at {TestEnvironment.BaseUrl} (set CONFREG_BASE_URL). Start the app first; see docs/02-tests.md.");

        // First DB-touching request: lets EF6 create the schema if it does not exist yet.
        c.Get("/Register/Step1").GetAwaiter().GetResult();
    }

    /// <summary>Wipe and reload the fixed seed (about 700 rows, well under a second).</summary>
    public void Reset() => Db.Exec(_seed);

    /// <summary>Runs the real expiry job once and returns how many registrations it expired.</summary>
    public int RunExpiryJob()
    {
        var (file, args) = TestEnvironment.ExpiryCommand;
        using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        string stdout = p.StandardOutput.ReadToEnd(), stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var line = stdout.Split('\n').FirstOrDefault(l => l.StartsWith("expired="));
        if (p.ExitCode != 0 || line == null)
            throw new InvalidOperationException($"Expiry job failed (exit {p.ExitCode}).\n{stdout}\n{stderr}");
        return int.Parse(line["expired=".Length..].Trim());
    }
}
