using ITInventory.Data.Common;

namespace ITInventory.Data.Entities;

/// <summary>
/// Bir firmanin ustlendigi TEK bir rol. Bir firmada birden fazla satir olabilir.
///
/// NEDEN AYRI TABLO:
/// Companies.CompanyType tek bir deger tutuyordu; gercekte ayni firma hem
/// tedarikci hem destek hem danismanlik veriyor olabiliyor. Sahadan gelen
/// ilk cozum "her rol icin ayri firma kaydi acalim" idi -- bu mumkun degil,
/// cunku (Name, CountryId) uzerinde unique index var ve ikinci kayit
/// reddediliyor. Rolu coklamak icin dogru yer bu tablo.
///
/// Companies.CompanyType SILINMEDI: eski kayitlar, eski raporlar ve dis
/// sorgular o kolona bakiyor. Kayit edilirken rol kumesinin en bastaki
/// degeri oraya da yaziliyor, yani kolon her zaman gecerli bir rol tutuyor.
/// </summary>
public class CompanyRole
{
    public int Id { get; set; }

    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public CompanyType Role { get; set; }
}
