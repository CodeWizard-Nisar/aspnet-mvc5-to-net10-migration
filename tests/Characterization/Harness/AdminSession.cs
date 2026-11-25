namespace Characterization.Harness;

/// <summary>A browser that has logged in to the admin portal. Added in Phase 5, when the admin became login-protected on every action.</summary>
public static class AdminSession
{
    public static async Task<ConfRegClient> Login(string email = "admin@confreg.example", string password = "ConfReg-Demo-1!")
    {
        var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", email), ("Password", password));
        if (r.Code != 302) throw new InvalidOperationException("Admin login failed: " + r.Body);
        return c;
    }

    public static bool IsAdminPath(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(path, "^/(Admin|Attendees|Talks|Registrations)([/?]|$)");
}
