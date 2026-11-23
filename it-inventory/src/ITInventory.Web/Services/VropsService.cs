using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

    public async Task<VropsDatacenterContent> GetDatacenterContentAsync(string datacenterName, CancellationToken ct = default)
    {
        var sonuc = new VropsDatacenterContent { DatacenterName = datacenterName };

        if (!IsConfigured)
        {
            sonuc.Error = "vROps baglantisi yapilandirilmamis. Vrops:Host ayarini ve "
                        + "Vrops__Username / Vrops__Password ortam degiskenlerini kontrol edin.";
            return sonuc;
        }

        var cacheKey = $"vrops:dc:{datacenterName}";
        if (_cache.TryGetValue<VropsDatacenterContent>(cacheKey, out var onbellekten) && onbellekten is not null)
            return onbellekten;

        try
        {
            var token = await TokenAlAsync(ct);

            // 1) datacenter'i adiyla bul
            var dcId = await DatacenterIdBulAsync(token, datacenterName, ct);
            if (dcId is null)
            {
                sonuc.Error = $"vROps'ta '{datacenterName}' adinda bir datacenter bulunamadi. "
                            + "Admin > Countries altindaki eslemeyi kontrol edin.";
                return sonuc;
            }

            // 2) datacenter'in altindaki ESXi host'lari
            var hostlar = await HostlariBulAsync(token, dcId, ct);

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
    private const int EnFazlaDerinlik = 4;

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
}
