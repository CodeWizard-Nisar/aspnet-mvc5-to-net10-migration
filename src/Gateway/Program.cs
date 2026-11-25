using ConfReg.Gateway;
using Yarp.ReverseProxy.Configuration;

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

app.MapReverseProxy();
app.Run();

public partial class Program { }
