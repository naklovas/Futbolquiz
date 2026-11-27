using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class YdRoleConfiguration : IEntityTypeConfiguration<YdRole>
{
    public void Configure(EntityTypeBuilder<YdRole> builder)
    {
        // Mevcut tablo - EF migration'ları bu tabloyu oluşturmaya/değiştirmeye çalışmasın.
        builder.ToTable("YDRoles", "dbo", t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(255);
    }
}
