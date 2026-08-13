namespace ITInventory.Data.Entities;

public class YdUser
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Kullanıcının bağlı olduğu ülke/şube (Ziraat_YD.RepositoryName ile aynı değer kümesi).</summary>
    public string? RepositoryName { get; set; }

    /// <summary>ExpirationNotifier'ın bu kullanıcıya süre dolum maili atıp atmayacağı. Varsayılan true.</summary>
    public bool ReceiveExpirationNotifications { get; set; } = true;

    public ICollection<YdUserRole> UserRoles { get; set; } = new List<YdUserRole>();
}
