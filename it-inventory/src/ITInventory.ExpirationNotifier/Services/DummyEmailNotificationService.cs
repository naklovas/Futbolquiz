using ITInventory.ExpirationNotifier.Models;
using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Services;

/// <summary>
/// Placeholder until the real mail service is wired in. Logs what would be sent so the
/// call site (ExpirationCheckService) and its data shape are already final; swap the
/// registration in Program.cs for a real implementation later without touching anything else.
/// </summary>
public class DummyEmailNotificationService : IEmailNotificationService
{
    private readonly ILogger<DummyEmailNotificationService> _logger;

    public DummyEmailNotificationService(ILogger<DummyEmailNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyAsync(IReadOnlyList<ExpiringItemNotification> expiredItems, IReadOnlyList<ExpiringItemNotification> upcomingItems)
    {
        _logger.LogInformation("[DUMMY EMAIL] Would send notification: {ExpiredCount} expired, {UpcomingCount} upcoming.",
            expiredItems.Count, upcomingItems.Count);

        foreach (var item in expiredItems)
            _logger.LogInformation("[DUMMY EMAIL]   EXPIRED  - {Label} '{Name}' ({Country}) - {ExpirationType} - {ExpiresAt:dd.MM.yyyy}",
                item.Label, item.Name, item.Country ?? "-", item.ExpirationType, item.ExpiresAt);

        foreach (var item in upcomingItems)
            _logger.LogInformation("[DUMMY EMAIL]   UPCOMING - {Label} '{Name}' ({Country}) - {ExpirationType} - {ExpiresAt:dd.MM.yyyy}",
                item.Label, item.Name, item.Country ?? "-", item.ExpirationType, item.ExpiresAt);

        return Task.CompletedTask;
    }
}
