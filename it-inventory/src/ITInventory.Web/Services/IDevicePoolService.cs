using ITInventory.Web.Models.DevicePool;

namespace ITInventory.Web.Services;

public interface IDevicePoolService
{
    Task<List<DiscoveredDeviceDto>> GetDevicesForCountryAsync(int countryId, int? categoryId = null);
}
