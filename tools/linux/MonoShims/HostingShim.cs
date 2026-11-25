using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConfReg.MonoShims
{
    /// <summary>
    /// TEST-ONLY stand-ins for two System.Web.Hosting.HostingEnvironment members that exist on .NET Framework under IIS but
    /// not in Mono's System.Web. They let the REAL Microsoft.AspNetCore.SystemWebAdapters.FrameworkServices package start up
    /// when the legacy app is hosted on Mono (a Linux CI stand-in for IIS). Only host-lifetime plumbing is replaced; the
    /// package's session, serialisation, locking and API-key code runs unmodified.
    /// </summary>
    public static class HostingShim
    {
        /// <summary>IIS-hosted .NET Framework 4.8 answers this itself. Used only to choose error detail; "false" is the safe value.</summary>
        /// <summary>HttpResponse.ClientDisconnectedToken is a .NET 4.6 member Mono lacks. "Never cancelled" is the safe stand-in.</summary>
        public static CancellationToken NeverCancelled() { return CancellationToken.None; }

        public static bool IsDevelopmentEnvironment() { return false; }

        /// <summary>
        /// IIS runs the work item and, on app-pool shutdown, waits for it. Here we just run it on the thread pool for the life
        /// of the process. It is the SystemWebAdapters generic host that stays alive in the background.
        /// </summary>
        public static void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
        {
            Task.Run(() => workItem(CancellationToken.None));
        }
    }
}
