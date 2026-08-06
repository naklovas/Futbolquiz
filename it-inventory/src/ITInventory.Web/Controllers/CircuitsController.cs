using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models.Circuits;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class CircuitsController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public CircuitsController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int? countryId)
    {
        var query = _db.Circuits.Include(c => c.Country).AsQueryable();

        if (!_currentUser.IsAdmin)
            query = query.Where(c => c.CountryId == _currentUser.CountryId);
        else if (countryId.HasValue)
            query = query.Where(c => c.CountryId == countryId.Value);

        var items = await query.OrderBy(c => c.Country!.Name).ThenBy(c => c.CircuitType).ToListAsync();

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = countryId;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        return View(items);
    }

    public async Task<IActionResult> Create()
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new CircuitFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? 0 : _currentUser.CountryId ?? 0
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CircuitFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new Circuit
        {
            CountryId = vm.CountryId,
            CircuitType = vm.CircuitType,
            CircuitCapacity = vm.CircuitCapacity,
            Provider = vm.Provider,
            Branch = vm.Branch,
            Location = vm.Location,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Circuits.Add(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Circuits.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        var vm = new CircuitFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            CircuitType = entity.CircuitType,
            CircuitCapacity = entity.CircuitCapacity,
            Provider = entity.Provider,
            Branch = entity.Branch,
            Location = entity.Location,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CircuitFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Circuits.FindAsync(id);
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
        entity.CircuitType = vm.CircuitType;
        entity.CircuitCapacity = vm.CircuitCapacity;
        entity.Provider = vm.Provider;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
        entity.StartDate = vm.StartDate;
        entity.EndDate = vm.EndDate;
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

        var entity = await _db.Circuits.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        _db.Circuits.Remove(entity);
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
