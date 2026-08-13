using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Web.Models;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminActivityLogController : Controller
{
    private static readonly string[] Actions = { "Create", "Update", "Delete", "Import", "Export", "Login", "Logout" };

    private static readonly string[] EntityTypes =
    {
        "PhysicalDevice", "Server", "ServerEndpoint", "License", "Circuit", "Company", "Application",
        "User", "Country", "OriginCountry", "Location", "DeviceProfile"
    };

    private readonly ITInventoryDbContext _db;

    public AdminActivityLogController(ITInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? username, string? action, string? entityType, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        var query = _db.ActivityLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(l => l.Username.Contains(username) || (l.FullName != null && l.FullName.Contains(username)));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(l => l.EntityType == entityType);

        if (fromDate.HasValue)
            query = query.Where(l => l.CreatedAt >= fromDate.Value.Date);

        if (toDate.HasValue)
            query = query.Where(l => l.CreatedAt < toDate.Value.Date.AddDays(1));

        var items = await query.OrderByDescending(l => l.CreatedAt).ToPagedResultAsync(page);

        ViewBag.ActionOptions = Actions;
        ViewBag.EntityTypeOptions = EntityTypes;
        ViewBag.Username = username;
        ViewBag.SelectedAction = action;
        ViewBag.SelectedEntityType = entityType;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View(items);
    }
}
