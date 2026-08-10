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
    public string? Country { get; set; }
    public DateTime ExpiresAt { get; set; }
}
