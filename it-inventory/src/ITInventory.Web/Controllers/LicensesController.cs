using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Models.Licenses;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class LicensesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public LicensesController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
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

        PagedResult<License> items;
        if (requiresSelection)
        {
            items = new PagedResult<License> { Items = Array.Empty<License>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.Licenses.Include(l => l.Country).Include(l => l.Company).AsQueryable();

            if (!isAdmin)
                query = query.Where(l => l.CountryId == _currentUser.CountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(l => l.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(l => l.Country!.Name).ThenBy(l => l.LicenseName).ToPagedResultAsync(page);
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

        var query = _db.Licenses.Include(l => l.Country).Include(l => l.Company).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(l => l.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(l => l.Country!.Name).ThenBy(l => l.LicenseName).ToListAsync();

        var headers = new[]
        {
            "Country", "License Name", "Company", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "License Expiration Date", "Notes"
        };
        var rows = items.Select(l => new object?[]
        {
            l.Country?.DisplayName ?? l.Country?.Name,
            l.LicenseName,
            l.Company?.Name,
            l.VendorSupplier,
            l.Branch,
            l.Location,
            l.SupportStartDate,
            l.SupportEndDate,
            l.ExpirationDate,
            l.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Licenses", headers, rows);
        await _activityLogger.LogAsync("Export", "License", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Licenses_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
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

        // Every foreign key below arrives as a plain number from a form field, so it has to be
        // checked against what the dropdown was actually allowed to offer -- otherwise a posted
        // id could point at another country's row.
        if (vm.CountryId.HasValue &&
            !await _db.Countries.AnyAsync(c => c.Id == vm.CountryId.Value
                && (_currentUser.IsAdmin || c.Id == _currentUser.CountryId)))
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");

        if (vm.CompanyId.HasValue &&
            !await _db.Companies.AnyAsync(c => c.Id == vm.CompanyId.Value && c.IsActive
                && (_currentUser.IsAdmin || c.CountryId == _currentUser.CountryId)))
            ModelState.AddModelError(nameof(vm.CompanyId), "Please select a valid company.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new License
        {
            CountryId = vm.CountryId!.Value,
            LicenseName = vm.LicenseName,
            VendorSupplier = vm.VendorSupplier,
            CompanyId = vm.CompanyId,
            Branch = vm.Branch,
            Location = vm.Location ?? string.Empty,
            SupportStartDate = vm.SupportStartDate,
            SupportEndDate = vm.SupportEndDate,
            ExpirationDate = vm.ExpirationDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Licenses.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "License", entity.LicenseName);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var vm = new LicenseFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            LicenseName = entity.LicenseName,
            VendorSupplier = entity.VendorSupplier,
            CompanyId = entity.CompanyId,
            Branch = entity.Branch,
            Location = entity.Location,
            SupportStartDate = entity.SupportStartDate,
            SupportEndDate = entity.SupportEndDate,
            ExpirationDate = entity.ExpirationDate,
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

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        // Every foreign key below arrives as a plain number from a form field, so it has to be
        // checked against what the dropdown was actually allowed to offer -- otherwise a posted
        // id could point at another country's row.
        if (vm.CountryId.HasValue &&
            !await _db.Countries.AnyAsync(c => c.Id == vm.CountryId.Value
                && (_currentUser.IsAdmin || c.Id == _currentUser.CountryId)))
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");

        if (vm.CompanyId.HasValue &&
            !await _db.Companies.AnyAsync(c => c.Id == vm.CompanyId.Value && c.IsActive
                && (_currentUser.IsAdmin || c.CountryId == _currentUser.CountryId)))
            ModelState.AddModelError(nameof(vm.CompanyId), "Please select a valid company.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = vm.CountryId!.Value;
        entity.LicenseName = vm.LicenseName;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.CompanyId = vm.CompanyId;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location ?? string.Empty;
        entity.SupportStartDate = vm.SupportStartDate;
        entity.SupportEndDate = vm.SupportEndDate;
        entity.ExpirationDate = vm.ExpirationDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "License", entity.LicenseName);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var licenseName = entity.LicenseName;
        _db.Licenses.Remove(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Delete", "License", licenseName);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        if (!_currentUser.IsAdmin) return Forbid();
        ViewBag.EntityName = "Licenses";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.IsAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Licenses",
            "License Name", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "License Expiration Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Licenses_Template.xlsx");
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

        var result = new ImportResultViewModel { EntityName = "Licenses" };
        var toAdd = new List<License>();

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

                var licenseName = ExcelImportHelpers.GetString(ws, row, headers, "License Name");
                var location = ExcelImportHelpers.GetString(ws, row, headers, "Location");

                if (string.IsNullOrEmpty(licenseName))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "License Name is required." });
                    continue;
                }

                if (string.IsNullOrEmpty(location))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Location is required." });
                    continue;
                }

                toAdd.Add(new License
                {
                    CountryId = country.Id,
                    LicenseName = licenseName,
                    VendorSupplier = ExcelImportHelpers.GetString(ws, row, headers, "Vendor/Supplier"),
                    Branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch"),
                    Location = location,
                    SupportStartDate = ExcelImportHelpers.GetDate(ws, row, headers, "Support Start Date"),
                    SupportEndDate = ExcelImportHelpers.GetDate(ws, row, headers, "Support End Date"),
                    ExpirationDate = ExcelImportHelpers.GetDate(ws, row, headers, "License Expiration Date"),
                    Notes = ExcelImportHelpers.GetString(ws, row, headers, "Notes"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Licenses.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "License", country.DisplayName ?? country.Name, $"{toAdd.Count} record(s) imported from Excel.");
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

        var effectiveCountryId = _currentUser.IsAdmin ? (int?)null : _currentUser.CountryId;
        var companiesQuery = _db.Companies.Where(c => c.IsActive);
        if (effectiveCountryId.HasValue)
            companiesQuery = companiesQuery.Where(c => c.CountryId == effectiveCountryId.Value);
        var companies = await companiesQuery.OrderBy(c => c.Name).ToListAsync();
        ViewBag.CompanyOptions = new SelectList(companies, "Id", "Name");

        ViewBag.AllLocations = await _db.Locations.Where(l => l.IsActive)
            .Select(l => new { l.CountryId, l.Branch }).ToListAsync();

        ViewBag.VendorOptions = new SelectList(companies.Select(c => c.Name));
    }
}
