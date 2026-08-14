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
    public DbSet<ServerEndpoint> ServerEndpoints => Set<ServerEndpoint>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Circuit> Circuits => Set<Circuit>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<OriginCountry> OriginCountries => Set<OriginCountry>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<CompanyContact> CompanyContacts => Set<CompanyContact>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<TestLoginCredential> TestLoginCredentials => Set<TestLoginCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ITInventoryDbContext).Assembly);
    }
}
