namespace ITInventory.Data.Entities;

public class YdUserRole
{
    public int UserId { get; set; }
    public YdUser? User { get; set; }

    public int RoleId { get; set; }
    public YdRole? Role { get; set; }
}
