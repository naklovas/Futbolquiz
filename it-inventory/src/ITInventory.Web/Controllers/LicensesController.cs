using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Entities;
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

    public LicensesController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int? countryId, int page = 1)
    {
        var query = _db.Licenses.Include(l => l.Country).AsQueryable();

        if (!_currentUser.IsAdmin)
            query = query.Where(l => l.CountryId == _currentUser.CountryId);
        else if (countryId.HasValue)
            query = query.Where(l => l.CountryId == countryId.Value);

        var items = await query.OrderBy(l => l.Country!.Name).ThenBy(l => l.LicenseName).ToPagedResultAsync(page);

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
            ExpirationDate = vm.ExpirationDate,
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
        entity.ExpirationDate = vm.ExpirationDate;
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

    public async Task<IActionResult> Import()
    {
        if (!_currentUser.CanEdit) return Forbid();
        ViewBag.EntityName = "Licenses";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.CanEdit) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Licenses",
            "License Name", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "License Expiration Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Licenses_Template.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file, int? countryId)
    {
        if (!_currentUser.CanEdit) return Forbid();

        if (file is null || file.Length == 0)
        {
            TempData["ImportError"] = "Please choose a file.";
            return RedirectToAction(nameof(Import));
        }

        var effectiveCountryId = _currentUser.IsAdmin ? countryId : _currentUser.CountryId;
        if (!effectiveCountryId.HasValue)
        {
            TempData["ImportError"] = "Please select a country.";
            return RedirectToAction(nameof(Import));
        }

        var country = await _db.Countries.FindAsync(effectiveCountryId.Value);
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
    }
}
