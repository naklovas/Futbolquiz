namespace ITInventory.Web.Models.DevicePool;

public class DiscoveredDeviceDto
{
    public int ZiraatYdId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string? DnsName { get; set; }
    public string? NetbiosName { get; set; }
    public string? MacAddress { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceProfile { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool AlreadyInInventory { get; set; }

    /// <summary>Only populated when showing devices across all countries (admin, no country filter selected).</summary>
    public string? Country { get; set; }
}
