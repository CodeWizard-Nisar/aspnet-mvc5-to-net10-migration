using System;
using System.ServiceProcess;
using System.Timers;
using ConfReg.Data.Services;

namespace ConfReg.LegacyService
{
    /// <summary>
    /// BASELINE-PROBLEM #8: a Windows Service whose entire job is to call ONE business-logic method on a timer.
    ///
    /// Why it is a problem rather than just old:
    ///  - It is a second deployable with its own installer (installutil), account, config file and logging.
    ///  - It needs Windows. It cannot run in a container or on Linux.
    ///  - No overlap guard, no retry, no health reporting, no structured logs. If ExpireUnpaid hangs, the
    ///    service still shows "Running" in services.msc.
    ///  - Nobody remembers it exists until registrations stop expiring after a server rebuild.
    /// Replaced in Phase 4 by Jobs.Worker (.NET 10, Quartz.NET, Serilog, a health endpoint).
    /// </summary>
    public class ExpiryWindowsService : ServiceBase
    {
        private readonly ExpiryService _expiry;
        private readonly double _intervalSeconds;
        private Timer _timer;

        public ExpiryWindowsService(ExpiryService expiry, double intervalSeconds)
        {
            ServiceName = "ConfRegExpiry";
            _expiry = expiry;
            _intervalSeconds = intervalSeconds;
        }

        protected override void OnStart(string[] args)
        {
            _timer = new Timer(_intervalSeconds * 1000);
            _timer.Elapsed += (s, e) => Tick();
            _timer.Start();
        }

        protected override void OnStop()
        {
            if (_timer != null) _timer.Stop();
        }

        public void RunInConsole()
        {
            OnStart(new string[0]);
            Console.WriteLine("ConfRegExpiry running. Press Enter to stop.");
            Console.ReadLine();
            OnStop();
        }

        private void Tick()
        {
            try
            {
                // The one wrapped method.
                int expired = _expiry.ExpireUnpaid();
                if (expired > 0) EventLog.WriteEntry("Expired " + expired + " unpaid registration(s).");
            }
            catch (Exception ex)
            {
                // Swallowed: the next tick tries again. Nobody is told.
                EventLog.WriteEntry(ex.ToString(), System.Diagnostics.EventLogEntryType.Error);
            }
        }
    }
}
