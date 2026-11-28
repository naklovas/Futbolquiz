// ---------------------------------------------------------------------------
// İşlem yetkileri (sayfayı görme yetkisi AppSettings:CurrentSiteName ile ayrıca kontrol edilir). DokuPanel'de kod değişmeden her
// yetki için ayrı bir site kaydı açılır, kişi / AD grupları oraya eklenir; site adı Yetki:<Ad> ayarına yazılır:
//   Yazma    — etiketleme, etiket açıklaması, işlev haritası (AI sınıflandırma, ad değiştirme, taşıma), versiyon kaydet / sil,
//              ERT eksikleri (AI önerisi, onay / ret)                    (boşsa eski Etiket:YazmaSiteAdi, o da boşsa Etiket:Yazanlar)
//   Topoloji — küme topolojisi ve etiket bağımlılık çizimi (trafik sorgusu)                       (boşsa Yazma ile aynı)
//   Disari   — Excel (CSV) / SVG / PNG dışa aktarma                                             (boşsa Yazma ile aynı)
//   Toplu    — ERT eksiklerinde tüm boşları tahmin ettirme, 50'den çok sunucu      (boşsa Eksik:TopluYetkililer, varsayılan visikhan)
//   Tls      — TLS toplu sorgu sekmesi (Excel'den IP listesi, AppResponse)                         (boşsa Topoloji ile aynı)
//   Fiziksel — Fiziksel envanter sekmesi (nlyte kabin / host / VM; IP kartındaki konum satırı serbest)   (boşsa Topoloji ile aynı)
//   FizikselDeger — fiziksel cihaz değerlerini kaydetme (denetim kaydı) ve elle değer girme; Fiziksel de gerekir   (boşsa Yazma ile aynı)
//   Zafiyet  — Açıklık taraması sayfası: görme, tarama, kural / kayıt, tanı (/api/zafiyet*, /api/birim*); uygulama / birim tahmini ve onayı
//              ayrıca Yazma, Excel ayrıca Disari ister                                              (boşsa Topoloji ile aynı)
// SAYFA (görme) yetkileri — IP sorgula ve Uygulama sorgula dışındaki her alt sayfa (kullanıcı: "IP sorgulama ve Uygulama sorgulama dışındaki tüm
// alt sayfalarda yetkilendirme olmalı"). Sayfanın okuma uçları dahil (Program.cs SayfaYetkisi ara katmanı); yazma işleri ayrıca Yazma ister:
//   Erisim   — Erişim sorgula sekmesi (/api/erisim, /api/opinnate)                                 (boşsa Topoloji ile aynı)
//   Islev    — Topoloji sekmesi: işlev haritası, küme topolojisi, versiyonlar (/api/islev, /api/tenantlar) (boşsa Topoloji ile aynı)
//   Etiket   — Sunucu etiketleme sekmesi (/api/etiket/*; IP kartındaki "+ Etiket" yalnız Yazma ister)   (boşsa Topoloji ile aynı)
//   Eksik    — ERT eksikleri sayfası (/api/eksik; IP kartındaki AI önerisi kolonu serbest)          (boşsa Topoloji ile aynı)
//   Imza     — Servis imzası sayfası (/api/imza)                                                   (boşsa Topoloji ile aynı)
//   Segment  — Segment / etiket tarama sayfası (/api/segment)                                      (boşsa Topoloji ile aynı)
//   DisErisim — İnternetten erişilen servisler raporu (/api/diserisim; DisErisimServisi'nin tabloları) (boşsa Topoloji ile aynı)
// Auth ara katmanı /api isteklerinde gereken siteleri sorar (kullanıcı + site başına Auth:CacheMinutes önbellek; aynı ad tek sorgu),
// sonuç context.Items["Yetki"]. DokuPanel'e ulaşılamazsa o işlem kapalı. Auth kapalıysa (yerel) yalnız listeler geçerli.
// ---------------------------------------------------------------------------
static class Yetki
{
    public const string Yazma = "Yazma", Topoloji = "Topoloji", Disari = "Disari", Toplu = "Toplu", Tls = "Tls", Fiziksel = "Fiziksel", FizikselDeger = "FizikselDeger",
        Zafiyet = "Zafiyet", Erisim = "Erisim", Islev = "Islev", Etiket = "Etiket", Eksik = "Eksik", Imza = "Imza", Segment = "Segment", DisErisim = "DisErisim";
    public static readonly string[] Turler = [Yazma, Topoloji, Disari, Toplu, Tls, Fiziksel, FizikselDeger, Zafiyet, Erisim, Islev, Etiket, Eksik, Imza, Segment, DisErisim];

    // Topoloji'ye düşen (boşsa Topoloji sitesi kullanılan) yetkiler
    static bool TopolojiyeDuser(string tur) => tur is Tls or Fiziksel or Zafiyet or Erisim or Islev or Etiket or Eksik or Imza or Segment or DisErisim;

    // Yetkinin DokuPanel site adı (yoksa null: liste / başka yetkiye bağlı)
    public static string? Site(IConfiguration cfg, string tur)
    {
        string? s = cfg[$"Yetki:{tur}"];
        if (string.IsNullOrWhiteSpace(s) && tur == Yazma) s = cfg["Etiket:YazmaSiteAdi"];
        if (string.IsNullOrWhiteSpace(s) && tur is Topoloji or Disari or FizikselDeger) return Site(cfg, Yazma);
        if (string.IsNullOrWhiteSpace(s) && TopolojiyeDuser(tur)) return Site(cfg, Topoloji);
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    public static bool Var(IConfiguration cfg, HttpContext ctx, string tur)
    {
        string? user = ctx.User?.Identity?.Name;
        if (tur == Toplu)   // toplu tahmin için yazma yetkisi de gerekir
            return Var(cfg, ctx, Yazma) && (Site(cfg, Toplu) is { } ts ? Doku(cfg, ctx, ts) : InList(cfg, "Eksik:TopluYetkililer", user, ["visikhan"]));
        if (tur == FizikselDeger && !Var(cfg, ctx, Fiziksel)) return false;
        if (Site(cfg, tur) is { } site) return Doku(cfg, ctx, site);
        return tur == Yazma ? InList(cfg, "Etiket:Yazanlar", user, []) : TopolojiyeDuser(tur) ? Var(cfg, ctx, Topoloji) : Var(cfg, ctx, Yazma);
    }

    // Sayfa yetkisi: istenen /api yolu hangi alt sayfaya ait (gerekenlerden BİRİ yeterli; birden çok sayfanın kullandığı uçlar). null = sayfa
    // yetkisi gerekmez (IP / Uygulama sorgula ve IP kartının kullandığı uçlar: /api/eksik/ip|durum|calistir, POST /api/etiket, /api/etiketler,
    // /api/fiziksel/ip, /api/tls …). Uçların kendi işlem yetkisi (Yazma, Topoloji, Toplu, Disari) ayrıca geçerli.
    public static (string[] Turler, string Ad)? SayfaYetkisi(PathString yol, string metot)
    {
        bool B(string p) => yol.StartsWithSegments(p);
        if (B("/api/zafiyet") || B("/api/birim") || B("/api/nessus/assetler")) return ([Zafiyet], "Açıklık taraması");
        if (B("/api/erisim") || B("/api/opinnate")) return ([Erisim], "Erişim sorgula");
        if (B("/api/islev") || B("/api/tenantlar")) return ([Islev], "Topoloji (işlev haritası)");
        if (B("/api/etiket/aciklama")) return ([Etiket, Islev], "Sunucu etiketleme");
        if (B("/api/etiket")) return yol.Value!.TrimEnd('/').Equals("/api/etiket", StringComparison.OrdinalIgnoreCase) && metot == "POST"
            ? null : ([Etiket], "Sunucu etiketleme");
        if (B("/api/eksik/ip") || B("/api/eksik/durum") || B("/api/eksik/calistir")) return null;
        if (B("/api/eksik/csv") || yol.Value!.TrimEnd('/').Equals("/api/eksik", StringComparison.OrdinalIgnoreCase)) return ([Eksik], "ERT eksikleri");
        if (B("/api/eksik")) return ([Eksik, Imza, Segment, Zafiyet], "ERT eksikleri");
        if (B("/api/imza")) return ([Imza], "Servis imzası");
        if (B("/api/segment/exeag")) return ([Segment, Etiket, Islev], "Segment / etiket tarama");
        if (B("/api/segment")) return ([Segment, Etiket], "Segment / etiket tarama");
        if (B("/api/tls/toplu")) return ([Tls], "TLS toplu sorgu");
        if (B("/api/diserisim")) return ([DisErisim], "İnternetten erişilen servisler");
        if (B("/api/ucmdb")) return ([Tls], "UCMDB web.config");
        if (B("/api/fiziksel") && !B("/api/fiziksel/ip")) return ([Fiziksel], "Fiziksel envanter");
        if (B("/api/envanter/yenile")) return ([Yazma], "Envanteri yeniden yükleme");
        return null;
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
