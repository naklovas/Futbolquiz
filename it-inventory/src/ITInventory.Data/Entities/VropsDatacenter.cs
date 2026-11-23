using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// vROps/Aria Operations datacenter adini bir ulkeye baglar.
///
/// Datacenter adlari tahmin edilebilir degil: bazilari ZALG_DC kalibinda,
/// bazilari degil. Nessus'un DeviceProfile degerlerinde oldugu gibi burada da
/// tahmin yurutmuyoruz -- eslemeyi admin kaydediyor.
///
/// Bir ulkenin birden fazla datacenter'i olabilir (orn. asil merkez + felaket
/// kurtarma), bu yuzden Countries'e tek kolon eklemek yerine ayri tablo.
/// </summary>
public class VropsDatacenter
{
    public int Id { get; set; }

    /// <summary>vROps'taki datacenter adi, birebir. Degistirilmemelidir.</summary>
    public string DatacenterName { get; set; } = string.Empty;

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    /// <summary>Asil merkez mi, felaket kurtarma mi. Buradan gelen sunuculara yazilir.</summary>
    public SiteRole SiteRole { get; set; } = SiteRole.Primary;

    public bool IsActive { get; set; } = true;
}
