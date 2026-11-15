using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UniversalITMonitor.Application.Accounts;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

public sealed class AccountController(IAccountService accounts) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        if (!await accounts.HasAnyUsersAsync())
        {
            return RedirectToAction(nameof(Setup));
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(model.Password));
            model.Password = string.Empty;
            return View(model);
        }

        var account = await accounts.AuthenticateAsync(model.UserName, model.Password);
        ModelState.Remove(nameof(model.Password));
        model.Password = string.Empty;

        if (account is null)
        {
            ModelState.AddModelError(string.Empty, "The username or password is incorrect, or the account is disabled.");
            return View(model);
        }

        await SignInAsync(account, model.RememberMe);
        return RedirectToLocal(model.ReturnUrl);
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Setup()
    {
        if (await accounts.HasAnyUsersAsync())
        {
            return RedirectToAction(nameof(Login));
        }

        return View(new SetupAdminViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Setup(SetupAdminViewModel model)
    {
        if (await accounts.HasAnyUsersAsync())
        {
            return RedirectToAction(nameof(Login));
        }

        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(model.Password));
            ModelState.Remove(nameof(model.ConfirmPassword));
            model.Password = string.Empty;
            model.ConfirmPassword = string.Empty;
            return View(model);
        }

        var created = await accounts.TryCreateFirstAdministratorAsync(
            model.DisplayName,
            model.UserName,
            model.Password);

        ModelState.Remove(nameof(model.Password));
        ModelState.Remove(nameof(model.ConfirmPassword));

        if (!created)
        {
            return RedirectToAction(nameof(Login));
        }

        var account = await accounts.AuthenticateAsync(model.UserName, model.Password);
        if (account is null)
        {
            return RedirectToAction(nameof(Login));
        }

        await SignInAsync(account, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInAsync(AuthenticatedAccount account, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id),
            new(ClaimTypes.Name, account.UserName),
            new(ClaimTypes.GivenName, account.DisplayName)
        };
        claims.AddRange(account.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties
        {
            IsPersistent = isPersistent,
            AllowRefresh = true
        };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }

    private IActionResult RedirectToLocal(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Home");

}
