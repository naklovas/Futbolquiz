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
}
