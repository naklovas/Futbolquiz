using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> builder)
    {
        builder.ToTable("Applications", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Company)
            .WithMany(c => c.Applications)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CountryId);
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.LicenseId);
    }
}
