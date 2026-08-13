using ITInventory.Data;
using ITInventory.Data.Entities;

namespace ITInventory.Web.Services;

/// <summary>
/// Writes its own row immediately (separate SaveChangesAsync from whatever the calling
/// controller action is doing) so an activity log entry doesn't get rolled back or entangled
/// with unrelated entity-save logic -- call this after the action's own save has already
/// succeeded, so only confirmed actions get logged.
/// </summary>
public class ActivityLogger : IActivityLogger
{
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ActivityLogger(ITInventoryDbContext db, ICurrentUserService currentUser, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(string action, string entityType, string? entityName = null, string? details = null)
    {
        var entry = new ActivityLog
        {
            CreatedAt = DateTime.UtcNow,
            Username = _currentUser.Username,
            FullName = _currentUser.FullName,
            CountryName = _currentUser.Country,
            Action = action,
            EntityType = entityType,
            EntityName = entityName,
            Details = details,
            IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };

        _db.ActivityLogs.Add(entry);
        await _db.SaveChangesAsync();
    }
}
