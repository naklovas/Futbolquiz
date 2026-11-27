namespace ITInventory.Web.Services;

public static class AppClaimTypes
{
    public const string FullName = "full_name";

    /// <summary>
    /// Kimligin DOGRULANDIGI AD domaini.
    ///
    /// Birden fazla domain devrede oldugundan, giris yapan kisinin hangi
    /// domainden geldigini bilmesi gerekiyor: ayni kullanici adi iki
    /// domainde bulunabiliyor ve yetkileri farkli oluyor. Bunu ekranda
    /// gostermeden kimse hangi hesapla calistigini anlayamaz.
    /// </summary>
    public const string Domain = "ad_domain";

    /// <summary>Kullanıcının bağlı olduğu ülke/şube (YDUsers.RepositoryName).</summary>
    public const string Country = "country";

    /// <summary>dbo.Countries.Id - RepositoryName için bir Country kaydı tanımlıysa set edilir.</summary>
    public const string CountryId = "country_id";

    /// <summary>dbo.Countries.DisplayName - varsa Country yerine ekranda bu gösterilir.</summary>
    public const string CountryDisplayName = "country_display_name";
}
