using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("Locations", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Branch).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Class).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CountryId, x.Branch }).IsUnique();
    }
}
