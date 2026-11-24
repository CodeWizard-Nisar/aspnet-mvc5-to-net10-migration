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
    /// How to run a background job ONCE; {job} is replaced by expire | emails | report. The command must print the job's
    /// one-line result on stdout ("expired=N", "sent=N", "report=PATH").
    ///
    /// This is the single setting that moved between phases (the tests themselves never changed):
    ///   Phases 1 to 3: the legacy Windows Service exe with --once (expiry only).
    ///   Phase 4:       the .NET 10 Worker, `dotnet ConfReg.Jobs.Worker.dll --run-job {job}`.
    /// The Phase 2 expiry tests passing unchanged against the Worker is the proof the migration kept behaviour.
    /// </summary>
    public static (string File, string Args) JobCommand(string job)
    {
        var custom = Environment.GetEnvironmentVariable("CONFREG_JOB_CMD");
        string template = !string.IsNullOrWhiteSpace(custom)
            ? custom
            : "dotnet \"" + Path.Combine(RepoRoot, "src", "Jobs.Worker", "bin", "Debug", "net10.0", "ConfReg.Jobs.Worker.dll") + "\" --run-job {job}";
        var parts = template.Replace("{job}", job).Split(' ', 2);
        return (parts[0], parts.Length > 1 ? parts[1] : "");
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
