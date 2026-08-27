namespace ITInventory.Data.Entities;

public class DeviceProfileCatalog
{
    public int Id { get; set; }

    /// <summary>Ziraat_YD.DeviceProfile ile birebir eşleşir. Değiştirilmemelidir.</summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>Ekranda gösterilen İngilizce etiket. Boşsa ProfileName gösterilir.</summary>
    public string? DisplayName { get; set; }

    public int? CategoryId { get; set; }
    public DeviceCategory? Category { get; set; }
}
