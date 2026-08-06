using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Licenses;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class LicensesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public LicensesController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int? countryId)
    {
        var query = _db.Licenses.Include(l => l.Country).AsQueryable();

        if (!_currentUser.IsAdmin)
            query = query.Where(l => l.CountryId == _currentUser.CountryId);
        else if (countryId.HasValue)
            query = query.Where(l => l.CountryId == countryId.Value);

        var items = await query.OrderBy(l => l.Country!.Name).ThenBy(l => l.LicenseName).ToListAsync();

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = countryId;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        return View(items);
    }

    public async Task<IActionResult> Create()
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new LicenseFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? 0 : _currentUser.CountryId ?? 0
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LicenseFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new License
        {
            CountryId = vm.CountryId,
            LicenseName = vm.LicenseName,
            VendorSupplier = vm.VendorSupplier,
            Branch = vm.Branch,
            Location = vm.Location,
            SupportStartDate = vm.SupportStartDate,
            SupportEndDate = vm.SupportEndDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Licenses.Add(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        var vm = new LicenseFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            LicenseName = entity.LicenseName,
            VendorSupplier = entity.VendorSupplier,
            Branch = entity.Branch,
            Location = entity.Location,
            SupportStartDate = entity.SupportStartDate,
            SupportEndDate = entity.SupportEndDate,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LicenseFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Licenses.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = vm.CountryId;
        entity.LicenseName = vm.LicenseName;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
        entity.SupportStartDate = vm.SupportStartDate;
        entity.SupportEndDate = vm.SupportEndDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        _db.Licenses.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        var countriesQuery = _currentUser.IsAdmin
            ? _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
            : _db.Countries.Where(c => c.Id == _currentUser.CountryId);
        var countries = await countriesQuery.Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();

        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");
    }
}
