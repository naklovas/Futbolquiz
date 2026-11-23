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
            var hostlar = await AltNesneleriGetirAsync(token, dcId, "HostSystem", ct);

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

        var govde = JsonSerializer.Serialize(new
        {
            username = _settings.Username,
            password = _settings.Password,
            authSource = _settings.AuthSource
        });

        using var istek = new HttpRequestMessage(HttpMethod.Post, "auth/token/acquire")
        {
            Content = new StringContent(govde, Encoding.UTF8, "application/json")
        };

        using var cevap = await _http.SendAsync(istek, ct);
        if (!cevap.IsSuccessStatusCode)
        {
            var metin = await cevap.Content.ReadAsStringAsync(ct);
            // Parola bu metne girmez; vROps sadece hata aciklamasi doner.
            throw new InvalidOperationException(
                $"Token alinamadi (HTTP {(int)cevap.StatusCode}). {Kisalt(metin)}");
        }

        using var akis = await cevap.Content.ReadAsStreamAsync(ct);
        var cozum = await JsonSerializer.DeserializeAsync<TokenCevap>(akis, JsonOpts, ct);
        if (string.IsNullOrEmpty(cozum?.Token))
            throw new InvalidOperationException("vROps token dondurmedi.");

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
    private Task<List<VropsHostDto>> AltNesneleriGetirAsync(string token, string kaynakId, string tur, CancellationToken ct)
        => IliskiliGetirAsync(token, kaynakId, "DESCENDANT", tur, ct);

    private async Task<List<VropsVmDto>> CocuklariGetirAsync(string token, string kaynakId, string tur, CancellationToken ct)
    {
        var host = await IliskiliGetirAsync(token, kaynakId, "CHILD", tur, ct);
        return host.Select(h => new VropsVmDto { VropsId = h.VropsId, Name = h.Name }).ToList();
    }

    private async Task<List<VropsHostDto>> IliskiliGetirAsync(
        string token, string kaynakId, string iliskiTuru, string kaynakTuru, CancellationToken ct)
    {
        var yol = $"resources/{kaynakId}/relationships?relationshipType={iliskiTuru}";
        var liste = await GetAsync<KaynakListesi>(token, yol, ct);

        return (liste?.ResourceList ?? new List<Kaynak>())
            .Where(r => string.Equals(r.ResourceKey?.ResourceKindKey, kaynakTuru, StringComparison.OrdinalIgnoreCase))
            .Select(r => new VropsHostDto
            {
                VropsId = r.Identifier ?? string.Empty,
                Name = r.ResourceKey?.Name ?? "(adsiz)"
            })
            .Where(x => !string.IsNullOrEmpty(x.VropsId))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
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
