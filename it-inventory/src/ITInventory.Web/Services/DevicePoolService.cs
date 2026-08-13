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

        // IP matching alone misses Servers added "from pool": a Server's IP now lives on a
        // separate ServerEndpoint row (since the host/endpoint split) that isn't auto-created
        // when adding from the pool, so its originating IP never lands in usedIps above and it
        // never shows as "already in inventory". SourceZiraatYdId is set on both entities
        // whenever "Add as Device"/"Add as Server" is used, regardless of whether an endpoint
        // was ever added, so it's a reliable fallback match.
        var usedSourceIds = (await _db.PhysicalDevices
                .Where(d => d.SourceZiraatYdId != null)
                .Select(d => d.SourceZiraatYdId!.Value)
                .ToListAsync())
            .Concat(await _db.Servers
                .Where(s => s.SourceZiraatYdId != null)
                .Select(s => s.SourceZiraatYdId!.Value)
                .ToListAsync())
            .ToHashSet();

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
                    AlreadyInInventory = usedIps.Contains(z.IpAddress) || usedSourceIds.Contains(z.Id)
                };
            })
            .Where(d => categoryId == null || d.CategoryId == categoryId)
            .OrderBy(d => d.IpAddress)
            .ToList();

        return devices;
    }
}
