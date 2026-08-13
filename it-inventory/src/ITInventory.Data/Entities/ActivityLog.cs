namespace ITInventory.Data.Entities;

public class ActivityLog
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? CountryName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public string? Details { get; set; }
    public string? EnvironmentName { get; set; }
}
