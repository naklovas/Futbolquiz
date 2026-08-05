using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class YdUserRoleConfiguration : IEntityTypeConfiguration<YdUserRole>
{
    public void Configure(EntityTypeBuilder<YdUserRole> builder)
    {
        builder.ToTable("YDUserRoles", "dbo", t => t.ExcludeFromMigrations());

        builder.HasKey(x => new { x.UserId, x.RoleId });

        builder.HasOne(x => x.User)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(x => x.UserId);

        builder.HasOne(x => x.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(x => x.RoleId);
    }
}
