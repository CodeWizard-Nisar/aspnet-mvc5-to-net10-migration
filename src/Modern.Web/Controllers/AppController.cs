using ConfReg.Modern.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ConfReg.Modern.Controllers;

/// <summary>
/// Same contract as Legacy.Web's AppController: every action hands its view model to Respond(), which returns the HTML view for
/// browsers and JSON when the caller sends ?format=json or Accept: application/json. The JSON is produced by LegacyJson, so it
/// is byte-identical to what the legacy app sends.
/// </summary>
public abstract class AppController : Controller
{
    protected bool WantsJson =>
        string.Equals(Request.Query["format"], "json", StringComparison.OrdinalIgnoreCase)
        || Request.GetTypedHeaders().Accept.Any(a => a.MediaType.Value is { } m && m.StartsWith("application/json", StringComparison.OrdinalIgnoreCase));

    protected IActionResult Respond(object model, string viewName = null)
    {
        if (WantsJson) return Content(LegacyJson.Serialize(model), "application/json; charset=utf-8");
        return View(viewName, model);
    }
}
