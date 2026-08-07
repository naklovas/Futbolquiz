using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Sunucu envanteri; fiziksel cihazlardan ayrı tutulur, ileride port/IP üzerinden
/// uygulama eşleştirmesi yapılabilmesi için IpAddress/Port bilgisi barındırır.
/// </summary>
public class Server : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public int? DeviceProfileId { get; set; }
    public DeviceProfileCatalog? DeviceProfile { get; set; }

    public int? SourceZiraatYdId { get; set; }

    /// <summary>Bu sunucuda çalışan/barınan uygulama (Servers &amp; Applications ilişkisi).</summary>
    public int? ApplicationId { get; set; }
    public Application? Application { get; set; }

    public string HostName { get; set; } = string.Empty;
    public ApplianceType ApplianceType { get; set; }
    public string? IpAddress { get; set; }
    public string? OperatingSystem { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNo { get; set; }
    public string? VendorSupplier { get; set; }
    public int? Port { get; set; }
    public string? Branch { get; set; }
    public string Location { get; set; } = string.Empty;

    public DateTime? StartOfSupportDate { get; set; }
    public DateTime? EndOfSupportDate { get; set; }
    public DateTime? EndOfLifeDate { get; set; }

    public string? Notes { get; set; }
}
