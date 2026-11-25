using System;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ConfReg.Domain.Wizard;

namespace ConfReg.Legacy.Infrastructure
{
    /// <summary>
    /// BASELINE-PROBLEM #6, fixed in Phase 5: a GLOBAL authorization filter, secure by default.
    ///
    /// Phase 1's only admin check was a Response.Redirect inside _AdminLayout.cshtml. A layout runs only when a view renders,
    /// so every redirecting POST (Cancel, Refund, Delete, Save), the CSV export and every JSON response skipped it entirely.
    /// A filter runs for EVERY action, before the action. Public controllers opt out with [AllowAnonymous].
    ///
    /// Why it was added to the LEGACY app, which is about to be replaced: during a strangler migration pages move one at a
    /// time. Securing only the modern pages would leave the not-yet-moved pages wide open and make "migrated" and "secured"
    /// the same decision. Securing the legacy app first means every stage of the migration is equally protected, and the
    /// same tests pass against every stage.
    ///
    /// Behaviour (identical to Modern.Web, so a page moving between them changes nothing visible):
    ///   browser page, not signed in  -> 302 to /Account/Login?returnUrl=...
    ///   JSON request, not signed in  -> 401, no redirect
    /// </summary>
    public class AdminAuthorizeFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationContext filterContext)
        {
            if (IsAnonymousAllowed(filterContext)) return;
            if (filterContext.HttpContext.Session != null && filterContext.HttpContext.Session[SessionKeys.AdminUserId] != null) return;

            var request = filterContext.HttpContext.Request;
            bool json = string.Equals(request["format"], "json", StringComparison.OrdinalIgnoreCase)
                || (request.AcceptTypes != null && request.AcceptTypes.Any(t => t != null && t.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)));

            filterContext.Result = json
                ? (ActionResult)new HttpUnauthorizedResult()
                : new RedirectResult("~/Account/Login?returnUrl=" + HttpUtility.UrlEncode(request.RawUrl));
        }

        private static bool IsAnonymousAllowed(AuthorizationContext c)
        {
            return c.ActionDescriptor.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
                || c.ActionDescriptor.ControllerDescriptor.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);
        }
    }
}
