using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Firma/tedarikçi referans kaydı (Fortinet, Microsoft vb.). Diğer envanter kayıtları gibi
/// ülkeye bağlıdır (CountryId) — her kurum/ülke kendi firma listesini yönetir.
/// </summary>
public class Company : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Menşei ülkesi (OriginCountries referans listesinden; firmanın kendi menşei, kurumun operasyon ülkesi değil).</summary>
    public int? OriginCountryId { get; set; }
    public OriginCountry? OriginCountry { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CompanyContact> Contacts { get; set; } = new List<CompanyContact>();
    public ICollection<License> Licenses { get; set; } = new List<License>();
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
