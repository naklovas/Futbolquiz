using System.Security.Claims;
using ITInventory.Data;
using ITInventory.Web.Configuration;
using ITInventory.Web.Models.Account;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITInventory.Web.Controllers;

[RequireHttps]
public class AccountController : Controller
{
    private readonly ILdapAuthenticationService _ldapAuth;
    private readonly DevLoginSettings _devLogin;
    private readonly LdapSettings _ldapSettings;
    private readonly ITInventoryDbContext _db;
    private readonly ILogger<AccountController> _logger;
    private readonly IActivityLogger _activityLogger;

    public AccountController(
        ILdapAuthenticationService ldapAuth,
        ITInventoryDbContext db,
        ILogger<AccountController> logger,
        IActivityLogger activityLogger,
        IOptions<DevLoginSettings> devLogin,
        IOptions<LdapSettings> ldapSettings)
    {
        _ldapAuth = ldapAuth;
        _devLogin = devLogin.Value;
        _ldapSettings = ldapSettings.Value;
        _db = db;
        _logger = logger;
        _activityLogger = activityLogger;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // AD/LDAP is the only way in, except behind the development switch below. The old
        // "test user" mode is gone entirely: it carried a second credential store and a PBKDF2
        // verifier whose salt was read back out of the stored string.
        //
        // DevLogin stores no secret at all -- it skips validation outright -- so it does not
        // bring that finding back. Program.cs refuses to start when it is enabled outside
        // Development, so it cannot reach production by being forgotten.
        if (_devLogin.Enabled)
        {
            _logger.LogWarning(
                "DEVLOGIN: parola dogrulanmadan giris denemesi -- {Username}", model.Username);
        }
        string? dogrulananDomain = null;

        if (_devLogin.Enabled)
        {
            // Gelistirme anahtari: domaini kullanici adindan okuyoruz ki
            // kapsam testleri gercek akisla ayni kaydi bulsun.
            dogrulananDomain = model.Username.Contains('@')
                ? model.Username[(model.Username.IndexOf('@') + 1)..]
                : _ldapSettings.VarsayilanDomain;
        }
        else if (!_ldapAuth.ValidateCredentials(model.Username, model.Password,
                     out var ldapError, out dogrulananDomain))
        {
            ModelState.AddModelError(string.Empty, ldapError ?? "Invalid username or password.");
            return View(model);
        }

        var bareUsername = model.Username.Contains('@') ? model.Username.Split('@')[0] : model.Username;

        // Kullaniciyi DOGRULANDIGI DOMAIN ile birlikte ariyoruz. Yalnizca
        // Username ile aramak, ayni adin iki domainde bulunabildigi bir
        // kurulumda iki farkli kisiyi ayni kayda -- ayni ulke yetkisine --
        // baglardi.
        //
        // Domain'i NULL olan ESKI kayitlar varsayilan domaine ait sayilir,
        // boylece bugun giris yapabilen kimse etkilenmiyor.
        var varsayilanMi = string.Equals(dogrulananDomain, _ldapSettings.VarsayilanDomain,
                                         StringComparison.OrdinalIgnoreCase);

        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == bareUsername
                && (u.Domain == dogrulananDomain
                    || (varsayilanMi && u.Domain == null)));

        if (user is null || !user.IsActive)
        {
            _logger.LogWarning(
                "Authentication succeeded but the user is not registered/active in the IT Inventory system: {Username} ({Domain})",
                bareUsername, dogrulananDomain);
            ModelState.AddModelError(string.Empty,
                "This user is not registered in the IT Inventory system. Please contact your system administrator.");
            return View(model);
        }

        var roles = user.UserRoles
            .Select(ur => ur.Role?.RoleName)
            .Where(r => !string.IsNullOrEmpty(r))
            .Cast<string>()
            .ToList();

        if (roles.Count == 0)
        {
            ModelState.AddModelError(string.Empty,
                "No role is assigned to this user. Please contact your system administrator.");
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(AppClaimTypes.FullName, user.FullName),
        };

        if (!string.IsNullOrWhiteSpace(user.RepositoryName))
        {
            claims.Add(new Claim(AppClaimTypes.Country, user.RepositoryName));

            var country = await _db.Countries.FirstOrDefaultAsync(c => c.Name == user.RepositoryName);
            if (country is not null)
            {
                claims.Add(new Claim(AppClaimTypes.CountryId, country.Id.ToString()));
                if (!string.IsNullOrWhiteSpace(country.DisplayName))
                    claims.Add(new Claim(AppClaimTypes.CountryDisplayName, country.DisplayName));
            }
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

        // SignInAsync only sets the auth cookie for future requests -- HttpContext.User still
        // reflects the anonymous principal from the start of this request unless set here, so
        // without this the login's own ActivityLog row would be written with an empty username.
        HttpContext.User = principal;
        await _activityLogger.LogAsync("Login", "User", user.Username);

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrEmpty(username))
            await _activityLogger.LogAsync("Logout", "User", username);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }
}
