using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;

namespace ConfReg.Data.Services
{
    public class RevenueRow
    {
        public int TicketType { get; set; }
        public int Registrations { get; set; }
        public decimal Revenue { get; set; }
    }

    /// <summary>
    /// Raw-ADO.NET reporting query used by the admin dashboard.
    /// </summary>
    public class ReportQueries
    {
        public List<RevenueRow> RevenueByTicketType()
        {
            var rows = new List<RevenueRow>();

            // BASELINE-PROBLEM #9: opens a connection and never closes or disposes it. The same goes for
            // the command and the reader. Every dashboard load strands one pooled connection until the
            // garbage collector happens to finalise it. With the default pool of 100 the app starts
            // throwing "Timeout expired... max pool size was reached" under steady admin traffic,
            // and nothing in the stack trace points back here.
            // Fixed in Phase 3: using blocks (and then the same query through the context).
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["ConfReg"].ConnectionString);
            conn.Open();
            var cmd = new SqlCommand(
                "SELECT TicketType, COUNT(*) AS Registrations, SUM(TotalAmount) AS Revenue " +
                "FROM Registrations WHERE Status = 1 GROUP BY TicketType ORDER BY TicketType", conn);
            var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new RevenueRow
                {
                    TicketType = reader.GetInt32(0),
                    Registrations = reader.GetInt32(1),
                    Revenue = reader.GetDecimal(2)
                });
            }
            return rows;
        }
    }
}
