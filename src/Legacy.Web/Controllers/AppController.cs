using System;
using System.Linq;
using System.Web.Mvc;

namespace ConfReg.Legacy.Controllers
{
    /// <summary>
    /// Base controller. Every action hands its view model to Respond(), which returns the HTML view for
    /// browsers and JSON when the caller sends ?format=json or Accept: application/json. That single seam is
    /// what lets the Phase 2 characterization tests snapshot every action without scraping HTML.
    /// </summary>
    public abstract class AppController : Controller
    {
        protected bool WantsJson
        {
            get
            {
                if (string.Equals(Request["format"], "json", StringComparison.OrdinalIgnoreCase)) return true;
                return Request.AcceptTypes != null
                    && Request.AcceptTypes.Any(t => t != null && t.StartsWith("application/json", StringComparison.OrdinalIgnoreCase));
            }
        }

        protected ActionResult Respond(object model, string viewName = null)
        {
            // JsonRequestBehavior.AllowGet: JavaScriptSerializer, PascalCase, "\/Date(ms)\/" dates.
            if (WantsJson) return Json(model, JsonRequestBehavior.AllowGet);
            return View(viewName, model);
        }
    }
}
