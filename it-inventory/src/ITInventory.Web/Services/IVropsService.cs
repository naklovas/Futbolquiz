using ITInventory.Web.Models.VropsPool;

namespace ITInventory.Web.Services;

public interface IVropsService
{
    /// <summary>Ayarlar eksikse false; ekran o zaman "yapilandirilmamis" uyarisi gosterir.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Bir datacenter'in ESXi host'larini ve uzerlerindeki VM'leri getirir.
    /// vROps'a ulasilamazsa istisna FIRLATMAZ -- donen nesnenin Error alani dolar.
    /// Liste ekraninin dis bir servis yuzunden 500 vermesini istemiyoruz.
    /// </summary>
    Task<VropsDatacenterContent> GetDatacenterContentAsync(string datacenterName, CancellationToken ct = default);

    /// <summary>
    /// Ornek VM ve host'larin HAM vROps alanlarini getirir.
    ///
    /// Amac teshis: "envantere ekle" formunu hangi property'den dolduracagimiza
    /// karar vermek icin vROps'un gercek alan adlarini gormek gerekiyor, ve bu
    /// adlar surume gore degisiyor. Iliski sorgusu KULLANMAZ, o yuzden datacenter
    /// listesi calismasa bile sonuc verir.
    ///
    /// Yalnizca admin ekranindan cagriliyor; liste ekranlarinda kullanilmaz.
    /// vROps'a ulasilamazsa istisna firlatmaz, Error alani dolar.
    /// </summary>
    Task<VropsAlanOrnekleri> OrnekAlanlariGetirAsync(int ornekSayisi = 3, CancellationToken ct = default);
}
