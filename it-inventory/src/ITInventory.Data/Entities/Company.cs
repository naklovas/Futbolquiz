using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Firma/tedarikçi referans kaydı (Fortinet, Microsoft vb.). Ülkeye bağlı değildir,
/// tüm ülkelerde ortak kullanılan global bir liste (Countries/DeviceCategories gibi).
/// </summary>
public class Company : AuditableEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Menşei ülkesi (serbest metin; bankanın operasyon ülkeleri ile ilgisi yoktur).</summary>
    public string? CountryOfOrigin { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CompanyContact> Contacts { get; set; } = new List<CompanyContact>();
    public ICollection<License> Licenses { get; set; } = new List<License>();
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
