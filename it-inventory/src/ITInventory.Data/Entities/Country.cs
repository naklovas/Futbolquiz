using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

public class Country : AuditableEntity
{
    public int Id { get; set; }

    /// <summary>Ziraat_YD.RepositoryName ile birebir eşleşir (Nessus repository/ülke adı).</summary>
    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<PhysicalDevice> PhysicalDevices { get; set; } = new List<PhysicalDevice>();
    public ICollection<Server> Servers { get; set; } = new List<Server>();
    public ICollection<License> Licenses { get; set; } = new List<License>();
    public ICollection<Circuit> Circuits { get; set; } = new List<Circuit>();
}
