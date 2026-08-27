using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class DeviceCategoryConfiguration : IEntityTypeConfiguration<DeviceCategory>
{
    public void Configure(EntityTypeBuilder<DeviceCategory> builder)
    {
        builder.ToTable("DeviceCategories", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasData(SeedData.Categories.Select(c => new DeviceCategory
        {
            Id = c.Id,
            Name = c.Name
        }));
    }
}
