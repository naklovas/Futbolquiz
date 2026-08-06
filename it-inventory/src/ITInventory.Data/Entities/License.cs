using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

public class License : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string LicenseName { get; set; } = string.Empty;
    public string? VendorSupplier { get; set; }
    public string? Branch { get; set; }
    public string Location { get; set; } = string.Empty;

    public DateTime? SupportStartDate { get; set; }
    public DateTime? SupportEndDate { get; set; }

    /// <summary>Lisansın kendi geçerlilik/son kullanma tarihi (destek bitiş tarihinden farklı olabilir).</summary>
    public DateTime? ExpirationDate { get; set; }

    public string? Notes { get; set; }
}
