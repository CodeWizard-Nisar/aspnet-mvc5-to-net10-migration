using ConfReg.Data;
using ConfReg.Data.Configuration;
using ConfReg.Data.Infrastructure;
using ConfReg.Data.Services;
using ConfReg.Domain.Abstractions;
using ConfReg.Domain.Security;
using ConfReg.Domain.Services;
using ConfReg.Domain.Wizard;
using ConfReg.Modern.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.SystemWebAdapters;
using System.Web.SessionState;
using Microsoft.Extensions.Options;
using Serilog;
using System.Data.Entity;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "ConfReg.Modern.Web").WriteTo.Console());

// ---- configuration: same ConfReg section and ConfReg__* variables as every other host --------------------------------------
builder.Services.AddOptions<ConfRegOptions>().Bind(builder.Configuration.GetSection(ConfRegOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "ConfReg:ConnectionString is not set (user-secrets or ConfReg__ConnectionString).")
    .ValidateOnStart();
var auth = builder.Configuration.GetSection(ModernAuthOptions.SectionName).Get<ModernAuthOptions>() ?? new ModernAuthOptions();

Database.SetInitializer<ConfRegContext>(null); // never create or seed from here; Legacy.Web owns the database (see docs/05-strangler.md)

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISettings>(sp => new ConfRegSettings(sp.GetRequiredService<IOptions<ConfRegOptions>>().Value));
builder.Services.AddSingleton<IConfRegContextFactory, ConfRegContextFactory>();
builder.Services.AddSingleton<IClientInfo, HttpContextClientInfo>();
builder.Services.AddSingleton<IAppPaths>(_ => new DirectoryAppPaths(AppContext.BaseDirectory));
builder.Services.AddSingleton<ISessionStore, SharedSessionStore>();
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<LegacyPasswordCipher>();
builder.Services.AddSingleton<WizardSession>();
builder.Services.AddSingleton<PaymentGateway>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<RegistrationService>();
builder.Services.AddSingleton<ReportQueries>();
builder.Services.AddSingleton<AdminAuthService>();

// ---- authentication: cookie auth + a GLOBAL authorize filter (secure by default) ------------------------------------------
builder.Services.AddConfRegAuthentication(auth.AcceptLegacySession);
builder.Services.AddAuthorization();
builder.Services.AddDataProtection().SetApplicationName("ConfReg.Admin")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "App_Data", "keys"))); // cookies survive restarts

builder.Services.AddControllersWithViews(o =>
    {
        // Every action requires a signed-in admin unless it says [AllowAnonymous]. A new controller is protected by default.
        o.Filters.Add(new AuthorizeFilter());
    })
    // The legacy wire format, explicitly (Infrastructure/LegacyJson.cs). The MVC formatter uses the same settings, so even
    // an action that returns an object instead of calling Respond() cannot drift from the legacy shape.
    .AddNewtonsoftJson(o => CopySettings(LegacyJson.Settings, o.SerializerSettings));

// ---- SHARED SESSION, ASP.NET Core (modern) side. Mirror of Legacy.Web/Global.asax.cs; docs/05-strangler.md explains each line.
builder.Services.AddSystemWebAdapters()
    .AddJsonSessionSerializer(o =>
    {
        foreach (var key in SessionKeys.Shared) o.KnownKeys[key.Item1] = key.Item2; // the SAME contract the legacy side registers
    })
    .AddRemoteAppClient(o =>
    {
        o.RemoteAppUrl = new Uri(builder.Configuration["LegacyApp:Url"] ?? "http://localhost:5080");
        o.ApiKey = builder.Configuration["ConfReg:RemoteAppApiKey"] ?? throw new InvalidOperationException(
            "ConfReg:RemoteAppApiKey is not set. It must equal the legacy app's ConfReg:RemoteAppApiKey (32 hex characters).");
    })
    .AddSessionClient(o =>
    {
        // THE session cookie name, and it must be the LEGACY app's (Web.config sessionState cookieName="ConfReg.Session").
        // The client reads this cookie off the incoming request and presents it to the legacy app, which is how the two
        // processes agree on WHOSE session they are talking about. Default it wrongly and every lookup returns a brand new,
        // empty session, with no error anywhere.
        o.CookieName = builder.Configuration["LegacyApp:SessionCookie"] ?? "ConfReg.Session";
    });
// NOTE: deliberately NO AddSession()/UseSession() here. ASP.NET Core's own session is a DIFFERENT store, and giving it the
// legacy cookie name (which is what I tried first) makes its middleware try to data-protect-unprotect a plain ASP.NET
// session id: "Error unprotecting the session cookie", and the shared session silently never arrives. Remote session
// replaces Core's session; the two must not share a cookie.

// Only reachable through the gateway: trust its X-Forwarded-* headers so IClientInfo sees the real client address.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// ---- A REAL FINDING: SystemWebAdapters 2.3.0 does not run on .NET 10 as shipped ---------------------------------------
// Microsoft.AspNetCore.SystemWebAdapters.CoreServices 2.3.0 (its own net10.0 build included) references
// Microsoft.AspNetCore.Server.IIS.IIISEnvironmentFeature, a type .NET 10 REMOVED. Two separate things then break:
//
//   1. MVC discovers controllers by enumerating every application part's types, so start-up died with
//      ReflectionTypeLoadException. Fixed by dropping the adapter assemblies as application parts: they contain no
//      controllers, views or tag helpers, so nothing is lost.
//   2. The adapter registers an IStartupFilter (HostingEnvironmentStartupFilter) that calls the removed type directly.
//      It exists to populate System.Web.Hosting.HostingEnvironment from IIS, which is meaningless off IIS and which this
//      application never reads: paths come from the injected IAppPaths. Removing that one filter leaves the parts we
//      DO rely on (HttpContext bridging, the session client) untouched.
//
// This is surgery on a dependency's registrations and it is the most fragile thing in Phase 5, so it fails LOUDLY if the
// package changes shape rather than silently doing nothing. Revisit when a release properly targets .NET 10.
// Documented in docs/05-strangler.md.
builder.Services.AddControllers().ConfigureApplicationPartManager(parts =>
{
    foreach (var part in parts.ApplicationParts
                 .Where(p => p.Name.StartsWith("Microsoft.AspNetCore.SystemWebAdapters", StringComparison.Ordinal))
                 .ToList())
        parts.ApplicationParts.Remove(part);
});

// Four registrations come from the package's IIS-hosting helper class (HostingRuntimeExtensions): one IStartupFilter and
// three IConfigureOptions<SystemWebAdaptersOptions>. They populate System.Web.Hosting.HostingEnvironment and app paths FROM
// IIS, which is meaningless off IIS and which this application never reads (paths come from the injected IAppPaths), and
// two of them touch the removed type. Remove them as a set, and fail LOUDLY if the package's shape changes.
var iisHostingRegistrations = builder.Services
    .Where(d => d.ImplementationType?.DeclaringType?.Name == "HostingRuntimeExtensions")
    .ToList();
if (iisHostingRegistrations.Count == 0)
    throw new InvalidOperationException(
        "Expected SystemWebAdapters' IIS-hosting registrations (HostingRuntimeExtensions) to be present so they could be " +
        "removed, and found none. The package's shape has changed: re-check whether the .NET 10 IIISEnvironmentFeature " +
        "problem still exists (see docs/05-strangler.md) before deleting this block.");
foreach (var registration in iisHostingRegistrations) builder.Services.Remove(registration);

var app = builder.Build();

// MIDDLEWARE ORDER, and it is not interchangeable:
//   UseRouting            first, because the adapter decides whether to fetch the session from the ENDPOINT's metadata
//                          ([Session] / the route metadata set below). Before routing there is no endpoint to ask.
//   UseSystemWebAdapters  second, so the shared session is loaded and bridged BEFORE anything reads it.
//   UseAuthentication     third. The legacy-session scheme reads the shared session, so a session that is not loaded yet
//                          is simply null: I had these two the other way round at first, and every cross-app login failed
//                          with a NullReferenceException swallowed into "not signed in". No error, no redirect loop, just
//                          an admin who could not get in. See docs/05-strangler.md.
//   UseAuthorization      last, as always: it needs the result of authentication.
app.UseForwardedHeaders();
app.UseRouting();
app.UseSystemWebAdapters();
app.UseAuthentication();
app.UseAuthorization();

// Which endpoints load the legacy session (a network call to the legacy app each time!):
//  - everything, while Auth:AcceptLegacySession is on, because authentication needs it;
//  - otherwise only controllers that ask with [Session] (the diagnostics page).
// ReadOnly on purpose: modern never writes the legacy session, so no write lock is ever held on the legacy side.
var routes = app.MapControllerRoute("admin-home", "Admin", new { controller = "Admin", action = "Index" });
var defaults = app.MapControllerRoute("default", "{controller=Admin}/{action=Index}/{id?}");
if (auth.AcceptLegacySession)
{
    var readOnly = new SessionAttribute { SessionBehavior = SessionStateBehavior.ReadOnly, IsPreLoad = true };
    routes.WithMetadata(readOnly);
    defaults.WithMetadata(readOnly);
}

app.MapGet("/health/live", () => Results.Ok("Healthy")).AllowAnonymous();
app.Run();

static void CopySettings(Newtonsoft.Json.JsonSerializerSettings from, Newtonsoft.Json.JsonSerializerSettings to)
{
    to.ContractResolver = from.ContractResolver;
    foreach (var converter in from.Converters) to.Converters.Add(converter);
    to.DateFormatHandling = from.DateFormatHandling;
    to.DateTimeZoneHandling = from.DateTimeZoneHandling;
    to.NullValueHandling = from.NullValueHandling;
    to.Formatting = from.Formatting;
    to.StringEscapeHandling = from.StringEscapeHandling;
    to.ReferenceLoopHandling = from.ReferenceLoopHandling;
    to.MissingMemberHandling = from.MissingMemberHandling;
    to.TypeNameHandling = from.TypeNameHandling;
}

/// <summary>Exposed so WebApplicationFactory can host the app in tests.</summary>
public partial class Program { }
