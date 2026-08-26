using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Circuits;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class CircuitsController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public CircuitsController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _activityLogger = activityLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? countryId, int page = 1)
    {
        var isAdmin = _currentUser.IsAdmin;
        var viewAll = isAdmin && countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        var requiresSelection = isAdmin && !viewAll && !selectedCountryId.HasValue;

        PagedResult<Circuit> items;
        if (requiresSelection)
        {
            items = new PagedResult<Circuit> { Items = Array.Empty<Circuit>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.Circuits.Include(c => c.Country).AsQueryable();

            if (!isAdmin)
                query = query.Where(c => c.CountryId == _currentUser.CountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(c => c.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(c => c.Country!.Name).ThenBy(c => c.CircuitType).ToPagedResultAsync(page);
        }

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = selectedCountryId;
        ViewBag.ViewAll = viewAll;
        ViewBag.RequiresSelection = requiresSelection;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = isAdmin;

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? countryId)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        var viewAll = countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        if (!viewAll && !selectedCountryId.HasValue) return BadRequest("Please select a country (or All Countries) first.");

        var query = _db.Circuits.Include(c => c.Country).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(c => c.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(c => c.Country!.Name).ThenBy(c => c.CircuitType).ToListAsync();

        var headers = new[]
        {
            "Country", "Circuit Type", "Capacity", "Provider", "Branch", "Location",
            "Start Date", "End Date", "Notes"
        };
        var rows = items.Select(c => new object?[]
        {
            c.Country?.DisplayName ?? c.Country?.Name,
            c.CircuitType,
            c.CircuitCapacity,
            c.Provider,
            c.Branch,
            c.Location,
            c.StartDate,
            c.EndDate,
            c.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Circuits", headers, rows);
        await _activityLogger.LogAsync("Export", "Circuit", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Circuits_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
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

        // The country arrives as a plain number from a form field. Checking that the number
        // exists is not enough: the id written into the new row has to COME FROM the row the
        // authorized lookup returned, never from the request. Otherwise the posted value still
        // travels straight into the INSERT and only an existence test stands in its way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0)
            && (_currentUser.IsAdmin || c.Id == _currentUser.CountryId));
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

        var entity = new Circuit
        {
            CountryId = country.Id,
            CircuitType = vm.CircuitType,
            CircuitCapacity = vm.CircuitCapacity,
            Provider = vm.Provider,
            Branch = vm.Branch,
            Location = vm.Location ?? string.Empty,
            StartDate = vm.StartDate,
            EndDate = vm.EndDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Circuits.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "Circuit", entity.CircuitType);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Circuits.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

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

        var entity = await _db.Circuits.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the ids written onto the row have to COME FROM the rows
        // the authorized lookups returned, never from the request. Otherwise the posted values
        // still travel straight into the UPDATE with only an existence test in the way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0)
            && (_currentUser.IsAdmin || c.Id == _currentUser.CountryId));
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

        entity.CountryId = country.Id;
        entity.CircuitType = vm.CircuitType;
        entity.CircuitCapacity = vm.CircuitCapacity;
        entity.Provider = vm.Provider;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location ?? string.Empty;
        entity.StartDate = vm.StartDate;
        entity.EndDate = vm.EndDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Circuit", entity.CircuitType);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Circuits.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var circuitType = entity.CircuitType;
        _db.Circuits.Remove(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Delete", "Circuit", circuitType);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        if (!_currentUser.IsAdmin) return Forbid();
        ViewBag.EntityName = "Circuits";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.IsAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Circuits",
            "Circuit Type", "Capacity", "Provider", "Branch", "Location",
            "Start Date", "End Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Circuits_Template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file, int? countryId)
    {
        if (!_currentUser.IsAdmin) return Forbid();

        if (!ExcelImportHelpers.IsValidUpload(file, out var uploadError))
        {
            TempData["ImportError"] = uploadError;
            return RedirectToAction(nameof(Import));
        }

        var effectiveCountryId = _currentUser.IsAdmin ? countryId : _currentUser.CountryId;
        if (!effectiveCountryId.HasValue)
        {
            TempData["ImportError"] = "Please select a country.";
            return RedirectToAction(nameof(Import));
        }

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == effectiveCountryId.Value
            && (_currentUser.IsAdmin || c.Id == _currentUser.CountryId));
        if (country is null)
        {
            TempData["ImportError"] = "Selected country not found.";
            return RedirectToAction(nameof(Import));
        }

        var result = new ImportResultViewModel { EntityName = "Circuits" };
        var toAdd = new List<Circuit>();

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

                var circuitType = ExcelImportHelpers.GetString(ws, row, headers, "Circuit Type");
                var location = ExcelImportHelpers.GetString(ws, row, headers, "Location");

                if (string.IsNullOrEmpty(circuitType))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Circuit Type is required." });
                    continue;
                }

                if (string.IsNullOrEmpty(location))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Location is required." });
                    continue;
                }

                toAdd.Add(new Circuit
                {
                    CountryId = country.Id,
                    CircuitType = circuitType,
                    CircuitCapacity = ExcelImportHelpers.GetString(ws, row, headers, "Capacity"),
                    Provider = ExcelImportHelpers.GetString(ws, row, headers, "Provider"),
                    Branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch"),
                    Location = location,
                    StartDate = ExcelImportHelpers.GetDate(ws, row, headers, "Start Date"),
                    EndDate = ExcelImportHelpers.GetDate(ws, row, headers, "End Date"),
                    Notes = ExcelImportHelpers.GetString(ws, row, headers, "Notes"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Circuits.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "Circuit", country.DisplayName ?? country.Name, $"{toAdd.Count} record(s) imported from Excel.");
        }

        result.SuccessCount = toAdd.Count;
        return View("ImportResult", result);
    }

    private async Task PopulateImportCountryInfo()
    {
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        if (_currentUser.IsAdmin)
        {
            var countries = await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();
            ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");
        }
        else
        {
            var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == _currentUser.CountryId);
            ViewBag.OwnCountryLabel = country?.DisplayName ?? country?.Name ?? _currentUser.Country;
        }
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        var countriesQuery = _currentUser.IsAdmin
            ? _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
            : _db.Countries.Where(c => c.Id == _currentUser.CountryId);
        var countries = await countriesQuery.Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();

        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");

        ViewBag.AllLocations = await _db.Locations.Where(l => l.IsActive)
            .Select(l => new { l.CountryId, l.Branch }).ToListAsync();
    }
}
