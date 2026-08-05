using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Data;

public class ITInventoryDbContext : DbContext
{
    public ITInventoryDbContext(DbContextOptions<ITInventoryDbContext> options) : base(options)
    {
    }

    public DbSet<Country> Countries => Set<Country>();
    public DbSet<DeviceCategory> DeviceCategories => Set<DeviceCategory>();
    public DbSet<DeviceProfileCatalog> DeviceProfileCatalogs => Set<DeviceProfileCatalog>();
    public DbSet<ZiraatYd> ZiraatYds => Set<ZiraatYd>();
    public DbSet<YdUser> YdUsers => Set<YdUser>();
    public DbSet<YdRole> YdRoles => Set<YdRole>();
    public DbSet<YdUserRole> YdUserRoles => Set<YdUserRole>();
    public DbSet<PhysicalDevice> PhysicalDevices => Set<PhysicalDevice>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Circuit> Circuits => Set<Circuit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ITInventoryDbContext).Assembly);
    }
}
