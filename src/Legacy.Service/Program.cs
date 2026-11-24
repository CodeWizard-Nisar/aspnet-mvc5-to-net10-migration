using System;
using System.Configuration;
using System.ServiceProcess;
using ConfReg.Data.Configuration;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;

namespace ConfReg.LegacyService
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // `--once` runs the wrapped method a single time and exits. Added in Phase 2 so the
            // characterization tests can trigger the REAL expiry job without waiting for the timer.
            // It is the only production-code change made for the tests, and Jobs.Worker (Phase 4)
            // must offer the same hook so the same tests keep working.
            if (args.Length > 0 && args[0] == "--once")
            {
                Console.WriteLine("expired=" + CreateExpiryService().ExpireUnpaid());
                return;
            }

            var service = new ExpiryWindowsService(CreateExpiryService(), IntervalSeconds());

            // `ConfReg.LegacyService.exe --console` runs it in a terminal for debugging.
            if (Environment.UserInteractive && args.Length > 0 && args[0] == "--console")
            {
                service.RunInConsole();
                return;
            }

            ServiceBase.Run(service);
        }

        // Composition root for this process: the same settings loader and the same service the web app uses.
        private static ExpiryService CreateExpiryService()
        {
            var settings = new ConfRegSettings(AppConfigLoader.Load());
            return new ExpiryService(new ConfRegContextFactory(settings), settings);
        }

        private static double IntervalSeconds()
        {
            return double.Parse(ConfigurationManager.AppSettings["Service.IntervalSeconds"] ?? "60");
        }
    }
}
