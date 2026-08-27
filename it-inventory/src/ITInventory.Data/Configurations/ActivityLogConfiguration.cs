using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.ToTable("ActivityLogs", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150);
        builder.Property(x => x.CountryName).HasMaxLength(150);
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityName).HasMaxLength(255);
        builder.Property(x => x.Details).HasMaxLength(1000);
        builder.Property(x => x.EnvironmentName).HasMaxLength(64);

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.Username);
        builder.HasIndex(x => x.EntityType);
    }
}
