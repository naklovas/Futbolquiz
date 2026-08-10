using ITInventory.Data;
using ITInventory.ExpirationNotifier.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Same expired/upcoming categorization as the web dashboard (Home/Index), run standalone
/// against every country -- there is no signed-in user to scope this to.
/// </summary>
public class ExpirationCheckService
{
    private readonly ITInventoryDbContext _db;
    private readonly IEmailNotificationService _emailService;
    private readonly ILogger<ExpirationCheckService> _logger;
    private readonly int _upcomingWindowDays;

    public ExpirationCheckService(ITInventoryDbContext db, IEmailNotificationService emailService, ILogger<ExpirationCheckService> logger, int upcomingWindowDays)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
        _upcomingWindowDays = upcomingWindowDays;
    }

    public async Task RunAsync()
    {
        var now = DateTime.UtcNow;
        var threshold = now.Date.AddDays(_upcomingWindowDays);

        var expired = new List<ExpiringItemNotification>();
        var upcoming = new List<ExpiringItemNotification>();

        void Add(string category, string label, ExpirationType type, string name, string? country, DateTime expiresAt)
        {
            var item = new ExpiringItemNotification
            {
                Category = category,
                Label = label,
                ExpirationType = type,
                Name = name,
                Country = country,
                ExpiresAt = expiresAt
            };
            (expiresAt < now ? expired : upcoming).Add(item);
        }

        var devices = await _db.PhysicalDevices.Include(d => d.Country)
            .Where(d => (d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                     || (d.EndOfLifeDate != null && d.EndOfLifeDate <= threshold))
            .ToListAsync();
        foreach (var d in devices)
        {
            var country = d.Country != null ? (d.Country.DisplayName ?? d.Country.Name) : null;
            if (d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                Add("PhysicalDevice", "Physical Device", ExpirationType.EndOfSupport, d.DeviceName, country, d.EndOfSupportDate.Value);
            if (d.EndOfLifeDate != null && d.EndOfLifeDate <= threshold)
                Add("PhysicalDevice", "Physical Device", ExpirationType.EndOfLife, d.DeviceName, country, d.EndOfLifeDate.Value);
        }

        var servers = await _db.Servers.Include(s => s.Country)
            .Where(s => (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                     || (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold))
            .ToListAsync();
        foreach (var s in servers)
        {
            var country = s.Country != null ? (s.Country.DisplayName ?? s.Country.Name) : null;
            if (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                Add("Server", "Server", ExpirationType.EndOfSupport, s.HostName, country, s.EndOfSupportDate.Value);
            if (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold)
                Add("Server", "Server", ExpirationType.EndOfLife, s.HostName, country, s.EndOfLifeDate.Value);
        }

        var licenses = await _db.Licenses.Include(l => l.Country)
            .Where(l => (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                     || (l.ExpirationDate != null && l.ExpirationDate <= threshold))
            .ToListAsync();
        foreach (var l in licenses)
        {
            var country = l.Country != null ? (l.Country.DisplayName ?? l.Country.Name) : null;
            if (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                Add("License", "License (Support)", ExpirationType.License, l.LicenseName, country, l.SupportEndDate.Value);
            if (l.ExpirationDate != null && l.ExpirationDate <= threshold)
                Add("License", "License (Expiration)", ExpirationType.License, l.LicenseName, country, l.ExpirationDate.Value);
        }

        var circuits = await _db.Circuits.Include(c => c.Country)
            .Where(c => c.EndDate != null && c.EndDate <= threshold)
            .ToListAsync();
        foreach (var c in circuits)
        {
            var country = c.Country != null ? (c.Country.DisplayName ?? c.Country.Name) : null;
            Add("Circuit", "Circuit", ExpirationType.EndOfSupport, c.CircuitType, country, c.EndDate!.Value);
        }

        _logger.LogInformation("Expiration check: {ExpiredCount} expired, {UpcomingCount} upcoming (within {Days} days).",
            expired.Count, upcoming.Count, _upcomingWindowDays);

        if (expired.Count == 0 && upcoming.Count == 0)
        {
            _logger.LogInformation("Nothing to notify.");
            return;
        }

        await _emailService.NotifyAsync(expired, upcoming);
    }
}
