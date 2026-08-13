using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using ITInventory.Web.Models;
using ITInventory.Web.Services;
using ITInventory.Web.Services.Import;
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
        "User", "Country", "OriginCountry", "Location", "DeviceProfile", "ActivityLog"
    };

    private readonly ITInventoryDbContext _db;
    private readonly IActivityLogger _activityLogger;

    public AdminActivityLogController(ITInventoryDbContext db, IActivityLogger activityLogger)
    {
        _db = db;
        _activityLogger = activityLogger;
    }

    public async Task<IActionResult> Index(string? username, string? action, string? entityType, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        // Only the first filter/search submit (the hidden "searched" field) triggers a query --
        // a bare navigation to the page (no querystring at all) shows the filter form and
        // nothing else, so a genuinely empty log table can never be mistaken for "the page is
        // broken" and a real result set is never confused with "you forgot to search".
        var hasSearched = Request.Query.ContainsKey("searched");

        ViewBag.HasSearched = hasSearched;
        ViewBag.ActionOptions = Actions;
        ViewBag.EntityTypeOptions = EntityTypes;
        ViewBag.Username = username;
        ViewBag.SelectedAction = action;
        ViewBag.SelectedEntityType = entityType;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        if (!hasSearched)
        {
            return View(new PagedResult<ActivityLog> { Items = Array.Empty<ActivityLog>(), PageNumber = 1, PageSize = PaginationExtensions.DefaultPageSize, TotalCount = 0 });
        }

        // Diagnostic numbers shown on the page itself -- if TotalUnfiltered is 0, the app
        // genuinely isn't seeing any rows (wrong DB/connection string, or the SQL script was
        // never run there); if TotalUnfiltered > 0 but the filtered count is 0, the filters
        // (most likely the date range) are excluding real rows and that's the next thing to
        // narrow down from these exact numbers instead of guessing blind.
        ViewBag.TotalUnfiltered = await _db.ActivityLogs.CountAsync();
        ViewBag.ServerTimeZone = TimeZoneInfo.Local.Id;
        ViewBag.ServerNowLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ViewBag.ServerNowUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var query = BuildFilteredQuery(username, action, entityType, fromDate, toDate);
        ViewBag.TotalFiltered = await query.CountAsync();

        var items = await query.OrderByDescending(l => l.CreatedAt).ToPagedResultAsync(page);

        return View(items);
    }

    public async Task<IActionResult> Export(string? username, string? action, string? entityType, DateTime? fromDate, DateTime? toDate)
    {
        var query = BuildFilteredQuery(username, action, entityType, fromDate, toDate);
        var items = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();

        var headers = new[] { "Time", "Username", "Full Name", "Country", "Action", "Entity Type", "Entity Name", "Details", "IP Address" };
        var rows = items.Select(l => new object?[]
        {
            l.CreatedAt,
            l.Username,
            l.FullName,
            l.CountryName,
            l.Action,
            l.EntityType,
            l.EntityName,
            l.Details,
            l.IpAddress
        });

        var bytes = ExcelImportHelpers.CreateExportBytes("Activity Log", headers, rows);
        await _activityLogger.LogAsync("Export", "ActivityLog", details: $"{items.Count} record(s) exported to Excel.");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ActivityLog_Export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    private IQueryable<ActivityLog> BuildFilteredQuery(string? username, string? action, string? entityType, DateTime? fromDate, DateTime? toDate)
    {
        var query = _db.ActivityLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(username))
            query = query.Where(l => l.Username.Contains(username) || (l.FullName != null && l.FullName.Contains(username)));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(l => l.EntityType == entityType);

        // CreatedAt is stored as UTC (DateTime.UtcNow), but fromDate/toDate are calendar days
        // picked in the admin's local time (same convention the table already uses to *display*
        // CreatedAt via ToLocalTime()). Comparing a UTC column directly against a naive local
        // date boundary is off by the server's UTC offset -- a record made "today" in local time
        // can carry a UTC timestamp dated "yesterday" (or vice versa near midnight), so it would
        // silently fall outside a same-day filter. Converting the local day boundaries to UTC
        // first keeps the filter and the displayed time consistent.
        if (fromDate.HasValue)
        {
            var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Unspecified), TimeZoneInfo.Local);
            query = query.Where(l => l.CreatedAt >= fromUtc);
        }

        if (toDate.HasValue)
        {
            var toUtcExclusive = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(toDate.Value.Date.AddDays(1), DateTimeKind.Unspecified), TimeZoneInfo.Local);
            query = query.Where(l => l.CreatedAt < toUtcExclusive);
        }

        return query;
    }
}
