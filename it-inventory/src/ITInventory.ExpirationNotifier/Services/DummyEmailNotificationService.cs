using ITInventory.ExpirationNotifier.Models;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Placeholder until the real mail service is wired in. Logs what would be sent to whom so
/// the call site (ExpirationCheckService) and its data shape are already final; swap the
/// registration in Program.cs for a real implementation later without touching anything else.
/// </summary>
public class DummyEmailNotificationService : IEmailNotificationService
{
    private readonly ILogger<DummyEmailNotificationService> _logger;

    public DummyEmailNotificationService(ILogger<DummyEmailNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(IReadOnlyList<CountryNotificationGroup> groups)
    {
        foreach (var group in groups)
        {
            if (group.Recipients.Count == 0)
            {
                _logger.LogWarning("[DUMMY EMAIL] {Country}: {ExpiredCount} expired, {UpcomingCount} upcoming, but no recipient email is configured (no matching YDUsers.Email and no admin has one either).",
                    group.CountryName, group.ExpiredItems.Count, group.UpcomingItems.Count);
                continue;
            }

            var recipientList = string.Join(", ", group.Recipients.Select(r => $"{r.FullName} <{r.Email}>"));
            _logger.LogInformation("[DUMMY EMAIL] {Country} -> {Recipients}: {ExpiredCount} expired, {UpcomingCount} upcoming.",
                group.CountryName, recipientList, group.ExpiredItems.Count, group.UpcomingItems.Count);

            foreach (var item in group.ExpiredItems)
                _logger.LogInformation("[DUMMY EMAIL]   EXPIRED  - {Label} '{Name}' - {ExpirationType} - {ExpiresAt:dd.MM.yyyy}",
                    item.Label, item.Name, item.ExpirationType, item.ExpiresAt);

            foreach (var item in group.UpcomingItems)
                _logger.LogInformation("[DUMMY EMAIL]   UPCOMING - {Label} '{Name}' - {ExpirationType} - {ExpiresAt:dd.MM.yyyy}",
                    item.Label, item.Name, item.ExpirationType, item.ExpiresAt);
        }

        return Task.CompletedTask;
    }
}
