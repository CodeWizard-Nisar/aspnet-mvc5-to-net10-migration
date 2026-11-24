using System;
using System.ServiceProcess;

namespace ConfReg.LegacyService
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            var service = new ExpiryWindowsService();

            // `ConfReg.LegacyService.exe --console` runs it in a terminal for debugging.
            if (Environment.UserInteractive && args.Length > 0 && args[0] == "--console")
            {
                service.RunInConsole();
                return;
            }

            ServiceBase.Run(service);
        }
    }
}
