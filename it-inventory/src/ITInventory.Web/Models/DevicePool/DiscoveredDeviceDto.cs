namespace ITInventory.Web.Models.DevicePool;

/// <summary>
/// Ziraat_YD tablosundaki port/servis bazlı satırların IP'ye göre gruplanmış,
/// tek bir cihazı temsil eden özet görünümü.
/// </summary>
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
}
