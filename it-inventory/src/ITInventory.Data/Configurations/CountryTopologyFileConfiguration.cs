using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class CountryTopologyFileConfiguration : IEntityTypeConfiguration<CountryTopologyFile>
{
    public void Configure(EntityTypeBuilder<CountryTopologyFile> builder)
    {
        builder.ToTable("CountryTopologyFiles", "dbo");

        builder.HasKey(x => x.CountryId);
        builder.Property(x => x.CountryId).ValueGeneratedNever();

        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(150).IsRequired();
        builder.Property(x => x.FileData).IsRequired();
        builder.Property(x => x.UploadedBy).HasMaxLength(100);

        builder.HasOne(x => x.Country)
            .WithMany()
            .HasForeignKey(x => x.CountryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
