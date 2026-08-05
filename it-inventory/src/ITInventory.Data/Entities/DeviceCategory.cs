namespace ITInventory.Data.Entities;

public class DeviceCategory
{
    public int Id { get; set; }

    /// <summary>Sunucu, Ağ Cihazı, Güvenlik, Ses/Görüntü, Depolama, Yazıcı, İstemci, Diğer.</summary>
    public string Name { get; set; } = string.Empty;

    public ICollection<DeviceProfileCatalog> DeviceProfiles { get; set; } = new List<DeviceProfileCatalog>();
}
