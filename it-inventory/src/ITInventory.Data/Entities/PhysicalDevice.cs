using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Ağ, güvenlik, ses/görüntü, depolama, yazıcı vb. fiziksel/sanal cihaz envanteri.
/// Elle girilebilir ya da ülkenin cihaz havuzundan (Ziraat_YD) seçilerek oluşturulabilir.
/// </summary>
public class PhysicalDevice : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public int CategoryId { get; set; }
    public DeviceCategory? Category { get; set; }

    public int? DeviceProfileId { get; set; }
    public DeviceProfileCatalog? DeviceProfile { get; set; }

    /// <summary>Cihaz havuzundan seçildiyse kaynak Ziraat_YD.Id, iz sürmek için.</summary>
    public int? SourceZiraatYdId { get; set; }

    public string DeviceName { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public ApplianceType ApplianceType { get; set; }
    public LocationCategory LocationCategory { get; set; }
    public SiteRole SiteRole { get; set; }
    public string? SoftwareVersion { get; set; }
    public string? SerialNo { get; set; }
    public string? IpAddress { get; set; }
    public string? MgmtIp { get; set; }
    public string? Branch { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? VendorSupplier { get; set; }
    public string? LicenceInfo { get; set; }

    public DateTime? StartOfSupportDate { get; set; }
    public DateTime? EndOfSupportDate { get; set; }
    public DateTime? EndOfLifeDate { get; set; }

    public string? Notes { get; set; }
}
