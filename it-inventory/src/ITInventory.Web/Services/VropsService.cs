using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ITInventory.Web.Configuration;
using ITInventory.Web.Models.VropsPool;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ITInventory.Web.Services;

/// <summary>
/// vROps / Aria Operations REST istemcisi.
///
/// TASARIM KARARI -- neden VM basina istek atmiyoruz:
/// Bir datacenter'da binlerce VM olabilir. Her VM icin ayri bir iliskiler
/// cagrisi, liste ekranini kullanilmaz hale getirir. Bunun yerine HOST'lardan
/// asagi iniyoruz: datacenter'in host'lari (1 cagri), sonra her host'un
/// cocuklari (host sayisi kadar cagri). Host sayisi onlarla olculur, VM sayisi
/// binlerle. Ayni veriyi cok daha az istekle aliyoruz ve VM -> host eslemesi
/// zaten bu yapidan dusuyor.
///
/// IP ve isletim sistemi gibi alanlar bu listede YOK: onlar VM basina ayri
/// istek gerektirir ve ancak kullanici "envantere ekle" dedigi anda lazim olur.
/// Device Pool da ayni mantikla calisiyor.
/// </summary>
public class VropsService : IVropsService
{
    private readonly HttpClient _http;
    private readonly VropsSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VropsService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // Reddedilen kimligi bir sure hatirliyoruz -- bkz. TokenAlAsync.
    private const string KimlikHatasiKey = "vrops:token:kimlik-hatasi";
    private const int KimlikHatasiDakika = 5;

    public VropsService(
        HttpClient http,
        IOptions<VropsSettings> settings,
        IMemoryCache cache,
        ILogger<VropsService> logger)
    {
        _settings = settings.Value;
        _cache = cache;
        _logger = logger;
        _http = http;

        if (!string.IsNullOrWhiteSpace(_settings.Host))
            _http.BaseAddress = new Uri($"https://{_settings.Host}/suite-api/api/");

        _http.DefaultRequestHeaders.Accept.Clear();
        // Bazi surumlerde varsayilan cevap XML. Bunu sormadan birakmiyoruz.
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<VropsDatacenterContent> GetDatacenterContentAsync(
        string datacenterName, string kaynakTuru = "VMwareAdapter Instance", CancellationToken ct = default)
    {
        var sonuc = new VropsDatacenterContent { DatacenterName = datacenterName };

        if (!IsConfigured)
        {
            sonuc.Error = "vROps baglantisi yapilandirilmamis. Vrops:Host ayarini ve "
                        + "Vrops__Username / Vrops__Password ortam degiskenlerini kontrol edin.";
            return sonuc;
        }

        var cacheKey = $"vrops:dc:{kaynakTuru}:{datacenterName}";
        if (_cache.TryGetValue<VropsDatacenterContent>(cacheKey, out var onbellekten) && onbellekten is not null)
            return onbellekten;

        try
        {
            var token = await TokenAlAsync(ct);

            // 1) kaynagi adiyla bul -- bir vCenter ya da bir datacenter olabilir
            List<VropsHostDto> hostlar;

            if (VcenterMi(kaynakTuru))
            {
                var vcenter = (await VcenterlariGetirAsync(token, ct))
                    .FirstOrDefault(v => string.Equals(v.Name, datacenterName, StringComparison.OrdinalIgnoreCase));

                if (vcenter is null)
                {
                    sonuc.Error = $"vROps'ta '{datacenterName}' adinda bir vCenter bulunamadi. "
                                + "Aria Fields ekranindaki vCenters tablosuyla karsilastirin.";
                    return sonuc;
                }

                hostlar = await VcenterHostlariAsync(token, vcenter.VropsId, ct);
            }
            else
            {
                var dcId = await DatacenterIdBulAsync(token, datacenterName, ct);
                if (dcId is null)
                {
                    sonuc.Error = $"vROps'ta '{datacenterName}' adinda bir datacenter bulunamadi. "
                                + "dbo.VropsDatacenters tablosundaki eslemeyi kontrol edin.";
                    return sonuc;
                }

                hostlar = await HostlariBulAsync(token, dcId, ct);
            }

            // 3) her host'un altindaki VM'ler -- VM -> host eslemesi buradan cikiyor
            foreach (var h in hostlar)
            {
                var vmler = await CocuklariGetirAsync(token, h.VropsId, "VirtualMachine", ct);
                h.VmCount = vmler.Count;

                foreach (var vm in vmler)
                {
                    vm.HostName = h.Name;
                    vm.HostVropsId = h.VropsId;
                    vm.IsSystemVm = vm.Name.StartsWith("vCLS-", StringComparison.OrdinalIgnoreCase);
                    sonuc.Vms.Add(vm);
                }
            }

            sonuc.Hosts = hostlar;

            // 4) IP / isletim sistemi / model gibi alanlar -- kaynak basina
            //    bir istek. Sinirin uzerindeki listelerde atlaniyor.
            await AlanlariYukleAsync(token, sonuc, ct);

            sonuc.FetchedAt = DateTime.UtcNow;

            _cache.Set(cacheKey, sonuc, TimeSpan.FromMinutes(Math.Max(1, _settings.CacheMinutes)));
            return sonuc;
        }
        catch (Exception ex)
        {
            // Liste ekrani dis bir servis yuzunden 500 vermemeli. Sebebi ekranda yaziyoruz.
            _logger.LogError(ex, "vROps'tan {Datacenter} icerigi alinamadi", datacenterName);
            sonuc.Error = $"vROps'a ulasilamadi: {ex.Message}";
            return sonuc;
        }
    }

    /// <summary>
    /// Host ve VM'lerin IP / isletim sistemi / model alanlarini yukler.
    ///
    /// Bu alanlar liste cagrisinda GELMIYOR, kaynak basina ayri bir
    /// properties istegi gerekiyor. 1678 VM'lik bir vCenter'da bu 1678
    /// istek demek, o yuzden DetailLimit'in uzerinde hic denemiyoruz ve
    /// ekranda alanlarin neden bos oldugunu yaziyoruz -- sessizce bos
    /// birakmak "vROps'ta bu bilgi yok" izlenimi verirdi.
    ///
    /// Es zamanli istek sayisini sinirliyoruz: vROps'u tek bir liste
    /// ekrani yuzunden yormak istemiyoruz.
    /// </summary>
    private async Task AlanlariYukleAsync(string token, VropsDatacenterContent sonuc, CancellationToken ct)
    {
        var toplam = sonuc.Hosts.Count + sonuc.Vms.Count;
        if (toplam == 0) return;

        if (toplam > Math.Max(1, _settings.DetailLimit))
        {
            sonuc.DetailsSkipped = true;
            _logger.LogInformation(
                "{Datacenter}: {Toplam} kaynak DetailLimit ({Limit}) ustunde, alanlar cekilmedi",
                sonuc.DatacenterName, toplam, _settings.DetailLimit);
            return;
        }

        using var kapi = new SemaphoreSlim(6);

        async Task Yukle(string kaynakId, Action<Dictionary<string, string>> uygula)
        {
            await kapi.WaitAsync(ct);
            try
            {
                var props = await GetAsync<OzellikListesi>(token, $"resources/{kaynakId}/properties", ct);

                var alanlar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in props?.Property ?? new List<Ozellik>())
                    if (!string.IsNullOrWhiteSpace(p.Name) && p.Value is not null)
                        alanlar[p.Name] = p.Value;

                uygula(alanlar);
            }
            catch (Exception ex)
            {
                // Tek bir kaynagin alanlari gelmezse liste yine gosterilmeli.
                _logger.LogWarning(ex, "vROps alanlari alinamadi: {Kaynak}", kaynakId);
            }
            finally
            {
                kapi.Release();
            }
        }

        var isler = new List<Task>(toplam);
        foreach (var h in sonuc.Hosts) isler.Add(Yukle(h.VropsId, p => HostAlanlariniDoldur(h, p)));
        foreach (var v in sonuc.Vms) isler.Add(Yukle(v.VropsId, p => VmAlanlariniDoldur(v, p)));

        await Task.WhenAll(isler);
    }

    // ------------------------------------------------------- alan okuma
    //
    // vROps'un property adlari surume ve kuruluma gore degisiyor, ve bir
    // alanin DEGERI de her kaynakta gelmiyor: VMware Tools kurulu degilse
    // konuk alanlari ya hic yok ya da "none" yaziyor (ornek dokumunde
    // zb0010v boyleydi). O yuzden tek bir ada baglanmiyoruz: her alan icin
    // ADAY LISTESI var, sirayla denenir, ilk DOLU olan alinir.
    //
    // Yanlis okunmus bir ad hicbir seyi kirmaz, o alan bos kalir.

    private static readonly string[] BosSayilanlar = { "none", "unknown", "n/a", "unset", "-" };

    private static bool Dolu(string? d)
        => !string.IsNullOrWhiteSpace(d)
           && !BosSayilanlar.Contains(d.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Aday adlari sirayla dener, ilk dolu degeri dondurur.</summary>
    private static string? IlkDolu(IDictionary<string, string> alanlar, params string[] adaylar)
    {
        foreach (var ad in adaylar)
            if (alanlar.TryGetValue(ad, out var d) && Dolu(d))
                return d.Trim();
        return null;
    }

    /// <summary>
    /// Bir alanin degerinden ILK KULLANILABILIR IPv4'u cikarir.
    ///
    /// vROps bazi alanlarda TEK METIN ICINDE BIRDEN FAZLA adres donduruyor
    /// ("192.168.63.76 192.168.63.77" gibi). Degeri oldugu gibi forma
    /// koymak dogrulamaya takiliyordu: IpAddressAttribute tek bir adres
    /// bekliyor. CIDR yazimi da (10.0.0.5/24) burada ayrisiyor.
    /// </summary>
    private static readonly char[] IpAyiraclari = { ',', ';', ' ', '\t', '|', '/', '\r', '\n' };

    private static string? IpSec(string? ham)
    {
        if (string.IsNullOrWhiteSpace(ham) || !Dolu(ham)) return null;

        return ham
            .Split(IpAyiraclari, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(KullanilabilirIp);
    }

    /// <summary>Adi kalibi tutan alanlarda ilk kullanilabilir IPv4.</summary>
    private static string? KalipIleIp(IDictionary<string, string> alanlar, Regex kalip)
        => alanlar
            .Where(x => kalip.IsMatch(x.Key))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => IpSec(x.Value))
            .FirstOrDefault(ip => ip is not null);

    private static readonly Regex IpAlaniKalibi =
        new(@"^net:.*\|ip_address$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Envantere yazmaya deger bir IPv4 mu? Link-local (169.254.*) ve geri
    /// dongu adreslerini eliyoruz; bunlar makinenin gercek adresi degil.
    /// </summary>
    private static bool KullanilabilirIp(string d)
        => System.Net.IPAddress.TryParse(d, out var ip)
           && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
           && !d.StartsWith("169.254.", StringComparison.Ordinal)
           && !d.StartsWith("127.", StringComparison.Ordinal);

    private static void VmAlanlariniDoldur(VropsVmDto vm, IDictionary<string, string> p)
    {
        vm.GuestHostName = IlkDolu(p, "summary|guest|hostName");

        // Tools varsa guest|fullName daha dogru; yoksa config|guestFullName
        // Tools olmadan da geliyor (zb0010v ornegi).
        vm.OperatingSystem = IlkDolu(p, "summary|guest|fullName", "config|guestFullName");

        vm.IpAddress = IpSec(IlkDolu(p, "summary|guest|ipAddress"))
                       ?? KalipIleIp(p, IpAlaniKalibi);

        vm.PowerState = IlkDolu(p, "summary|runtime|powerState");

        // Host adi iliskiden geliyor ama property'de de var; iliski
        // yoksa buraya dusuyoruz.
        if (string.IsNullOrWhiteSpace(vm.HostName))
            vm.HostName = IlkDolu(p, "summary|parentHost") ?? string.Empty;
    }

    private static void HostAlanlariniDoldur(VropsHostDto h, IDictionary<string, string> p)
    {
        h.Vendor = IlkDolu(p, "hardware|vendor", "hardware|manufacturer");
        h.Model = IlkDolu(p, "hardware|vendorModel", "hardware|hostModel", "hardware|model");
        h.Version = IlkDolu(p, "summary|version", "config|product|version", "sys|build", "runtime|build");
        h.SerialNo = IlkDolu(p, "hardware|serialNumber", "hardware|serviceTag", "hardware|biosUuid");

        h.IpAddress = IpSec(IlkDolu(p, "summary|managementAddress", "net|mgmt_address"))
                      ?? KalipIleIp(p, IpAlaniKalibi);
    }

    // -------------------------------------------------------- alan kesfi
    public async Task<VropsAlanOrnekleri> OrnekAlanlariGetirAsync(int ornekSayisi = 3, CancellationToken ct = default)
    {
        var sonuc = new VropsAlanOrnekleri();

        if (!IsConfigured)
        {
            sonuc.Error = "vROps baglantisi yapilandirilmamis.";
            return sonuc;
        }

        // Onbellege almiyoruz: bu bir teshis ekrani, guncel hali gorulmeli.
        try
        {
            var token = await TokenAlAsync(ct);
            var adet = Math.Clamp(ornekSayisi, 1, 10);

            // vCenter'lar: ulke ayrimi datacenter yerine vCenter'a
            // baglanacaksa esleme bunlardan birine yapilacak.
            // Patlasa da ornekleri kaybetmiyoruz.
            try
            {
                sonuc.Vcenters = await VcenterlariGetirAsync(token, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "vROps adaptor listesi alinamadi");
                sonuc.Vcenters = new List<VropsVcenter>();
            }

            sonuc.Vms = await OrneklerAsync(token, "VirtualMachine", adet, atlaVcls: true, ct);
            sonuc.Hosts = await OrneklerAsync(token, "HostSystem", adet, atlaVcls: false, ct);
            sonuc.FetchedAt = DateTime.UtcNow;
            return sonuc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "vROps alan kesfi basarisiz");
            sonuc.Error = $"vROps'a ulasilamadi: {ex.Message}";
            return sonuc;
        }
    }

    /// <summary>
    /// vROps'ta tanimli adaptor orneklerini getirir -- pratikte vCenter'lar.
    /// Her vCenter bir "VMwareAdapter Instance" kaydi olarak goruluyor, ama
    /// kaynak tipinin adi surume gore degisebiliyor; o yuzden filtrelemiyoruz,
    /// ne donerse ekrana yaziyoruz ve gercek adlari oradan okuyoruz.
    /// </summary>
    private async Task<List<VropsVcenter>> VcenterlariGetirAsync(string token, CancellationToken ct)
    {
        var liste = await GetAsync<AdaptorListesi>(token, "adapters", ct);

        return (liste?.AdapterInstancesInfoDto ?? new List<Adaptor>())
            .Select(a => new VropsVcenter
            {
                VropsId = a.Id ?? string.Empty,
                Name = a.ResourceKey?.Name ?? "(adsiz)",
                AdapterKind = a.ResourceKey?.AdapterKindKey ?? string.Empty,
                ResourceKind = a.ResourceKey?.ResourceKindKey ?? string.Empty
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<VropsKaynakAlanlari>> OrneklerAsync(
        string token, string tur, int adet, bool atlaVcls, CancellationToken ct)
    {
        var liste = await GetAsync<KaynakListesi>(token, $"resources?resourceKind={tur}&page=0&pageSize=50", ct);
        var kaynaklar = (liste?.ResourceList ?? new List<Kaynak>())
            .Where(r => !string.IsNullOrEmpty(r.Identifier));

        // vCLS-* makineleri vSphere'in kendi yardimcilari: konuk IP'si ve
        // isletim sistemi hic gelmiyor, yani alanlarin dolu olup olmadigini
        // onlardan anlayamayiz.
        if (atlaVcls)
            kaynaklar = kaynaklar.Where(r =>
                !(r.ResourceKey?.Name ?? string.Empty).StartsWith("vCLS-", StringComparison.OrdinalIgnoreCase));

        var secilenler = kaynaklar.Take(adet).ToList();
        var cikti = new List<VropsKaynakAlanlari>(secilenler.Count);

        foreach (var r in secilenler)
        {
            var kayit = new VropsKaynakAlanlari
            {
                VropsId = r.Identifier!,
                Name = r.ResourceKey?.Name ?? "(adsiz)",
                Kind = r.ResourceKey?.ResourceKindKey ?? tur
            };

            // Bir ornek patlarsa digerlerini kaybetmiyoruz.
            try
            {
                var props = await GetAsync<OzellikListesi>(token, $"resources/{r.Identifier}/properties", ct);
                foreach (var p in props?.Property ?? new List<Ozellik>())
                {
                    if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Value))
                        kayit.Properties[p.Name!] = p.Value!;
                }
            }
            catch (Exception ex)
            {
                kayit.Error = $"properties: {ex.Message}";
            }

            if (string.Equals(tur, "VirtualMachine", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (var e in await IliskililerAsync(token, r.Identifier!, "PARENT", ct))
                        kayit.Parents.Add(new VropsIliskiliKaynak { Kind = e.Kind, Name = e.Name });
                }
                catch (Exception ex)
                {
                    kayit.Error = (kayit.Error is null ? "" : kayit.Error + " | ") + $"parents: {ex.Message}";
                }
            }

            cikti.Add(kayit);
        }

        return cikti;
    }

    // ------------------------------------------------------------------ token
    private async Task<string> TokenAlAsync(CancellationToken ct)
    {
        const string cacheKey = "vrops:token";
        if (_cache.TryGetValue<string>(cacheKey, out var mevcut) && !string.IsNullOrEmpty(mevcut))
            return mevcut;

        // Son deneme kimlik yuzunden reddedildiyse YENIDEN DENEMIYORUZ.
        // Her deneme vROps tarafinda bir basarisiz giris sayiliyor; kullanici
        // ekrani yeniledikce hesap kilitleniyordu. Ayar duzeltilince uygulama
        // yeniden baslatilir, ya da bu kayit kendiliginden dusuyor.
        if (_cache.TryGetValue<string>(KimlikHatasiKey, out var sonHata) && !string.IsNullOrEmpty(sonHata))
            throw new InvalidOperationException(sonHata);

        // authSource BOSSA alani istege HIC KOYMUYORUZ. Bos string gondermekle
        // ayni sey degil: bu kurulumda yerel hesaplar alan yokken kabul ediliyor,
        // "Local" yazildiginda 401 donuyor. Alan yalnizca vROps'ta tanimli bir AD
        // kaynagi adi verildiginde gidiyor.
        object govdeNesnesi = string.IsNullOrWhiteSpace(_settings.AuthSource)
            ? new { username = _settings.Username, password = _settings.Password }
            : new
            {
                username = _settings.Username,
                password = _settings.Password,
                authSource = _settings.AuthSource
            };

        var govde = JsonSerializer.Serialize(govdeNesnesi);

        using var istek = new HttpRequestMessage(HttpMethod.Post, "auth/token/acquire")
        {
            Content = new StringContent(govde, Encoding.UTF8, "application/json")
        };

        using var cevap = await _http.SendAsync(istek, ct);
        if (!cevap.IsSuccessStatusCode)
        {
            var metin = await cevap.Content.ReadAsStringAsync(ct);
            // Parola bu metne girmez; vROps sadece hata aciklamasi doner.
            var mesaj = $"Token alinamadi (HTTP {(int)cevap.StatusCode}). {Kisalt(metin)}";

            if ((int)cevap.StatusCode is 401 or 403)
            {
                mesaj += $" -- Bu hata {KimlikHatasiDakika} dakika onbellekte tutuluyor: "
                       + "her deneme vROps'ta bir basarisiz giris sayiliyor ve hesabi "
                       + "kilitleyebilir. Ayarlari duzelttikten sonra uygulamayi yeniden baslatin.";
                _cache.Set(KimlikHatasiKey, mesaj, TimeSpan.FromMinutes(KimlikHatasiDakika));
            }

            throw new InvalidOperationException(mesaj);
        }

        using var akis = await cevap.Content.ReadAsStreamAsync(ct);
        var cozum = await JsonSerializer.DeserializeAsync<TokenCevap>(akis, JsonOpts, ct);
        if (string.IsNullOrEmpty(cozum?.Token))
            throw new InvalidOperationException("vROps token dondurmedi.");

        // Basarili giris, varsa eski kimlik hatasini siler.
        _cache.Remove(KimlikHatasiKey);

        // Token'in kendi gecerliligi daha uzun; biz kisa tutup yenilenmesine birakiyoruz.
        _cache.Set(cacheKey, cozum.Token, TimeSpan.FromMinutes(20));
        return cozum.Token;
    }

    /// <summary>
    /// Esleme satiri bir vCenter'i mi isaret ediyor? Adaptor ornegi olan her
    /// kaynak tipini vCenter sayiyoruz; tam ad surume gore degisebiliyor
    /// ("VMwareAdapter Instance"), o yuzden tam esleme aramiyoruz.
    /// </summary>
    private static bool VcenterMi(string? kaynakTuru)
        => (kaynakTuru ?? string.Empty).Contains("Adapter Instance", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bir vCenter'in altindaki ESXi host'lari.
    ///
    /// Once adaptor filtresini deniyoruz: tek cagri, en ucuzu. Bu parametrenin
    /// bu surumde desteklendiginden EMIN DEGILIZ -- desteklenmiyorsa HTTP 400
    /// doner, tipki relationshipType=DESCENDANT'ta oldugu gibi. O yuzden
    /// basarisizligi yutup bildigimiz CHILD yuruyusune donuyoruz.
    /// </summary>
    private async Task<List<VropsHostDto>> VcenterHostlariAsync(string token, string vcenterId, CancellationToken ct)
    {
        try
        {
            var yol = $"resources?adapterInstanceId={Uri.EscapeDataString(vcenterId)}"
                    + "&resourceKind=HostSystem&page=0&pageSize=1000";
            var liste = await GetAsync<KaynakListesi>(token, yol, ct);

            var hostlar = (liste?.ResourceList ?? new List<Kaynak>())
                .Where(r => !string.IsNullOrEmpty(r.Identifier))
                .Select(r => new VropsHostDto
                {
                    VropsId = r.Identifier!,
                    Name = r.ResourceKey?.Name ?? "(adsiz)"
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hostlar.Count > 0)
                return hostlar;

            _logger.LogInformation(
                "adapterInstanceId filtresi host dondurmedi, CHILD yuruyusune geciliyor ({Vcenter})", vcenterId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "adapterInstanceId filtresi calismadi, CHILD yuruyusune geciliyor ({Vcenter})", vcenterId);
        }

        return await HostlariBulAsync(token, vcenterId, ct);
    }

    // ------------------------------------------------------- datacenter bulma
    private async Task<string?> DatacenterIdBulAsync(string token, string ad, CancellationToken ct)
    {
        var yol = $"resources?resourceKind=Datacenter&name={Uri.EscapeDataString(ad)}";
        var liste = await GetAsync<KaynakListesi>(token, yol, ct);

        return liste?.ResourceList?
            .FirstOrDefault(r => string.Equals(r.ResourceKey?.Name, ad, StringComparison.OrdinalIgnoreCase))?
            .Identifier;
    }

    // ------------------------------------------------- iliski uzerinden cekme
    //
    // NEDEN DESCENDANT KULLANMIYORUZ:
    // Bu vROps surumu relationshipType icin yalnizca PARENT, CHILD ve ALL
    // kabul ediyor; DESCENDANT'a "Value DESCENDANT is invalid" diye HTTP 400
    // donuyor. O yuzden datacenter'dan host'a CHILD ile kademe kademe
    // iniyoruz.
    //
    // Hiyerarsi tek adim degil: Datacenter -> Cluster -> HostSystem -> VM,
    // ve araya klasor (Folder) girebiliyor, tek basina duran host'lar da
    // ComputeResource altinda asili olabiliyor. Sabit iki kademe yazmak
    // yerine sinirli derinlikte bir arama yapiyoruz: HostSystem bulunca
    // duruyoruz, VM'lerin icine hic inmiyoruz.
    //
    // Sinir 6: vCenter'dan baslarsak zincir vCenter -> Datacenter -> Cluster
    // -> Host, yani dort kademe, araya klasor girerse bes. Dort yetmiyordu.
    private const int EnFazlaDerinlik = 6;

    private async Task<List<VropsHostDto>> HostlariBulAsync(string token, string dcId, CancellationToken ct)
    {
        var hostlar = new Dictionary<string, VropsHostDto>(StringComparer.OrdinalIgnoreCase);
        var gorulen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dcId };
        var sira = new List<string> { dcId };

        for (var derinlik = 0; derinlik < EnFazlaDerinlik && sira.Count > 0; derinlik++)
        {
            var sonraki = new List<string>();

            foreach (var id in sira)
            {
                foreach (var cocuk in await IliskililerAsync(token, id, "CHILD", ct))
                {
                    if (string.IsNullOrEmpty(cocuk.Id) || !gorulen.Add(cocuk.Id))
                        continue;

                    if (string.Equals(cocuk.Kind, "HostSystem", StringComparison.OrdinalIgnoreCase))
                    {
                        hostlar[cocuk.Id] = new VropsHostDto { VropsId = cocuk.Id, Name = cocuk.Name };
                        continue;   // host bulundu, altina inmiyoruz -- altinda VM'ler var
                    }

                    // VM'lerin altinda host olmaz; bosa istek atmayalim.
                    if (string.Equals(cocuk.Kind, "VirtualMachine", StringComparison.OrdinalIgnoreCase))
                        continue;

                    sonraki.Add(cocuk.Id);
                }
            }

            sira = sonraki;
        }

        return hostlar.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<VropsVmDto>> CocuklariGetirAsync(string token, string kaynakId, string tur, CancellationToken ct)
    {
        var cocuklar = await IliskililerAsync(token, kaynakId, "CHILD", ct);
        return cocuklar
            .Where(c => string.Equals(c.Kind, tur, StringComparison.OrdinalIgnoreCase))
            .Select(c => new VropsVmDto { VropsId = c.Id, Name = c.Name })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Bir kaynagin iliskililerini TURUYLE BIRLIKTE dondurur. Tur bilgisi
    /// lazim, cunku datacenter'in altinda cluster, klasor ve host birlikte
    /// gelebiliyor ve hangisinin icine inecegimize ona gore karar veriyoruz.
    /// </summary>
    private async Task<List<(string Id, string Name, string Kind)>> IliskililerAsync(
        string token, string kaynakId, string iliskiTuru, CancellationToken ct)
    {
        var yol = $"resources/{kaynakId}/relationships?relationshipType={iliskiTuru}";
        var liste = await GetAsync<KaynakListesi>(token, yol, ct);

        return (liste?.ResourceList ?? new List<Kaynak>())
            .Where(r => !string.IsNullOrEmpty(r.Identifier))
            .Select(r => (
                Id:   r.Identifier!,
                Name: r.ResourceKey?.Name ?? "(adsiz)",
                Kind: r.ResourceKey?.ResourceKindKey ?? string.Empty))
            .ToList();
    }

    private async Task<T?> GetAsync<T>(string token, string yol, CancellationToken ct)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Get, yol);
        istek.Headers.TryAddWithoutValidation("Authorization", $"vRealizeOpsToken {token}");

        using var cevap = await _http.SendAsync(istek, ct);
        if (!cevap.IsSuccessStatusCode)
        {
            var metin = await cevap.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"vROps isteği başarısız (HTTP {(int)cevap.StatusCode}) -- {yol}. {Kisalt(metin)}");
        }

        using var akis = await cevap.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(akis, JsonOpts, ct);
    }

    private static string Kisalt(string? s)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 300 ? s : s[..300]);

    // ------------------------------------------------------- cevap sekilleri
    private sealed class TokenCevap
    {
        public string? Token { get; set; }
    }

    private sealed class KaynakListesi
    {
        public List<Kaynak>? ResourceList { get; set; }
    }

    private sealed class Kaynak
    {
        public string? Identifier { get; set; }
        public KaynakAnahtari? ResourceKey { get; set; }
    }

    private sealed class KaynakAnahtari
    {
        public string? Name { get; set; }
        public string? ResourceKindKey { get; set; }
    }

    private sealed class OzellikListesi
    {
        public List<Ozellik>? Property { get; set; }
    }

    private sealed class Ozellik
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    private sealed class AdaptorListesi
    {
        public List<Adaptor>? AdapterInstancesInfoDto { get; set; }
    }

    private sealed class Adaptor
    {
        public string? Id { get; set; }
        public AdaptorAnahtari? ResourceKey { get; set; }
    }

    private sealed class AdaptorAnahtari
    {
        public string? Name { get; set; }
        public string? AdapterKindKey { get; set; }
        public string? ResourceKindKey { get; set; }
    }
}
