using ITInventory.Web.Models.DevicePool;

namespace ITInventory.Web.Services;

public interface IDevicePoolService
{
    /// <summary>Devices discovered for a given Ziraat_YD.RepositoryName value (real data, not the Countries table).</summary>
    Task<List<DiscoveredDeviceDto>> GetDevicesForRepositoryAsync(string repositoryName, int? categoryId = null);
}
