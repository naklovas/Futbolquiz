using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models.Admin;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminCountriesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly ICurrentUserService _currentUser;

    public AdminCountriesController(ITInventoryDbContext db, IActivityLogger activityLogger, ICurrentUserService currentUser)
    {
        _db = db;
        _activityLogger = activityLogger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        return View(countries);
    }

    [HttpGet]
    public IActionResult Create() => View(new CountryFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CountryFormViewModel vm)
    {
        if (await _db.Countries.AnyAsync(c => c.Name == vm.Name))
            ModelState.AddModelError(nameof(vm.Name), "A country with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        _db.Countries.Add(new Country
        {
            Name = vm.Name,
            DisplayName = vm.DisplayName,
            Code = vm.Code,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "Country", vm.DisplayName ?? vm.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var isAdmin = User.IsAdministrator();

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id && isAdmin);
        if (country is null) return NotFound();

        return View(new CountryFormViewModel
        {
            Id = country.Id,
            Name = country.Name,
            DisplayName = country.DisplayName,
            Code = country.Code,
            IsActive = country.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CountryFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();

        if (id != vm.Id) return BadRequest();

        if (await _db.Countries.AnyAsync(c => c.Name == vm.Name && c.Id != id))
            ModelState.AddModelError(nameof(vm.Name), "A country with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id && isAdmin);
        if (country is null) return NotFound();

        country.Name = vm.Name;
        country.DisplayName = vm.DisplayName;
        country.Code = vm.Code;
        country.IsActive = vm.IsActive;
        country.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Country", country.DisplayName ?? country.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.IsAdministrator();

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id && isAdmin);
        if (country is null) return NotFound();

        var countryLabel = country.DisplayName ?? country.Name;

        // Seven different tables carry a CountryId FK (Restrict) into Countries -- Physical
        // Devices/Servers/Licenses/Circuits are the obvious "inventory" ones, but Companies,
        // Applications, and Locations (branches) also reference it and are easy to forget to
        // check, so a generic "records are linked" message left the admin guessing which one.
        // Naming the exact table(s) here instead turns that into a one-look answer.
        var blockers = new List<string>();
        if (await _db.PhysicalDevices.AnyAsync(x => x.CountryId == id)) blockers.Add("Physical Devices");
        if (await _db.Servers.AnyAsync(x => x.CountryId == id)) blockers.Add("Servers");
        if (await _db.Licenses.AnyAsync(x => x.CountryId == id)) blockers.Add("Licenses");
        if (await _db.Circuits.AnyAsync(x => x.CountryId == id)) blockers.Add("Circuits");
        if (await _db.Companies.AnyAsync(x => x.CountryId == id)) blockers.Add("Companies");
        if (await _db.Applications.AnyAsync(x => x.CountryId == id)) blockers.Add("Applications");
        if (await _db.Locations.AnyAsync(x => x.CountryId == id)) blockers.Add("Locations (Branches)");

        if (blockers.Count > 0)
        {
            TempData["Error"] = $"Could not delete '{countryLabel}' because it still has records in: {string.Join(", ", blockers)}. Delete or move those first.";
            return RedirectToAction(nameof(Index));
        }

        _db.Countries.Remove(country);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "Country", countryLabel);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"Could not delete '{countryLabel}' because inventory records are linked to it. Delete or move the related records first.";
        }

        return RedirectToAction(nameof(Index));
    }
}
