using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Models.PhysicalDevices;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class PhysicalDevicesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public PhysicalDevicesController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
    {
        _db = db;
        _currentUser = currentUser;
        _activityLogger = activityLogger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? countryId, int? categoryId, int page = 1)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        var viewAll = isAdmin && countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        var requiresSelection = isAdmin && !viewAll && !selectedCountryId.HasValue;

        PagedResult<PhysicalDevice> items;
        if (requiresSelection)
        {
            items = new PagedResult<PhysicalDevice> { Items = Array.Empty<PhysicalDevice>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.PhysicalDevices
                .Include(d => d.Country)
                .Include(d => d.Category)
                .AsQueryable();

            if (!isAdmin)
                query = query.Where(d => d.CountryId == scopedCountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(d => d.CountryId == selectedCountryId.Value);

            if (categoryId.HasValue)
                query = query.Where(d => d.CategoryId == categoryId.Value);

            items = await query
                .OrderBy(d => d.Country!.Name).ThenBy(d => d.DeviceName)
                .ToPagedResultAsync(page);
        }

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Categories = await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = selectedCountryId;
        ViewBag.ViewAll = viewAll;
        ViewBag.RequiresSelection = requiresSelection;
        ViewBag.SelectedCategoryId = categoryId;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.IsAdmin = isAdmin;

        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? countryId, int? categoryId)
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

        var viewAll = countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;
        if (!viewAll && !selectedCountryId.HasValue) return BadRequest("Please select a country (or All Countries) first.");

        var query = _db.PhysicalDevices.Include(d => d.Country).Include(d => d.Category).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(d => d.CountryId == selectedCountryId.Value);
        if (categoryId.HasValue)
            query = query.Where(d => d.CategoryId == categoryId.Value);

        var items = await query.OrderBy(d => d.Country!.Name).ThenBy(d => d.DeviceName).ToListAsync();

        var headers = new[]
        {
            "Country", "Category", "Device Name", "Brand", "Model", "Physical/Virtual",
            "Location Category", "Site Role", "Software Version", "Serial Number", "IP Address", "Management IP", "Branch",
            "Location", "Vendor/Supplier", "License Info", "Support Start Date", "Support End Date",
            "End of Life Date", "Notes"
        };
        var rows = items.Select(d => new object?[]
        {
            d.Country?.DisplayName ?? d.Country?.Name,
            d.Category?.Name,
            d.DeviceName,
            d.Brand,
            d.Model,
            d.ApplianceType.ToString(),
            d.LocationCategory.ToString(),
            d.SiteRole.ToString(),
            d.SoftwareVersion,
            d.SerialNo,
            d.IpAddress,
            d.MgmtIp,
            d.Branch,
            d.Location,
            d.VendorSupplier,
            d.LicenceInfo,
            d.StartOfSupportDate,
            d.EndOfSupportDate,
            d.EndOfLifeDate,
            d.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Physical Devices", headers, rows);
        await _activityLogger.LogAsync("Export", "PhysicalDevice", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"PhysicalDevices_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Create(bool fromPool = false, int? sourceId = null, int? countryId = null, int? categoryId = null)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();
        var scopedRepository = User.ScopedRepositoryName();

        if (!_currentUser.CanEdit) return Forbid();

        var vm = new PhysicalDeviceFormViewModel
        {
            CountryId = isAdmin ? countryId ?? 0 : scopedCountryId ?? 0,
            CategoryId = categoryId ?? 0
        };

        if (fromPool && sourceId.HasValue)
        {
            // The scope is in the query rather than in a check after it, so a pool row outside
            // the caller's country is never fetched at all, and it comes off the authenticated
            // principal's claims (see PrincipalScope) so the restriction is visible right here
            // instead of behind a DI-resolved interface. An administrator reaches every
            // country's pool by design -- that branch has nothing to narrow, which is why a
            // scanner tracing sourceId to this query still reports it. See the audit note in
            // docs/fortify-access-control.md.
            var source = await _db.ZiraatYds.FirstOrDefaultAsync(z => z.Id == sourceId.Value
                && (isAdmin || z.RepositoryName == scopedRepository));
            if (source is not null)
            {
                vm.SourceZiraatYdId = source.Id;
                vm.DeviceName = source.DnsName ?? source.NetbiosName ?? source.IpAddress;
                vm.IpAddress = source.IpAddress;
                vm.SoftwareVersion = source.OperatingSystem;

                var profile = await _db.DeviceProfileCatalogs
                    .FirstOrDefaultAsync(p => p.ProfileName == source.DeviceProfile);
                if (profile is not null)
                {
                    vm.DeviceProfileId = profile.Id;
                    if (vm.CategoryId == 0 && profile.CategoryId.HasValue)
                        vm.CategoryId = profile.CategoryId.Value;
                }
            }
        }

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PhysicalDeviceFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();
        var scopedRepository = User.ScopedRepositoryName();

        if (!_currentUser.CanEdit) return Forbid();

        if (!isAdmin)
            vm.CountryId = scopedCountryId ?? 0;

        vm.ApplianceType = ApplianceType.Physical;

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

        var category = await _db.DeviceCategories.FirstOrDefaultAsync(c => c.Id == (vm.CategoryId ?? 0));
        if (category is null)
        {
            ModelState.AddModelError(nameof(vm.CategoryId), "Please select a valid category.");
            await PopulateDropdowns();
            return View(vm);
        }

        DeviceProfileCatalog? deviceProfile = null;
        if (vm.DeviceProfileId.HasValue)
        {
            deviceProfile = await _db.DeviceProfileCatalogs.FirstOrDefaultAsync(p => p.Id == vm.DeviceProfileId.Value);
            if (deviceProfile is null)
            {
                ModelState.AddModelError(nameof(vm.DeviceProfileId), "Please select a valid device profile.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        ZiraatYd? poolRecord = null;
        if (vm.SourceZiraatYdId.HasValue)
        {
            poolRecord = await _db.ZiraatYds.FirstOrDefaultAsync(z => z.Id == vm.SourceZiraatYdId.Value
                && (isAdmin || z.RepositoryName == scopedRepository));
            if (poolRecord is null)
            {
                ModelState.AddModelError(string.Empty, "The selected device pool record is not valid.");
                await PopulateDropdowns();
                return View(vm);
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new PhysicalDevice
        {
            CountryId = country.Id,
            CategoryId = category.Id,
            DeviceProfileId = deviceProfile?.Id,
            SourceZiraatYdId = poolRecord?.Id,
            DeviceName = vm.DeviceName,
            Brand = vm.Brand,
            Model = vm.Model,
            ApplianceType = vm.ApplianceType,
            LocationCategory = vm.LocationCategory!.Value,
            SiteRole = vm.SiteClassification!.Value,
            SoftwareVersion = vm.SoftwareVersion,
            SerialNo = vm.SerialNo,
            IpAddress = vm.IpAddress,
            MgmtIp = vm.MgmtIp,
            Branch = vm.Branch,
            Location = vm.Location ?? string.Empty,
            VendorSupplier = vm.VendorSupplier,
            LicenceInfo = vm.LicenceInfo,
            StartOfSupportDate = vm.StartOfSupportDate,
            EndOfSupportDate = vm.EndOfSupportDate,
            EndOfLifeDate = vm.EndOfLifeDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.PhysicalDevices.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "PhysicalDevice", entity.DeviceName);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.PhysicalDevices.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        var vm = new PhysicalDeviceFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            CategoryId = entity.CategoryId,
            DeviceProfileId = entity.DeviceProfileId,
            SourceZiraatYdId = entity.SourceZiraatYdId,
            DeviceName = entity.DeviceName,
            Brand = entity.Brand,
            Model = entity.Model,
            ApplianceType = entity.ApplianceType,
            LocationCategory = entity.LocationCategory,
            SiteClassification = entity.SiteRole,
            SoftwareVersion = entity.SoftwareVersion,
            SerialNo = entity.SerialNo,
            IpAddress = entity.IpAddress,
            MgmtIp = entity.MgmtIp,
            Branch = entity.Branch,
            Location = entity.Location,
            VendorSupplier = entity.VendorSupplier,
            LicenceInfo = entity.LicenceInfo,
            StartOfSupportDate = entity.StartOfSupportDate,
            EndOfSupportDate = entity.EndOfSupportDate,
            EndOfLifeDate = entity.EndOfLifeDate,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PhysicalDeviceFormViewModel vm)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.PhysicalDevices.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        if (!isAdmin)
            vm.CountryId = scopedCountryId ?? 0;

        vm.ApplianceType = ApplianceType.Physical;

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

        var category = await _db.DeviceCategories.FirstOrDefaultAsync(c => c.Id == (vm.CategoryId ?? 0));
        if (category is null)
        {
            ModelState.AddModelError(nameof(vm.CategoryId), "Please select a valid category.");
            await PopulateDropdowns();
            return View(vm);
        }

        DeviceProfileCatalog? deviceProfile = null;
        if (vm.DeviceProfileId.HasValue)
        {
            deviceProfile = await _db.DeviceProfileCatalogs.FirstOrDefaultAsync(p => p.Id == vm.DeviceProfileId.Value);
            if (deviceProfile is null)
            {
                ModelState.AddModelError(nameof(vm.DeviceProfileId), "Please select a valid device profile.");
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
        entity.CategoryId = category.Id;
        entity.DeviceProfileId = deviceProfile?.Id;
        entity.DeviceName = vm.DeviceName;
        entity.Brand = vm.Brand;
        entity.Model = vm.Model;
        entity.ApplianceType = vm.ApplianceType;
        entity.LocationCategory = vm.LocationCategory!.Value;
        entity.SiteRole = vm.SiteClassification!.Value;
        entity.SoftwareVersion = vm.SoftwareVersion;
        entity.SerialNo = vm.SerialNo;
        entity.IpAddress = vm.IpAddress;
        entity.MgmtIp = vm.MgmtIp;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location ?? string.Empty;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.LicenceInfo = vm.LicenceInfo;
        entity.StartOfSupportDate = vm.StartOfSupportDate;
        entity.EndOfSupportDate = vm.EndOfSupportDate;
        entity.EndOfLifeDate = vm.EndOfLifeDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "PhysicalDevice", entity.DeviceName);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.PhysicalDevices.FirstOrDefaultAsync(x => x.Id == id && (isAdmin || x.CountryId == scopedCountryId));
        if (entity is null) return NotFound();

        var deviceName = entity.DeviceName;
        _db.PhysicalDevices.Remove(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Delete", "PhysicalDevice", deviceName);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();
        ViewBag.EntityName = "Physical Devices";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        var isAdmin = User.IsAdministrator();

        if (!isAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Physical Devices",
            "Category", "Device Name", "Brand", "Model", "Physical/Virtual", "Location Category", "Site Role",
            "Software Version", "Serial Number", "IP Address", "Management IP", "Branch",
            "Location", "Vendor/Supplier", "License Info", "Support Start Date", "Support End Date",
            "End of Life Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PhysicalDevices_Template.xlsx");
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

        var categories = await _db.DeviceCategories.ToListAsync();
        var categoryLookup = categories.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        var result = new ImportResultViewModel { EntityName = "Physical Devices" };
        var toAdd = new List<PhysicalDevice>();

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

                var categoryText = ExcelImportHelpers.GetString(ws, row, headers, "Category");
                var deviceName = ExcelImportHelpers.GetString(ws, row, headers, "Device Name");
                var location = ExcelImportHelpers.GetString(ws, row, headers, "Location");
                var applianceRaw = ExcelImportHelpers.GetString(ws, row, headers, "Physical/Virtual");

                if (string.IsNullOrEmpty(categoryText))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Category is required." });
                    continue;
                }

                if (!categoryLookup.TryGetValue(categoryText, out var category))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = $"Category '{categoryText}' not found." });
                    continue;
                }

                if (string.IsNullOrEmpty(deviceName))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Device Name is required." });
                    continue;
                }

                if (string.IsNullOrEmpty(location))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Location is required." });
                    continue;
                }

                if (!ExcelImportHelpers.TryParseApplianceType(applianceRaw, out var applianceType, out var applianceError))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = applianceError! });
                    continue;
                }

                var locationCategoryRaw = ExcelImportHelpers.GetString(ws, row, headers, "Location Category");
                if (!ExcelImportHelpers.TryParseLocationCategory(locationCategoryRaw, out var locationCategory, out var locationCategoryError))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = locationCategoryError! });
                    continue;
                }

                var siteRoleRaw = ExcelImportHelpers.GetString(ws, row, headers, "Site Role");
                if (!ExcelImportHelpers.TryParseSiteRole(siteRoleRaw, out var siteRole, out var siteRoleError))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = siteRoleError! });
                    continue;
                }

                toAdd.Add(new PhysicalDevice
                {
                    CountryId = country.Id,
                    CategoryId = category.Id,
                    DeviceName = deviceName,
                    Brand = ExcelImportHelpers.GetString(ws, row, headers, "Brand"),
                    Model = ExcelImportHelpers.GetString(ws, row, headers, "Model"),
                    ApplianceType = applianceType,
                    LocationCategory = locationCategory,
                    SiteRole = siteRole,
                    SoftwareVersion = ExcelImportHelpers.GetString(ws, row, headers, "Software Version"),
                    SerialNo = ExcelImportHelpers.GetString(ws, row, headers, "Serial Number"),
                    IpAddress = ExcelImportHelpers.GetString(ws, row, headers, "IP Address"),
                    MgmtIp = ExcelImportHelpers.GetString(ws, row, headers, "Management IP"),
                    Branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch"),
                    Location = location,
                    VendorSupplier = ExcelImportHelpers.GetString(ws, row, headers, "Vendor/Supplier"),
                    LicenceInfo = ExcelImportHelpers.GetString(ws, row, headers, "License Info"),
                    StartOfSupportDate = ExcelImportHelpers.GetDate(ws, row, headers, "Support Start Date"),
                    EndOfSupportDate = ExcelImportHelpers.GetDate(ws, row, headers, "Support End Date"),
                    EndOfLifeDate = ExcelImportHelpers.GetDate(ws, row, headers, "End of Life Date"),
                    Notes = ExcelImportHelpers.GetString(ws, row, headers, "Notes"),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username
                });
            }
        }

        if (toAdd.Count > 0)
        {
            _db.PhysicalDevices.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "PhysicalDevice", country.DisplayName ?? country.Name, $"{toAdd.Count} record(s) imported from Excel.");
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
        ViewBag.CategoryOptions = new SelectList(await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewBag.AllLocations = await _db.Locations.Where(l => l.IsActive)
            .Select(l => new { l.CountryId, l.Branch }).ToListAsync();

        var effectiveCountryId = isAdmin ? (int?)null : scopedCountryId;

        var companiesQuery = _db.Companies.Where(c => c.IsActive);
        if (effectiveCountryId.HasValue)
            companiesQuery = companiesQuery.Where(c => c.CountryId == effectiveCountryId.Value);
        var vendors = await companiesQuery.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        ViewBag.VendorOptions = new SelectList(vendors);

        var licensesQuery = _db.Licenses.AsQueryable();
        if (effectiveCountryId.HasValue)
            licensesQuery = licensesQuery.Where(l => l.CountryId == effectiveCountryId.Value);
        var licenceInfos = await licensesQuery.OrderBy(l => l.LicenseName).Select(l => l.LicenseName).Distinct().ToListAsync();
        ViewBag.LicenceInfoOptions = new SelectList(licenceInfos);
    }
}
