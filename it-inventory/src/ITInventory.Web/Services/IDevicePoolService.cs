using ITInventory.Web.Models.DevicePool;

namespace ITInventory.Web.Services;

public interface IDevicePoolService
{
    /// <summary>
    /// Bir ülkenin Nessus taramasında keşfedilen, IP bazında gruplanmış cihaz listesini döner.
    /// </summary>
    Task<List<DiscoveredDeviceDto>> GetDevicesForCountryAsync(int countryId, int? categoryId = null);
}
