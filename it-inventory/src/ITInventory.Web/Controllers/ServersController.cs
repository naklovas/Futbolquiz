using ClosedXML.Excel;
using ITInventory.Data;
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

    public ServersController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

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
            var query = _db.Servers.Include(s => s.Country).AsQueryable();

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

    public async Task<IActionResult> Create(bool fromPool = false, int? sourceId = null, int? countryId = null)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var vm = new ServerFormViewModel
        {
            CountryId = _currentUser.IsAdmin ? countryId ?? 0 : _currentUser.CountryId ?? 0
        };

        if (fromPool && sourceId.HasValue)
        {
            var source = await _db.ZiraatYds.FindAsync(sourceId.Value);
            if (source is not null)
            {
                vm.SourceZiraatYdId = source.Id;
                vm.HostName = source.DnsName ?? source.NetbiosName ?? source.IpAddress;
                vm.IpAddress = source.IpAddress;
                vm.OperatingSystem = source.OperatingSystem;

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
            ApplianceType = vm.ApplianceType,
            IpAddress = vm.IpAddress,
            OperatingSystem = vm.OperatingSystem,
            Brand = vm.Brand,
            Model = vm.Model,
            SerialNo = vm.SerialNo,
            VendorSupplier = vm.VendorSupplier,
            Port = vm.Port,
            Branch = vm.Branch,
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
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.Servers.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        var vm = new ServerFormViewModel
        {
            Id = entity.Id,
            CountryId = entity.CountryId,
            DeviceProfileId = entity.DeviceProfileId,
            SourceZiraatYdId = entity.SourceZiraatYdId,
            HostName = entity.HostName,
            ApplianceType = entity.ApplianceType,
            IpAddress = entity.IpAddress,
            OperatingSystem = entity.OperatingSystem,
            Brand = entity.Brand,
            Model = entity.Model,
            SerialNo = entity.SerialNo,
            VendorSupplier = entity.VendorSupplier,
            Port = entity.Port,
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

        var entity = await _db.Servers.FindAsync(id);
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
        entity.HostName = vm.HostName;
        entity.ApplianceType = vm.ApplianceType;
        entity.IpAddress = vm.IpAddress;
        entity.OperatingSystem = vm.OperatingSystem;
        entity.Brand = vm.Brand;
        entity.Model = vm.Model;
        entity.SerialNo = vm.SerialNo;
        entity.VendorSupplier = vm.VendorSupplier;
        entity.Port = vm.Port;
        entity.Branch = vm.Branch;
        entity.Location = vm.Location;
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

        var entity = await _db.Servers.FindAsync(id);
        if (entity is null) return NotFound();
        if (!_currentUser.IsAdmin && entity.CountryId != _currentUser.CountryId) return Forbid();

        _db.Servers.Remove(entity);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Import()
    {
        if (!_currentUser.CanEdit) return Forbid();
        ViewBag.EntityName = "Servers";
        await PopulateImportCountryInfo();
        return View("Import");
    }

    public IActionResult DownloadTemplate()
    {
        if (!_currentUser.CanEdit) return Forbid();

        var bytes = ExcelImportHelpers.CreateTemplateBytes("Servers",
            "Host Name", "Physical/Virtual", "IP Address", "Operating System",
            "Brand", "Model", "Serial Number", "Vendor/Supplier", "Port", "Branch", "Location",
            "Support Start Date", "Support End Date", "End of Life Date", "Notes");

        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Servers_Template.xlsx");
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
                var location = ExcelImportHelpers.GetString(ws, row, headers, "Location");
                var applianceRaw = ExcelImportHelpers.GetString(ws, row, headers, "Physical/Virtual");

                if (string.IsNullOrEmpty(hostName))
                {
                    result.Errors.Add(new ImportRowError { RowNumber = row, Message = "Host Name is required." });
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

                toAdd.Add(new Server
                {
                    CountryId = country.Id,
                    HostName = hostName,
                    ApplianceType = applianceType,
                    IpAddress = ExcelImportHelpers.GetString(ws, row, headers, "IP Address"),
                    OperatingSystem = ExcelImportHelpers.GetString(ws, row, headers, "Operating System"),
                    Brand = ExcelImportHelpers.GetString(ws, row, headers, "Brand"),
                    Model = ExcelImportHelpers.GetString(ws, row, headers, "Model"),
                    SerialNo = ExcelImportHelpers.GetString(ws, row, headers, "Serial Number"),
                    VendorSupplier = ExcelImportHelpers.GetString(ws, row, headers, "Vendor/Supplier"),
                    Port = ExcelImportHelpers.GetInt(ws, row, headers, "Port"),
                    Branch = ExcelImportHelpers.GetString(ws, row, headers, "Branch"),
                    Location = location,
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
