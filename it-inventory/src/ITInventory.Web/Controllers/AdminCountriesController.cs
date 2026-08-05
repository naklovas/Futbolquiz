using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminCountriesController : Controller
{
    private readonly ITInventoryDbContext _db;

    public AdminCountriesController(ITInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        return View(countries);
    }

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
            Code = vm.Code,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var country = await _db.Countries.FindAsync(id);
        if (country is null) return NotFound();

        return View(new CountryFormViewModel
        {
            Id = country.Id,
            Name = country.Name,
            Code = country.Code,
            IsActive = country.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CountryFormViewModel vm)
    {
        if (id != vm.Id) return BadRequest();

        if (await _db.Countries.AnyAsync(c => c.Name == vm.Name && c.Id != id))
            ModelState.AddModelError(nameof(vm.Name), "A country with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var country = await _db.Countries.FindAsync(id);
        if (country is null) return NotFound();

        country.Name = vm.Name;
        country.Code = vm.Code;
        country.IsActive = vm.IsActive;
        country.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var country = await _db.Countries.FindAsync(id);
        if (country is null) return NotFound();

        _db.Countries.Remove(country);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Could not delete because inventory records are linked to this country. Delete or move the related records first.";
        }

        return RedirectToAction(nameof(Index));
    }
}
