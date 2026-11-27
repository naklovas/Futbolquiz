using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class CompanyRoleConfiguration : IEntityTypeConfiguration<CompanyRole>
{
    public void Configure(EntityTypeBuilder<CompanyRole> builder)
    {
        builder.ToTable("CompanyRoles", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion<int>();

        builder.HasOne(x => x.Company)
            .WithMany(c => c.Roles)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Ayni rol ayni firmada iki kez bulunmasin: form coklu secim oldugu
        // icin cift gonderim mumkun ve listede "Support, Support" gorunurdu.
        builder.HasIndex(x => new { x.CompanyId, x.Role }).IsUnique();
    }
}
