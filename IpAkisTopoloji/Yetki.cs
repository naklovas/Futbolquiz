// ---------------------------------------------------------------------------
// İşlem yetkileri (sayfayı görme yetkisi AppSettings:CurrentSiteName ile ayrıca kontrol edilir). DokuPanel'de kod değişmeden her
// yetki için ayrı bir site kaydı açılır, kişi / AD grupları oraya eklenir; site adı Yetki:<Ad> ayarına yazılır:
//   Yazma    — etiketleme, etiket açıklaması, işlev haritası (AI sınıflandırma, ad değiştirme, taşıma), versiyon kaydet / sil,
//              ERT eksikleri (AI önerisi, onay / ret)                    (boşsa eski Etiket:YazmaSiteAdi, o da boşsa Etiket:Yazanlar)
//   Topoloji — küme topolojisi ve etiket bağımlılık çizimi (trafik sorgusu)                       (boşsa Yazma ile aynı)
//   Disari   — Excel (CSV) / SVG / PNG dışa aktarma                                             (boşsa Yazma ile aynı)
//   Toplu    — ERT eksiklerinde tüm boşları tahmin ettirme, 50'den çok sunucu      (boşsa Eksik:TopluYetkililer, varsayılan visikhan)
// Auth ara katmanı /api isteklerinde gereken siteleri sorar (kullanıcı + site başına Auth:CacheMinutes önbellek; aynı ad tek sorgu),
// sonuç context.Items["Yetki"]. DokuPanel'e ulaşılamazsa o işlem kapalı. Auth kapalıysa (yerel) yalnız listeler geçerli.
// ---------------------------------------------------------------------------
static class Yetki
{
    public const string Yazma = "Yazma", Topoloji = "Topoloji", Disari = "Disari", Toplu = "Toplu";
    public static readonly string[] Turler = [Yazma, Topoloji, Disari, Toplu];

    // Yetkinin DokuPanel site adı (yoksa null: liste / başka yetkiye bağlı)
    public static string? Site(IConfiguration cfg, string tur)
    {
        string? s = cfg[$"Yetki:{tur}"];
        if (string.IsNullOrWhiteSpace(s) && tur == Yazma) s = cfg["Etiket:YazmaSiteAdi"];
        if (string.IsNullOrWhiteSpace(s) && tur is Topoloji or Disari) return Site(cfg, Yazma);
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public static bool Var(IConfiguration cfg, HttpContext ctx, string tur)
    {
        string? user = ctx.User?.Identity?.Name;
        if (tur == Toplu)   // toplu tahmin için yazma yetkisi de gerekir
            return Var(cfg, ctx, Yazma) && (Site(cfg, Toplu) is { } ts ? Doku(cfg, ctx, ts) : InList(cfg, "Eksik:TopluYetkililer", user, ["visikhan"]));
        if (Site(cfg, tur) is { } site) return Doku(cfg, ctx, site);
        return tur == Yazma ? InList(cfg, "Etiket:Yazanlar", user, []) : Var(cfg, ctx, Yazma);
    }

    // Arayüz için tüm yetkiler (/api/me)
    public static Dictionary<string, bool> Hepsi(IConfiguration cfg, HttpContext ctx) =>
        Turler.ToDictionary(t => t.ToLowerInvariant(), t => Var(cfg, ctx, t));

    public static IResult Red(HttpContext ctx, string ne) =>
        Results.Json(new { error = $"{ne} yetkiniz yok ({ctx.User?.Identity?.Name}). Yetki DokuPanel'den verilir." }, statusCode: 403);

    static bool Doku(IConfiguration cfg, HttpContext ctx, string site)
    {
        if (!cfg.GetValue("Auth:Enabled", true)) return true;
        return ctx.Items["Yetki"] is Dictionary<string, bool> d && d.TryGetValue(site, out bool ok) && ok;
    }

    // Liste boşsa (ve varsayılan yoksa) herkes; DOMAIN\ öneki fark etmez
    static bool InList(IConfiguration cfg, string key, string? user, string[] varsayilan)
    {
        var allowed = cfg.GetSection(key).GetChildren().Select(c => c.Value).OfType<string>().Where(s => s.Trim() != "").ToArray();
        if (allowed.Length == 0) allowed = varsayilan;
        if (allowed.Length == 0) return true;
        string u = (user ?? "").Split('\\').Last();
        return allowed.Any(a => string.Equals(a.Split('\\').Last(), u, StringComparison.OrdinalIgnoreCase));
    }

    // Ara katman: bu istek için gereken (yapılandırılmış) site adlarını DokuPanel'e sorar, önbellekler
    public static async Task YukleAsync(HttpContext ctx, IConfiguration cfg, Microsoft.Extensions.Caching.Memory.IMemoryCache cache, ILogger log)
    {
        var sites = Turler.Select(t => Site(cfg, t)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sites.Count == 0) return;
        string user = ctx.User?.Identity?.Name ?? "?";
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            string key = $"yetki2:{user.ToLowerInvariant()}:{site.ToLowerInvariant()}";
            if (!Microsoft.Extensions.Caching.Memory.CacheExtensions.TryGetValue(cache, key, out bool ok))
            {
                var hataOnce = ctx.Items["AuthHata"];
                ok = await AppRel.AuthHelper.YetkiKontrol(ctx, cfg, site);
                if (!ok) log.LogInformation("İşlem yetkisi yok: {User} ({Site}) - {Hata}", user, site, ctx.Items["AuthHata"]);
                ctx.Items["AuthHata"] = hataOnce;
                Microsoft.Extensions.Caching.Memory.CacheExtensions.Set(cache, key, ok, TimeSpan.FromMinutes(cfg.GetValue("Auth:CacheMinutes", 5)));
            }
            d[site] = ok;
        }
        ctx.Items["Yetki"] = d;
    }
}
