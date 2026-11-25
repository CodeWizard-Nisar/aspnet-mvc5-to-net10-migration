using System.Security.Claims;
using System.Text.Encodings.Web;
using ConfReg.Domain.Wizard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.SystemWebAdapters;
using Microsoft.Extensions.Options;

namespace ConfReg.Modern.Infrastructure;

public class ModernAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// TRANSITION SWITCH. While the login page still lives in Legacy.Web, an admin signs in there and the proof is Session["AdminUserId"].
    /// With this on, Modern.Web recognises that legacy login by reading the SHARED session (read-only), so pages can move
    /// to Modern.Web BEFORE the login does. Turn it off once Account has moved: from then on modern pages trust only
    /// their own cookie, and the legacy session is no longer part of authentication at all.
    /// </summary>
    public bool AcceptLegacySession { get; set; }
}

public static class AuthSetup
{
    public const string CookieScheme = "ConfRegAdmin";
    public const string LegacySessionScheme = "LegacySession";
    public const string SelectorScheme = "Selector";
    public const string CookieName = ".ConfReg.Admin";

    public static void AddConfRegAuthentication(this IServiceCollection services, bool acceptLegacySession)
    {
        var auth = services.AddAuthentication(SelectorScheme)
            // Picks the scheme per request: our own cookie if the browser has one, otherwise (if enabled) the legacy session.
            .AddPolicyScheme(SelectorScheme, SelectorScheme, o =>
                o.ForwardDefaultSelector = ctx =>
                    !acceptLegacySession || ctx.Request.Cookies.ContainsKey(CookieName) ? CookieScheme : LegacySessionScheme)
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.Name = CookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;               // a first line of CSRF defence; see docs (no antiforgery tokens yet)
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // set Always in production behind HTTPS
                o.ExpireTimeSpan = TimeSpan.FromMinutes(60);        // same as the legacy session timeout
                o.SlidingExpiration = true;

                // Match Legacy.Web exactly, so a page moving between the two changes nothing a client can see:
                //   page -> 302 /Account/Login?returnUrl=...      (note the lower-case "returnUrl")
                //   JSON -> 401, no redirect
                o.LoginPath = "/Account/Login";
                o.ReturnUrlParameter = "returnUrl";
                o.Events.OnRedirectToLogin = ctx => Respond(ctx, 401);
                o.Events.OnRedirectToAccessDenied = ctx => Respond(ctx, 403);
            });
        if (acceptLegacySession)
            auth.AddScheme<AuthenticationSchemeOptions, LegacySessionAuthenticationHandler>(LegacySessionScheme, null);
    }

    private static Task Respond(RedirectContext<CookieAuthenticationOptions> ctx, int status)
    {
        if (WantsJson(ctx.Request)) ctx.Response.StatusCode = status;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    }

    /// <summary>The one definition of "this caller wants JSON", shared by both schemes so they cannot answer differently.</summary>
    public static bool WantsJson(HttpRequest request) =>
        string.Equals(request.Query["format"], "json", StringComparison.OrdinalIgnoreCase)
        || request.Headers.Accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));

    /// <summary>Where an unauthenticated browser is sent. Must match Legacy.Web's filter exactly, including the parameter's spelling.</summary>
    public static string LoginRedirect(HttpRequest request) =>
        "/Account/Login?returnUrl=" + Uri.EscapeDataString(request.Path + request.QueryString);
}

/// <summary>
/// Turns the legacy app's login (three values in the shared session) into a ClaimsPrincipal. READ-ONLY by design: it never
/// writes the session, so it never takes the write lock on the legacy side, and a failure here cannot corrupt legacy state.
/// </summary>
public class LegacySessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            var session = Context.AsSystemWeb().Session;
            if (session[SessionKeys.AdminUserId] is not int id) return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Name, session[SessionKeys.AdminEmail] as string ?? ""),
                new Claim(ClaimTypes.Role, session[SessionKeys.AdminRole] as string ?? "")
            }, LegacySessionAuthentication);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
        catch (Exception ex)
        {
            // The legacy app unreachable, or the session not loaded for this endpoint: not signed in, not a 500.
            Logger.LogWarning(ex, "Could not read the legacy session for authentication");
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    /// <summary>
    /// Challenge behaviour, which a custom handler does NOT get for free: the base implementation just sets 401.
    /// That is right for a JSON caller and wrong for a browser, which must be sent to the login page exactly as the cookie
    /// scheme and the legacy app do. Found by the characterization tests in the MIXED topology, where an anonymous browser
    /// hitting a modern page is handled by THIS scheme (no admin cookie yet) and got a bare 401 instead of a redirect.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (AuthSetup.WantsJson(Request)) Response.StatusCode = StatusCodes.Status401Unauthorized;
        else Response.Redirect(AuthSetup.LoginRedirect(Request));
        return Task.CompletedTask;
    }

    private const string LegacySessionAuthentication = "LegacySession";
}
