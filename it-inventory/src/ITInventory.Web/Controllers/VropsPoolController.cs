using ITInventory.Data;
using ITInventory.Data.Entities;
using ITInventory.Web.Common;
using ITInventory.Web.Models.VropsPool;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ITInventory.Web.Controllers;

/// <summary>
/// vROps/Aria havuzu: bir ulkenin datacenter'larindaki ESXi host'lari ve
/// uzerlerinde calisan VM'leri gosterir, envantere aktarimi baslatir.
///
/// Device Pool ile ayni desen: liste disaridan gelir, "envantere ekle" linki
/// ilgili formu on-doldurur. Fark, kaynagin SQL tablosu degil canli API olmasi.
/// </summary>
public class VropsPoolController : Controller
{
    private readonly IVropsService _vrops;
    private readonly ITInventoryDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public VropsPoolController(IVropsService vrops, ITInventoryDbContext db, ICurrentUserService currentUser)
    {
        _vrops = vrops;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? countryId, bool showSystemVms = false)
    {
        var isAdmin = User.IsAdministrator();
        var scopedCountryId = User.ScopedCountryId();

        ViewBag.IsAdmin = isAdmin;
        ViewBag.CanEdit = _currentUser.CanEdit;
        ViewBag.ShowSystemVms = showSystemVms;
        ViewBag.IsConfigured = _vrops.IsConfigured;

        // Admin her ulkeyi secebilir; digerleri yalnizca kendi ulkesini gorur.
        // Secim istekten gelse de kapsam kontrolu sorgunun icinde.
        var seciliUlkeId = isAdmin ? countryId : scopedCountryId;

        var ulkeler = await _db.VropsDatacenters
            .Where(d => d.IsActive && (isAdmin || d.CountryId == scopedCountryId))
            .Select(d => d.Country!)
            .Distinct()
            .OrderBy(c => c.DisplayName ?? c.Name)
            .ToListAsync();

        ViewBag.Countries = ulkeler;
        ViewBag.SelectedCountryId = seciliUlkeId;

        if (!seciliUlkeId.HasValue)
        {
            ViewBag.NoCountryAssigned = !isAdmin;
            return View(new List<VropsDatacenterContent>());
        }

        // Ulkenin datacenter'lari -- kapsam kosulu sorgunun icinde.
        var datacenterlar = await _db.VropsDatacenters
            .Where(d => d.CountryId == seciliUlkeId.Value
                     && d.IsActive
                     && (isAdmin || d.CountryId == scopedCountryId))
            .OrderBy(d => d.DatacenterName)
            .ToListAsync();

        if (datacenterlar.Count == 0)
        {
            ViewBag.NoDatacenterMapped = true;
            return View(new List<VropsDatacenterContent>());
        }

        var icerikler = new List<VropsDatacenterContent>();
        foreach (var dc in datacenterlar)
        {
            var icerik = await _vrops.GetDatacenterContentAsync(dc.DatacenterName, dc.ResourceKind, HttpContext.RequestAborted);
            icerikler.Add(icerik);
        }

        // "Zaten envanterde mi" isareti: ada gore eslestiriyoruz, cunku vROps
        // kimligini tutan bir alanimiz henuz yok. Aktarim calistiginda o alan
        // eklenecek ve bu kontrol ona tasinacak.
        var hostAdlari = icerikler.SelectMany(i => i.Hosts).Select(h => h.Name).Distinct().ToList();
        var vmAdlari = icerikler.SelectMany(i => i.Vms).Select(v => v.Name).Distinct().ToList();

        var mevcutCihazlar = await _db.PhysicalDevices
            .Where(d => hostAdlari.Contains(d.DeviceName)
                     && (isAdmin || d.CountryId == scopedCountryId))
            .Select(d => new { d.Id, d.DeviceName })
            .ToListAsync();

        var mevcutSunucular = await _db.Servers
            .Where(s => vmAdlari.Contains(s.HostName)
                     && (isAdmin || s.CountryId == scopedCountryId))
            .Select(s => new { s.Id, s.HostName })
            .ToListAsync();

        var cihazSozluk = mevcutCihazlar
            .GroupBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var sunucuSozluk = mevcutSunucular
            .GroupBy(x => x.HostName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        foreach (var icerik in icerikler)
        {
            foreach (var h in icerik.Hosts)
                if (cihazSozluk.TryGetValue(h.Name, out var cihazId)) h.ExistingPhysicalDeviceId = cihazId;

            foreach (var v in icerik.Vms)
                if (sunucuSozluk.TryGetValue(v.Name, out var sunucuId)) v.ExistingServerId = sunucuId;
        }

        ViewBag.SelectedCountryName = ulkeler.FirstOrDefault(c => c.Id == seciliUlkeId.Value)?.DisplayName
                                      ?? ulkeler.FirstOrDefault(c => c.Id == seciliUlkeId.Value)?.Name;

        return View(icerikler);
    }

    /// <summary>
    /// Teshis ekrani: ornek VM ve host'larin HAM vROps alanlari.
    ///
    /// "Envantere ekle" formunu hangi property'den dolduracagimiza karar
    /// vermek icin vROps'un gercek alan adlari gerekiyor; bu adlar surume
    /// gore degisiyor, tahminle kodlanamiyor.
    ///
    /// Ham altyapi bilgisi oldugu icin YALNIZCA ADMIN. Ulke kapsami
    /// uygulanamiyor: alanlar ulkeye gore ayrilmiyor, ornekler vROps'un
    /// tamamindan geliyor.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Alanlar(int ornekSayisi = 3)
    {
        if (!User.IsAdministrator()) return Forbid();

        ViewBag.IsConfigured = _vrops.IsConfigured;
        ViewBag.OrnekSayisi = Math.Clamp(ornekSayisi, 1, 10);

        var model = await _vrops.OrnekAlanlariGetirAsync(ornekSayisi, HttpContext.RequestAborted);
        return View(model);
    }
}
