namespace ITInventory.Web.Models.Home;

public class DashboardViewModel
{
    public int PhysicalDeviceCount { get; set; }
    public int ServerCount { get; set; }
    public int LicenseCount { get; set; }
    public int CircuitCount { get; set; }

    public List<ExpiringItem> ExpiredItems { get; set; } = new();
    public List<ExpiringItem> UpcomingItems { get; set; } = new();
}

public enum ExpirationType
{
    License,
    EndOfSupport,
    EndOfLife
}

public class ExpiringItem
{
    public string Type { get; set; } = string.Empty;
    public ExpirationType ExpirationType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Country { get; set; }
    public DateTime ExpiresAt { get; set; }
}
