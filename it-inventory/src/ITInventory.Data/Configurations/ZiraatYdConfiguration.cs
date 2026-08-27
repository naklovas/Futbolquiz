using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class ZiraatYdConfiguration : IEntityTypeConfiguration<ZiraatYd>
{
    public void Configure(EntityTypeBuilder<ZiraatYd> builder)
    {
        // Harici Nessus senkronizasyon servisi tarafından beslenen tablo - salt okunur.
        builder.ToTable("Ziraat_YD", "dbo", t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.IpAddress).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Protocol).HasMaxLength(20);
        builder.Property(x => x.DnsName).HasMaxLength(255);
        builder.Property(x => x.RepositoryName).HasMaxLength(255);
        builder.Property(x => x.MacAddress).HasColumnName("macAddress").HasMaxLength(100);
        builder.Property(x => x.NetbiosName).HasColumnName("netbiosName").HasMaxLength(255);
        builder.Property(x => x.OperatingSystem).HasColumnName("operatingSystem").HasMaxLength(500);
        builder.Property(x => x.DeviceProfile).HasMaxLength(100);
        builder.Property(x => x.ProfileSource).HasMaxLength(50);
    }
}
