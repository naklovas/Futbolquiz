using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>Ülkeye bağlı uygulama envanteri (Servers &amp; Applications sayfasının ikinci bacağı).</summary>
public class Application : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public int? LicenseId { get; set; }
    public License? License { get; set; }

    public ApplicationType ApplicationType { get; set; }

    /// <summary>Uygulama dışa (internete) açık mı.</summary>
    public bool IsExternallyExposed { get; set; }

    public string? Url { get; set; }

    /// <summary>Cloud (bulut) uygulaması mı.</summary>
    public bool IsCloudApplication { get; set; }

    public string? Notes { get; set; }

    public ICollection<Server> Servers { get; set; } = new List<Server>();
}
