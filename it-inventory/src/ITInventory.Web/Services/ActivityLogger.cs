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
    private readonly IConfiguration _configuration;

    public ActivityLogger(ITInventoryDbContext db, ICurrentUserService currentUser, IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _configuration = configuration;
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
            // Set per-deployment in appsettings.json (e.g. "dev", "test", "prod") -- lets a log
            // entry be traced back to which environment produced it without exposing the
            // client's real IP address.
            EnvironmentName = _configuration["AppEnvironment"]
        };

        _db.ActivityLogs.Add(entry);
        await _db.SaveChangesAsync();
    }
}
