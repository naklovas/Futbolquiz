namespace ITInventory.ExpirationNotifier.Models;

public enum ExpirationType
{
    License,
    EndOfSupport,
    EndOfLife
}

public class ExpiringItemNotification
{
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ExpirationType ExpirationType { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class NotificationRecipient
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// One country's worth of expiring items plus who should be emailed about them:
/// that country's users (YDUsers.RepositoryName == Countries.Name, same match the
/// web app uses to scope a logged-in user to a country) union every admin.
/// </summary>
public class CountryNotificationGroup
{
    public int CountryId { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public List<NotificationRecipient> Recipients { get; set; } = new();
    public List<ExpiringItemNotification> ExpiredItems { get; set; } = new();
    public List<ExpiringItemNotification> UpcomingItems { get; set; } = new();
}
