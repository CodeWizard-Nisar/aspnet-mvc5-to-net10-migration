using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using ConfReg.Data.Infrastructure;

namespace ConfReg.Data.Services
{
    public class RevenueRow
    {
        public int TicketType { get; set; }
        public int Registrations { get; set; }
        public decimal Revenue { get; set; }
    }

    /// <summary>Reporting query used by the admin dashboard.</summary>
    public class ReportQueries
    {
        private readonly IConfRegContextFactory _db;
        public ReportQueries(IConfRegContextFactory db) { _db = db; }

        public List<RevenueRow> RevenueByTicketType()
        {
            // BASELINE-PROBLEM #9, fixed. Phase 1 opened a SqlConnection, a command and a reader and disposed none of
            // them, stranding a pooled connection per dashboard load until the pool ran dry.
            //
            // The query is still hand-written SQL, but it now runs through the context: EF opens the connection,
            // materialises the rows, and closes it, and the using block disposes the context. There is no longer
            // anything here that can be forgotten. (It also drops the System.Data.SqlClient vs
            // Microsoft.Data.SqlClient difference between net48 and net10.0.) A regression test pins this.
            using (var ctx = _db.Create())
            {
                return ctx.Database.SqlQuery<RevenueRow>(
                    "SELECT TicketType, COUNT(*) AS Registrations, SUM(TotalAmount) AS Revenue " +
                    "FROM Registrations WHERE Status = 1 GROUP BY TicketType ORDER BY TicketType").ToList();
            }
        }
    }
}
