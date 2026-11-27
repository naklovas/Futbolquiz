using ITInventory.Data;
using ITInventory.Data.Common;
using ITInventory.ExpirationNotifier.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Same expired/upcoming categorization as the web dashboard (Home/Index), run standalone
/// against every country -- there is no signed-in user to scope this to. Recipients are
/// resolved per country: that country's YDUsers (RepositoryName == Countries.Name, the same
/// match the web app uses) union every admin, so nobody is left without a recipient list.
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

        void Add(string category, string label, ExpirationType type, string name, int countryId, string countryName, DateTime expiresAt)
        {
            var item = new ExpiringItemNotification
            {
                Category = category,
                Label = label,
                ExpirationType = type,
                Name = name,
                CountryId = countryId,
                CountryName = countryName,
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
            var countryName = d.Country?.DisplayName ?? d.Country?.Name ?? "?";
            if (d.EndOfSupportDate != null && d.EndOfSupportDate <= threshold)
                Add("PhysicalDevice", "Physical Device", ExpirationType.EndOfSupport, d.DeviceName, d.CountryId, countryName, d.EndOfSupportDate.Value);
            if (d.EndOfLifeDate != null && d.EndOfLifeDate <= threshold)
                Add("PhysicalDevice", "Physical Device", ExpirationType.EndOfLife, d.DeviceName, d.CountryId, countryName, d.EndOfLifeDate.Value);
        }

        var servers = await _db.Servers.Include(s => s.Country)
            .Where(s => (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                     || (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold))
            .ToListAsync();
        foreach (var s in servers)
        {
            var countryName = s.Country?.DisplayName ?? s.Country?.Name ?? "?";
            if (s.EndOfSupportDate != null && s.EndOfSupportDate <= threshold)
                Add("Server", "Server", ExpirationType.EndOfSupport, s.HostName, s.CountryId, countryName, s.EndOfSupportDate.Value);
            if (s.EndOfLifeDate != null && s.EndOfLifeDate <= threshold)
                Add("Server", "Server", ExpirationType.EndOfLife, s.HostName, s.CountryId, countryName, s.EndOfLifeDate.Value);
        }

        var licenses = await _db.Licenses.Include(l => l.Country)
            .Where(l => (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                     || (l.ExpirationDate != null && l.ExpirationDate <= threshold))
            .ToListAsync();
        foreach (var l in licenses)
        {
            var countryName = l.Country?.DisplayName ?? l.Country?.Name ?? "?";
            if (l.SupportEndDate != null && l.SupportEndDate <= threshold)
                Add("License", "License (Support)", ExpirationType.License, l.LicenseName, l.CountryId, countryName, l.SupportEndDate.Value);
            if (l.ExpirationDate != null && l.ExpirationDate <= threshold)
                Add("License", "License (Expiration)", ExpirationType.License, l.LicenseName, l.CountryId, countryName, l.ExpirationDate.Value);
        }

        var circuits = await _db.Circuits.Include(c => c.Country)
            .Where(c => c.EndDate != null && c.EndDate <= threshold)
            .ToListAsync();
        foreach (var c in circuits)
        {
            var countryName = c.Country?.DisplayName ?? c.Country?.Name ?? "?";
            Add("Circuit", "Circuit", ExpirationType.EndOfSupport, c.CircuitType, c.CountryId, countryName, c.EndDate!.Value);
        }

        _logger.LogInformation("Expiration check: {ExpiredCount} expired, {UpcomingCount} upcoming (within {Days} days).",
            expired.Count, upcoming.Count, _upcomingWindowDays);

        if (expired.Count == 0 && upcoming.Count == 0)
        {
            _logger.LogInformation("Nothing to notify.");
            return;
        }

        var groups = await BuildCountryGroupsAsync(expired, upcoming);
        await _emailService.NotifyAsync(groups);
    }

    private async Task<List<CountryNotificationGroup>> BuildCountryGroupsAsync(
        List<ExpiringItemNotification> expired, List<ExpiringItemNotification> upcoming)
    {
        var adminRecipients = await _db.YdUserRoles
            .Where(ur => ur.Role!.RoleName == RoleNames.Admin)
            .Select(ur => ur.User!)
            .Where(u => u.IsActive && u.ReceiveExpirationNotifications && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new NotificationRecipient { FullName = u.FullName, Email = u.Email! })
            .ToListAsync();

        var countries = await _db.Countries.ToDictionaryAsync(c => c.Id);

        var byCountry = expired.Concat(upcoming)
            .Select(i => i.CountryId)
            .Distinct()
            .ToList();

        var groups = new List<CountryNotificationGroup>();
        foreach (var countryId in byCountry)
        {
            countries.TryGetValue(countryId, out var country);
            var countryName = country != null ? (country.DisplayName ?? country.Name) : "?";

            var countryRecipients = country != null
                ? await _db.YdUsers
                    .Where(u => u.IsActive && u.ReceiveExpirationNotifications && u.RepositoryName == country.Name && !string.IsNullOrWhiteSpace(u.Email))
                    .Select(u => new NotificationRecipient { FullName = u.FullName, Email = u.Email! })
                    .ToListAsync()
                : new List<NotificationRecipient>();

            var recipients = countryRecipients.Concat(adminRecipients)
                .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            groups.Add(new CountryNotificationGroup
            {
                CountryId = countryId,
                CountryName = countryName,
                Recipients = recipients,
                ExpiredItems = expired.Where(i => i.CountryId == countryId).ToList(),
                UpcomingItems = upcoming.Where(i => i.CountryId == countryId).ToList()
            });
        }

        return groups;
    }
}
