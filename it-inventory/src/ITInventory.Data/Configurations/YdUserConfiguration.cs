using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class YdUserConfiguration : IEntityTypeConfiguration<YdUser>
{
    public void Configure(EntityTypeBuilder<YdUser> builder)
    {
        builder.ToTable("YDUsers", "dbo", t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(100);
        builder.Property(x => x.RepositoryName).HasMaxLength(255);
        builder.HasIndex(x => x.Username).IsUnique();
    }
}
