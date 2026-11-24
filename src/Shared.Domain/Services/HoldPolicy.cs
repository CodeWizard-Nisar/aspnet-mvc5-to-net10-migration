using System.Configuration;

namespace ConfReg.Domain.Services
{
    /// <summary>
    /// BASELINE-PROBLEM #3: ConfigurationManager.AppSettings read inline, at the point of use,
    /// in a static class, with the default hard-coded next to it. There are ~10 of these across
    /// the solution (grep "AppSettings"). No single place lists what the app can be configured
    /// with, nothing is typed, and nothing can be faked in a test.
    /// Fixed in Phase 3: ISettings, backed by IOptions on .NET 10.
    /// </summary>
    public static class HoldPolicy
    {
        public static int Minutes
        {
            get { return int.Parse(ConfigurationManager.AppSettings["Registration.HoldMinutes"] ?? "30"); }
        }

        public static int MaxTalks
        {
            get { return int.Parse(ConfigurationManager.AppSettings["Registration.MaxTalks"] ?? "5"); }
        }
    }
}
