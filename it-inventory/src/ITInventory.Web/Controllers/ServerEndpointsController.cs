using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Models;
using ITInventory.Web.Models.ServerEndpoints;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class ServerEndpointsController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activityLogger;

    public ServerEndpointsController(ITInventoryDbContext db, ICurrentUserService currentUser, IActivityLogger activityLogger)
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

        PagedResult<ServerEndpoint> items;
        if (requiresSelection)
        {
            items = new PagedResult<ServerEndpoint> { Items = Array.Empty<ServerEndpoint>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 };
        }
        else
        {
            var query = _db.ServerEndpoints
                .Include(e => e.Server).ThenInclude(s => s!.Country)
                .Include(e => e.Application)
                .AsQueryable();

            if (!isAdmin)
                query = query.Where(e => e.Server!.CountryId == _currentUser.CountryId);
            else if (selectedCountryId.HasValue)
                query = query.Where(e => e.Server!.CountryId == selectedCountryId.Value);

            items = await query.OrderBy(e => e.Server!.Country!.Name).ThenBy(e => e.Server!.HostName).ToPagedResultAsync(page);
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

        var query = _db.ServerEndpoints
            .Include(e => e.Server).ThenInclude(s => s!.Country)
            .Include(e => e.Application)
            .AsQueryable();
        if (selectedCountryId.HasValue)
            query = query.Where(e => e.Server!.CountryId == selectedCountryId.Value);

        var items = await query.OrderBy(e => e.Server!.Country!.Name).ThenBy(e => e.Server!.HostName).ToListAsync();

        var headers = new[] { "Country", "Server", "IP Address", "Port", "Application", "Notes" };
        var rows = items.Select(e => new object?[]
        {
            e.Server?.Country?.DisplayName ?? e.Server?.Country?.Name,
            e.Server?.HostName,
            e.IpAddress,
            e.Port,
            e.Application?.Name,
            e.Notes
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Server Endpoints", headers, rows);
        await _activityLogger.LogAsync("Export", "ServerEndpoint", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ServerEndpoints_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (!_currentUser.CanEdit) return Forbid();

        await PopulateDropdowns();
        return View(new ServerEndpointFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ServerEndpointFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var server = await _db.Servers.FirstOrDefaultAsync(s => s.Id == vm.ServerId
            && (_currentUser.IsAdmin || s.CountryId == _currentUser.CountryId));
        if (server is null)
            ModelState.AddModelError(nameof(vm.ServerId), "Please select a valid server.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        var entity = new ServerEndpoint
        {
            ServerId = vm.ServerId!.Value,
            IpAddress = vm.IpAddress,
            Port = vm.Port,
            ApplicationId = vm.ApplicationId,
            Notes = vm.Notes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username
        };

        _db.ServerEndpoints.Add(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Create", "ServerEndpoint", $"{entity.IpAddress}:{entity.Port}");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.ServerEndpoints.Include(e => e.Server).FirstOrDefaultAsync(e => e.Id == id
            && (_currentUser.IsAdmin || e.Server!.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var vm = new ServerEndpointFormViewModel
        {
            Id = entity.Id,
            ServerId = entity.ServerId,
            IpAddress = entity.IpAddress,
            Port = entity.Port,
            ApplicationId = entity.ApplicationId,
            Notes = entity.Notes
        };

        await PopulateDropdowns();
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ServerEndpointFormViewModel vm)
    {
        if (!_currentUser.CanEdit) return Forbid();
        if (id != vm.Id) return BadRequest();

        var entity = await _db.ServerEndpoints.Include(e => e.Server).FirstOrDefaultAsync(e => e.Id == id
            && (_currentUser.IsAdmin || e.Server!.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var server = await _db.Servers.FirstOrDefaultAsync(s => s.Id == vm.ServerId
            && (_currentUser.IsAdmin || s.CountryId == _currentUser.CountryId));
        if (server is null)
            ModelState.AddModelError(nameof(vm.ServerId), "Please select a valid server.");

        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(vm);
        }

        entity.ServerId = vm.ServerId!.Value;
        entity.IpAddress = vm.IpAddress;
        entity.Port = vm.Port;
        entity.ApplicationId = vm.ApplicationId;
        entity.Notes = vm.Notes;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = _currentUser.Username;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "ServerEndpoint", $"{entity.IpAddress}:{entity.Port}");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!_currentUser.CanEdit) return Forbid();

        var entity = await _db.ServerEndpoints.Include(e => e.Server).FirstOrDefaultAsync(e => e.Id == id
            && (_currentUser.IsAdmin || e.Server!.CountryId == _currentUser.CountryId));
        if (entity is null) return NotFound();

        var label = $"{entity.IpAddress}:{entity.Port}";
        _db.ServerEndpoints.Remove(entity);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Delete", "ServerEndpoint", label);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        var serversQuery = _currentUser.IsAdmin
            ? _db.Servers.Include(s => s.Country).AsQueryable()
            : _db.Servers.Include(s => s.Country).Where(s => s.CountryId == _currentUser.CountryId);
        var servers = await serversQuery.OrderBy(s => s.Country!.Name).ThenBy(s => s.HostName)
            .Select(s => new { s.Id, Label = s.HostName + " (" + (s.Country!.DisplayName ?? s.Country.Name) + ")" })
            .ToListAsync();
        ViewBag.ServerOptions = new SelectList(servers, "Id", "Label");

        var applicationsQuery = _currentUser.IsAdmin
            ? _db.Applications.AsQueryable()
            : _db.Applications.Where(a => a.CountryId == _currentUser.CountryId);
        var applications = await applicationsQuery.OrderBy(a => a.Name).ToListAsync();
        ViewBag.ApplicationOptions = new SelectList(applications, "Id", "Name");
    }
}
