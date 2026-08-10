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

    /// <summary>
    /// String, not the ExpirationType enum -- System.Text.Json serializes enums as their
    /// numeric value by default, and the dashboard's client-side JS (badges, filter, chart
    /// grouping) matches against the string names ("License"/"EndOfSupport"/"EndOfLife").
    /// </summary>
    public string ExpirationType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Country { get; set; }
    public DateTime ExpiresAt { get; set; }
}
