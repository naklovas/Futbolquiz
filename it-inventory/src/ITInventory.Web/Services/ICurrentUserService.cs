namespace ITInventory.Web.Services;

public interface ICurrentUserService
{
    int UserId { get; }
    string Username { get; }
    string FullName { get; }

    /// <summary>Kullanıcının ülkesi (RepositoryName). Admin için null olabilir.</summary>
    string? Country { get; }

    /// <summary>dbo.Countries.Id. RepositoryName için henüz bir Country kaydı yoksa null.</summary>
    int? CountryId { get; }

    bool IsAdmin { get; }

    /// <summary>Admin veya country_manager - ekleme/düzenleme yapabilir.</summary>
    bool CanEdit { get; }
}
