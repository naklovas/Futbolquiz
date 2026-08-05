using ITInventory.Data;
using ITInventory.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminDeviceProfilesController : Controller
{
    private readonly ITInventoryDbContext _db;

    public AdminDeviceProfilesController(ITInventoryDbContext db)
    {
        _db = db;
    }

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
        var profile = await _db.DeviceProfileCatalogs.FindAsync(id);
        if (profile is null) return NotFound();

        profile.CategoryId = categoryId;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
