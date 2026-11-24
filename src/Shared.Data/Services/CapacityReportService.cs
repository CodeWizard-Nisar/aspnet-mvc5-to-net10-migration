using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ConfReg.Data.Infrastructure;
using ConfReg.Domain.Abstractions;

namespace ConfReg.Data.Services
{
    /// <summary>Writes the nightly capacity report (CSV) to ~/App_Data/reports. One file per UTC day, rewritten if re-run.</summary>
    public class CapacityReportService
    {
        public const int WarningPercent = 80; // same threshold as the admin dashboard

        private readonly IConfRegContextFactory _db;
        private readonly IAppPaths _paths;

        public CapacityReportService(IConfRegContextFactory db, IAppPaths paths)
        {
            _db = db;
            _paths = paths;
        }

        /// <summary>Returns the full path of the report written.</summary>
        public string WriteReport(DateTime nowUtc)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TalkId,Title,Track,Room,StartsUtc,Capacity,Taken,Remaining,PercentFull,Warning");
            using (var ctx = _db.Create())
            {
                var seats = SeatCounts.For(ctx);
                foreach (var t in ctx.Talks.OrderBy(t => t.StartsUtc).ThenBy(t => t.Id).ToList())
                {
                    int taken = seats.ContainsKey(t.Id) ? seats[t.Id] : 0;
                    int percent = t.Capacity == 0 ? 100 : (int)Math.Round(100.0 * taken / t.Capacity);
                    sb.AppendLine(string.Join(",", new[]
                    {
                        t.Id.ToString(CultureInfo.InvariantCulture), Csv(t.Title), Csv(t.Track), Csv(t.Room),
                        t.StartsUtc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
                        t.Capacity.ToString(CultureInfo.InvariantCulture), taken.ToString(CultureInfo.InvariantCulture),
                        (t.Capacity - taken).ToString(CultureInfo.InvariantCulture), percent.ToString(CultureInfo.InvariantCulture),
                        percent > WarningPercent ? "YES" : "no"
                    }));
                }
            }

            string dir = _paths.Resolve("~/App_Data/reports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "capacity-" + nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");

            // Write to a temp name, then rename: a reader (or a crash mid-write) never sees a half-written report.
            string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            return path;
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value; // spreadsheet formula guard
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
