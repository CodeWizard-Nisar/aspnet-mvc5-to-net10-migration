using System.Runtime.InteropServices;

namespace Characterization.Harness;

/// <summary>
/// Everything that changes between "which build of ConfReg is under test" and "which machine".
/// All of it is overridable by environment variable, so the tests themselves never change.
/// </summary>
public static class TestEnvironment
{
    public static string BaseUrl =>
        (Environment.GetEnvironmentVariable("CONFREG_BASE_URL") ?? "http://localhost:5080").TrimEnd('/');

    /// <summary>The database the app under test uses. THE TESTS WIPE AND RELOAD IT. Use a throwaway database.</summary>
    public static string SqlConnection =>
        Environment.GetEnvironmentVariable("CONFREG_TEST_SQL")
        ?? "Server=localhost,1433;Database=ConfReg;User Id=sa;Password=ConfReg_Dev_Passw0rd!;TrustServerCertificate=True";

    /// <summary>
    /// How to run the expiry job ONCE. It must print "expired=N". Phase 1 and 3: the Windows Service exe with
    /// --once. Phase 4 changes this one setting to point at Jobs.Worker; the tests do not change.
    /// </summary>
    public static (string File, string Args) ExpiryCommand
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("CONFREG_EXPIRY_CMD");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                var parts = custom.Split(' ', 2);
                return (parts[0], parts.Length > 1 ? parts[1] : "");
            }
            var exe = Path.Combine(RepoRoot, "src", "Legacy.Service", "bin", "Debug", "net48", "ConfReg.LegacyService.exe");
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? (exe, "--once")
                : ("mono", $"\"{exe}\" --once"); // the net48 exe runs on Mono off Windows
        }
    }

    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConfReg.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Could not find ConfReg.sln above " + AppContext.BaseDirectory);
        }
    }

    public static string SnapshotDir => Path.Combine(RepoRoot, "tests", "Characterization", "Snapshots");
    public static bool UpdateSnapshots => Environment.GetEnvironmentVariable("CONFREG_UPDATE_SNAPSHOTS") == "1";
}
