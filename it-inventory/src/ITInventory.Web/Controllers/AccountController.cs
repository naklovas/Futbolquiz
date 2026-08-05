using System.Security.Claims;
using ITInventory.Data;
using ITInventory.Web.Models.Account;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    private readonly ILdapAuthenticationService _ldapAuth;
    private readonly ITInventoryDbContext _db;
    private readonly ILogger<AccountController> _logger;

    public AccountController(ILdapAuthenticationService ldapAuth, ITInventoryDbContext db, ILogger<AccountController> logger)
    {
        _ldapAuth = ldapAuth;
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        if (!_ldapAuth.ValidateCredentials(model.Username, model.Password, out var ldapError))
        {
            ModelState.AddModelError(string.Empty, ldapError ?? "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        var bareUsername = model.Username.Contains('@') ? model.Username.Split('@')[0] : model.Username;

        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == bareUsername);

        if (user is null || !user.IsActive)
        {
            _logger.LogWarning(
                "AD kimlik doğrulaması başarılı ama kullanıcı IT Envanter sisteminde tanımlı/aktif değil: {Username}",
                bareUsername);
            ModelState.AddModelError(string.Empty,
                "Bu kullanıcı IT Envanter sistemine tanımlı değil. Lütfen sistem yöneticinize başvurun.");
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
                "Kullanıcıya atanmış bir rol bulunamadı. Lütfen sistem yöneticinize başvurun.");
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
                claims.Add(new Claim(AppClaimTypes.CountryId, country.Id.ToString()));
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return Redirect(model.ReturnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}
