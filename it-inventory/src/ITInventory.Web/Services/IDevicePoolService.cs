using ITInventory.Web.Models.DevicePool;

namespace ITInventory.Web.Services;

public interface IDevicePoolService
{
    Task<List<DiscoveredDeviceDto>> GetDevicesForCountryAsync(int countryId, int? categoryId = null);

    /// <summary>Admin-only: returns discovered devices across all countries, unfiltered by RepositoryName.</summary>
    Task<List<DiscoveredDeviceDto>> GetAllDevicesAsync(int? categoryId = null);
}
