namespace ITInventory.Data.Entities;

/// <summary>
/// Single-row table (Id is always 1) holding the hashed password for the app's "Test Login"
/// bypass mode. Kept in the DB instead of appsettings.json/code so it isn't a literal secret
/// sitting in source control or a config file, and so it can be rotated with a SQL UPDATE
/// instead of a redeploy.
/// </summary>
public class TestLoginCredential
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
