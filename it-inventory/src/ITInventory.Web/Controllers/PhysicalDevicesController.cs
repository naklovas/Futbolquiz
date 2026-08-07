using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Entities;
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

    public PhysicalDevicesController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(string? countryId, int? categoryId, int page = 1)
    {
        var isAdmin = _currentUser.IsAdmin;
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
                query = query.Where(d => d.CountryId == _currentUser.CountryId);
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

    public async Task<IActionResult> Create(bool fromPool = false, int? sourceId = null, int? countryId = null, int? categoryId = null)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new PhysicalDeviceFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? countryId ?? 0 : _currentUser.CountryId ?? 0,
            CategoryId = categoryId ?? 0
        };

        if (fromPool && sourceId.HasValue)
        {
            var source = await _db.ZiraatYds.FindAsync(sourceId.Value);
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
        if (!_currentUser.CanEdit) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new PhysicalDevice
        {
            CountryId = vm.CountryId,
            CategoryId = vm.CategoryId,
            DeviceProfileId = vm.DeviceProfileId,
            SourceZiraatYdId = vm.SourceZiraatYdId,
            DeviceName = vm.DeviceName,
            Brand = vm.Brand,
            Model = vm.Model,
            ApplianceType = vm.ApplianceType,
            SoftwareVersion = vm.SoftwareVersion,
            SerialNo = vm.SerialNo,
            IpAddress = vm.IpAddress,
            MgmtIp = vm.MgmtIp,
            Branch = vm.Branch,
            Location = vm.Location,
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
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.PhysicalDevices.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

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
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.PhysicalDevices.FindAsync(id);
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
        entity.CategoryId = vm.CategoryId;
        entity.DeviceProfileId = vm.DeviceProfileId;
        entity.DeviceName = vm.DeviceName;
        entity.Brand = vm.Brand;
        entity.Model = vm.Model;
        entity.ApplianceType = vm.ApplianceType;
        entity.SoftwareVersion = vm.SoftwareVersion;
        entity.SerialNo = vm.SerialNo;
        entity.IpAddress = vm.IpAddress;
        entity.MgmtIp = vm.MgmtIp;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.LicenceInfo = vm.LicenceInfo;
        entity.StartOfSupportDate = vm.StartOfSupportDate;
        entity.EndOfSupportDate = vm.EndOfSupportDate;
        entity.EndOfLifeDate = vm.EndOfLifeDate;
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

        var entity = await _db.PhysicalDevices.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        _db.PhysicalDevices.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Import()
    {
        if (!_currentUser.CanEdit) return Forbid();
        ViewBag.EntityName = "Physical Devices";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.CanEdit) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Physical Devices",
            "Category", "Device Name", "Brand", "Model", "Physical/Virtual",
            "Software Version", "Serial Number", "IP Address", "Management IP", "Branch",
            "Location", "Vendor/Supplier", "License Info", "Support Start Date", "Support End Date",
            "End of Life Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PhysicalDevices_Template.xlsx");
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

                toAdd.Add(new PhysicalDevice
                {
                    CountryId = country.Id,
                    CategoryId = category.Id,
                    DeviceName = deviceName,
                    Brand = ExcelImportHelpers.GetString(ws, row, headers, "Brand"),
                    Model = ExcelImportHelpers.GetString(ws, row, headers, "Model"),
                    ApplianceType = applianceType,
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
        ViewBag.CategoryOptions = new SelectList(await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
    }
}
