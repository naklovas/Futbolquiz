using System.Security.Claims;
using ITInventory.Data.Common;
using ITInventory.Web.Services;

namespace ITInventory.Web.Common;

/// <summary>
/// Reads the signed-in user's data scope straight off their authentication claims.
///
/// ICurrentUserService reads exactly these same claims (see CurrentUserService) and stays the
/// right thing to use everywhere else -- CanEdit, Username, UserId and so on. These helpers
/// exist for the one place where it matters how the value ARRIVES: inside a database query.
///
/// Reached through a DI-resolved interface, the scope is just an opaque property; anyone
/// auditing "is this lookup restricted to the caller?" -- a reviewer reading the method, or a
/// static analyser tracing the request id to the query -- has nothing to go on, because the
/// hop from the interface to HttpContext.User is resolved at runtime by the container. Read
/// off the ClaimsPrincipal in the same method as the query, the answer is on the screen.
///
/// Note what this does NOT change: an administrator legitimately reaches every country's
/// records, so on that path there is no narrowing to express. That is the intended access
/// model, and no rewriting of the query can make it look otherwise.
/// </summary>
public static class PrincipalScope
{
    /// <summary>True when the caller may reach every country's records.</summary>
    public static bool IsAdministrator(this ClaimsPrincipal user) =>
        user.IsInRole(RoleNames.Admin);

    /// <summary>
    /// dbo.Countries.Id the caller is confined to, from the CountryId claim stamped at login.
    /// Null for an administrator, and for a user whose RepositoryName has no Countries row yet.
    /// </summary>
    public static int? ScopedCountryId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(AppClaimTypes.CountryId), out var id) ? id : null;

    /// <summary>
    /// The RepositoryName (country) the caller is confined to, from the Country claim stamped
    /// at login. This is the value dbo.Ziraat_YD rows are keyed by.
    /// </summary>
    public static string? ScopedRepositoryName(this ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimTypes.Country);
}
