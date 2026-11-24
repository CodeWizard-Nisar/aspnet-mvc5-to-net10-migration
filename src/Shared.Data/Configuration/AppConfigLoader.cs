using System;
using System.Configuration;

namespace ConfReg.Data.Configuration
{
    /// <summary>
    /// .NET Framework only (excluded from the net10.0 build). The single place that touches ConfigurationManager.
    /// BASELINE-PROBLEM #3 was ten scattered reads; now there is exactly this one, run once at start-up.
    ///
    /// Precedence, highest first:
    ///   1. environment variable  ConfReg__Name        (containers, IIS app-pool env, CI)
    ///   2. appSettings           ConfReg:Name         (Web.config, or secrets.config pulled in by &lt;appSettings file="..."&gt;)
    ///   3. the default on ConfRegOptions
    /// Secrets belong in 1 or in the git-ignored secrets.config, never in Web.config (BASELINE-PROBLEM #4, fixed).
    /// </summary>
    public static class AppConfigLoader
    {
        public static ConfRegOptions Load()
        {
            var o = new ConfRegOptions();
            o.ConnectionString = Get("ConnectionString");
            o.PaymentApiKey = Get("PaymentApiKey");
            o.LegacyCryptoKey = Get("LegacyCryptoKey");
            o.LegacyCryptoIv = Get("LegacyCryptoIv");
            o.HoldMinutes = GetInt("HoldMinutes", o.HoldMinutes);
            o.MaxTalksPerRegistration = GetInt("MaxTalksPerRegistration", o.MaxTalksPerRegistration);
            return o;
        }

        public static string Get(string name)
        {
            string env = Environment.GetEnvironmentVariable("ConfReg__" + name);
            if (!string.IsNullOrEmpty(env)) return env;
            return ConfigurationManager.AppSettings["ConfReg:" + name];
        }

        private static int GetInt(string name, int fallback)
        {
            string raw = Get(name);
            return string.IsNullOrEmpty(raw) ? fallback : int.Parse(raw);
        }
    }
}
