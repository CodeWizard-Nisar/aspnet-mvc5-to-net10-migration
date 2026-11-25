using ConfReg.Gateway;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

// ContentRootPath = the folder the program lives in, not the shell's current directory, so appsettings.json is found
// however the gateway is launched (a service manager, a container, or `dotnet bin/.../ConfReg.Gateway.dll` from anywhere).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

// Routes come from configuration at start-up (restart to cut a page over or back). See docs/05-strangler.md for why we did not
// hot-reload: a route change is a deployment decision and should leave a restart in the logs.
var (routes, clusters) = MigrationRoutes.Build(builder.Configuration);
builder.Services.AddReverseProxy().LoadFromMemory(routes, clusters);

var app = builder.Build();

app.MapGet("/gateway/health", () => Results.Ok(new { status = "Healthy" }));

// Where does each page live right now? The first thing to look at when "the wrong app answered".
app.MapGet("/gateway/routes", (IConfiguration config) => Results.Json(new
{
    pages = MigrationRoutes.Resolve(config).ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
    everythingElse = "Legacy"
}));

// A half-started stack is the normal state during a strangler migration: one app is up, the other is not. Without this,
// the browser shows a bare 500/502 and the console shows a socket stack trace, which says nothing about WHICH app is
// missing or what to do. YARP records the failure in IForwarderErrorFeature, so turn that into a straight answer.
app.Use(async (context, next) =>
{
    await next();

    var error = context.Features.Get<IForwarderErrorFeature>();
    if (error is null || context.Response.HasStarted) return;

    string cluster = context.GetReverseProxyFeature()?.Cluster?.Config.ClusterId ?? "unknown";
    bool legacy = cluster == MigrationRoutes.LegacyCluster;
    string app_ = legacy ? "Legacy.Web (.NET Framework 4.8)" : "Modern.Web (.NET 10)";
    string fix = legacy
        ? "Legacy.Web needs Windows with IIS Express or IIS; it cannot run on Kestrel. Start it with:\n" +
          "  iisexpress.exe /path:\"<repo>\\src\\Legacy.Web\" /port:5080\n" +
          "Or move the admin pages to Modern.Web and skip it entirely:\n" +
          "  tools\\windows\\run-stack.ps1 -SkipLegacy"
        : "Start it with:\n  dotnet run -c Release --project src\\Modern.Web";

    context.Response.StatusCode = StatusCodes.Status502BadGateway;
    context.Response.ContentType = "text/plain; charset=utf-8";
    await context.Response.WriteAsync(
        $"502 Bad Gateway from the ConfReg migration gateway.\n\n" +
        $"This page is routed to {app_}, which did not answer.\n" +
        $"  requested : {context.Request.Path}{context.Request.QueryString}\n" +
        $"  routed to : cluster \"{cluster}\"\n" +
        $"  reason    : {error.Error} ({error.Exception?.Message ?? "no exception"})\n\n" +
        $"{fix}\n\n" +
        $"Which app owns which page: GET /gateway/routes\n" +
        $"Pages not listed there fall through to Legacy.Web by design, so the old app keeps serving anything\n" +
        $"that has not been migrated yet. See docs/05-strangler.md and docs/00-setup.md.\n");
});

app.MapReverseProxy();
app.Run();

public partial class Program { }
