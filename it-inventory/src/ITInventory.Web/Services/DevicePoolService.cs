using ITInventory.Data;
using ITInventory.Web.Models.DevicePool;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Services;

public class DevicePoolService : IDevicePoolService
{
    private readonly ITInventoryDbContext _db;

    public DevicePoolService(ITInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<List<DiscoveredDeviceDto>> GetDevicesForRepositoryAsync(string repositoryName, int? categoryId = null)
    {
        var rawRows = await _db.ZiraatYds
            .Where(z => z.RepositoryName == repositoryName)
            .ToListAsync();

        var profiles = await _db.DeviceProfileCatalogs.Include(p => p.Category).ToListAsync();
        var profileLookup = profiles.ToDictionary(p => p.ProfileName, p => p, StringComparer.OrdinalIgnoreCase);

        var usedIps = (await _db.PhysicalDevices
                .Where(d => d.IpAddress != null)
                .Select(d => d.IpAddress!)
                .ToListAsync())
            .Concat(await _db.ServerEndpoints
                .Where(e => e.IpAddress != null)
                .Select(e => e.IpAddress!)
                .ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var devices = rawRows
            .GroupBy(z => z.IpAddress)
            .Select(g => g.OrderByDescending(z => z.LastSeenAt ?? z.TenableLastSeen ?? DateTime.MinValue).First())
            .Select(z =>
            {
                profileLookup.TryGetValue(z.DeviceProfile ?? string.Empty, out var profile);
                return new DiscoveredDeviceDto
                {
                    ZiraatYdId = z.Id,
                    IpAddress = z.IpAddress,
                    DnsName = z.DnsName,
                    NetbiosName = z.NetbiosName,
                    MacAddress = z.MacAddress,
                    OperatingSystem = z.OperatingSystem,
                    DeviceProfile = z.DeviceProfile,
                    CategoryId = profile?.CategoryId,
                    CategoryName = profile?.Category?.Name,
                    LastSeenAt = z.LastSeenAt ?? z.TenableLastSeen,
                    AlreadyInInventory = usedIps.Contains(z.IpAddress)
                };
            })
            .Where(d => categoryId == null || d.CategoryId == categoryId)
            .OrderBy(d => d.IpAddress)
            .ToList();

        return devices;
    }
}
