using System.Diagnostics;
using ITInventory.Data;
using ITInventory.Web.Common;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Home;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class HomeController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public HomeController(ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? countryId)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        var viewAll = string.IsNullOrEmpty(countryId) || countryId == "all";
        int? selectedCountryId = !viewAll && int.TryParse(countryId, out var parsedCountryId) ? parsedCountryId : null;

        var physicalDevices = _db.PhysicalDevices.Include(d => d.Country).AsQueryable();
        var servers = _db.Servers.Include(s => s.Country).AsQueryable();
        var licenses = _db.Licenses.Include(l => l.Country).AsQueryable();
        var circuits = _db.Circuits.Include(c => c.Country).AsQueryable();

        if (!isAdmin)
        {
            physicalDevices = physicalDevices.Where(d => d.CountryId == scopedCountryId);
            servers = servers.Where(s => s.CountryId == scopedCountryId);
            licenses = licenses.Where(l => l.CountryId == scopedCountryId);
            circuits = circuits.Where(c => c.CountryId == scopedCountryId);
        }
        else if (selectedCountryId.HasValue)
        {
            physicalDevices = physicalDevices.Where(d => d.CountryId == selectedCountryId.Value);
            servers = servers.Where(s => s.CountryId == selectedCountryId.Value);
            licenses = licenses.Where(l => l.CountryId == selectedCountryId.Value);
            circuits = circuits.Where(c => c.CountryId == selectedCountryId.Value);
        }

        var now = DateTime.UtcNow;
        var threshold = now.Date.AddDays(90);

        var expired = new List<ExpiringItem>();
        var upcoming = new List<ExpiringItem>();

        void Add(string label, ExpirationType type, string name, string? country, DateTime expiresAt)
        {
            var item = new ExpiringItem { Type = label, ExpirationType = type.ToString(), Name = name, Country = country, ExpiresAt = expiresAt };
            (expiresAt < now ? expired : upcoming).Add(item);
        }

        var qualifyingDevices = await physicalDevices
            .Where(d => (d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                     || (d.EndOfLifeDate != null && d.EndOfLifeDate <= threshold))
            .ToListAsync();
        foreach (var d in qualifyingDevices)
        {
            var country = d.Country != null ? (d.Country.DisplayName ?? d.Country.Name) : null;
            if (d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                Add("Physical Device", ExpirationType.EndOfSupport, d.DeviceName, country, d.EndOfSupportDate.Value);
            if (d.EndOfLifeDate != null && d.EndOfLifeDate <= threshold)
                Add("Physical Device", ExpirationType.EndOfLife, d.DeviceName, country, d.EndOfLifeDate.Value);
        }

        var qualifyingServers = await servers
            .Where(s => (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                     || (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold))
            .ToListAsync();
        foreach (var s in qualifyingServers)
        {
            var country = s.Country != null ? (s.Country.DisplayName ?? s.Country.Name) : null;
            if (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                Add("Server", ExpirationType.EndOfSupport, s.HostName, country, s.EndOfSupportDate.Value);
            if (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold)
                Add("Server", ExpirationType.EndOfLife, s.HostName, country, s.EndOfLifeDate.Value);
        }

        var qualifyingLicenses = await licenses
            .Where(l => (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                     || (l.ExpirationDate != null && l.ExpirationDate <= threshold))
            .ToListAsync();
        foreach (var l in qualifyingLicenses)
        {
            var country = l.Country != null ? (l.Country.DisplayName ?? l.Country.Name) : null;
            if (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                Add("License (Support)", ExpirationType.License, l.LicenseName, country, l.SupportEndDate.Value);
            if (l.ExpirationDate != null && l.ExpirationDate <= threshold)
                Add("License (Expiration)", ExpirationType.License, l.LicenseName, country, l.ExpirationDate.Value);
        }

        var qualifyingCircuits = await circuits
            .Where(c => c.EndDate != null && c.EndDate <= threshold)
            .ToListAsync();
        foreach (var c in qualifyingCircuits)
        {
            var country = c.Country != null ? (c.Country.DisplayName ?? c.Country.Name) : null;
            Add("Circuit", ExpirationType.EndOfSupport, c.CircuitType, country, c.EndDate!.Value);
        }

        var vm = new DashboardViewModel
        {
            PhysicalDeviceCount = await physicalDevices.CountAsync(),
            ServerCount = await servers.CountAsync(),
            LicenseCount = await licenses.CountAsync(),
            CircuitCount = await circuits.CountAsync(),
            ExpiredItems = expired.OrderBy(e => e.ExpiresAt).ToList(),
            UpcomingItems = upcoming.OrderBy(e => e.ExpiresAt).ToList()
        };

        // Only makes sense for a single, specific country -- "All Countries" has no one
        // topology diagram to show.
        var topologyCountryId = isAdmin ? selectedCountryId : scopedCountryId;
        if (topologyCountryId.HasValue)
        {
            var topology = await _db.CountryTopologyFiles
                .Where(f => f.CountryId == topologyCountryId.Value)
                .Select(f => new { f.FileName, f.UploadedAt })
                .FirstOrDefaultAsync();

            vm.TopologyCountryId = topologyCountryId;
            vm.TopologyFileName = topology?.FileName;
            vm.TopologyUploadedAt = topology?.UploadedAt;
        }

        ViewBag.Countries = await _db.Countries.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedCountryId = selectedCountryId;
        ViewBag.ViewAll = viewAll;
        ViewBag.IsAdmin = isAdmin;

        return View(vm);
    }

    [HttpGet]
    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
