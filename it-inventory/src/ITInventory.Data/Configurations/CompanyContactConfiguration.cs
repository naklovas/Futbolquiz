using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class CompanyContactConfiguration : IEntityTypeConfiguration<CompanyContact>
{
    public void Configure(EntityTypeBuilder<CompanyContact> builder)
    {
        builder.ToTable("CompanyContacts", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PersonName).HasMaxLength(150);
        builder.Property(x => x.Title).HasMaxLength(150);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Email).HasMaxLength(150);

        builder.HasOne(x => x.Company)
            .WithMany(c => c.Contacts)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CompanyId);
    }
}
