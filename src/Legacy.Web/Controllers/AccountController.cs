using System.Web.Mvc;
using ConfReg.Data.Services;
using ConfReg.Legacy.Models;

namespace ConfReg.Legacy.Controllers
{
    public class AccountController : AppController
    {
        private readonly AdminAuthService _auth;
        public AccountController(AdminAuthService auth) { _auth = auth; }

        [HttpGet]
        public ActionResult Login(string returnUrl)
        {
            return Respond(new LoginForm { ReturnUrl = returnUrl });
        }

        [HttpPost]
        public ActionResult Login(LoginForm form)
        {
            var user = _auth.Validate(form.Email, form.Password);
            if (user == null)
            {
                form.Password = null;
                form.Errors.Add("Invalid email or password.");
                return Respond(form);
            }

            // BASELINE-PROBLEM #6 (login half): authentication is three loose values in Session. There is no
            // auth cookie, no principal, no claims. Nothing downstream can use [Authorize] or User.Identity.
            //
            // Unplanned problems, same spot: (a) the session id is not regenerated on login, so a session
            // fixed before login stays valid after it; (b) ReturnUrl is followed without being checked,
            // an open redirect.
            Session["AdminUserId"] = user.Id;
            Session["AdminEmail"] = user.Email;
            Session["AdminRole"] = user.Role;

            if (!string.IsNullOrEmpty(form.ReturnUrl)) return Redirect(form.ReturnUrl);
            return Redirect("~/Admin");
        }

        [HttpGet]
        public ActionResult Logout()
        {
            Session.Remove("AdminUserId");
            Session.Remove("AdminEmail");
            Session.Remove("AdminRole");
            return RedirectToAction("Login");
        }
    }
}
