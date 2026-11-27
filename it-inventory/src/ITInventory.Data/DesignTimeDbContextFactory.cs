using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ITInventory.Data;

/// <summary>
/// `dotnet ef migrations` komutları için tasarım zamanı context üretimi.
/// Buradaki bağlantı dizesi sadece migration oluştururken kullanılır, runtime'da
/// gerçek bağlantı Web projesindeki appsettings.json'dan gelir.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ITInventoryDbContext>
{
    public ITInventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ITInventoryDbContext>();
        optionsBuilder.UseSqlServer("Server=.;Database=ITInventory;Trusted_Connection=True;TrustServerCertificate=True;");
        return new ITInventoryDbContext(optionsBuilder.Options);
    }
}
