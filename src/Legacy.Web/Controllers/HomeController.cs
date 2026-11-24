using System.Web.Mvc;

namespace ConfReg.Legacy.Controllers
{
    public class HomeController : AppController
    {
        public ActionResult Index()
        {
            return Respond(new { Name = "ConfReg", Version = "legacy-net48" });
        }
    }
}
