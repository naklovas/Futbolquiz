using ITInventory.ExpirationNotifier.Models;

namespace ITInventory.ExpirationNotifier.Services;

public interface IEmailNotificationService
{
    Task NotifyAsync(IReadOnlyList<CountryNotificationGroup> groups);
}
