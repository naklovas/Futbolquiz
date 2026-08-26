using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Admin;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminUsersController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly ICurrentUserService _currentUser;

    public AdminUsersController(ITInventoryDbContext db, IActivityLogger activityLogger, ICurrentUserService currentUser)
    {
        _db = db;
        _activityLogger = activityLogger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var users = await _db.YdUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username)
            .ToListAsync();

        ViewBag.CountryDisplayNames = await _db.Countries
            .Where(c => c.DisplayName != null)
            .ToDictionaryAsync(c => c.Name, c => c.DisplayName!);

        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new UserFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel vm, List<int>? selectedRoleIds)
    {
        var requestedRoleIds = selectedRoleIds ?? new List<int>();

        if (await _db.YdUsers.AnyAsync(u => u.Username == vm.Username))
            ModelState.AddModelError(nameof(vm.Username), "This username already exists.");

        var validRoleIds = await _db.YdRoles.Select(r => r.Id).ToListAsync();
        if (requestedRoleIds.Except(validRoleIds).Any())
            ModelState.AddModelError(string.Empty, "One or more selected roles are invalid.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns(requestedRoleIds);
            return View(vm);
        }

        var country = vm.CountryId.HasValue ? await _db.Countries.FirstOrDefaultAsync(c => c.Id == vm.CountryId.Value && _currentUser.IsAdmin) : null;

        var user = new YdUser
        {
            Username = vm.Username,
            FullName = vm.FullName,
            Email = vm.Email,
            RepositoryName = country?.Name,
            IsActive = vm.IsActive,
            ReceiveExpirationNotifications = vm.ReceiveExpirationNotifications,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var roleId in requestedRoleIds)
            user.UserRoles.Add(new YdUserRole { RoleId = roleId });

        _db.YdUsers.Add(user);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "User", user.Username);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id && _currentUser.IsAdmin);
        if (user is null) return NotFound();

        var country = !string.IsNullOrEmpty(user.RepositoryName)
            ? await _db.Countries.FirstOrDefaultAsync(c => c.Name == user.RepositoryName)
            : null;

        var vm = new UserFormViewModel
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            CountryId = country?.Id,
            IsActive = user.IsActive,
            ReceiveExpirationNotifications = user.ReceiveExpirationNotifications
        };

        await PopulateDropdowns(user.UserRoles.Select(ur => ur.RoleId));
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UserFormViewModel vm, List<int>? selectedRoleIds)
    {
        if (id != vm.Id) return BadRequest();

        var requestedRoleIds = selectedRoleIds ?? new List<int>();

        if (await _db.YdUsers.AnyAsync(u => u.Username == vm.Username && u.Id != id))
            ModelState.AddModelError(nameof(vm.Username), "This username already exists.");

        var validRoleIds = await _db.YdRoles.Select(r => r.Id).ToListAsync();
        if (requestedRoleIds.Except(validRoleIds).Any())
            ModelState.AddModelError(string.Empty, "One or more selected roles are invalid.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns(requestedRoleIds);
            return View(vm);
        }

        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id && _currentUser.IsAdmin);
        if (user is null) return NotFound();

        var country = vm.CountryId.HasValue ? await _db.Countries.FirstOrDefaultAsync(c => c.Id == vm.CountryId.Value && _currentUser.IsAdmin) : null;

        user.Username = vm.Username;
        user.FullName = vm.FullName;
        user.Email = vm.Email;
        user.RepositoryName = country?.Name;
        user.IsActive = vm.IsActive;
        user.ReceiveExpirationNotifications = vm.ReceiveExpirationNotifications;

        user.UserRoles.Clear();
        foreach (var roleId in requestedRoleIds)
            user.UserRoles.Add(new YdUserRole { UserId = user.Id, RoleId = roleId });

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "User", user.Username);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.YdUsers.FirstOrDefaultAsync(u => u.Id == id && _currentUser.IsAdmin);
        if (user is null) return NotFound();

        var username = user.Username;
        _db.YdUsers.Remove(user);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Delete", "User", username);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns(IEnumerable<int>? selectedRoleIds = null)
    {
        ViewBag.SelectedRoleIds = (selectedRoleIds ?? Enumerable.Empty<int>()).ToHashSet();
        var countries = await _db.Countries.OrderBy(c => c.Name).Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();
        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");
        ViewBag.Roles = await _db.YdRoles.OrderBy(r => r.RoleName).ToListAsync();
    }
}
