namespace ITInventory.Web.Services;

public static class AppClaimTypes
{
    public const string FullName = "full_name";

    /// <summary>Kullanıcının bağlı olduğu ülke/şube (YDUsers.RepositoryName).</summary>
    public const string Country = "country";

    /// <summary>dbo.Countries.Id - RepositoryName için bir Country kaydı tanımlıysa set edilir.</summary>
    public const string CountryId = "country_id";

    /// <summary>dbo.Countries.DisplayName - varsa Country yerine ekranda bu gösterilir.</summary>
    public const string CountryDisplayName = "country_display_name";
}
