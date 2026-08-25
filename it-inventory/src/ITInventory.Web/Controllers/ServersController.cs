using ClosedXML.Excel;
using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Import;
using ITInventory.Web.Models.Servers;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class ServersController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public ServersController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
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

        PagedResult<Server> items;
        if (requiresSelection)
        {
            items = new PagedResult<Server> { Items = Array.Empty<Server>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.Servers.Include(s => s.Country).Include(s => s.HostPhysicalDevice).Include(s => s.Endpoints).AsQueryable();

            if (!isAdmin)
                query = query.Where(s => s.CountryId == _currentUser.CountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(s => s.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(s => s.Country!.Name).ThenBy(s => s.HostName).ToPagedResultAsync(page);
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

        var query = _db.Servers.Include(s => s.Country).Include(s => s.HostPhysicalDevice).AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(s => s.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(s => s.Country!.Name).ThenBy(s => s.HostName).ToListAsync();

        var headers = new[]
        {
            "Country", "Host Name", "Physical/Virtual", "Location Category", "Site Role", "Host (ESX/Physical Device)", "Operating System",
            "Brand", "Model", "Serial Number", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "End of Life Date", "Notes"
        };
        var rows = items.Select(s => new object?[]
        {
            s.Country?.DisplayName ?? s.Country?.Name,
            s.HostName,
            s.ApplianceType.ToString(),
            s.LocationCategory.ToString(),
            s.SiteRole.ToString(),
            s.HostPhysicalDevice?.DeviceName,
            s.OperatingSystem,
            s.Brand,
            s.Model,
            s.SerialNo,
            s.VendorSupplier,
            s.Branch,
            s.Location,
            s.StartOfSupportDate,
            s.EndOfSupportDate,
            s.EndOfLifeDate,
            s.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Servers", headers, rows);
        await _activityLogger.LogAsync("Export", "Server", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Servers_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    // New Servers are always virtual -- a physical server is just a Physical Device (there's
    // already a "Server" device category for that), so keeping a Physical option here only
    // duplicated that. Existing rows created before this change may still be Physical; Edit
    // leaves those alone and fully editable, this only affects new records.
    [HttpGet]
    public async Task<IActionResult> Create(bool fromPool = false, int? sourceId = null, int? countryId = null)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new ServerFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? countryId ?? 0 : _currentUser.CountryId ?? 0,
            ApplianceType = ApplianceType.Virtual
        };

        if (fromPool && sourceId.HasValue)
        {
            var source = await _db.ZiraatYds.FindAsync(sourceId.Value);
            if (source is not null && !_currentUser.IsAdmin && source.RepositoryName != _currentUser.Country)
                source = null;
            if (source is not null)
            {
                vm.SourceZiraatYdId = source.Id;
                vm.HostName = source.DnsName ?? source.NetbiosName ?? source.IpAddress;
                vm.OperatingSystem = source.OperatingSystem;
                vm.IpAddress = source.IpAddress;

                var profile = await _db.DeviceProfileCatalogs
                    .FirstOrDefaultAsync(p => p.ProfileName == source.DeviceProfile);
                if (profile is not null)
                    vm.DeviceProfileId = profile.Id;
            }
        }

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServerFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        // IP Address and ESXi/Physical Server only exist on the "New Server" form (see the
        // isNew branching in _Form.cshtml) -- they're absent entirely when editing, so they
        // can't carry a ViewModel-level [Required] without breaking Edit. Enforced here instead,
        // Create-only.
        if (string.IsNullOrWhiteSpace(vm.IpAddress))
            ModelState.AddModelError(nameof(vm.IpAddress), "IP address is required.");
        if (!vm.HostPhysicalDeviceId.HasValue)
            ModelState.AddModelError(nameof(vm.HostPhysicalDeviceId), "ESXi / Physical Server is required.");

        // Location Category/Branch aren't collected on "New Server" at all -- the VM's location
        // is the same as the ESXi/physical host it runs on, so it's taken from there instead of
        // asking the user to enter it a second time.
        PhysicalDevice? hostDevice = null;
        if (vm.HostPhysicalDeviceId.HasValue)
        {
            hostDevice = await _db.PhysicalDevices.FirstOrDefaultAsync(d => d.Id == vm.HostPhysicalDeviceId.Value
                && (_currentUser.IsAdmin || d.CountryId == _currentUser.CountryId));
            if (hostDevice is null)
                ModelState.AddModelError(nameof(vm.HostPhysicalDeviceId), "Selected ESXi / Physical Server was not found.");
        }

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new Server
        {
            CountryId = vm.CountryId,
            DeviceProfileId = vm.DeviceProfileId,
            SourceZiraatYdId = vm.SourceZiraatYdId,
            HostName = vm.HostName,
            // Create's form no longer submits ApplianceType (it's gone from the "New Server"
            // view since new Servers are always virtual) -- forcing it here rather than trusting
            // vm.ApplianceType avoids it silently binding to the enum's default (Physical = 0).
            ApplianceType = ApplianceType.Virtual,
            LocationCategory = hostDevice!.LocationCategory,
            SiteRole = hostDevice.SiteRole,
            HostPhysicalDeviceId = vm.HostPhysicalDeviceId,
            OperatingSystem = vm.OperatingSystem,
            Brand = vm.Brand,
            Model = vm.Model,
            SerialNo = vm.SerialNo,
            VendorSupplier = vm.VendorSupplier,
            Branch = hostDevice.Branch,
            Location = vm.Location,
            StartOfSupportDate = vm.StartOfSupportDate,
            EndOfSupportDate = vm.EndOfSupportDate,
            EndOfLifeDate = vm.EndOfLifeDate,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.Servers.Add(entity);
        await _db.SaveChangesAsync();

        // A Server's IP lives on a separate ServerEndpoint row, not on the Server itself, so the
        // IP Address field on Create (pre-filled from the pool row, or typed in manually) isn't
        // saved as part of the entity above -- it creates the server's first ServerEndpoint here
        // instead. Without this, "Add as Server" never used to create an endpoint at all (see
        // the Device Pool "In Inventory" fix), so the pool row's IP just vanished.
        if (!string.IsNullOrWhiteSpace(vm.IpAddress))
        {
            _db.ServerEndpoints.Add(new ServerEndpoint
            {
                ServerId = entity.Id,
                IpAddress = vm.IpAddress,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username
            });
            await _db.SaveChangesAsync();
        }

        await _activityLogger.LogAsync("Create", "Server", entity.HostName);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Servers.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var vm = new ServerFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            DeviceProfileId = entity.DeviceProfileId,
            SourceZiraatYdId = entity.SourceZiraatYdId,
            HostName = entity.HostName,
            ApplianceType = entity.ApplianceType,
            LocationCategory = entity.LocationCategory,
            SiteRole = entity.SiteRole,
            HostPhysicalDeviceId = entity.HostPhysicalDeviceId,
            OperatingSystem = entity.OperatingSystem,
            Brand = entity.Brand,
            Model = entity.Model,
            SerialNo = entity.SerialNo,
            VendorSupplier = entity.VendorSupplier,
            Branch = entity.Branch,
            Location = entity.Location,
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
    public async Task<IActionResult> Edit(int id, ServerFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.Servers.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        if (!_currentUser.IsAdmin)
            vm.CountryId = _currentUser.CountryId ?? 0;

        // Branch is shown/editable here (unlike Create, where it's taken from the ESXi host
        // instead and the field doesn't exist at all) -- see ServerFormViewModel.Branch for why
        // this isn't a blanket [Required] on the model.
        if (string.IsNullOrWhiteSpace(vm.Branch))
            ModelState.AddModelError(nameof(vm.Branch), "Branch is required.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.CountryId = vm.CountryId;
        entity.HostName = vm.HostName;
        entity.ApplianceType = vm.ApplianceType;
        entity.LocationCategory = vm.LocationCategory;
        entity.SiteRole = vm.SiteRole;
        entity.HostPhysicalDeviceId = vm.HostPhysicalDeviceId;
        entity.OperatingSystem = vm.OperatingSystem;
        entity.Brand = vm.Brand;
        entity.Model = vm.Model;
        entity.SerialNo = vm.SerialNo;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
        entity.StartOfSupportDate = vm.StartOfSupportDate;
        entity.EndOfSupportDate = vm.EndOfSupportDate;
        entity.EndOfLifeDate = vm.EndOfLifeDate;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "Server", entity.HostName);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Servers.FirstOrDefaultAsync(x => x.Id == id && (_currentUser.IsAdmin || x.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var hostName = entity.HostName;
        _db.Servers.Remove(entity);

        try
        {
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Delete", "Server", hostName);
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Could not delete because endpoints (IP/Port/Application mappings) are linked to this server. Delete those first.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Import()
    {
        if (!_currentUser.IsAdmin) return Forbid();
        ViewBag.EntityName = "Servers";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.IsAdmin) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Servers",
            "Host Name", "Physical/Virtual", "Location Category", "Site Role", "Operating System",
            "Brand", "Model", "Serial Number", "Vendor/Supplier", "Branch", "Location",
            "Support Start Date", "Support End Date", "End of Life Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Servers_Template.xlsx");
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

        var country = await _db.Countries.FindAsync(effectiveCountryId.Value);
        if (country is null)
        {
            TempData["ImportError"] = "Selected country not found.";
            return RedirectToAction(nameof(Import));
        }

        var result = new ImportResultViewModel { EntityName = "Servers" };
        var toAdd = new List<Server>();

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

                var hostName = ExcelImportHelpers.GetString(ws, row, headers, "Host Name");
                var applianceRaw = ExcelImportHelpers.GetString(ws, row, headers, "Physical/Virtual");

                if (string.IsNullOrEmpty(hostName))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Host Name is required." });
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

                toAdd.Add(new Server
                {
                    CountryId = country.Id,
                    HostName = hostName,
                    ApplianceType = applianceType,
                    LocationCategory = locationCategory,
                    SiteRole = siteRole,
                    OperatingSystem = ExcelImportHelpers.GetString(ws, row, headers, "Operating System"),
                    Brand = ExcelImportHelpers.GetString(ws, row, headers, "Brand"),
                    Model = ExcelImportHelpers.GetString(ws, row, headers, "Model"),
                    SerialNo = ExcelImportHelpers.GetString(ws, row, headers, "Serial Number"),
                    VendorSupplier = ExcelImportHelpers.GetString(ws, row, headers, "Vendor/Supplier"),
                    Branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch"),
                    Location = ExcelImportHelpers.GetString(ws, row, headers, "Location"),
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
            _db.Servers.AddRange(toAdd);
            await _db.SaveChangesAsync();
            await _activityLogger.LogAsync("Import", "Server", country.DisplayName ?? country.Name, $"{toAdd.Count} record(s) imported from Excel.");
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

        // Only "ESXi / Physical Server" category Physical Devices can host a VM -- without
        // this filter every switch/firewall/etc. in the country showed up here too.
        var physicalDevicesQuery = _db.PhysicalDevices.Where(d => d.Category!.Name == "ESXi / Physical Server");
        if (effectiveCountryId.HasValue)
            physicalDevicesQuery = physicalDevicesQuery.Where(d => d.CountryId == effectiveCountryId.Value);
        var physicalDevices = await physicalDevicesQuery.OrderBy(d => d.DeviceName).ToListAsync();
        ViewBag.HostPhysicalDeviceOptions = new SelectList(physicalDevices, "Id", "DeviceName");

        ViewBag.AllLocations = await _db.Locations.Where(l => l.IsActive)
            .Select(l => new { l.CountryId, l.Branch }).ToListAsync();

        var companiesQuery = _db.Companies.Where(c => c.IsActive);
        if (effectiveCountryId.HasValue)
            companiesQuery = companiesQuery.Where(c => c.CountryId == effectiveCountryId.Value);
        var vendors = await companiesQuery.OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        ViewBag.VendorOptions = new SelectList(vendors);
    }
}
