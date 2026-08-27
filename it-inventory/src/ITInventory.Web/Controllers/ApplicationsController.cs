using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Applications;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class ApplicationsController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public ApplicationsController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
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

        PagedResult<Application> items;
        if (requiresSelection)
        {
            items = new PagedResult<Application> { Items = Array.Empty<Application>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.Applications
                .Include(a => a.Country)
                .Include(a => a.Company)
                .Include(a => a.License)
                .AsQueryable();

            if (!isAdmin)
                query = query.Where(a => a.CountryId == scopedCountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(a => a.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(a => a.Country!.Name).ThenBy(a => a.Name).ToPagedResultAsync(page);
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

        var query = _db.Applications.Include(a => a.Country).Include(a => a.Company).Include(a => a.License).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(a => a.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(a => a.Country!.Name).ThenBy(a => a.Name).ToListAsync();

        var headers = new[]
        {
            "Country", "Application Name", "Company", "License", "Application Type",
            "Externally Exposed?", "URL", "Cloud Application?", "Notes"
        };
        var rows = items.Select(a => new object?[]
        {
            a.Country?.DisplayName ?? a.Country?.Name,
            a.Name,
            a.Company?.Name,
            a.License?.LicenseName,
            a.ApplicationType.ToString(),
            a.IsExternallyExposed ? "Yes" : "No",
            a.Url,
            a.IsCloudApplication ? "Yes" : "No",
            a.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Applications", headers, rows);
        await _activityLogger.LogAsync("Export", "Application", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Applications_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var vm = new ApplicationFormViewModel
        {
            CountryId = isAdmin ? 0 : scopedCountryId ?? 0
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApplicationFormViewModel vm)
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

        License? license = null;
        if (vm.LicenseId.HasValue)
        {
            license = await _db.Licenses.FirstOrDefaultAsync(l => l.Id == vm.LicenseId.Value
                && (isAdmin || l.CountryId == scopedCountryId));
            if (license is null)
            {
                ModelState.AddModelError(nameof(vm.LicenseId), "Please select a valid license.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new Application
        {
            CountryId = country.Id,
            Name = vm.Name,
            CompanyId = company?.Id,
            LicenseId = license?.Id,
            ApplicationType = vm.ApplicationType!.Value,
            IsExternallyExposed = vm.IsExternallyExposed,
            Url = vm.Url,
            IsCloudApplication = vm.IsCloudApplication,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Applications.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "Application", entity.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Applications.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        var vm = new ApplicationFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            Name = entity.Name,
            CompanyId = entity.CompanyId,
            LicenseId = entity.LicenseId,
            ApplicationType = entity.ApplicationType,
            IsExternallyExposed = entity.IsExternallyExposed,
            Url = entity.Url,
            IsCloudApplication = entity.IsCloudApplication,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ApplicationFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Applications.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
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

        License? license = null;
        if (vm.LicenseId.HasValue)
        {
            license = await _db.Licenses.FirstOrDefaultAsync(l => l.Id == vm.LicenseId.Value
                && (isAdmin || l.CountryId == scopedCountryId));
            if (license is null)
            {
                ModelState.AddModelError(nameof(vm.LicenseId), "Please select a valid license.");
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
        entity.Name = vm.Name;
        entity.CompanyId = company?.Id;
        entity.LicenseId = license?.Id;
        entity.ApplicationType = vm.ApplicationType!.Value;
        entity.IsExternallyExposed = vm.IsExternallyExposed;
        entity.Url = vm.Url;
        entity.IsCloudApplication = vm.IsCloudApplication;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Application", entity.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Applications.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        var appName = entity.Name;
        _db.Applications.Remove(entity);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "Application", appName);
        }
        catch (DbUpdateException)
        {
            TempData["ImportError"] = "Could not delete because servers are linked to this application. Unlink them first.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();
        ViewBag.EntityName = "Applications";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Applications",
            "Application Name", "Company", "License", "Application Type",
            "Externally Exposed?", "URL", "Cloud Application?");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Applications_Template.xlsx");
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

        var companies = await _db.Companies.ToListAsync();
        var companyLookup = companies.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var licenses = await _db.Licenses.Where(l => l.CountryId == country.Id).ToListAsync();
        var licenseLookup = licenses.ToDictionary(l => l.LicenseName, l => l, StringComparer.OrdinalIgnoreCase);

        var result = new ImportResultViewModel { EntityName = "Applications" };
        var toAdd = new List<Application>();

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

                var name = ExcelImportHelpers.GetString(ws, row, headers, "Application Name");
                if (string.IsNullOrEmpty(name))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Application Name is required." });
                    continue;
                }

                var companyName = ExcelImportHelpers.GetString(ws, row, headers, "Company");
                Company? company = null;
                if (!string.IsNullOrEmpty(companyName) && !companyLookup.TryGetValue(companyName, out company))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = $"Company '{companyName}' not found." });
                    continue;
                }

                var licenseName = ExcelImportHelpers.GetString(ws, row, headers, "License");
                License? license = null;
                if (!string.IsNullOrEmpty(licenseName) && !licenseLookup.TryGetValue(licenseName, out license))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = $"License '{licenseName}' not found for this country." });
                    continue;
                }

                var typeRaw = ExcelImportHelpers.GetString(ws, row, headers, "Application Type");
                if (!TryParseApplicationType(typeRaw, out var appType, out var typeError))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = typeError! });
                    continue;
                }

                toAdd.Add(new Application
                {
                    CountryId = country.Id,
                    Name = name,
                    CompanyId = company?.Id,
                    LicenseId = license?.Id,
                    ApplicationType = appType,
                    IsExternallyExposed = ParseYesNo(ExcelImportHelpers.GetString(ws, row, headers, "Externally Exposed?")),
                    Url = ExcelImportHelpers.GetString(ws, row, headers, "URL"),
                    IsCloudApplication = ParseYesNo(ExcelImportHelpers.GetString(ws, row, headers, "Cloud Application?")),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.Applications.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "Application", country.DisplayName ?? country.Name, $"{toAdd.Count} record(s) imported from Excel.");
        }

        result.SuccessCount = toAdd.Count;
        return View("ImportResult", result);
    }

    private static bool TryParseApplicationType(string? raw, out ApplicationType value, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("Web", StringComparison.OrdinalIgnoreCase))
        {
            value = ApplicationType.Web;
            return true;
        }
        if (raw.Trim().Equals("Mobile", StringComparison.OrdinalIgnoreCase))
        {
            value = ApplicationType.Mobile;
            return true;
        }
        if (raw.Trim().Equals("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            value = ApplicationType.Desktop;
            return true;
        }

        value = ApplicationType.Web;
        error = $"Unrecognized Application Type '{raw}' (expected 'Mobile', 'Web' or 'Desktop').";
        return false;
    }

    private static bool ParseYesNo(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) && raw.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);

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

        var licensesQuery = _db.Licenses.AsQueryable();
        if (effectiveCountryId.HasValue)
            licensesQuery = licensesQuery.Where(l => l.CountryId == effectiveCountryId.Value);
        var licenses = await licensesQuery.OrderBy(l => l.LicenseName).ToListAsync();
        ViewBag.LicenseOptions = new SelectList(licenses, "Id", "LicenseName");
    }
}
