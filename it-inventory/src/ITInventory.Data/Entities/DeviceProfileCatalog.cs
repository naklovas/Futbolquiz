namespace ITInventory.Data.Entities;

public class DeviceProfileCatalog
{
    public int Id { get; set; }

    /// <summary>Ziraat_YD.DeviceProfile ile birebir eşleşir.</summary>
    public string ProfileName { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public DeviceCategory? Category { get; set; }
}
