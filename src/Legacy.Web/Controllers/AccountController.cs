using System.Web.Mvc;
using ConfReg.Data.Services;
using ConfReg.Domain.Wizard;
using ConfReg.ViewModels;

namespace ConfReg.Legacy.Controllers
{
    [AllowAnonymous] // public: the registration wizard, the home page, and the login page itself
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
            Session[SessionKeys.AdminUserId] = user.Id;
            Session[SessionKeys.AdminEmail] = user.Email;
            Session[SessionKeys.AdminRole] = user.Role;

            // E4, fixed in Phase 5: only follow a LOCAL returnUrl. Phase 1 redirected to whatever it was given (open redirect).
            if (!string.IsNullOrEmpty(form.ReturnUrl) && Url.IsLocalUrl(form.ReturnUrl)) return Redirect(form.ReturnUrl);
            return Redirect("~/Admin");
        }

        [HttpGet]
        public ActionResult Logout()
        {
            Session.Remove(SessionKeys.AdminUserId);
            Session.Remove(SessionKeys.AdminEmail);
            Session.Remove(SessionKeys.AdminRole);
            return RedirectToAction("Login");
        }
    }
}
