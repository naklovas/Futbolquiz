using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models.Admin;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminLocationsController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly ICurrentUserService _currentUser;

    public AdminLocationsController(ITInventoryDbContext db, IActivityLogger activityLogger, ICurrentUserService currentUser)
    {
        _db = db;
        _activityLogger = activityLogger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var locations = await _db.Locations.Include(l => l.Country)
            .OrderBy(l => l.Country!.Name).ThenBy(l => l.Branch)
            .ToListAsync();
        return View(locations);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View(new LocationFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LocationFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();

        if (await _db.Locations.AnyAsync(l => l.CountryId == vm.CountryId && l.Branch == vm.Branch))
            ModelState.AddModelError(nameof(vm.Branch), "This branch already exists for the selected country.");

        // The country arrives as a plain number from a form field. Checking that the number
        // exists is not enough: the id written into the new row has to COME FROM the row the
        // authorized lookup returned, never from the request. Otherwise the posted value still
        // travels straight into the INSERT with only an existence test in the way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0) && isAdmin);
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        _db.Locations.Add(new Location
        {
            CountryId = country.Id,
            Branch = vm.Branch,
            Class = vm.Class,
            IsActive = vm.IsActive,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "Location", vm.Branch);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var isAdmin = User.IsAdministrator();

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id && isAdmin);
        if (location is null) return NotFound();

        await PopulateDropdowns();
        return View(new LocationFormViewModel
        {
            Id = location.Id,
            CountryId = location.CountryId,
            Branch = location.Branch,
            Class = location.Class,
            IsActive = location.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, LocationFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();

        if (id != vm.Id) return BadRequest();

        if (await _db.Locations.AnyAsync(l => l.CountryId == vm.CountryId && l.Branch == vm.Branch && l.Id != id))
            ModelState.AddModelError(nameof(vm.Branch), "This branch already exists for the selected country.");

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the ids written onto the row have to COME FROM the rows
        // the authorized lookups returned, never from the request. Otherwise the posted values
        // still travel straight into the UPDATE with only an existence test in the way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0) && isAdmin);
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id && isAdmin);
        if (location is null) return NotFound();

        location.CountryId = country.Id;
        location.Branch = vm.Branch;
        location.Class = vm.Class;
        location.IsActive = vm.IsActive;
        location.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Location", location.Branch);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.IsAdministrator();

        var location = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id && isAdmin);
        if (location is null) return NotFound();

        var branch = location.Branch;
        _db.Locations.Remove(location);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "Location", branch);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Could not delete this location.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Import()
    {
        return View();
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var bytes = ExcelImportHelpers.CreateTemplateBytes("Locations", "Country", "Branch", "Class");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Locations_Template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (!ExcelImportHelpers.IsValidUpload(file, out var uploadError))
        {
            TempData["ImportError"] = uploadError;
            return RedirectToAction(nameof(Import));
        }

        var countries = await _db.Countries.ToListAsync();
        var countriesByLabel = countries
            .GroupBy(c => (c.DisplayName ?? c.Name).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var countriesByName = countries
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var existingKeys = (await _db.Locations.Select(l => new { l.CountryId, l.Branch }).ToListAsync())
            .Select(l => (l.CountryId, l.Branch))
            .ToHashSet();

        var result = new ImportResultViewModel { EntityName = "Locations" };
        var toAdd = new List<Location>();

        using (var stream = new MemoryStream())
        {
            await file.CopyToAsync(stream);
            stream.Position = 0;
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheets.First();
            var headers = ExcelImportHelpers.ReadHeaders(ws);
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            for (var row = 2; row <= lastRow; row++)
            {
                if (ExcelImportHelpers.IsRowEmpty(ws, row, headers)) continue;

                var countryText = ExcelImportHelpers.GetString(ws, row, headers, "Country")?.Trim();
                var branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch")?.Trim();
                var cls = ExcelImportHelpers.GetString(ws, row, headers, "Class")?.Trim();

                if (string.IsNullOrEmpty(countryText))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Country is required." });
                    continue;
                }

                if (string.IsNullOrEmpty(branch))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Branch is required." });
                    continue;
                }

                if (!countriesByLabel.TryGetValue(countryText, out var country) &&
                    !countriesByName.TryGetValue(countryText, out country))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = $"Country not found: \"{countryText}\". Add it under Admin > Countries first." });
                    continue;
                }

                if (!existingKeys.Add((country.Id, branch)))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = $"\"{branch}\" already exists for {countryText} -- skipped." });
                    continue;
                }

                toAdd.Add(new Location
                {
                    CountryId = country.Id,
                    Branch = branch,
                    Class = string.IsNullOrEmpty(cls) ? "Yurtdışı Şube" : cls,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Locations.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "Location", details: $"{toAdd.Count} record(s) imported from Excel.");
        }

        result.SuccessCount = toAdd.Count;
        return View("~/Views/Shared/ImportResult.cshtml", result);
    }

    private async Task PopulateDropdowns()
    {
        var countries = await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
            .Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();
        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");
    }
}
