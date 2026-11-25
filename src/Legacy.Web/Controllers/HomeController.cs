using System.Web.Mvc;

namespace ConfReg.Legacy.Controllers
{
    [AllowAnonymous] // public: the registration wizard, the home page, and the login page itself
    public class HomeController : AppController
    {
        public ActionResult Index()
        {
            return Respond(new { Name = "ConfReg", Version = "legacy-net48" });
        }
    }
}
