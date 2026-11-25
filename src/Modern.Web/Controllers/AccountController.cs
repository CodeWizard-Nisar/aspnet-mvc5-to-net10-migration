using System.Security.Claims;
using ConfReg.Data.Services;
using ConfReg.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ConfReg.Modern.Infrastructure;

namespace ConfReg.Modern.Controllers;

/// <summary>
/// Login and logout, ported from Legacy.Web. The behaviour a client can see is identical (same form, same errors, same redirect
/// rules); what changed is HOW being signed in is remembered: an authentication cookie with a ClaimsPrincipal, instead of three
/// loose values in Session. That is what lets the global authorize filter, roles and User.Identity work.
/// </summary>
[AllowAnonymous]
public class AccountController(AdminAuthService auth) : AppController
{
    [HttpGet]
    public IActionResult Login(string returnUrl) => Respond(new LoginForm { ReturnUrl = returnUrl });

    [HttpPost]
    public async Task<IActionResult> Login(LoginForm form)
    {
        // Same service as Legacy.Web, including the transparent upgrade of legacy-encrypted passwords to BCrypt on first login.
        var user = auth.Validate(form.Email, form.Password);
        if (user == null)
        {
            form.Password = null;
            form.Errors.Add("Invalid email or password.");
            return Respond(form);
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        }, AuthSetup.CookieScheme);
        // New cookie on every sign-in: there is no pre-login session identifier to fixate (E3, which the legacy app has).
        await HttpContext.SignInAsync(AuthSetup.CookieScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(form.ReturnUrl) && Url.IsLocalUrl(form.ReturnUrl)) return LocalRedirect(form.ReturnUrl);
        return LocalRedirect("~/Admin");
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(AuthSetup.CookieScheme);
        return RedirectToAction("Login");
    }
}
