using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Web.Common;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminDeviceProfilesController : Controller
{
    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;
    private readonly ICurrentUserService _currentUser;

    public AdminDeviceProfilesController(ITInventoryDbContext db, IActivityLogger activityLogger, ICurrentUserService currentUser)
    {
        _db = db;
        _activityLogger = activityLogger;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var profiles = await _db.DeviceProfileCatalogs
            .Include(p => p.Category)
            .OrderBy(p => p.ProfileName)
            .ToListAsync();

        ViewBag.Categories = await _db.DeviceCategories.OrderBy(c => c.Name).ToListAsync();
        return View(profiles);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCategory(int id, int? categoryId)
    {
        var isAdmin = User.IsAdministrator();

        var profile = await _db.DeviceProfileCatalogs.FirstOrDefaultAsync(p => p.Id == id && isAdmin);
        if (profile is null) return NotFound();

        profile.CategoryId = categoryId;
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Update", "DeviceProfile", profile.ProfileName);
        return RedirectToAction(nameof(Index));
    }
}
