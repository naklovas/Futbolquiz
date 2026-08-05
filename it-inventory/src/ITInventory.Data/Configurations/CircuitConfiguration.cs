using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class CircuitConfiguration : IEntityTypeConfiguration<Circuit>
{
    public void Configure(EntityTypeBuilder<Circuit> builder)
    {
        builder.ToTable("Circuits", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CircuitType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CircuitCapacity).HasMaxLength(50);
        builder.Property(x => x.Provider).HasMaxLength(150);
        builder.Property(x => x.Branch).HasMaxLength(150);
        builder.Property(x => x.Location).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasOne(x => x.Country)
            .WithMany(c => c.Circuits)
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.CountryId);
    }
}
