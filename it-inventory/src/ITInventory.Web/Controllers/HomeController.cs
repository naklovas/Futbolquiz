using System.Diagnostics;
using ITInventory.Data;
using ITInventory.Web.Models;
using ITInventory.Web.Models.Home;
using ITInventory.Web.Services;
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

    public async Task<IActionResult> Index()
    {
        var physicalDevices = _db.PhysicalDevices.Include(d => d.Country).AsQueryable();
        var servers = _db.Servers.Include(s => s.Country).AsQueryable();
        var licenses = _db.Licenses.Include(l => l.Country).AsQueryable();
        var circuits = _db.Circuits.Include(c => c.Country).AsQueryable();

        if (!_currentUser.IsAdmin)
        {
            physicalDevices = physicalDevices.Where(d => d.CountryId == _currentUser.CountryId);
            servers = servers.Where(s => s.CountryId == _currentUser.CountryId);
            licenses = licenses.Where(l => l.CountryId == _currentUser.CountryId);
            circuits = circuits.Where(c => c.CountryId == _currentUser.CountryId);
        }

        var threshold = DateTime.UtcNow.Date.AddDays(90);

        var expiring = new List<ExpiringItem>();

        expiring.AddRange((await physicalDevices
                .Where(d => d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                .ToListAsync())
            .Select(d => new ExpiringItem
            {
                Type = "Fiziksel Cihaz",
                Name = d.DeviceName,
                Country = d.Country?.Name,
                ExpiresAt = d.EndOfSupportDate!.Value
            }));

        expiring.AddRange((await servers
                .Where(s => s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                .ToListAsync())
            .Select(s => new ExpiringItem
            {
                Type = "Sunucu",
                Name = s.HostName,
                Country = s.Country?.Name,
                ExpiresAt = s.EndOfSupportDate!.Value
            }));

        expiring.AddRange((await licenses
                .Where(l => l.SupportEndDate != null && l.SupportEndDate <= threshold)
                .ToListAsync())
            .Select(l => new ExpiringItem
            {
                Type = "Lisans",
                Name = l.LicenseName,
                Country = l.Country?.Name,
                ExpiresAt = l.SupportEndDate!.Value
            }));

        expiring.AddRange((await circuits
                .Where(c => c.EndDate != null && c.EndDate <= threshold)
                .ToListAsync())
            .Select(c => new ExpiringItem
            {
                Type = "Hat",
                Name = c.CircuitType,
                Country = c.Country?.Name,
                ExpiresAt = c.EndDate!.Value
            }));

        var vm = new DashboardViewModel
        {
            PhysicalDeviceCount = await physicalDevices.CountAsync(),
            ServerCount = await servers.CountAsync(),
            LicenseCount = await licenses.CountAsync(),
            CircuitCount = await circuits.CountAsync(),
            ExpiringItems = expiring.OrderBy(e => e.ExpiresAt).ToList()
        };

        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
