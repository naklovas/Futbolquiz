using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

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
                query = query.Where(l => l.CountryId == scopedCountryId);
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
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var vm = new LicenseFormViewModel
        {
            CountryId = isAdmin ? 0 : scopedCountryId ?? 0
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LicenseFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        if (!isAdmin)
            vm.CountryId = scopedCountryId ?? 0;

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the ids written into the new row have to COME FROM the
        // rows the authorized lookups returned, never from the request. Otherwise the posted
        // values still travel straight into the INSERT with only an existence test in the way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0)
            && (isAdmin || c.Id == scopedCountryId));
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }

        Company? company = null;
        if (vm.CompanyId.HasValue)
        {
            company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == vm.CompanyId.Value && c.IsActive
                && (isAdmin || c.CountryId == scopedCountryId));
            if (company is null)
            {
                ModelState.AddModelError(nameof(vm.CompanyId), "Please select a valid company.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new License
        {
            CountryId = country.Id,
            LicenseName = vm.LicenseName,
            VendorSupplier = vm.VendorSupplier,
            CompanyId = company?.Id,
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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        if (!isAdmin)
            vm.CountryId = scopedCountryId ?? 0;

        // Every foreign key below arrives as a plain number from a form field. Checking that the
        // number exists is not enough: the ids written onto the row have to COME FROM the rows
        // the authorized lookups returned, never from the request. Otherwise the posted values
        // still travel straight into the UPDATE with only an existence test in the way.
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == (vm.CountryId ?? 0)
            && (isAdmin || c.Id == scopedCountryId));
        if (country is null)
        {
            ModelState.AddModelError(nameof(vm.CountryId), "Please select a valid country.");
            await PopulateDropdowns();
            return View(vm);
        }

        Company? company = null;
        if (vm.CompanyId.HasValue)
        {
            company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == vm.CompanyId.Value && c.IsActive
                && (isAdmin || c.CountryId == scopedCountryId));
            if (company is null)
            {
                ModelState.AddModelError(nameof(vm.CompanyId), "Please select a valid company.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = country.Id;
        entity.LicenseName = vm.LicenseName;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.CompanyId = company?.Id;
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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Licenses.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
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
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();
        ViewBag.EntityName = "Licenses";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Licenses",
            "License Name", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "License Expiration Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Licenses_Template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file, int? countryId)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!isAdmin) return Forbid();

        if (!ExcelImportHelpers.IsValidUpload(file, out var uploadError))
        {
            TempData["ImportError"] = uploadError;
            return RedirectToAction(nameof(Import));
        }

        var effectiveCountryId = isAdmin ? countryId : scopedCountryId;
        if (!effectiveCountryId.HasValue)
        {
            TempData["ImportError"] = "Please select a country.";
            return RedirectToAction(nameof(Import));
        }

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == effectiveCountryId.Value
            && (isAdmin || c.Id == scopedCountryId));
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
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();
        var scopedRepository = User.ScopedRepositoryName();

        ViewBag.IsAdmin = isAdmin;

        if (isAdmin)
        {
            var countries = await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();
            ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");
        }
        else
        {
            var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == scopedCountryId);
            ViewBag.OwnCountryLabel = country?.DisplayName ?? country?.Name ?? scopedRepository;
        }
    }

    private async Task PopulateDropdowns()
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        ViewBag.IsAdmin = isAdmin;

        var countriesQuery = isAdmin
            ? _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name)
            : _db.Countries.Where(c => c.Id == scopedCountryId);
        var countries = await countriesQuery.Select(c => new { c.Id, Label = c.DisplayName ?? c.Name }).ToListAsync();

        ViewBag.CountryOptions = new SelectList(countries, "Id", "Label");

        var effectiveCountryId = isAdmin ? (int?)null : scopedCountryId;
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
