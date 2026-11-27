using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>Hat/devre envanteri (Internet, L3 MPLS, vb.).</summary>
public class Circuit : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string CircuitType { get; set; } = string.Empty;
    public string? CircuitCapacity { get; set; }
    public string? Provider { get; set; }
    public string? Branch { get; set; }
    public string Location { get; set; } = string.Empty;

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? Notes { get; set; }
}
