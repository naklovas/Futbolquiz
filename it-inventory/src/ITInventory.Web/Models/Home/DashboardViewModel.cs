namespace ITInventory.Web.Models.Home;

public class DashboardViewModel
{
    public int PhysicalDeviceCount { get; set; }
    public int ServerCount { get; set; }
    public int LicenseCount { get; set; }
    public int CircuitCount { get; set; }

    public List<ExpiringItem> ExpiringItems { get; set; } = new();
}

public class ExpiringItem
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Country { get; set; }
    public DateTime ExpiresAt { get; set; }
}
