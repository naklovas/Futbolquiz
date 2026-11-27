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

    /// <summary>
    /// Birincil rol. Roles kumesinin en bastaki degeri burada da tutulur;
    /// coklu rol destegi oncesindeki kayitlar ve raporlar bu kolona bakiyor.
    /// Ekranda gosterilen rol listesi icin <see cref="EtkinRoller"/> kullanin.
    /// </summary>
    public CompanyType CompanyType { get; set; } = CompanyType.Vendor;

    /// <summary>Only used when the role set contains Other -- the free-text description of that role.</summary>
    public string? OtherTypeDescription { get; set; }

    /// <summary>Firmanin ustlendigi rollerin tamami (Supplier + Support + Consultant gibi).</summary>
    public ICollection<CompanyRole> Roles { get; set; } = new List<CompanyRole>();

    /// <summary>Menşei ülkesi (OriginCountries referans listesinden; firmanın kendi menşei, kurumun operasyon ülkesi değil).</summary>
    public int? OriginCountryId { get; set; }
    public OriginCountry? OriginCountry { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<CompanyContact> Contacts { get; set; } = new List<CompanyContact>();
    public ICollection<License> Licenses { get; set; } = new List<License>();
    public ICollection<Application> Applications { get; set; } = new List<Application>();

    /// <summary>
    /// Gosterilecek rol kumesi, sirali ve tekrarsiz.
    ///
    /// Roles bos donerse CompanyType'a dusuyor. Iki durumda bos olabilir:
    /// coklu rol gecisinin SQL betigi henuz calistirilmamissa, ya da sorgu
    /// Roles'u Include etmemisse. Ikisinde de tek rollu eski davranis
    /// gecerli olur; liste hicbir zaman bos gorunmez.
    /// </summary>
    public IReadOnlyList<CompanyType> EtkinRoller =>
        Roles.Count > 0
            ? Roles.Select(r => r.Role).Distinct().OrderBy(r => (int)r).ToList()
            : new List<CompanyType> { CompanyType };

    /// <summary>Rollerin tek satirlik metni; Other icin serbest aciklamayi parantez icinde gosterir.</summary>
    public string RollerMetni =>
        string.Join(", ", EtkinRoller.Select(r =>
            r == CompanyType.Other && !string.IsNullOrWhiteSpace(OtherTypeDescription)
                ? $"Other ({OtherTypeDescription})"
                : r.ToString()));
}
