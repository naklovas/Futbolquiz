using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class ServerConfiguration : IEntityTypeConfiguration<Server>
{
    public void Configure(EntityTypeBuilder<Server> builder)
    {
        builder.ToTable("Servers", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.HostName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.OperatingSystem).HasMaxLength(255);
        builder.Property(x => x.Brand).HasMaxLength(100);
        builder.Property(x => x.Model).HasMaxLength(150);
        builder.Property(x => x.SerialNo).HasMaxLength(150);
        builder.Property(x => x.VendorSupplier).HasMaxLength(150);
        builder.Property(x => x.Branch).HasMaxLength(150);
        builder.Property(x => x.Location).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
        builder.Property(x => x.ApplianceType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LocationCategory)
            .HasConversion(
                v => v.ToString(),
                v => v == "EVM" ? LocationCategory.Turkiye : Enum.Parse<LocationCategory>(v))
            .HasMaxLength(20);
        builder.Property(x => x.SiteRole).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(x => x.Country)
            .WithMany(c => c.Servers)
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeviceProfile)
            .WithMany()
            .HasForeignKey(x => x.DeviceProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.HostPhysicalDevice)
            .WithMany()
            .HasForeignKey(x => x.HostPhysicalDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.HostPhysicalDeviceId);
    }
}
