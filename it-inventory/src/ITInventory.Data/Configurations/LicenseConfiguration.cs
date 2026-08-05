using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.ToTable("Licenses", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LicenseName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VendorSupplier).HasMaxLength(150);
        builder.Property(x => x.Branch).HasMaxLength(150);
        builder.Property(x => x.Location).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasOne(x => x.Country)
            .WithMany(c => c.Licenses)
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CountryId);
    }
}
