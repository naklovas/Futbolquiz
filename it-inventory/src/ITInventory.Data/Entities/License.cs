using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

public class License : AuditableEntity
{
    public int Id { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    public string LicenseName { get; set; } = string.Empty;
    public string? VendorSupplier { get; set; }

    /// <summary>Lisansı sağlayan firma (Companies tablosuna bağlantı; VendorSupplier serbest metni ile birlikte durur).</summary>
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }
    public string? Branch { get; set; }
    public string Location { get; set; } = string.Empty;

    public DateTime? SupportStartDate { get; set; }
    public DateTime? SupportEndDate { get; set; }

    /// <summary>Lisansın kendi geçerlilik/son kullanma tarihi (destek bitiş tarihinden farklı olabilir).</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Suresiz lisans: bitis tarihi YOK.
    ///
    /// Bos bir tarih "henuz girilmedi" ile "hic bitmiyor" arasini ayirmiyordu.
    /// Bu bayrak ikisini ayiriyor: isaretliyse alan bilerek bos, eksik degil.
    /// </summary>
    public bool IsPerpetual { get; set; }

    public string? Notes { get; set; }
}
