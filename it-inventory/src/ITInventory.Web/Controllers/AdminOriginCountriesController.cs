using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Admin;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminOriginCountriesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly ICurrentUserService _currentUser;

    public AdminOriginCountriesController(ITInventoryDbContext db, IActivityLogger activityLogger, ICurrentUserService currentUser)
    {
        _db = db;
        _activityLogger = activityLogger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var originCountries = await _db.OriginCountries.OrderBy(c => c.Name).ToListAsync();
        return View(originCountries);
    }

    [HttpGet]
    public IActionResult Create() => View(new OriginCountryFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OriginCountryFormViewModel vm)
    {
        if (await _db.OriginCountries.AnyAsync(c => c.Name == vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "A country with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        _db.OriginCountries.Add(new OriginCountry
        {
            Name = vm.Name,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "OriginCountry", vm.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var originCountry = await _db.OriginCountries.FirstOrDefaultAsync(o => o.Id == id && _currentUser.IsAdmin);
        if (originCountry is null) return NotFound();

        return View(new OriginCountryFormViewModel
        {
            Id = originCountry.Id,
            Name = originCountry.Name,
            IsActive = originCountry.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OriginCountryFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        if (await _db.OriginCountries.AnyAsync(c => c.Name == vm.Name && c.Id != id))
            ModelState.AddModelError(nameof(vm.Name), "A country with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var originCountry = await _db.OriginCountries.FirstOrDefaultAsync(o => o.Id == id && _currentUser.IsAdmin);
        if (originCountry is null) return NotFound();

        originCountry.Name = vm.Name;
        originCountry.IsActive = vm.IsActive;
        originCountry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "OriginCountry", originCountry.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var originCountry = await _db.OriginCountries.FirstOrDefaultAsync(o => o.Id == id && _currentUser.IsAdmin);
        if (originCountry is null) return NotFound();

        var name = originCountry.Name;
        _db.OriginCountries.Remove(originCountry);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "OriginCountry", name);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Could not delete because companies are linked to this country. Unlink them first.";
        }

        return RedirectToAction(nameof(Index));
    }
}
