namespace ITInventory.Web.Services;

public interface IActivityLogger
{
    Task LogAsync(string action, string entityType, string? entityName = null, string? details = null);
}
