using ITInventory.Data.Common;
using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class PhysicalDeviceConfiguration : IEntityTypeConfiguration<PhysicalDevice>
{
    public void Configure(EntityTypeBuilder<PhysicalDevice> builder)
    {
        builder.ToTable("PhysicalDevices", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DeviceName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Brand).HasMaxLength(100);
        builder.Property(x => x.Model).HasMaxLength(150);
        builder.Property(x => x.SoftwareVersion).HasMaxLength(150);
        builder.Property(x => x.SerialNo).HasMaxLength(150);
        builder.Property(x => x.IpAddress).HasMaxLength(50);
        builder.Property(x => x.MgmtIp).HasMaxLength(50);
        builder.Property(x => x.Branch).HasMaxLength(150);
        builder.Property(x => x.Location).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VendorSupplier).HasMaxLength(150);
        builder.Property(x => x.LicenceInfo).HasMaxLength(150);
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
            .WithMany(c => c.PhysicalDevices)
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DeviceProfile)
            .WithMany()
            .HasForeignKey(x => x.DeviceProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.CategoryId);
    }
}
