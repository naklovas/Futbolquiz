namespace ITInventory.Data.Entities;

public class YdRole
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<YdUserRole> UserRoles { get; set; } = new List<YdUserRole>();
}
