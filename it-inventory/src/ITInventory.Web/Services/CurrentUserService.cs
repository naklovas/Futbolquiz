using System.Security.Claims;
using ITInventory.Data.Common;

namespace ITInventory.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Username => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
    public string FullName => User.FindFirstValue(AppClaimTypes.FullName) ?? Username;
    public string? Country => User.FindFirstValue(AppClaimTypes.Country);

    public int? CountryId => int.TryParse(User.FindFirstValue(AppClaimTypes.CountryId), out var id) ? id : null;

    public bool IsAdmin => User.IsInRole(RoleNames.Admin);
    public bool CanEdit => User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.CountryManager);
}
