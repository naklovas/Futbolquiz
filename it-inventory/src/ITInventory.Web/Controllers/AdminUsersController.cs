using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminUsersController : Controller
{
    private readonly ITInventoryDbContext _db;

    public AdminUsersController(ITInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _db.YdUsers
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return View(users);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new UserFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormViewModel vm)
    {
        if (await _db.YdUsers.AnyAsync(u => u.Username == vm.Username))
            ModelState.AddModelError(nameof(vm.Username), "Bu kullanıcı adı zaten kayıtlı.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var country = vm.CountryId.HasValue ? await _db.Countries.FindAsync(vm.CountryId.Value) : null;

        var user = new YdUser
        {
            Username = vm.Username,
            FullName = vm.FullName,
            Email = vm.Email,
            RepositoryName = country?.Name,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var roleId in vm.SelectedRoleIds)
            user.UserRoles.Add(new YdUserRole { RoleId = roleId });

        _db.YdUsers.Add(user);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id);
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
            SelectedRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToList()
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UserFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        if (await _db.YdUsers.AnyAsync(u => u.Username == vm.Username && u.Id != id))
            ModelState.AddModelError(nameof(vm.Username), "Bu kullanıcı adı zaten kayıtlı.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var user = await _db.YdUsers
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        var country = vm.CountryId.HasValue ? await _db.Countries.FindAsync(vm.CountryId.Value) : null;

        user.Username = vm.Username;
        user.FullName = vm.FullName;
        user.Email = vm.Email;
        user.RepositoryName = country?.Name;
        user.IsActive = vm.IsActive;

        user.UserRoles.Clear();
        foreach (var roleId in vm.SelectedRoleIds)
            user.UserRoles.Add(new YdUserRole { UserId = user.Id, RoleId = roleId });

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.YdUsers.FindAsync(id);
        if (user is null) return NotFound();

        _db.YdUsers.Remove(user);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.CountryOptions = new SelectList(await _db.Countries.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewBag.Roles = await _db.YdRoles.OrderBy(r => r.RoleName).ToListAsync();
    }
}
