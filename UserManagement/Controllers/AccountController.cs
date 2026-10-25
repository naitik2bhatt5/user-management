using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using UserManagement.Services;

namespace UserManagement.Controllers;

/// <summary>Sign-in for the admin pages (Address providers). Credentials come from the "Admin" configuration section.</summary>
public class AccountController : Controller
{
    private readonly IAdminLogin _login;

    public AccountController(IAdminLogin login) => _login = login;

    // GET /Account/Login
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Configured = _login.Configured;
        return View();
    }

    // POST /Account/Login
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? username, string? password, string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.Configured = _login.Configured;

        var client = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        switch (_login.TryLogin(username, password, client))
        {
            case LoginResult.Success:
                var identity = new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, username!.Trim()) }, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = false });
                return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Providers");

            case LoginResult.LockedOut:
                ViewBag.Error = "Too many failed attempts. Try again in 15 minutes.";
                break;
            case LoginResult.NotConfigured:
                ViewBag.Error = "No admin password is configured on the server.";
                break;
            default:
                await Task.Delay(750); // slows down guessing
                ViewBag.Error = "Wrong username or password.";
                break;
        }
        return View();
    }

    // POST /Account/Logout
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Users");
    }

    // Shown when a signed-in user is not allowed; we only have one role, so this is unused but keeps the default path valid.
    public IActionResult Denied() => RedirectToAction(nameof(Login));
}
