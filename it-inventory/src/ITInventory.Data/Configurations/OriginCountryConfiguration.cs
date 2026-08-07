using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class OriginCountryConfiguration : IEntityTypeConfiguration<OriginCountry>
{
    public void Configure(EntityTypeBuilder<OriginCountry> builder)
    {
        builder.ToTable("OriginCountries", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasIndex(x => x.Name).IsUnique();
    }
}
