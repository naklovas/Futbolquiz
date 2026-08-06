using ITInventory.Data;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

public class DevicePoolController : Controller
{
    private readonly IDevicePoolService _devicePoolService;
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public DevicePoolController(IDevicePoolService devicePoolService, ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _devicePoolService = devicePoolService;
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(string? country, int? categoryId)
    {
        ViewBag.Countries = await _db.ZiraatYds
            .Where(z => z.RepositoryName != null)
            .Select(z => z.RepositoryName!)
            .Distinct()
            .OrderBy(r => r)
            .ToListAsync();

        ViewBag.CountryDisplayNames = await _db.Countries
            .Where(c => c.DisplayName != null)
            .ToDictionaryAsync(c => c.Name, c => c.DisplayName!);

        ViewBag.Categories = await _db.DeviceCategories
            .Where(c => _db.DeviceProfileCatalogs.Any(p => p.CategoryId == c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync();

        var effectiveCountry = _currentUser.IsAdmin ? country : _currentUser.Country;

        ViewBag.SelectedCountry = effectiveCountry;
        ViewBag.SelectedCategoryId = categoryId;
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        if (string.IsNullOrEmpty(effectiveCountry))
        {
            ViewBag.NoCountryAssigned = !_currentUser.IsAdmin;
            return View(new List<Models.DevicePool.DiscoveredDeviceDto>());
        }

        var devices = await _devicePoolService.GetDevicesForRepositoryAsync(effectiveCountry, categoryId);

        // Resolve a matching Countries.Id (if one exists) so the "Add as ..." links can prefill the inventory form.
        ViewBag.MatchedCountryId = await _db.Countries
            .Where(c => c.Name == effectiveCountry)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();

        return View(devices);
    }
}
