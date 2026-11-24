using Microsoft.Data.SqlClient;

namespace Characterization.Harness;

/// <summary>Direct SQL, used to reset state, back-date rows (the app has no clock to move) and assert on stored data.</summary>
public static class Db
{
    public static T? Scalar<T>(string sql, params (string Name, object Value)[] args)
    {
        using var conn = new SqlConnection(TestEnvironment.SqlConnection);
        conn.Open();
        using var cmd = Command(conn, sql, args);
        var v = cmd.ExecuteScalar();
        return v == null || v is DBNull ? default : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public static int Exec(string sql, params (string Name, object Value)[] args)
    {
        using var conn = new SqlConnection(TestEnvironment.SqlConnection);
        conn.Open();
        using var cmd = Command(conn, sql, args);
        cmd.CommandTimeout = 120;
        return cmd.ExecuteNonQuery();
    }

    public static string StatusOf(string referenceCode) =>
        Scalar<string>("SELECT CASE Status WHEN 0 THEN 'Pending' WHEN 1 THEN 'Paid' WHEN 2 THEN 'Cancelled' WHEN 3 THEN 'Expired' END FROM Registrations WHERE ReferenceCode=@c",
            ("@c", referenceCode)) ?? "<missing>";

    public static int Count(string table, string where = "1=1") => Scalar<int>($"SELECT COUNT(*) FROM {table} WHERE {where}");

    /// <summary>Pretend a registration was created N minutes ago (the legacy app has no injectable clock).</summary>
    public static void BackDate(string referenceCode, int minutes) =>
        Exec("UPDATE Registrations SET CreatedUtc = DATEADD(minute, -@m, SYSUTCDATETIME()) WHERE ReferenceCode=@c", ("@m", minutes), ("@c", referenceCode));

    private static SqlCommand Command(SqlConnection conn, string sql, (string Name, object Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd;
    }
}
