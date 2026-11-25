using System.Data.Entity;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ConfReg.Data.Configuration;
using ConfReg.Data.Seed;
using Microsoft.AspNetCore.SystemWebAdapters;
using ConfReg.Domain.Security;
using ConfReg.Domain.Wizard;
using ConfReg.Legacy.Infrastructure;

namespace ConfReg.Legacy
{
    /// <summary>
    /// BASELINE-PROBLEM #5, partly addressed in Phase 3: Global.asax is still the start-up hook (it has to be:
    /// HttpApplication is how ASP.NET Framework starts), but it no longer holds wiring decisions. It calls the
    /// composition root once, and controllers receive their services through constructors.
    /// What remains of #5, retired in Phase 5: HttpApplication does not exist on ASP.NET Core, where
    /// Modern.Web/Program.cs replaces this file.
    /// </summary>
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            var hasher = new BCryptPasswordHasher();

            // Creates and seeds the database on first run (200 attendees, 20 talks, 150 registrations).
            Database.SetInitializer(new ConfRegInitializer(hasher));

            DependencyResolver.SetResolver(LegacyComposition.Build(hasher));

            // Shared session, FRAMEWORK (legacy) side. docs/05-strangler.md explains every line; the order matters
            // (the serializer must be added before the remote-app server builder). Skipped when SharedSession is off,
            // which is how the Mono-based test host runs without the IIS-only plumbing.
            if (AppConfigLoader.Get("SharedSessionEnabled") != "false")
            {
                // CS0618 suppressed deliberately. The package marks this overload obsolete and points to
                // HttpApplicationHost.RegisterHost(...), but in 2.3.0 the builder that RegisterHost hands you exposes
                // only AddDataProtection and the web-object activator - there is no way to register the SESSION SERVER
                // through it (verified against the shipped assembly). So this obsolete call is the only functional path
                // for shared session in this version. Revisit when a release exposes session on the host builder.
#pragma warning disable CS0618
                this.AddSystemWebAdapters()
                    .AddJsonSessionSerializer(o =>
                    {
                        foreach (var key in SessionKeys.Shared) o.KnownKeys[key.Item1] = key.Item2; // THE contract, from Shared.Domain
                    })
                    .AddRemoteAppServer(o => o.ApiKey = AppConfigLoader.Get("RemoteAppApiKey"))
                    .AddSessionServer();
#pragma warning restore CS0618
            }

            GlobalFilters.Filters.Add(new AdminAuthorizeFilter()); // secure by default; public controllers opt out

            AreaRegistration.RegisterAllAreas();
            RegisterRoutes(RouteTable.Routes);
        }

        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // /Admin -> dashboard. Everything else is plain {controller}/{action}.
            routes.MapRoute(
                name: "Dashboard",
                url: "Admin",
                defaults: new { controller = "Admin", action = "Index" });

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
