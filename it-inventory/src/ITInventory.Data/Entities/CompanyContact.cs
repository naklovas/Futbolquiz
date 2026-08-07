namespace ITInventory.Data.Entities;

/// <summary>Bir firmaya ait kontak kişisi (bir firmanın birden fazla kontağı olabilir).</summary>
public class CompanyContact
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string? PersonName { get; set; }

    /// <summary>Unvan.</summary>
    public string? Title { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
}
