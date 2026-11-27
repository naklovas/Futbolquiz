using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class VropsDatacenterConfiguration : IEntityTypeConfiguration<VropsDatacenter>
{
    public void Configure(EntityTypeBuilder<VropsDatacenter> builder)
    {
        builder.ToTable("VropsDatacenters", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DatacenterName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ResourceKind).HasMaxLength(100).IsRequired()
               .HasDefaultValue("VMwareAdapter Instance");

        // Ayni kaynak iki ulkeye baglanamaz.
        builder.HasIndex(x => x.DatacenterName).IsUnique();

        builder.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
