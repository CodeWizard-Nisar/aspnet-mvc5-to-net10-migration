using System.Data.Entity;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ConfReg.Data.Seed;
using ConfReg.Domain.Security;
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
