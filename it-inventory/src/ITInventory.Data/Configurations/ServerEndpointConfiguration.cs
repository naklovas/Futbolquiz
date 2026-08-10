using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class ServerEndpointConfiguration : IEntityTypeConfiguration<ServerEndpoint>
{
    public void Configure(EntityTypeBuilder<ServerEndpoint> builder)
    {
        builder.ToTable("ServerEndpoints", "dbo");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IpAddress).HasMaxLength(50);
        builder.Property(x => x.CreatedBy).HasMaxLength(100);
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);

        builder.HasOne(x => x.Server)
            .WithMany(s => s.Endpoints)
            .HasForeignKey(x => x.ServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Application)
            .WithMany(a => a.ServerEndpoints)
            .HasForeignKey(x => x.ApplicationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.ServerId);
        builder.HasIndex(x => x.ApplicationId);
    }
}
