using System.Data.Entity;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ConfReg.Data;
using ConfReg.Data.Seed;

namespace ConfReg.Legacy
{
    /// <summary>
    /// BASELINE-PROBLEM #5: Global.asax as the application's startup wiring.
    ///
    /// Routes, the database initializer and any other start-up work are all hung off one
    /// HttpApplication subclass. It works, but:
    ///  - Start-up order is implicit and spread across event handlers, not visible in one place.
    ///  - HttpApplication does not exist on ASP.NET Core. Program.cs / the host builder replaces it.
    ///  - There is no dependency-injection container: controllers `new` their own services.
    /// Replaced in Phase 5 (Modern.Web/Program.cs); Legacy.Web keeps this file until it is retired.
    /// </summary>
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            // Creates and seeds the database on first run (200 attendees, 20 talks, 150 registrations).
            Database.SetInitializer(new ConfRegInitializer());

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
