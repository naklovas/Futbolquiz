using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class DeviceProfileCatalogConfiguration : IEntityTypeConfiguration<DeviceProfileCatalog>
{
    public void Configure(EntityTypeBuilder<DeviceProfileCatalog> builder)
    {
        builder.ToTable("DeviceProfileCatalog", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProfileName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.HasIndex(x => x.ProfileName).IsUnique();

        builder.HasOne(x => x.Category)
            .WithMany(c => c.DeviceProfiles)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasData(SeedData.DeviceProfiles.Select((p, idx) => new DeviceProfileCatalog
        {
            Id = idx + 1,
            ProfileName = p.ProfileName,
            DisplayName = p.DisplayName,
            CategoryId = p.CategoryId
        }));
    }
}
