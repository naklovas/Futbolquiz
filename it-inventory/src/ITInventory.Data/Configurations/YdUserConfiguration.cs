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
        builder.Property(x => x.Domain).HasMaxLength(100);
        builder.Property(x => x.FullName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(100);
        builder.Property(x => x.RepositoryName).HasMaxLength(255);
        builder.Property(x => x.ReceiveExpirationNotifications).HasDefaultValue(true);
        // Benzersizlik artik (Username, Domain) ciftinde. Kullanicilar
        // birden fazla domainden geliyor ve ayni kullanici adi iki domainde
        // bulunabilir; Username tek basina UNIQUE kalirsa ikincisi eklenemez.
        // Domain'i NULL olan ESKI kayitlar varsayilan domaine ait sayilir ve
        // SQL Server NULL'lari esit saydigi icin her ad icin en fazla bir
        // tane olabilir -- istedigimiz de bu.
        builder.HasIndex(x => new { x.Username, x.Domain }).IsUnique();
    }
}
