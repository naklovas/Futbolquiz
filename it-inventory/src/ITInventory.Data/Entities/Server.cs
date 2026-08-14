using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Sunucu (makine) envanteri; fiziksel cihazlardan ayrı tutulur. IP/Port/Application
/// bilgisi burada değil, ayrı ServerEndpoint kayıtlarında tutulur (bir sunucunun
/// birden fazla uygulama/port eşlemesi olabilir).
/// </summary>
public class Server : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public int? DeviceProfileId { get; set; }
    public DeviceProfileCatalog? DeviceProfile { get; set; }

    public int? SourceZiraatYdId { get; set; }

    public string HostName { get; set; } = string.Empty;
    public ApplianceType ApplianceType { get; set; }
    public LocationCategory LocationCategory { get; set; }
    public SiteRole SiteRole { get; set; }

    /// <summary>Sanal ise bu sunucunun üzerinde çalıştığı fiziksel host (Physical Devices'a bağlantı).</summary>
    public int? HostPhysicalDeviceId { get; set; }
    public PhysicalDevice? HostPhysicalDevice { get; set; }

    public string? OperatingSystem { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SerialNo { get; set; }
    public string? VendorSupplier { get; set; }
    public string? Branch { get; set; }

    /// <summary>Fiziksel sunucular için konum; sanal sunucularda genelde host'un konumu geçerlidir.</summary>
    public string? Location { get; set; }

    public DateTime? StartOfSupportDate { get; set; }
    public DateTime? EndOfSupportDate { get; set; }
    public DateTime? EndOfLifeDate { get; set; }

    public string? Notes { get; set; }

    public ICollection<ServerEndpoint> Endpoints { get; set; } = new List<ServerEndpoint>();
}
