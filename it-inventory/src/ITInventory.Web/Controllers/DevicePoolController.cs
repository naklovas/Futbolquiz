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

    public async Task<IActionResult> Index(int? countryId, int? categoryId)
    {
        ViewBag.Countries = await _db.Countries.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.Categories = await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync();

        var effectiveCountryId = _currentUser.IsAdmin ? countryId : _currentUser.CountryId;

        ViewBag.SelectedCountryId = effectiveCountryId;
        ViewBag.SelectedCategoryId = categoryId;
        ViewBag.IsAdmin = _currentUser.IsAdmin;

        if (effectiveCountryId is null)
        {
            if (_currentUser.IsAdmin)
            {
                ViewBag.NoCountryAssigned = false;
                ViewBag.ShowingAllCountries = true;
                var allDevices = await _devicePoolService.GetAllDevicesAsync(categoryId);
                return View(allDevices);
            }

            ViewBag.NoCountryAssigned = true;
            return View(new List<Models.DevicePool.DiscoveredDeviceDto>());
        }

        var devices = await _devicePoolService.GetDevicesForCountryAsync(effectiveCountryId.Value, categoryId);
        return View(devices);
    }
}
