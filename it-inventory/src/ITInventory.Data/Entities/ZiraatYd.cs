namespace ITInventory.Data.Entities;

/// <summary>
/// Nessus/Tenable taramalarını SQL Server'a aktaran harici servis tarafından beslenen tablo.
/// Bu uygulama bu tabloyu sadece okur; şema harici servis tarafından yönetilir.
/// </summary>
public class ZiraatYd
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; }
    public string? Protocol { get; set; }
    public int? PluginId { get; set; }
    public string? DnsName { get; set; }
    public int? RepositoryId { get; set; }
    public string? RepositoryName { get; set; }
    public string? MacAddress { get; set; }
    public string? NetbiosName { get; set; }
    public string? OperatingSystem { get; set; }
    public DateTime? FirstSeenAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? TenableFirstSeen { get; set; }
    public DateTime? TenableLastSeen { get; set; }
    public string? DeviceProfile { get; set; }
    public string? ProfileSource { get; set; }
}
