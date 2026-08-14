using ITInventory.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITInventory.Data.Configurations;

public class TestLoginCredentialConfiguration : IEntityTypeConfiguration<TestLoginCredential>
{
    public void Configure(EntityTypeBuilder<TestLoginCredential> builder)
    {
        builder.ToTable("TestLoginConfig", "dbo");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PasswordHash).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(100);
    }
}
