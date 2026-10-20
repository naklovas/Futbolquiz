using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// ERT eksikleri: ERT_HOSTIPADDRESS'te APPNAME'i boş (IP, port) satırları için AI'dan uygulama önerisi.
// Kanıtlar (sunucu başına): işlev haritasının bilgi özeti (ERT diğer portlar / WEBSITE / SERVICENAME, segment, VIP, MSY, ÇSY,
// Kahin, SM kurulum ve iş istekleri; IslevService.ProfilesAsync), Carbon Black (son Eksik:Saat saat: hangi portu hangi exe
// kabul ediyor, kimler bağlanıyor, exe'ler nereye gidiyor) ve aday uygulamalar (aynı sunucu, VIP, muhafızın uygulamaları,
// adı kanıtlarla örtüşen ERT uygulamaları). AI adayı harfi harfine seçer ya da "yeni" ad önerir.
// Sonuçlar ERT'ye YAZILMAZ: Eksik:OneriTablosu (dbo.DF_ERT_UYGULAMA_ONERI, sql/ert_oneri.sql); onay / ret / elle düzeltme,
// Excel'e (CSV) aktarım. Tablo yoksa öneriler bellekte tutulur (uyarı gösterilir).
// ---------------------------------------------------------------------------
record EksikOneri(string? Uygulama, bool Yeni, int Guven, string? Gerekce, string Durum, string? Kaynak, DateTime Tarih);
record EksikSatir(string Ip, string Port, string? Hostname, string? Segment, string? Website, string? Servis, string? Muhafiz,
    string? Birim, List<string> Diger, bool Altyapi, EksikOneri? Oneri, bool Test = false);
record EksikListe(List<EksikSatir> Satirlar, int Toplam, int TumSatir, int TumIp, int Onerili, int Onayli, IslevDurum Durum,
    bool Yazabilir, string? Uyari, bool TopluYetki = false, string? Bilgi = null, string? Surum = null);
// Max: bu çalıştırmada en çok kaç sunucu (boş = Eksik:MaxIp; 0 = sınırsız)
record EksikCalistir(List<string>? Ips, bool Hepsi, bool Yeniden, int? Saat, bool Test = false, int? Max = null);
record EksikKarar(string? Ip, string? Port, string? Karar, string? Uygulama);

sealed class EksikService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, IslevService islev,
    ILogger<EksikService> log)
{
    public const string Onay = "Onaylandı", Red = "Reddedildi", Ai = "AI", Elle = "Elle";
    readonly object _gate = new();
    IslevDurum _durum = new(false, null, 0, 0, 0, null, null, null, null, null, []);
    CancellationTokenSource? _cts;
    // (ip, port) → öneri; tablo yoksa yalnız bellekte
    readonly Dictionary<(string, string), EksikOneri> _mem = new();
    readonly Dictionary<string, string> _kanit = new();   // ip → AI'a giden metin (bu çalışmada)
    Dictionary<(string, string), EksikOneri>? _cache;
    string? _uyari;

    public IslevDurum Durum { get { lock (_gate) return _durum; } }
    string Table => cfg["Eksik:OneriTablosu"] is { Length: > 0 } t && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")
        ? t : "dbo.DF_ERT_UYGULAMA_ONERI";
    string ConnStr => cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");

    // ---------------- Eksik satırlar ----------------
    static string Key(string? port) => port ?? "";

    // Otomatik (dinamik) portlar hiç listelenmez: Eksik:DinamikPortBaslangic (varsayılan 49152) ve üstü (kullanıcı kararı)
    public static List<(string Ip, string Port, HostApp Row)> Missing(EnvanterSnapshot env, int dyn = 49152) =>
        env.HostIps.SelectMany(ip => env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName))
                .Where(h => !(int.TryParse(h.Port, out int n) && n >= dyn))
                .GroupBy(h => Key(h.Port)).Select(g => (ip, g.Key, g.First())))
            .ToList();
    int Dyn => cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);

    // Test segmenti: KonsolideSegmentler4.Domain'inde Eksik:TestIfadeleri (varsayılan "test") geçiyor; varsayılan olarak gizli, AI'a gitmez
    bool IsTest(EnvanterSnapshot env, string ip)
    {
        var words = cfg.GetSection("Eksik:TestIfadeleri").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (words.Count == 0) words = ["test"];
        string? d = env.FindSegment(ip)?.Domain;
        return d != null && words.Any(w => d.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    // IP sorgulama kartı: bu IP'nin port başına önerisi (ERT tablosundaki "AI önerisi" kolonu)
    public async Task<Dictionary<string, EksikOneri>> ForIpAsync(string ip, CancellationToken ct) =>
        (await LoadAsync(false, ct)).Where(kv => kv.Key.Item1 == ip && kv.Value.Durum != "").ToDictionary(kv => kv.Key.Item2, kv => kv.Value);

    // sirala: ip | port | sunucu | segment | oneri | guven | durum (yon: azalan)
    public async Task<EksikListe> ListAsync(string? ara, string? durum, bool altyapi, int sayfa, int boyut, bool yazabilir, CancellationToken ct,
        bool test = false, string? sirala = null, bool azalan = false)
    {
        var env = await envanter.GetAsync(false, ct);
        var infra = AppTopology.InfraPorts(cfg);
        var oneri = await LoadAsync(false, ct);
        var all = Missing(env, Dyn);
        var rows = all.Select(m =>
        {
            var hosts = env.AppsOnIp(m.Ip);
            oneri.TryGetValue((m.Ip, m.Port), out var o);
            return new EksikSatir(m.Ip, m.Port, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(), env.FindSegment(m.Ip)?.Label,
                m.Row.Website, m.Row.ServiceName, m.Row.VarlikMuhafizi ?? hosts.Select(h => h.VarlikMuhafizi).OfType<string>().FirstOrDefault(),
                m.Row.SunucuIsletenBirim, env.AppNamesOnIp(m.Ip).Take(4).ToList(), infra.Contains(m.Port), o, IsTest(env, m.Ip));
        }).ToList();
        string? q = ara?.Trim();
        bool Has(string? v) => v != null && v.Contains(q!, StringComparison.CurrentCultureIgnoreCase);
        var f = rows.Where(r => altyapi || !r.Altyapi).Where(r => test || !r.Test)
            .Where(r => string.IsNullOrEmpty(q) || Has(r.Ip) || Has(r.Port) || Has(r.Hostname) || Has(r.Website) || Has(r.Servis)
                        || Has(r.Muhafiz) || Has(r.Segment) || Has(r.Oneri?.Uygulama) || Has(r.Oneri?.Gerekce) || r.Diger.Any(Has))
            .Where(r => durum switch
            {
                "yok" => r.Oneri == null,
                "ai" => r.Oneri?.Durum == Ai,
                "onay" => r.Oneri?.Durum is Onay or Elle,
                "red" => r.Oneri?.Durum == Red,
                _ => true
            })
            .ToList();
        var cmp = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
        static int PortN(string p) => int.TryParse(p, out var n) ? n : -1;
        IOrderedEnumerable<EksikSatir> Ord<T>(Func<EksikSatir, T> k, IComparer<T>? c = null) => azalan ? f.OrderByDescending(k, c) : f.OrderBy(k, c);
        f = (sirala switch
        {
            "ip" => Ord(r => IpKey(r.Ip)).ThenBy(r => PortN(r.Port)),
            "port" => Ord(r => PortN(r.Port)).ThenBy(r => IpKey(r.Ip)),
            "sunucu" => Ord(r => r.Hostname ?? "\uffff", cmp).ThenBy(r => PortN(r.Port)),
            "segment" => Ord(r => r.Segment ?? "\uffff", cmp).ThenBy(r => IpKey(r.Ip)),
            // öneri: önerisi olmayanlar hep sonda
            "oneri" => (azalan ? f.OrderBy(r => r.Oneri?.Uygulama == null).ThenByDescending(r => r.Oneri?.Uygulama ?? "", cmp)
                               : f.OrderBy(r => r.Oneri?.Uygulama == null).ThenBy(r => r.Oneri?.Uygulama ?? "", cmp)).ThenBy(r => IpKey(r.Ip)),
            "guven" => (azalan ? f.OrderByDescending(r => r.Oneri?.Guven ?? -1) : f.OrderBy(r => r.Oneri == null).ThenBy(r => r.Oneri?.Guven ?? 0)).ThenBy(r => IpKey(r.Ip)),
            "durum" => Ord(r => r.Oneri?.Durum ?? "\uffff", cmp).ThenByDescending(r => r.Oneri?.Guven ?? 0),
            // varsayılan: IP + port (sabit; kullanıcı: tahmin bitince satır öneriye göre başka sayfaya kayıyordu)
            _ => f.OrderBy(r => IpKey(r.Ip)).ThenBy(r => PortN(r.Port))
        }).ToList();
        int size = Math.Clamp(boyut, 20, 1000), page = Math.Max(1, sayfa);
        string bilgi;
        try { bilgi = Bilgi().Durum; } catch (Exception ex) { bilgi = "genel bilgi dosyası okunamadı: " + ex.Message; }
        return new EksikListe(f.Skip((page - 1) * size).Take(size).ToList(), f.Count, rows.Count, rows.Select(r => r.Ip).Distinct().Count(),
            rows.Count(r => r.Oneri != null), rows.Count(r => r.Oneri?.Durum is Onay or Elle), Durum, yazabilir, _uyari, false, bilgi, Surum);
    }

    static string IpKey(string ip) => string.Join(".", ip.Split('.').Select(p => p.PadLeft(3, '0')));

    // Çalışan sürüm (yayınlanan dll'in tarihi + SM tarih kolonu): eski dll ile yeni dosyalar karışınca sayfada görülsün
    static readonly string Surum = $"sunucu sürümü {File.GetLastWriteTime(typeof(EksikService).Assembly.Location):yyyy-MM-dd HH:mm}";

    // ---------------- Öneri tablosu ----------------
    async Task<Dictionary<(string, string), EksikOneri>> LoadAsync(bool force, CancellationToken ct)
    {
        if (_cache != null && !force) return _cache;
        var map = new Dictionary<(string, string), EksikOneri>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT IP, PORT, UYGULAMA, YENI, GUVEN, GEREKCE, DURUM, KAYNAK, TARIH FROM {Table}", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await r.ReadAsync(ct))
                map[(r.GetString(0), S(1) ?? "")] = new EksikOneri(S(2), !r.IsDBNull(3) && r.GetBoolean(3), r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    S(5), S(6) ?? Ai, S(7), r.IsDBNull(8) ? DateTime.Now : r.GetDateTime(8));
            _uyari = null;
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            _uyari = $"Öneri tablosu ({Table}) yok; öneriler yalnız bellekte tutuluyor (uygulama yeniden başlayınca kaybolur). sql/ert_oneri.sql betiğini çalıştırın.";
            lock (_gate) foreach (var kv in _mem) map[kv.Key] = kv.Value;
        }
        return _cache = map;
    }

    async Task SaveAsync(string ip, string port, EksikOneri o, string? kanit, CancellationToken ct)
    {
        lock (_gate) _mem[(ip, port)] = o;
        if (_cache != null) lock (_gate) _cache[(ip, port)] = o;
        if (_uyari != null) return;
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"""
                UPDATE {Table} SET UYGULAMA = @u, YENI = @y, GUVEN = @g, GEREKCE = @r, DURUM = @d, KAYNAK = @k, TARIH = SYSDATETIME(),
                       KANIT = COALESCE(@kn, KANIT)
                WHERE IP = @ip AND PORT = @p;
                IF @@ROWCOUNT = 0
                  INSERT INTO {Table} (IP, PORT, UYGULAMA, YENI, GUVEN, GEREKCE, DURUM, KAYNAK, TARIH, KANIT)
                  VALUES (@ip, @p, @u, @y, @g, @r, @d, @k, SYSDATETIME(), @kn);
                """, conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            cmd.Parameters.AddWithValue("@p", port);
            cmd.Parameters.AddWithValue("@u", (object?)o.Uygulama ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@y", o.Yeni);
            cmd.Parameters.AddWithValue("@g", o.Guven);
            cmd.Parameters.AddWithValue("@r", (object?)Cut(o.Gerekce, 1000) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", o.Durum);
            cmd.Parameters.AddWithValue("@k", (object?)o.Kaynak ?? DBNull.Value);
            cmd.Parameters.Add("@kn", System.Data.SqlDbType.NVarChar, -1).Value = (object?)kanit ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 208) { _uyari = $"Öneri tablosu ({Table}) yok; sql/ert_oneri.sql betiğini çalıştırın."; }
    }

    static string? Cut(string? s, int n) => s == null ? null : s.Length > n ? s[..n] : s;

    // Onay / ret / elle düzeltme
    public async Task DecideAsync(EksikKarar k, string user, CancellationToken ct)
    {
        string ip = k.Ip?.Trim() ?? throw new InvalidOperationException("IP gerekli."), port = k.Port?.Trim() ?? "";
        var all = await LoadAsync(false, ct);
        all.TryGetValue((ip, port), out var cur);
        var env = await envanter.GetAsync(false, ct);
        EksikOneri o = k.Karar switch
        {
            "onay" when cur?.Uygulama != null => cur with { Durum = Onay, Kaynak = user, Tarih = DateTime.Now },
            "red" => (cur ?? new EksikOneri(null, false, 0, null, Red, user, DateTime.Now)) with { Durum = Red, Kaynak = user, Tarih = DateTime.Now },
            "elle" when !string.IsNullOrWhiteSpace(k.Uygulama) => new EksikOneri(Snap(env, k.Uygulama!.Trim(), out bool yeni), yeni, 100,
                cur?.Gerekce, Elle, user, DateTime.Now),
            "sil" => new EksikOneri(null, false, 0, null, "", user, DateTime.Now),
            _ => throw new InvalidOperationException("Geçersiz karar ya da öneri yok.")
        };
        if (k.Karar == "sil")
        {
            lock (_gate) { _mem.Remove((ip, port)); _cache?.Remove((ip, port)); }
            if (_uyari == null)
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand($"DELETE FROM {Table} WHERE IP = @ip AND PORT = @p", conn);
                cmd.Parameters.AddWithValue("@ip", ip); cmd.Parameters.AddWithValue("@p", port);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        else await SaveAsync(ip, port, o, null, ct);
        log.LogInformation("ERT önerisi: {User} {Ip}:{Port} {Karar} [{App}]", user, ip, port, k.Karar, o.Uygulama);
    }

    // Ad ERT kataloğunda varsa kataloğun yazımı; yoksa olduğu gibi (yeni)
    static string Snap(EnvanterSnapshot env, string name, out bool yeni)
    {
        var hit = env.AppNames.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                  ?? env.AppNames.FirstOrDefault(a => Norm(a) == Norm(name));
        yeni = hit == null;
        return hit ?? name;
    }
    static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // AI'ın yazdığı adı kurum içindeki uygulama adlarıyla (ERT kataloğu) karşılaştırır (kullanıcı: "içerideki uygulama isimleri kontrol
    // edilsin, yoksa serbest atsın"): birebir / harf-rakam eşitse o; değilse kelime düzeyinde tek bir açık eşleşme varsa (AI'ın kelimelerinin
    // hepsi katalog adında ya da katalog adının hepsi AI'ın adında; ya da biri diğerini harf-rakam olarak içeriyor) katalog yazımı.
    // Birden çok eşit aday varsa ya da hiç yoksa AI'ın adı serbest kalır (yeni); not gerekçeye eklenir.
    static readonly HashSet<string> GenelKelime = ["siste", "uygul", "servi", "web", "api", "yonet", "modul", "proje", "veri", "verit", "app",
        "uygulama", "core", "test", "prod", "ortak", "genel", "islem", "batch", "zaman"];
    string CatalogMatch(EnvanterSnapshot env, string name, out bool yeni, out string? not)
    {
        not = null;
        string exact = Snap(env, name, out yeni);
        if (!yeni) return exact;
        PrepCatalog(env);
        var a = IslevService.Tokens(name);
        string na = Norm(name);
        var key = a.Where(t => t.Length >= 3 && !GenelKelime.Contains(t)).ToHashSet();
        if (key.Count == 0 && na.Length < 4) { not = "ERT kataloğunda yok"; return name; }
        bool Anahtar(HashSet<string> t) => t.Any(x => x.Length >= 3 && !GenelKelime.Contains(x));
        var cat = _catTok!.Where(c => !env.IsSystemApp(c.App)).ToList();
        // 1) aynı kelime kümesi  2) AI'ın kelimelerinin hepsi katalog adında  3) katalog adının hepsi AI'ın adında  4) harf-rakam içerme
        var esit = cat.Where(c => c.Tok.SetEquals(a)).Select(c => c.App).ToList();
        var ust = cat.Where(c => key.Count > 0 && a.IsSubsetOf(c.Tok)).Select(c => c.App).ToList();
        var altHam = cat.Where(c => Anahtar(c.Tok) && c.Tok.IsSubsetOf(a)).OrderByDescending(c => c.Tok.Count).ToList();
        // katalog adı AI'ın adının en az yarısını (anahtar kelimelerde) karşılamalı: "KREDİ" ⊂ "Kredi Kartı Takip" tek başına yetmez
        var alt = altHam.Where(c => c.Tok.Count(t => key.Contains(t)) * 2 >= key.Count).ToList();
        var icer = cat.Where(c => na.Length >= 4 && Norm(c.App) is { Length: >= 4 } nc && (nc.Contains(na) || na.Contains(nc))).Select(c => c.App).ToList();
        string? hit = null; List<string>? coklu = null;
        if (esit.Count == 1) hit = esit[0];
        else if (esit.Count > 1) coklu = esit;
        else if (ust.Count == 1) hit = ust[0];
        else if (ust.Count > 1) coklu = ust;
        else if (alt.Count > 0 && (alt.Count == 1 || alt[0].Tok.Count > alt[1].Tok.Count)) hit = alt[0].App;
        else if (alt.Count > 1) coklu = alt.Select(c => c.App).ToList();
        else if (icer.Count == 1) hit = icer[0];
        else if (icer.Count > 1) coklu = icer;
        if (hit == null)
        {
            not = coklu != null ? "ERT kataloğunda birden çok benzer ad: " + string.Join(" | ", coklu.Take(4)) + (coklu.Count > 4 ? " …" : "") + " — serbest bırakıldı"
                : altHam.Count > 0 ? "ERT kataloğunda tam karşılığı yok, serbest ad; benzer: " + string.Join(" | ", altHam.Take(3).Select(c => c.App))
                : "ERT kataloğunda yok, serbest ad";
            return name;
        }
        yeni = false;
        not = $"ERT kataloğundaki '{hit}' adıyla eşlendi (AI: '{name}')";
        return hit;
    }

    // ---------------- Kanıt (AI'a giden metin) ----------------
    // yeni = false: son AI isteğinde giden (kayıtlı) metin; true: kanıtlar şimdi yeniden toplanır (AI'a sorulmaz)
    public async Task<string> EvidenceAsync(string ip, CancellationToken ct, bool yeni = false)
    {
        if (!yeni) lock (_gate) if (_kanit.TryGetValue(ip, out var k)) return k;
        if (!yeni) try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT TOP 1 KANIT FROM {Table} WHERE IP = @ip AND KANIT IS NOT NULL ORDER BY TARIH DESC", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            if (await cmd.ExecuteScalarAsync(ct) is string s) return s;
        }
        catch (SqlException) { /* tablo yoksa yeniden üretilir */ }
        var env = await envanter.GetAsync(false, ct);
        var miss = Missing(env, Dyn).Where(m => m.Ip == ip).Select(m => m.Port).ToList();
        var texts = await BuildAsync([ip], miss.Count > 0 ? new() { [ip] = miss } : new() { [ip] = [] }, env, Hours(null), ct);
        return texts.GetValueOrDefault(ip) ?? "";
    }

    int Hours(int? h) => Math.Clamp(h ?? cfg.GetValue("Eksik:Saat", 24), 1, Math.Max(1, cfg.GetValue("MaxRangeHours", 24 * 7)));

    // Sunucu başına kanıt metni: işlev haritası bilgi özeti + Carbon Black + aday uygulamalar
    // Ajan / araç ürünleri (kullanıcı: Dataskope ajanı kuruldu diye sunucu Dataskope uygulaması OLMAZ): SM talebinde, aday listesinde ya da
    // AI önerisinde geçerse sunucunun iş uygulaması sayılmaz; yalnız boş portu kabul eden exe o ürüne aitse "Altyapı: <ürün>" olabilir.
    static readonly string[] DefaultAjanlar = ["Dataskope", "Infraskope", "Carbon Black", "Splunk", "SiteScope", "Trend Micro", "Deep Security",
        "McAfee", "Trellix", "Symantec", "Tanium", "Qualys", "Tenable", "Nessus", "CyberArk", "SCCM", "Zabbix", "Dynatrace", "AppDynamics",
        "AppInternals", "Commvault", "Veeam", "NetBackup", "Guardium", "Imperva", "Varonis", "Forcepoint", "Ansible", "Nagios", "PRTG", "SolarWinds"];
    string[] Ajanlar() => cfg.GetSection("Eksik:AjanUygulamalari").GetChildren().Select(c => c.Value).OfType<string>().Where(v => v.Trim() != "") is var l && l.Any()
        ? l.ToArray() : DefaultAjanlar;
    // Ürünlerin eski / diğer adları (ör. Forcepoint'in eski adı Websense; segment adı WEBSENSE kalmış olabilir)
    static readonly Dictionary<string, string[]> AjanTakma = new(StringComparer.OrdinalIgnoreCase)
    {
        ["forcepoint"] = ["websense"], ["trellix"] = ["mcafee", "fireeye"], ["carbonblack"] = ["bit9"],
        ["trendmicro"] = ["apexone", "officescan"], ["deepsecurity"] = ["trendmicro"],
        ["symantec"] = ["endpointprotection", "sepm", "sepmanager"]
    };
    static IEnumerable<string> AjanAdlari(string ajan) => new[] { Norm(ajan) }.Concat(AjanTakma.GetValueOrDefault(Norm(ajan)) ?? []).Where(x => x.Length >= 4);
    string? AjanOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string n = Norm(text);
        return Ajanlar().FirstOrDefault(a => AjanAdlari(a).Any(n.Contains));
    }
    // Sunucu o ürünün KENDİ sunucusu mu (kullanıcı: WEBSENSE segmentindeki, ERT'de Forcepoint Data Discovery portları olan sunucuda 5432 =
    // Forcepoint veritabanı): aynı sunucunun ERT uygulamalarında, sunucu adında ya da segment adında ürün (ya da eski adı) geçiyorsa
    // Ayrıca SM talebinde BU SUNUCUYU anlatan açıklama ürünün sunucusu olduğunu söylüyorsa (kullanıcı örneği: hedef açıklaması "Symantec
    // Endpoint Protection ürünü sunucusu"); "ajan / agent / kurulum" geçen açıklamalar sayılmaz (Dataskope ajanı kurulan kart sunucusu).
    // Ürün kanıtı: 2 = var (aynı sunucunun ERT uygulamaları, sunucu adı, bu sunucuyu anlatan SM açıklaması, hedef olduğu talebin kısa
    // açıklaması, sanal sunucu notu, kurulum talebi, ürünün sunucu bileşeni exe'si), 0 = yok. Segment adı kanıt DEĞİL.
    // Kullanıcı: WEBSENSE_11 segmentinde SEPM (Symantec) sunucusu var → segment adı tek başına ürünü belirlemez.
    int UrunKaniti(EnvanterSnapshot env, string ip, string ajan, string? evidence)
    {
        var adlar = AjanAdlari(ajan).ToList();
        bool Has(string? t) => t != null && adlar.Any(Norm(t).Contains);
        if (env.AppNamesOnIp(ip).Any(Has) || env.AppsOnIp(ip).Any(h => Has(h.UnixName))) return 2;
        if (evidence != null)
        {
            bool hedefTalep = false;
            foreach (var l in evidence.Split('\n'))
            {
                if (l.StartsWith("- ") && System.Text.RegularExpressions.Regex.IsMatch(l, @"^- \S+ \S+ · bu sunucu")) hedefTalep = l.Contains("bu sunucu HEDEF") || l.Contains("HEM HEDEF");
                if ((l.Contains("(BU SUNUCU)") || l.StartsWith("  • ") || l.StartsWith("MSY sanal sunucu:") || l.StartsWith("ÇSY sanal sunucu:")
                     || (hedefTalep && l.StartsWith("    Kısa açıklama:"))) && UrunSunucusuMetni(l, ajan)) return 2;
                // sanal sunucu notu ürünü anıyorsa ("Symantec Endpoint Protection ürünü") ajan kelimesi yoksa yeter
                if ((l.StartsWith("MSY sanal sunucu:") || l.StartsWith("ÇSY sanal sunucu:")) && adlar.Any(Norm(l).Contains) && !AjanKelime.IsMatch(l)) return 2;
                if (l.StartsWith("Sunucu kurulum talebi") && l.IndexOf("| uygulama:", StringComparison.Ordinal) is int i and >= 0
                    && Has(l[(i + 11)..].Split('|')[0])) return 2;
                // gelen bağlantı kabul eden exe ürünün sunucu bileşeni (ör. ...\symantec endpoint protection manager\apache\bin\httpd.exe)
                if (l.StartsWith("- gelen port ") && l.Split('[').Skip(1).Select(x => x.Split(']')[0])
                    .Any(path => Has(path) && SunucuKelime.IsMatch(path) && !AjanKelime.IsMatch(path))) return 2;
            }
        }
        return 0;   // segment adı sayılmaz (kullanıcı: WEBSENSE segmentinde yalnız Forcepoint değil başka ürünlerin sunucuları da var)
    }
    bool AjanSunucusu(EnvanterSnapshot env, string ip, string ajan, string? evidence = null) => UrunKaniti(env, ip, ajan, evidence) == 2;
    string? BaskaUrun(EnvanterSnapshot env, string ip, string ajan, string? evidence) =>
        Ajanlar().FirstOrDefault(a => Norm(a) != Norm(ajan) && UrunKaniti(env, ip, a, evidence) == 2);
    static readonly System.Text.RegularExpressions.Regex SunucuKelime = new(@"sunucu|server|y[öo]netim|manager|konsol|console|veritaban|database|\bdb\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    // "sunucu kurulumu" ürünün sunucusunun kurulduğunu söyler; başka "kurulum" (sunucuya X kurulumu) ajan kurulumudur
    static readonly System.Text.RegularExpressions.Regex AjanKelime = new(@"ajan|agent|client|istemci|(?<!sunucu\w*\s*)kurulum|install|y[üu]kle", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static bool UrunSunucusuMetni(string? t, string ajan) => t != null && AjanAdlari(ajan).Any(Norm(t).Contains)
        && SunucuKelime.IsMatch(t) && !AjanKelime.IsMatch(t);
    // Boş portu kabul eden exe (kanıttaki Carbon Black "- gelen port X" satırı) bu ajana mı ait
    static bool PortExeHas(string? evidence, string port, string ajan) => evidence != null && port.Length > 0 &&
        evidence.Split('\n').Any(l => (l.StartsWith($"- gelen port {port}:") || l.StartsWith($"- gelen port {port} (")) && Norm(l).Contains(Norm(ajan)));

    // Veritabanı sunucusu ipuçları (kullanıcı: DB sunucusunda öneri "<uygulama> Veritabanı" olmalı): bilinen DB portları
    // (Eksik:VeritabaniPortlari ile değişir), sunucu adında sql / db / ora kısaltmaları, portu kabul eden DB süreçleri.
    // "?" ile bitenler başka uygulamalarca da sık kullanılır (ör. 5000, 9000): yalnız portu kabul eden süreç DB süreciyse sayılır
    static readonly Dictionary<string, string> DefaultDbPortlari = new()
    {
        ["1433"] = "SQL Server", ["1434"] = "SQL Server Browser", ["2382"] = "SQL Server Analysis Services (browser)", ["2383"] = "SQL Server Analysis Services",
        ["4022"] = "SQL Server Service Broker", ["5022"] = "SQL Server AlwaysOn / mirroring uç noktası", ["1521"] = "Oracle", ["1522"] = "Oracle",
        ["1523"] = "Oracle", ["1524"] = "Oracle", ["1525"] = "Oracle", ["1526"] = "Oracle / Informix",
        ["1527"] = "Oracle", ["1528"] = "Oracle", ["1529"] = "Oracle", ["1530"] = "Oracle",
        ["1630"] = "Oracle Connection Manager", ["2483"] = "Oracle (TCP)", ["2484"] = "Oracle (TCPS)", ["6200"] = "Oracle ONS (RAC)",
        ["5432"] = "PostgreSQL", ["5433"] = "PostgreSQL / Vertica", ["5444"] = "EDB Postgres", ["6432"] = "PgBouncer (PostgreSQL)",
        ["3306"] = "MySQL / MariaDB", ["33060"] = "MySQL X", ["4567"] = "Galera (MariaDB küme)", ["50000"] = "DB2",
        ["50001"] = "DB2", ["446"] = "DB2 DRDA", ["523"] = "DB2 yönetim", ["9088"] = "Informix",
        ["9089"] = "Informix", ["4100"] = "Sybase ASE?", ["5000"] = "Sybase ASE?", ["5001"] = "Sybase ASE?",
        ["1025"] = "Teradata?", ["5480"] = "Netezza", ["8563"] = "Exasol", ["3050"] = "Firebird",
        ["26257"] = "CockroachDB", ["30013"] = "SAP HANA", ["30015"] = "SAP HANA", ["30017"] = "SAP HANA",
        ["30041"] = "SAP HANA", ["39013"] = "SAP HANA", ["39015"] = "SAP HANA", ["27017"] = "MongoDB",
        ["27018"] = "MongoDB", ["27019"] = "MongoDB", ["9042"] = "Cassandra", ["9160"] = "Cassandra Thrift",
        ["7000"] = "Cassandra küme içi?", ["7001"] = "Cassandra küme içi?", ["6379"] = "Redis", ["26379"] = "Redis Sentinel",
        ["11211"] = "Memcached", ["9200"] = "Elasticsearch?", ["9300"] = "Elasticsearch küme içi?", ["8091"] = "Couchbase?",
        ["8092"] = "Couchbase?", ["8093"] = "Couchbase?", ["11210"] = "Couchbase", ["7687"] = "Neo4j",
        ["7474"] = "Neo4j HTTP?", ["8086"] = "InfluxDB?", ["8123"] = "ClickHouse HTTP?", ["9000"] = "ClickHouse?",
        ["9440"] = "ClickHouse TLS", ["10000"] = "Hive?", ["21050"] = "Impala", ["16000"] = "HBase Master",
        ["16020"] = "HBase RegionServer"
    };
    static readonly string[] DbSurecleri = ["sqlservr", "sqlbrowser", "msmdsrv", "oracle", "tnslsnr", "ocssd", "cmadmin", "postgres", "edb-postgres", "pgbouncer",
        "mysqld", "mariadbd", "db2sysc", "db2syscs", "oninit", "dataserver", "hdbindexserver", "hdbnameserver", "mongod", "mongos", "redis-server",
        "memcached", "influxd", "clickhouse-server", "cockroach", "fbserver", "firebird", "tdatcmd", "nzstart"];
    static readonly System.Text.RegularExpressions.Regex DbAd = new(@"(sql|db|ora(?=\d|db|cl|$)|orcl|exa|dwh|pgs)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    Dictionary<string, string> DbPortlari() =>
        cfg.GetSection("Eksik:VeritabaniPortlari").GetChildren().Where(c => c.Value is { Length: > 0 }).ToDictionary(c => c.Key, c => c.Value!) is { Count: > 0 } d
            ? d : DefaultDbPortlari;
    // Kanıttaki Carbon Black "- gelen port X" satırı
    static string? PortLine(string? evidence, string port) => evidence == null || port.Length == 0 ? null :
        evidence.Split('\n').FirstOrDefault(l => l.StartsWith($"- gelen port {port}:") || l.StartsWith($"- gelen port {port} ("));
    static string? DbSurec(string? line) => line == null ? null : DbSurecleri.FirstOrDefault(p => line.Contains(p + ".exe", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(line, $@"[\\/]{p}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    // Bu port veritabanı portu mu: bilinen DB portu (Sybase 5000 gibi belirsizler yalnız süreçle) ya da portu kabul eden süreç DB süreci
    bool DbPort(string? evidence, string port, out string? tek)
    {
        string? surec = DbSurec(PortLine(evidence, port));
        tek = DbPortlari().GetValueOrDefault(port);
        if (surec != null) { tek ??= surec; return true; }
        return tek != null && !tek.EndsWith('?');
    }
    string DbHints(EnvanterSnapshot env, string ip, List<string> miss, string evidence)
    {
        var parts = new List<string>();
        string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault();
        if (host != null && DbAd.Match(host) is { Success: true } m) parts.Add($"sunucu adı '{host}' içinde '{m.Value}' (isimlendirme standardında veritabanı sunucusu)");
        var ports = env.AppsOnIp(ip).Select(h => Key(h.Port)).Where(p => p.Length > 0).Distinct().ToList();
        foreach (var p in ports.Where(p => DbPort(evidence, p, out _)))   // "?"li portlar yalnız DB süreciyle
        {
            DbPort(evidence, p, out var tek);
            parts.Add($"port {p}{(tek != null ? $" ({tek.TrimEnd('?')}{(tek.EndsWith('?') ? "?" : "")})" : "")}{(miss.Contains(p) ? " — uygulama adı boş" : "")}{(DbSurec(PortLine(evidence, p)) is { } sr ? $", portu kabul eden süreç {sr}" : "")}");
        }
        return parts.Count == 0 ? "" : "VERİTABANI SUNUCUSU İPUÇLARI: " + string.Join("; ", parts) + "\n";
    }

    async Task<Dictionary<string, string>> BuildAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env,
        int hours, CancellationToken ct)
    {
        var profiles = await islev.ProfilesAsync(ips, env, ct);
        var cb = await CarbonBlackAsync(ips, hours, ct);
        var smf = await SmFieldsAsync(ips, ports, env, ct);
        var outp = new Dictionary<string, string>();
        foreach (var ip in ips)
        {
            var sb = new StringBuilder();
            var miss = ports.GetValueOrDefault(ip) ?? [];
            string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault();
            sb.AppendLine($"### Sunucu {ip}{(host != null ? " · " + host : "")} · segment {env.FindSegment(ip)?.Label ?? "?"}");
            var empty = env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName) && miss.Contains(Key(h.Port))).GroupBy(h => Key(h.Port));
            sb.AppendLine("UYGULAMA ADI BOŞ PORTLAR (ERT satırı):");
            foreach (var g in empty)
            {
                var h = g.First();
                string pi = PortBilgisi.Find(g.Key, cfg) is { } info ? $" (bilinen kullanım: {info.Name})" : "";
                sb.AppendLine($"- port {(g.Key == "" ? "(yok)" : g.Key)}{pi}: " + string.Join(", ", new[]
                {
                    h.Website is { } w ? "WEBSITE=" + w : null, h.ServiceName is { } sv ? "SERVICENAME=" + sv : null,
                    h.VipIp is { } vi ? $"VIP={vi}:{h.VipPort}" : null, h.UnixName is { } un ? "UNIXNAME=" + un : null,
                    h.VarlikMuhafizi is { } vm ? "muhafız=" + vm : null, h.SunucuIsletenBirim is { } sb2 ? "işleten=" + sb2 : null
                }.OfType<string>().DefaultIfEmpty("başka bilgi yok")));
            }
            // Sıra önemli (kullanıcı: SM açıklamaları önemsenmiyordu): metin Eksik:MaxKarakter'de kesilir ve uzun profil (ERT, MSY, ÇSY,
            // Kahin…) başta olunca SM / CB kesiliyordu. Güçlü kanıtlar önce, komşular ve profil sonda.
            if (smf.TryGetValue(ip, out var smfText)) sb.Append(smfText);
            if (cb.TryGetValue(ip, out var c)) sb.Append(c.Text(env, ip, miss));
            else sb.AppendLine($"Carbon Black (son {hours} saat): bu sunucuda ağ olayı yok (ajan yok ya da trafik yok).");
            sb.Append(DbHints(env, ip, miss, sb.ToString()));
            string profil = profiles.TryGetValue(ip, out var p) ? p.Metin.TrimEnd() : "";
            // Envanter Kahini (kullanıcı: "çok dikkate alıyor, en son baksın"): 1. turda AI'a gitmez, sonda ayrı bölüm (AskAsync 2. tur)
            profil = string.Join("\n", profil.Split('\n').Where(l => !l.StartsWith("Envanter Kahini: ")));
            string nb = Neighbors(env, ip, miss);
            string sofar = sb.ToString() + nb + profil;
            var cands = Candidates(env, ip, sofar)
                .Where(c => AjanOf(c) is not { } a || AjanSunucusu(env, ip, a, sofar) || miss.Any(p => PortExeHas(sofar, p, a))).ToList();   // ajan adları aday değil (ürünün kendi sunucusu hariç)
            if (cands.Count > 0) sb.AppendLine("ADAY UYGULAMALAR (ERT'deki adlar): " + string.Join(" | ", cands));
            sb.Append(nb);
            if (profil.Length > 0) sb.AppendLine("SUNUCU PROFİLİ (ERT, segment, VIP, sanal sunucu, SM kurulum — ek bilgi):").AppendLine(profil);
            if (p?.Kahin is { Count: > 0 } kh) { sb.AppendLine(KahinBas); foreach (var k in kh) sb.AppendLine("- " + k); }
            outp[ip] = sb.ToString();
        }
        return outp;
    }

    // Önerilen ad ERT'deki işleten birim / varlık muhafızı / uygulama sahibi değerlerinden biri mi (katalogda aynı adlı uygulama yoksa)
    HashSet<string>? _ekipler; EnvanterSnapshot? _ekipEnv;
    string? EkipAdi(EnvanterSnapshot env, string ip, string app)
    {
        lock (_gate)
            if (!ReferenceEquals(_ekipEnv, env))
            {
                _ekipler = env.HostIps.SelectMany(i => env.AppsOnIp(i)).SelectMany(h => new[] { h.SunucuIsletenBirim, h.VarlikMuhafizi, h.UygulamaVarlikSahibi })
                    .OfType<string>().Select(Norm).Where(x => x.Length >= 4).ToHashSet();
                _ekipEnv = env;
            }
        string n = Norm(System.Text.RegularExpressions.Regex.Replace(app, @"\s*(veritaban[ıi]|database|db)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (n.Length < 4 || env.AppNames.Any(a => Norm(a) == n)) return null;
        var h = env.AppsOnIp(ip);
        if (h.Any(x => x.SunucuIsletenBirim is { } b && Norm(b) == n)) return "işleten birim";
        if (h.Any(x => x.VarlikMuhafizi is { } m && Norm(m) == n)) return "varlık muhafızı";
        if (h.Any(x => x.UygulamaVarlikSahibi is { } s && Norm(s) == n)) return "uygulama sahibi";
        return _ekipler!.Contains(n) ? "işleten birim / muhafız / sahip listesi" : null;
    }

    // SM açıklamalarında adı geçen sistemler: ERT kataloğundaki adlar (çok kelimeliyse kelimelerin hepsi, tek kelimeliyse adın kendisi metinde)
    // ve ajan / ürün adları (ör. "Symantec Endpoint Protection ürünü" → "Symantec Endpoint Protection"; yalnız ürün sunucusu metniyse).
    const string SmAdayBas = "SM AÇIKLAMALARINDA ADI GEÇEN SİSTEMLER (kodla çıkarıldı; bu sunucunun uygulaması için EN GÜÇLÜ aday):";
    record SmAday(string Ad, int N, int Port);
    List<SmAday> SmAdaylari(EnvanterSnapshot env, List<(string Text, bool Port)> texts)
    {
        if (texts.Count == 0) return [];
        PrepCatalog(env);
        var say = new Dictionary<string, (int n, int p)>(StringComparer.OrdinalIgnoreCase);
        void Add(string ad, bool port) { var c = say.GetValueOrDefault(ad); say[ad] = (c.n + 1, c.p + (port ? 1 : 0)); }
        foreach (var (text, port) in texts)
        {
            var tok = IslevService.Tokens(text);
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (app, ct) in _catTok!)
            {
                if (env.IsSystemApp(app)) continue;
                var key = ct.Where(t => t.Length >= 3 && !GenelKelime.Contains(t)).ToList();
                if (key.Count == 0) continue;
                bool hit = key.Count >= 2 ? key.All(tok.Contains)
                    : app.Trim().Length >= 3 && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\p{{L}}\d]){System.Text.RegularExpressions.Regex.Escape(app.Trim())}(?![\p{{L}}\d])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (hit) found.Add(app);
            }
            // aynı metinde hem "KÖS" hem "KÖS Raporlama" bulunduysa kısa olanı at
            foreach (var a in found.Where(a => !found.Any(b => b.Length > a.Length && Norm(b).Contains(Norm(a)))).ToList()) Add(a, port);
            if (AjanOf(text) is { } ajan && UrunSunucusuMetni(text, ajan)
                && System.Text.RegularExpressions.Regex.Match(text, $@"(?i:{System.Text.RegularExpressions.Regex.Escape(ajan)})(\s+[A-ZÇĞİÖŞÜ][\wçğıöşüÇĞİÖŞÜ.]*){{0,3}}") is { Success: true } m
                && !found.Any(f => Norm(f).Contains(Norm(ajan))))
                Add(UrunAdi(m.Value), port);
        }
        return say.Select(kv => new SmAday(kv.Key, kv.Value.n, kv.Value.p)).OrderByDescending(a => a.Port).ThenByDescending(a => a.N).Take(4).ToList();
    }
    // "SYMANTEC ENDPOINT PROTECTION ÜRÜNÜ" → sondaki genel kelimeler atılır
    static readonly HashSet<string> UrunSonEk = new(StringComparer.OrdinalIgnoreCase) { "ürünü", "ürün", "sunucusu", "sunucu", "sunucuları", "için", "server", "uygulaması", "yazılımı" };
    static string UrunAdi(string s)
    {
        var w = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (w.Count > 1 && UrunSonEk.Contains(w[^1].Trim('.', ','))) w.RemoveAt(w.Count - 1);
        return string.Join(' ', w);
    }
    static string? SmAdayOf(string? evidence) => evidence?.Split('\n').FirstOrDefault(l => l.StartsWith(SmAdayBas)) is { } l
        ? l[SmAdayBas.Length..].Split(" | ")[0].Split(" (")[0].Trim() is { Length: > 0 } a ? a : null : null;

    // SM talepleri (kullanıcı tarifi): sunucu ZT_HEDEF / ZT_KAYNAK'ta (IP) ya da ZT_HEDEFHOSTNAME / ZT_KAYNAKHOSTNAME'de (UNIXNAME) aranır;
    // bulunan talebin DESCRIPTION, BRIEF_DESCRIPTION, ZT_KAYNAKACIKLAMA, ZT_HEDEFACIKLAMA alanları AI'a gider. Sunucunun talepte HEDEF mi
    // KAYNAK mı olduğu yazılır: hedefse ZT_HEDEFACIKLAMA bu sunucuyu, ZT_KAYNAKACIKLAMA karşı tarafı anlatır (kaynaksa tersi). Hedef olup
    // ZT_HEDEFPORT'u boş portu içeren talepler başa; en yeni 10. Talepte doluysa ZT_HEDEFUYGULAMA / ZT_KAYNAKUYGULAMA
    // ek bilgi. Tek sorgu / sunucu, tarih Eksik:SmTarihKolonu (SUBMIT_DATE; canlıda OPEN_TIME yok) ≥ SmYil.
    async Task<Dictionary<string, string>> SmFieldsAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env, CancellationToken ct)
    {
        var outp = new Dictionary<string, string>();
        var want = ips.Select(ip => (ip, host: env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault())).ToList();
        if (want.Count == 0) return outp;
        var tables = cfg.GetSection("Eksik:SmTablolari").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (tables.Count == 0) tables = ["dbo.REQUESTM1"];
        string dateCol = cfg["Eksik:SmTarihKolonu"] is { Length: > 0 } dc && System.Text.RegularExpressions.Regex.IsMatch(dc, "^[A-Za-z_][A-Za-z0-9_]*$") ? dc : "SUBMIT_DATE";
        var since = DateTime.Today.AddYears(-Math.Clamp(cfg.GetValue("Eksik:SmYil", cfg.GetValue("Islev:SmYil", 3)), 1, 20));
        var rows = new List<Dictionary<string, string>>();
        string? err = null;
        string[] names = ["NUMBER", "TARIH", "ZT_HEDEF", "ZT_KAYNAK", "ZT_HEDEFHOSTNAME", "ZT_KAYNAKHOSTNAME", "ZT_HEDEFPORT", "ZT_PORT",
            "BRIEF_DESCRIPTION", "DESCRIPTION", "ZT_HEDEFACIKLAMA", "ZT_KAYNAKACIKLAMA", "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA", "ZT_KAYNAKUYGULAMA"];
        foreach (var table in tables.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")))
        {
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand("", conn) { CommandTimeout = cfg.GetValue("Islev:SqlTimeoutSaniye", 180) };
                int n = 0;
                string P(string v, bool like) { string name = "@f" + n++; cmd.Parameters.AddWithValue(name, like ? $"%{v}%" : v); return name; }
                var conds = new List<string>();
                foreach (var (ip, host) in want)
                {
                    string l = P(ip, true);
                    conds.Add($"req.ZT_HEDEF LIKE {l} OR req.ZT_KAYNAK LIKE {l}");
                    if (host is { Length: >= 4 }) { string h = P(host, false); conds.Add($"req.ZT_HEDEFHOSTNAME = {h} OR req.ZT_KAYNAKHOSTNAME = {h}"); }
                }
                cmd.Parameters.AddWithValue("@since", since);
                string sel = string.Join(", ", names.Select(c => c switch
                {
                    "TARIH" => $"req.[{dateCol}]",
                    "DESCRIPTION" => "LEFT(CAST(req.[DESCRIPTION] AS nvarchar(max)), 3000)",
                    _ => $"req.[{c}]"
                }));
                cmd.CommandText = $"""
                    SELECT TOP (300) {sel}
                    FROM {table} req WITH (NOLOCK)
                    WHERE req.[{dateCol}] >= @since AND ({string.Join(" OR ", conds)})
                    ORDER BY req.[{dateCol}] DESC
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (r.IsDBNull(i)) continue;
                        object v = r.GetValue(i);
                        string s = v is DateTime dt ? dt.ToString("yyyy-MM-dd") : v is byte[] ? IslevService.ParseHexOrBinary(v) : Convert.ToString(v) ?? "";
                        if (!string.IsNullOrWhiteSpace(s)) row[names[i]] = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
                    }
                    rows.Add(row);
                }
            }
            catch (SqlException ex) { err = $"{table}: {ex.Message}"; }
        }
        static string? G(Dictionary<string, string> r, params string[] keys) => keys.Select(k => r.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        static bool HasIp(string? text, string ip) => text != null && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\d.]){System.Text.RegularExpressions.Regex.Escape(ip)}(?![\d])");
        static bool HasPort(string? text, string port) => text != null && port.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<!\d){port}(?!\d)");
        foreach (var (ip, host) in want)
        {
            bool Same(string? h) => host is { Length: >= 4 } && h != null && h.Split(',', ';', ' ').Any(x => string.Equals(x.Trim().Split('.')[0], host.Split('.')[0], StringComparison.OrdinalIgnoreCase));
            var miss = (ports.GetValueOrDefault(ip) ?? []).Where(p => p.Length > 0).ToList();
            var all = rows.Select(r =>
                {
                    bool hedef = HasIp(G(r, "ZT_HEDEF"), ip) || Same(G(r, "ZT_HEDEFHOSTNAME"));
                    bool kaynak = HasIp(G(r, "ZT_KAYNAK"), ip) || Same(G(r, "ZT_KAYNAKHOSTNAME"));
                    // port eşleşmesi yalnız ZT_HEDEFPORT'ta (kullanıcı)
                    var ps = hedef ? miss.Where(p => HasPort(G(r, "ZT_HEDEFPORT"), p)).ToList() : [];
                    string? aj = AjanOf(string.Join(" ", new[] { "BRIEF_DESCRIPTION", "DESCRIPTION", "ZT_HEDEFACIKLAMA", "ZT_KAYNAKACIKLAMA",
                        "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA", "ZT_KAYNAKUYGULAMA" }.Select(k => G(r, k))));
                    if (aj != null && (AjanSunucusu(env, ip, aj) || UrunSunucusuMetni(G(r, hedef ? "ZT_HEDEFACIKLAMA" : "ZT_KAYNAKACIKLAMA"), aj)
                        || (hedef && (UrunSunucusuMetni(G(r, "BRIEF_DESCRIPTION"), aj) || UrunSunucusuMetni(Cut(G(r, "DESCRIPTION"), 400), aj))))) aj = null;   // ürünün kendi sunucusunda bu talepler gerçek uygulama kanıtı
                    return (r, hedef, kaynak, ps, aj);
                })
                .Where(x => x.hedef || x.kaynak).DistinctBy(x => G(x.r, "NUMBER")).ToList();
            var mine = all
                .OrderBy(x => x.aj != null).ThenByDescending(x => x.ps.Count > 0).ThenByDescending(x => x.hedef).ThenByDescending(x => G(x.r, "TARIH")).Take(10).ToList();
            if (mine.Count == 0)
            {
                outp[ip] = err != null ? $"SM TALEPLERİ: okunamadı ({Cut(err, 150)})\n"
                    : $"SM TALEPLERİ: son {DateTime.Today.Year - since.Year} yılda bu sunucunun (IP {ip}{(host != null ? ", ad " + host : "")}) ZT_HEDEF / ZT_KAYNAK olduğu talep bulunamadı.\n";
                continue;
            }
            var sb = new StringBuilder();
            // Aranan (boş) port başına açık özet: bu porta erişim talebi var mı (tüm eşleşen talepler üzerinden, yalnız ilk 10 değil)
            foreach (var p in miss)
            {
                var hitAll = all.Where(x => x.ps.Contains(p)).OrderByDescending(x => G(x.r, "TARIH")).ToList();
                var hit = hitAll.Where(x => x.aj == null).ToList();
                var ajHit = hitAll.Where(x => x.aj != null).ToList();
                if (ajHit.Count > 0)
                    sb.AppendLine($"PORT {p}: {ajHit.Count} talep AJAN / ARAÇ talebi ({string.Join(", ", ajHit.Select(x => x.aj).Distinct())}; {string.Join(", ", ajHit.Take(3).Select(x => G(x.r, "NUMBER")))}) — " +
                        "bu ürünün sunucuya kurulumu / erişimi içindir, sunucunun iş uygulamasını GÖSTERMEZ.");
                sb.AppendLine(hit.Count > 0
                    ? $"PORT {p}'E ERİŞİM TALEBİ VAR: {hit.Count} talepte bu sunucu hedef ve ZT_HEDEFPORT {p} içeriyor (son {G(hit[0].r, "TARIH")}; {string.Join(", ", hit.Take(5).Select(x => G(x.r, "NUMBER")))})"
                      + (hit.Select(x => G(x.r, "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA")).OfType<string>().GroupBy(a => a, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault() is { } ga
                          ? $"; talepteki hedef uygulama: {ga.Key} ({ga.Count()})" : "")
                    : ajHit.Count > 0 ? $"PORT {p}'E İŞ UYGULAMASI İÇİN ERİŞİM TALEBİ YOK (yalnız ajan / araç talepleri var)"
                    : $"PORT {p}'E ERİŞİM TALEBİ YOK (son {DateTime.Today.Year - since.Year} yılda bu sunucuya ZT_HEDEFPORT {p} olan talep bulunamadı)");
            }
            // Kullanıcı: kaynak / hedef açıklamaları yeterince önemsenmiyordu → talepler içinde kaybolmasın diye önce TÜM eşleşen taleplerdeki
            // BU SUNUCUYU anlatan açıklama (hedefse ZT_HEDEFACIKLAMA, kaynaksa ZT_KAYNAKACIKLAMA) tekilleştirilip sayısıyla, ayrıca hedef
            // olduğu taleplerde erişen tarafın (ZT_KAYNAKACIKLAMA) açıklaması
            string? Own(Dictionary<string, string> r, bool hedef) => G(r, hedef ? "ZT_HEDEFACIKLAMA" : "ZT_KAYNAKACIKLAMA");
            var bunu = all.Where(x => x.aj == null).Select(x => (t: Own(x.r, x.hedef), x)).Where(y => y.t != null)
                .GroupBy(y => Norm(y.t!)).Select(g => (t: g.First().t!, n: g.Count(), son: g.Max(y => G(y.x.r, "TARIH")), port: g.Any(y => y.x.ps.Count > 0)))
                .OrderByDescending(z => z.port).ThenByDescending(z => z.n).ThenByDescending(z => z.son).Take(8).ToList();
            if (bunu.Count > 0)
            {
                sb.AppendLine("BU SUNUCUYU ANLATAN TALEP AÇIKLAMALARI (talep sahibinin bu sunucu için yazdığı; EN GÜÇLÜ SM KANITI — sunucunun hangi sistem /");
                sb.AppendLine("uygulama olduğunu doğrudan söyler):");
                foreach (var z in bunu) sb.AppendLine($"  • \"{Cut(z.t, 250)}\" — {z.n} talep, son {z.son}{(z.port ? " · boş portu anan talepte" : "")}");
            }
            // Hedef olduğu taleplerin kısa açıklamaları (kullanıcı: kısa açıklama / açıklama alanları da en kritik SM kanıtı)
            var kisa = all.Where(x => x.hedef && x.aj == null).Select(x => (t: G(x.r, "BRIEF_DESCRIPTION"), x)).Where(y => y.t != null)
                .GroupBy(y => Norm(y.t!)).Select(g => (t: g.First().t!, n: g.Count(), port: g.Any(y => y.x.ps.Count > 0)))
                .OrderByDescending(z => z.port).ThenByDescending(z => z.n).Take(6).ToList();
            if (kisa.Count > 0)
            {
                sb.AppendLine("BU SUNUCUNUN HEDEF OLDUĞU TALEPLERİN KISA AÇIKLAMALARI (sunucuya ne için erişildiğini / sunucunun ne olduğunu söyler):");
                foreach (var z in kisa) sb.AppendLine($"  • \"{Cut(z.t, 200)}\" — {z.n} talep{(z.port ? " · boş portu anan talepte" : "")}");
            }
            // Açıklamalarda adı geçen sistemler (kodla çıkarılır; AI bulamazsa / Kahin'e giderse bu kullanılır — SmAday)
            var adaylar = SmAdaylari(env, all.Where(x => x.aj == null && (x.hedef || Own(x.r, false) != null))
                .Select(x => (string.Join(" · ", new[] { Own(x.r, x.hedef), x.hedef ? G(x.r, "BRIEF_DESCRIPTION") : null, x.hedef ? Cut(G(x.r, "DESCRIPTION"), 400) : null }
                    .OfType<string>()), x.ps.Count > 0)).ToList());
            if (adaylar.Count > 0)
                sb.AppendLine(SmAdayBas + " " + string.Join(" | ", adaylar.Select(a => $"{a.Ad} ({a.N} talep{(a.Port > 0 ? $", boş portu anan {a.Port}" : "")})")));
            var karsi = all.Where(x => x.hedef && x.aj == null).Select(x => G(x.r, "ZT_KAYNAKACIKLAMA")).OfType<string>()
                .GroupBy(Norm).OrderByDescending(g => g.Count()).Take(5).Select(g => $"\"{Cut(g.First(), 120)}\" ({g.Count()})").ToList();
            if (karsi.Count > 0) sb.AppendLine("Bu sunucuya erişen taraflar (kaynak açıklaması; İSTEMCİLER, bu sunucunun uygulaması değil): " + string.Join("; ", karsi));
            sb.AppendLine($"SM TALEPLERİ (bu sunucunun hedef ya da kaynak olduğu erişim talepleri, en yeni; {mine.Count(x => x.hedef)} hedef, {mine.Count(x => x.kaynak && !x.hedef)} kaynak):");
            foreach (var (r, hedef, kaynak, ps, aj) in mine)
            {
                string rol = hedef && kaynak ? "bu sunucu HEM HEDEF HEM KAYNAK" : hedef ? $"bu sunucu HEDEF (kaynak: {Cut(G(r, "ZT_KAYNAK", "ZT_KAYNAKHOSTNAME"), 60)})"
                    : $"bu sunucu KAYNAK (hedef: {Cut(G(r, "ZT_HEDEF", "ZT_HEDEFHOSTNAME"), 60)})";
                string port = G(r, "ZT_HEDEFPORT") ?? "";
                if (aj != null) sb.AppendLine($"- [AJAN / ARAÇ TALEBİ: {aj} — sunucunun uygulaması DEĞİL, uygulama kanıtı olarak kullanma]");
                sb.AppendLine($"- {G(r, "NUMBER")} {G(r, "TARIH")} · {rol}{(port.Length > 0 ? $" · hedef port {Cut(port, 40)}" : "")}{(ps.Count > 0 ? $" · BOŞ PORT {string.Join(",", ps)}" : "")}");
                if (G(r, "BRIEF_DESCRIPTION") is { } bd) sb.AppendLine($"    Kısa açıklama: {Cut(bd, 200)}");
                // açıklamaları sunucunun rolüne göre etiketle
                if (G(r, hedef ? "ZT_HEDEFACIKLAMA" : "ZT_KAYNAKACIKLAMA") is { } own) sb.AppendLine($"    {(hedef ? "Hedef" : "Kaynak")} açıklaması (BU SUNUCU): {Cut(own, 250)}");
                if (G(r, hedef ? "ZT_KAYNAKACIKLAMA" : "ZT_HEDEFACIKLAMA") is { } other) sb.AppendLine($"    {(hedef ? "Kaynak" : "Hedef")} açıklaması (karşı taraf): {Cut(other, 200)}");
                if (G(r, "DESCRIPTION") is { } d) sb.AppendLine($"    Açıklama: {Cut(d, 400)}");
                string? hApp = G(r, "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA"), kApp = G(r, "ZT_KAYNAKUYGULAMA");
                if (hApp != null || kApp != null)
                    sb.AppendLine($"    Talepteki uygulama alanları: {(hApp != null ? $"hedef uygulama = {hApp}{(hedef ? " (BU SUNUCU)" : "")}" : "")}{(hApp != null && kApp != null ? "; " : "")}{(kApp != null ? $"kaynak uygulama = {kApp}{(kaynak && !hedef ? " (BU SUNUCU)" : "")}" : "")}");
            }
            outp[ip] = sb.ToString();
        }
        return outp;
    }

    // Komşu ipuçları: benzer adlı sunucular (ad sonundaki sayı atılınca aynı: zb1pwatmvzsra01/02…), aynı segmentte aynı portu
    // kullanan sunucular ve segmentin yaygın uygulamaları (hepsi ERT'de uygulama adı dolu satırlardan)
    Dictionary<string, Dictionary<string, int>>? _byStem, _bySegPort, _bySeg;
    EnvanterSnapshot? _nbEnv;
    static string? Stem(string? host) => host == null ? null
        : System.Text.RegularExpressions.Regex.Replace(host.Split('.')[0].ToLowerInvariant(), @"[-_]?\d+$", "") is { Length: >= 4 } st ? st : null;
    static void Count(Dictionary<string, Dictionary<string, int>> d, string? k, string app)
    {
        if (k == null) return;
        if (!d.TryGetValue(k, out var m)) d[k] = m = new(StringComparer.OrdinalIgnoreCase);
        m[app] = m.GetValueOrDefault(app) + 1;
    }
    static string Top(Dictionary<string, Dictionary<string, int>> d, string? k, int n = 5) =>
        k != null && d.TryGetValue(k, out var m) ? string.Join(", ", m.OrderByDescending(x => x.Value).Take(n).Select(x => $"{x.Key} ({x.Value})")) : "";
    string Neighbors(EnvanterSnapshot env, string ip, List<string> miss)
    {
        if (!ReferenceEquals(_nbEnv, env))
        {
            _byStem = new(); _bySegPort = new(); _bySeg = new();
            foreach (var i in env.HostIps)
            {
                var rows = env.AppsOnIp(i).Where(h => h.AppName != null && !env.IsSystemApp(h.AppName)).ToList();
                if (rows.Count == 0) continue;
                string? seg = env.FindSegment(i)?.Label, stem = Stem(env.AppsOnIp(i).Select(h => h.UnixName).OfType<string>().FirstOrDefault());
                foreach (var app in rows.Select(h => h.AppName!).Distinct(StringComparer.OrdinalIgnoreCase)) { Count(_byStem, stem, app); Count(_bySeg, seg, app); }
                foreach (var h in rows.Where(h => h.Port != null)) Count(_bySegPort, seg + "|" + h.Port, h.AppName!);
            }
            _nbEnv = env;
        }
        var sb = new StringBuilder();
        string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault(), segL = env.FindSegment(ip)?.Label, st = Stem(host);
        if (Top(_byStem!, st) is { Length: > 0 } a) sb.AppendLine($"BENZER ADLI SUNUCULAR ({st}*) ERT'de: {a}");
        foreach (var p in miss.Where(p => p != ""))
            if (Top(_bySegPort!, segL + "|" + p) is { Length: > 0 } b) sb.AppendLine($"AYNI SEGMENTTE ({segL}) {p} PORTUNU KULLANAN SUNUCULAR ERT'de (zayıf ipucu): {b}");
        if (Top(_bySeg!, segL, 6) is { Length: > 0 } c) sb.AppendLine($"SEGMENTİN ({segL}) YAYGIN UYGULAMALARI (ZAYIF ipucu — segmentte farklı ürünlerin sunucuları olabilir, segment adından ürün türetme): {c}");
        return sb.ToString();
    }

    // Aday uygulamalar: aynı sunucunun diğer portları, VIP'leri ve havuz komşuları, aynı muhafızın uygulamaları,
    // adı kanıt metniyle (exe yolu, WEBSITE, sunucu adı…) kelime düzeyinde örtüşen ERT uygulamaları
    Dictionary<string, List<string>>? _byMuhafiz;
    List<(string App, HashSet<string> Tok)>? _catTok;
    EnvanterSnapshot? _candEnv;
    void PrepCatalog(EnvanterSnapshot env)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_candEnv, env)) return;
            _byMuhafiz = env.HostIps.SelectMany(i => env.AppsOnIp(i)).Where(h => h.VarlikMuhafizi != null && h.AppName != null && !env.IsSystemApp(h.AppName))
                .GroupBy(h => h.VarlikMuhafizi!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.GroupBy(h => h.AppName!).OrderByDescending(x => x.Count()).Select(x => x.Key).Take(8).ToList(), StringComparer.OrdinalIgnoreCase);
            _catTok = env.AppNames.Select(a => (a, IslevService.Tokens(a))).Where(x => x.Item2.Count > 0).ToList();
            _candEnv = env;
        }
    }
    List<string> Candidates(EnvanterSnapshot env, string ip, string evidence)
    {
        PrepCatalog(env);
        var list = new List<string>();
        void Add(IEnumerable<string?> xs) { foreach (var x in xs) if (x != null && !env.IsSystemApp(x) && !list.Contains(x, StringComparer.OrdinalIgnoreCase)) list.Add(x); }
        Add(env.AppNamesOnIp(ip));
        foreach (var m in env.MemberOf(ip))
        {
            Add(env.Match(m.LbIp, m.LbPort).Apps.Select(a => a.AppName));
            Add(env.VipMembers(m.LbIp, m.LbPort).Where(x => x.Ip != ip).SelectMany(x => env.AppNamesOnIp(x.Ip)).Take(6));
        }
        string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault(), segL = env.FindSegment(ip)?.Label;
        if (_byStem != null && Stem(host) is { } st && _byStem.TryGetValue(st, out var sm)) Add(sm.OrderByDescending(x => x.Value).Take(5).Select(x => (string?)x.Key));
        if (_bySegPort != null)
            foreach (var h in env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName) && h.Port != null))
                if (_bySegPort.TryGetValue(segL + "|" + h.Port, out var spm)) Add(spm.OrderByDescending(x => x.Value).Take(3).Select(x => (string?)x.Key));
        foreach (var mu in env.AppsOnIp(ip).Select(h => h.VarlikMuhafizi).OfType<string>().Distinct())
            if (_byMuhafiz!.TryGetValue(mu, out var apps)) Add(apps);
        var tok = IslevService.Tokens(evidence);
        Add(_catTok!.Select(c => (c.App, n: c.Tok.Count(t => t.Length >= 3 && tok.Contains(t)), r: (double)c.Tok.Count(tok.Contains) / c.Tok.Count))
            .Where(x => x.n >= 1 && x.r >= 0.5).OrderByDescending(x => x.r).ThenByDescending(x => x.n).Take(12).Select(x => (string?)x.App));
        return list.Take(30).ToList();
    }

    // ---------------- Carbon Black ----------------
    // Bilinen ürün exe'leri (kullanıcı: veri ambarı sunucusunda sas.exe çalışıyordu, AI bakmadı). Anahtar: exe adı ya da yol parçası.
    static readonly (string Kalip, string Urun)[] BilinenUrunler =
    [
        ("sas.exe", "SAS"), ("\\sashome\\", "SAS"), ("\\sasfoundation\\", "SAS"), ("objspawn", "SAS"), ("sasstudio", "SAS"),
        ("odi", "Oracle Data Integrator"), ("pmserver", "Informatica PowerCenter"), ("infaservice", "Informatica"), ("\\informatica\\", "Informatica"),
        ("dtexec", "SQL Server Integration Services"), ("msdtssrvr", "SQL Server Integration Services"), ("msmdsrv", "SQL Server Analysis Services"),
        ("reportingservicesservice", "SQL Server Reporting Services"), ("\\ssrs\\", "SQL Server Reporting Services"), ("\\power bi report server\\", "Power BI Report Server"),
        ("mstrsvr", "MicroStrategy"), ("\\microstrategy\\", "MicroStrategy"), ("cogbootstrap", "IBM Cognos"), ("\\cognos\\", "IBM Cognos"),
        ("nqsserver", "Oracle BI (OBIEE)"), ("\\qlik", "Qlik"), ("\\tableau", "Tableau"), ("\\datastage\\", "IBM DataStage"), ("\\talend", "Talend"),
        ("\\pentaho", "Pentaho"), ("ctmag", "Control-M"), ("\\control-m", "Control-M"), ("\\bmc software\\control", "Control-M"),
        ("amqrmppa", "IBM MQ"), ("\\ibm\\mq\\", "IBM MQ"), ("sapstartsrv", "SAP"), ("\\usr\\sap\\", "SAP"), ("\\biztalk", "BizTalk"),
        ("\\tibco", "TIBCO"), ("\\websphere", "IBM WebSphere"), ("\\weblogic", "Oracle WebLogic"), ("\\jboss", "JBoss"), ("\\tomcat", "Apache Tomcat"),
        ("\\elastic", "Elasticsearch"), ("\\kafka", "Kafka"), ("\\rabbitmq", "RabbitMQ"), ("\\ibm\\iib", "IBM Integration Bus"), ("\\ace\\", "IBM App Connect")
    ];
    static string? BilinenUrun(string yol)
    {
        string y = yol.Replace('/', '\\').ToLowerInvariant(), exe = y.Split('\\').Last();
        return BilinenUrunler.FirstOrDefault(b => b.Kalip.Contains('\\') ? y.Contains(b.Kalip) : exe.StartsWith(b.Kalip)).Urun;
    }

    sealed class Cb
    {
        public readonly List<(string Yon, string Port, string Yol, long Hits, int Peers, List<string> Ornek)> Rows = [];
        public string Text(EnvanterSnapshot env, string ip, List<string> missing)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CARBON BLACK (bu sunucudaki süreçlerin ağ bağlantıları):");
            // Sunucuda çalışan iş programları (genel Windows / ajan süreçleri hariç) — sunucunun ne iş yaptığını en doğrudan gösterir
            var progs = Rows.Select(r => r.Yol).Where(y => y != "-").Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(y => !SurecAi.GenelExe(y.Split('\\', '/').Last(), y)).Take(12).ToList();
            if (progs.Count > 0)
                sb.AppendLine("SUNUCUDA ÇALIŞAN İŞ PROGRAMLARI (genel sistem / ajan süreçleri hariç): " + string.Join("; ", progs.Select(y =>
                    $"{y.Split('\\', '/').Last()} [{y}]{(BilinenUrun(y) is { } u ? $" = {u}" : "")}")));
            string Exe(string yol) => yol.Split('\\', '/').Last();
            string Peers(List<string> ips) => string.Join(", ", ips.Select(p =>
                env.IsVipGw(p, out _) ? "LB GW" : env.AppNamesOnIp(p).FirstOrDefault() is { } a ? a : env.VipMembers(p).Count > 0 ? "VIP " + p : p).Distinct().Take(5));
            foreach (var g in Rows.Where(r => r.Yon == "in").GroupBy(r => r.Port).OrderBy(g => missing.Contains(g.Key) ? 0 : 1).ThenByDescending(g => g.Sum(r => r.Hits)).Take(15))
            {
                var procs = g.OrderByDescending(r => r.Hits).Take(3).Select(r =>
                    Exe(r.Yol) is var e && (e.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase) || e.Equals("System", StringComparison.OrdinalIgnoreCase))
                        ? "ntoskrnl.exe (HTTP.sys: asıl uygulama HTTP.sys'e kayıtlı ayrı bir exe)" : $"{e} [{r.Yol}]");
                sb.AppendLine($"- gelen port {g.Key}{(missing.Contains(g.Key) ? " (ERT'de uygulama adı boş)" : "")}: {string.Join("; ", procs)} · {g.Sum(r => r.Hits)} bağlantı, {g.Max(r => r.Peers)} istemci · istemciler: {Peers(g.SelectMany(r => r.Ornek).ToList())}");
            }
            foreach (var g in Rows.Where(r => r.Yon == "out").GroupBy(r => r.Yol).OrderByDescending(g => g.Sum(r => r.Hits)).Take(10))
                sb.AppendLine($"- {Exe(g.Key)} [{g.Key}] dışarı bağlanıyor: " + string.Join(", ",
                    g.OrderByDescending(r => r.Hits).Take(5).Select(r => $"port {r.Port} → {Peers(r.Ornek)}")));
            return sb.ToString();
        }
    }

    async Task<Dictionary<string, Cb>> CarbonBlackAsync(List<string> ips, int hours, CancellationToken ct)
    {
        var outp = new Dictionary<string, Cb>();
        if (ips.Count == 0 || string.IsNullOrEmpty(cfg["Splunk:BaseUrl"])) return outp;
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        var valid = ips.Where(ip => LookupQuery.TryParseIpv4(ip, out _)).ToList();
        foreach (var part in valid.Chunk(20))
        {
            string terms = string.Join(" OR ", part.Select(ip => $"TERM({ip})"));
            string inList = string.Join(",", part.Select(ip => $"\"{ip}\""));
            string spl = $"""
                search index={index} sourcetype="{sourcetype}" ({terms}) local_ip IN ({inList})
                | eval yon=if(lower(direction)=="outbound", "out", "in"),
                       yol=coalesce(process_path, "-"),
                       port=if(yon=="out", remote_port, local_port)
                | stats count dc(remote_ip) as peers values(remote_ip) as ornek by local_ip yon port yol
                | eval ornek=mvjoin(mvindex(ornek, 0, 7), " ")
                """;
            var q = new LookupQuery(part[0], DateTimeOffset.Now.AddHours(-hours), DateTimeOffset.Now);
            SplunkResult res;
            try { res = await SplunkService.ExportAsync(spl, q, cfg, factory.CreateClient("splunk"), ct, "Splunk"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "ERT eksikleri: Carbon Black sorgusu başarısız"); continue; }
            foreach (var r in res.Rows)
            {
                string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
                if (V("local_ip") is not { } ip) continue;
                if (!outp.TryGetValue(ip, out var c)) outp[ip] = c = new Cb();
                c.Rows.Add((V("yon") ?? "in", V("port") ?? "", V("yol") ?? "-", long.TryParse(V("count"), out var n) ? n : 1,
                    int.TryParse(V("peers"), out var pe) ? pe : 0,
                    (V("ornek") ?? "").Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != ip && x != "127.0.0.1").ToList()));
            }
        }
        return outp;
    }

    // ---------------- Arka plan işi ----------------
    public bool Start(EksikCalistir req, string user)
    {
        lock (_gate)
        {
            if (_durum.Calisiyor) return false;
            _cts = new CancellationTokenSource();
            _durum = new(true, "Başlıyor", 0, 0, 0, null, null, user, DateTimeOffset.Now, null, []);
        }
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(req, ct); Set(d => d with { Calisiyor = false, Asama = "Bitti", Bitis = DateTimeOffset.Now }); }
            catch (OperationCanceledException) { Set(d => d with { Calisiyor = false, Asama = "Durduruldu", Bitis = DateTimeOffset.Now }); }
            catch (Exception ex)
            {
                log.LogError(ex, "ERT eksikleri: AI önerisi başarısız");
                Set(d => d with { Calisiyor = false, Asama = "Hata", Mesaj = ex.Message, Bitis = DateTimeOffset.Now });
            }
        });
        return true;
    }

    public void Stop() { lock (_gate) _cts?.Cancel(); }
    void Set(Func<IslevDurum, IslevDurum> f) { lock (_gate) _durum = f(_durum); }
    void Fail(string msg) => Set(d => d with { Hata = d.Hata + 1, Hatalar = d.Hatalar.Count < 30 ? [.. d.Hatalar, msg] : d.Hatalar });

    async Task RunAsync(EksikCalistir req, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var infra = AppTopology.InfraPorts(cfg);
        var have = await LoadAsync(true, ct);
        var wanted = req.Ips is { Count: > 0 } ? req.Ips.ToHashSet() : null;
        // Hedef: istenen IP'ler ya da önerisi olmayanlar; altyapı portları ve onaylanmış / elle girilmiş olanlar atlanır
        var cands = Missing(env, Dyn).Where(m => !infra.Contains(m.Port))
            .Where(m => wanted == null || wanted.Contains(m.Ip))
            .Where(m => wanted != null || req.Test || !IsTest(env, m.Ip))   // test segmentleri yalnız seçilirse
            .Where(m => !have.TryGetValue((m.Ip, m.Port), out var o) || (o.Durum is not (Onay or Elle) && (req.Yeniden || wanted != null)))
            .GroupBy(m => m.Ip).OrderByDescending(g => g.Count()).ToList();
        int allIps = cands.Count, allPorts = cands.Sum(g => g.Count());
        var todo = cands.Take(req.Max is 0 ? int.MaxValue : Math.Clamp(req.Max ?? cfg.GetValue("Eksik:MaxIp", 200), 1, 100000))
            .ToDictionary(g => g.Key, g => g.Select(m => m.Port).ToList());
        if (todo.Count == 0) throw new InvalidOperationException("Öneri bekleyen satır yok (altyapı portları ve onaylananlar hariç).");
        int total = todo.Values.Sum(v => v.Count), hours = Hours(req.Saat);
        // Etiket alanı özet için: bu çalıştırmada kaç sunucu / bekleyen toplam
        Set(d => d with { Asama = "Kanıtlar toplanıyor", Toplam = total,
            Etiket = todo.Count < allIps ? $"bu çalıştırmada {todo.Count} / {allIps} sunucu ({allPorts - total} port sonraki çalıştırmalara kaldı)" : $"{allIps} sunucu" });
        // Sunucu sunucu (kullanıcı: 20'lik toplu kanıt toplama SM tablosuyla çok uzadı): her sunucunun kanıtı toplanır, hemen AI'a
        // sorulur ve kaydedilir; aynı anda Eksik:Paralel (varsayılan 3) sunucu. İlk öneriler hemen gelir.
        int paralel = Math.Clamp(cfg.GetValue("Eksik:Paralel", 3), 1, 8);
        using var gate = new SemaphoreSlim(paralel);
        Set(d => d with { Asama = $"Kanıt + AI (sunucu sunucu, aynı anda {paralel})" });
        var tasks = todo.Keys.Select(async ip =>
        {
            await gate.WaitAsync(ct);   // iptalde burada çıkar (semafor alınmadan; finally'ye girmez)
            try
            {
                var texts = await BuildAsync([ip], todo, env, hours, ct);
                lock (_gate) _kanit[ip] = texts.GetValueOrDefault(ip) ?? "";
                var got = await AskAsync([ip], todo, texts, env, ct);
                foreach (var port in todo[ip])
                {
                    var o = got.GetValueOrDefault((ip, port)) ?? new EksikOneri(null, false, 0, "AI bu port için cevap vermedi.", Ai, "AI", DateTime.Now);
                    await SaveAsync(ip, port, o, texts.GetValueOrDefault(ip), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fail($"{ip}: {ex.Message}"); }
            finally
            {
                Set(d => d with { Biten = d.Biten + todo[ip].Count });
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks);
    }

    // Kullanıcının genel bilgiler dosyası (Eksik:BilgiDosyasi; .txt, UTF-8 ya da Windows-1254): her AI isteğinin başına eklenir.
    // Model istekler arası hatırlamadığı için "öğretmek" = her istekte göndermek. Dosya değişince (tarih) yeniden okunur.
    (string Path, DateTime At, string Text, bool Cut)? _bilgi;
    public (string? Text, string Durum) Bilgi()
    {
        string? path = cfg["Eksik:BilgiDosyasi"];
        if (string.IsNullOrWhiteSpace(path)) return (null, "genel bilgi dosyası tanımlı değil (Eksik:BilgiDosyasi)");
        if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(path)) return (null, $"genel bilgi dosyası bulunamadı: {System.IO.Path.GetFileName(path)}");
        var at = File.GetLastWriteTimeUtc(path);
        if (_bilgi is not { } b || b.Path != path || b.At != at)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1254).GetString(bytes);
            }
            int max = Math.Clamp(cfg.GetValue("Eksik:BilgiMaxKarakter", 15000), 1000, 200000);
            bool cut = text.Length > max;
            _bilgi = b = (path, at, cut ? text[..max] : text, cut);
        }
        return (b.Text, $"genel bilgi dosyası: {System.IO.Path.GetFileName(path)} · {b.Text.Length:N0} karakter{(b.Cut ? " (Eksik:BilgiMaxKarakter sınırında kesildi)" : "")}");
    }

    const string KahinBas = "ENVANTER KAHİNİ (önceki bir AI projesinin kayıtlı tahmini; yalnız diğer kanıtlarla bulunamazsa — 2. tur):";
    static (string Ana, string? Kahin) SplitKahin(string? text)
    {
        if (text == null) return ("", null);
        int i = text.IndexOf(KahinBas, StringComparison.Ordinal);
        return i < 0 ? (text, null) : (text[..i], text[i..]);
    }

    async Task<Dictionary<(string, string), EksikOneri>> AskAsync(string[] ips, Dictionary<string, List<string>> todo,
        Dictionary<string, string> texts, EnvanterSnapshot env, CancellationToken ct)
    {
        var sb = new StringBuilder();
        if (Bilgi().Text is { Length: > 0 } genel)
            sb.AppendLine("KURUMA ÖZEL GENEL BİLGİLER (kullanıcının hazırladığı; kanıtları yorumlarken bunlara göre düşün):")
              .AppendLine(genel.Trim()).AppendLine("--- genel bilgilerin sonu ---").AppendLine();
        sb.AppendLine("""
            Bir bankanın veri merkezinde çalışan kıdemli bir altyapı mimarısın. Kurumun uygulama envanterinde (ERT) aşağıdaki
            sunucuların bazı portlarında UYGULAMA ADI BOŞ. Verilen kanıtlara bakarak her boş port için o portta hangi uygulamanın
            çalıştığını bul.

            Kanıtların gücü (güçlüden zayıfa): o portu kabul eden exe'nin adı ve klasör yolu (ör. d:\apps\odeme.api\odeme.exe);
            bu sunucunun HEDEF olduğu SM erişim talepleri (özellikle boş portu anan): "BU SUNUCU" diye işaretli açıklama bu
            sunucuyu anlatır, "karşı taraf" açıklaması diğer ucu (onu bu sunucunun uygulaması sanma); talepte bu sunucu için
            yazılmış uygulama alanı varsa çok güçlü; aynı satırdaki WEBSITE (IIS site adı) ve SERVICENAME; aynı sunucunun diğer portlarındaki uygulamalar; VIP ve havuz
            komşuları; BENZER ADLI SUNUCULARIN (aynı ad, farklı numara) uygulamaları; aynı segmentte aynı portu kullanan
            sunucuların uygulamaları; bu porta bağlanan istemci uygulamalar ve exe'nin bağlandığı yerler; sanal sunucu notları;
            iş istekleri; sunucu adındaki kısaltmalar (ör. "atm", "sql", "web"). ntoskrnl.exe görünüyorsa port HTTP.sys üzerindendir; asıl uygulama aynı sunucudaki ve adı WEBSITE ile
            örtüşen exe'dir.

            Kurallar:
            - Uygulama adını mümkünse "ADAY UYGULAMALAR" listesinden HARFİ HARFİNE seç ("yeni": false).
            - Hiçbir aday uymuyor ama kanıt açıkça bir uygulamayı gösteriyorsa kısa bir ad öner ("yeni": true).
            - Port bir altyapı / araç hizmetiyse (izleme ajanı, yedekleme, antivirüs, yönetim) "uygulama": "Altyapı: <ne>" yaz.
            - AJAN / ARAÇ ÜRÜNLERİ (Dataskope, Carbon Black, Splunk, SiteScope, yedekleme, antivirüs, izleme…) sunucunun İŞ UYGULAMASI
              DEĞİLDİR. Bir SM talebinde sunucuya böyle bir ajanın kurulduğu ya da o ürüne erişim açıldığı yazıyorsa bu, sunucunun o
              uygulama olduğunu GÖSTERMEZ ("[AJAN / ARAÇ TALEBİ]" işaretli talepleri uygulama kanıtı sayma). Ajan adını yalnız boş
              portu kabul eden exe o ürüne aitse "Altyapı: <ürün>" olarak yaz; aksi halde diğer kanıtlara (exe yolu, sunucu adı,
              benzer adlı sunucular, WEBSITE…) bak, yoksa null. İSTİSNA: sunucunun KENDİSİ o ürünün sunucusuysa (aynı sunucuda ERT'de o
              ürünün kayıtlı portları var, sunucu adı ya da segment adı ürünü / eski adını anıyor — ör. WEBSENSE segmenti = Forcepoint)
              ürün bu sunucunun uygulamasıdır: o ürünün adını yaz (veritabanı portundaysa "veritabani": true → "<ürün> Veritabanı").
            - VERİTABANI SUNUCUSU: bilinen DB portu (1433, 1521, 1522, 5432…), portu kabul eden DB süreci (sqlservr.exe, oracle.exe,
              tnslsnr.exe…) ve sunucu adındaki sql / db / ora kısaltması (ör. zbkossql01) veritabanı sunucusu işaretidir
              ("VERİTABANI SUNUCUSU İPUÇLARI" satırı). Böyle bir portta "SQL Server" gibi teknoloji adı YAZMA: veritabanı hangi
              uygulamaya aitse onu bul (bu porta bağlanan istemci uygulamalar, aynı sunucunun diğer portları, benzer adlı sunucular,
              SM talepleri, sunucu adındaki uygulama kısaltması) ve "uygulama" alanına yalnız UYGULAMA adını, "veritabani": true
              yaz (kod "<uygulama> Veritabanı" yapar). Gerekçede teknolojiyi belirt (ör. "1433 SQL Server; istemcileri KREDİ").
              Sahibi bulunamazsa uygulama null, gerekçede "SQL Server veritabanı, sahibi belirsiz" yaz.
            - "SM AÇIKLAMALARINDA ADI GEÇEN SİSTEMLER" satırı varsa bu sunucunun uygulaması büyük olasılıkla oradaki İLK sistemdir; exe yolu
              açıkça başka bir uygulamayı göstermiyorsa onu seç. "KISA AÇIKLAMALARI" da (BRIEF_DESCRIPTION) aynı derecede güçlüdür.
            - "BU SUNUCUYU ANLATAN TALEP AÇIKLAMALARI" satırları talep sahibinin bu sunucu için yazdığı açıklamalardır (HEDEF ise
              ZT_HEDEFACIKLAMA, KAYNAK ise ZT_KAYNAKACIKLAMA). Bir sistem / uygulama / veritabanı adı geçiyorsa bunu exe yolundan sonra
              EN GÜÇLÜ kanıt say ve gerekçede o açıklamayı alıntıla. Açıklama sunucunun NE OLDUĞUNU söylüyorsa (ör. "Symantec
              Endpoint Protection ürünü sunucusu", "X uygulama sunucusu", "Y veritabanı") bu SUNUCU DÜZEYİNDE KESİN kanıttır: o boş port
              için ayrıca erişim talebi, exe yolu ya da istemci bulunmasa da öneri o sistemdir (güven 70+); "bu port için kanıt yok"
              diye null YAZMA. Yalnız portun başka bir sisteme ait olduğunu gösteren daha güçlü kanıt (exe yolu) varsa ondan ayrıl. komşu ve segment ipuçlarını bunun önüne koyma. "Bu
              sunucuya erişen taraflar" ise istemcilerdir, onların adını bu sunucuya yazma.
            - Bu sunucunun HEDEF olduğu bir talepte açıklama "<A> için / <A> karşılığı … <B>'nin DB'lerine / sunucularına / servisine
              erişim" diyorsa erişilen sistem <B> BU SUNUCUDUR (A karşı taraftır, isteyen uygulamadır). Ör. "KKB Karşılığı / Ziraat
              Katılım prod segmentinin KÖS Db'lerine erişim talebi" + bu sunucu HEDEF → uygulama "KÖS", veritabani true (KKB Karşılığı
              istemci). Port veritabanı portu olsa da sahibi açıklamada adı geçen sistemdir; bu durumda null YAZMA. Adaylarda o sistemi
              içeren bir ad varsa onu seç, yoksa açıklamadaki kısa adı yaz ("yeni": true).
            - İşleten birim, varlık muhafızı ve uygulama sahibi EKİP / BİRİM adlarıdır (ör. "KATILIM VERİ AMBARI"); uygulama adı olarak
              YAZMA. Uygulamayı programlardan bul: "SUNUCUDA ÇALIŞAN İŞ PROGRAMLARI" satırındaki exe'ler ve "= Ürün" etiketleri (ör.
              sas.exe = SAS) sunucunun ne çalıştırdığını doğrudan gösterir; boş portu kabul eden exe yoksa bile sunucudaki iş programı
              güçlü ipucudur.
            - Segment adı ve aynı segmentteki diğer sunucular ZAYIF ipucudur (bir segmentte birden çok ürünün sunucusu olabilir; ör.
              WEBSENSE segmentinde Symantec sunucusu). SM açıklaması, talebin kısa açıklaması, sanal sunucu notu ya da exe yolu başka bir
              ürünü / uygulamayı söylüyorsa onu seç; segmentten ürün adı TÜRETME.
            - Kanıt yetersizse "uygulama": null ve güven düşük yaz. Uydurma.

            Yalnızca JSON dizi döndür, başka metin yazma. Her boş port için bir eleman:
            [{"ip":"…","port":"…","uygulama":"… ya da null","yeni":false,"veritabani":false,"guven":0-100,"gerekce":"tek cümle; hangi kanıta dayandığın"}]
            """);
        string kurallar = sb.ToString();
        int max = cfg.GetValue("Eksik:MaxKarakter", 6000);
        // 1. tur: Kahin'siz kanıt. 2. tur (yalnız bulunamayan portlar, Kahin varsa): aynı kanıt + Kahin, son çare olarak.
        string Prompt(Dictionary<string, List<string>> ports, bool kahin)
        {
            var q = new StringBuilder(kurallar);
            if (kahin) q.AppendLine("""

                2. TUR — ENVANTER KAHİNİ: aşağıdaki portlar için diğer kanıtlarla uygulama BULUNAMADI. Şimdi en sonda verilen "ENVANTER
                KAHİNİ" bölümüne de bak. Kahin, daha önce başka bir AI projesinin yaptığı ve doğrulanmamış tahmindir; yalnız diğer
                kanıtlarla ÇELİŞMİYORSA kullan, güven en fazla 50 olsun, gerekçeye "Kahin tahmini (son çare)" yaz. Kahin de
                ikna edici değilse uygulama null.
                """);
            foreach (var ip in ports.Keys)
            {
                var (ana, kh) = SplitKahin(texts.GetValueOrDefault(ip));
                q.AppendLine().AppendLine($"=== Sunucu {ip} · boş portlar: {string.Join(", ", ports[ip].Select(p => p == "" ? "(port yok)" : p))}")
                 .AppendLine(Cut(ana, max));
                if (kahin && kh != null) q.AppendLine(Cut(kh, 1500));
            }
            // Küçük model uzun kanıttan sonra baştaki kuralları unutabiliyor: en önemli kurallar sona tekrar
            q.AppendLine().AppendLine("""
                HATIRLATMA (kanıtlardan önemli): genel bilgiler dosyasındaki kurallar SM taleplerinden önce gelir. "BU SUNUCUYU ANLATAN
                TALEP AÇIKLAMALARI" bir sistem adı söylüyorsa öneriyi ona dayandır ve gerekçede alıntıla; port için ayrı kanıt olmaması
                null yazmak için sebep değildir. Ajan / araç kurulum ya da
                erişim talepleri (Dataskope vb.) sunucunun uygulamasını göstermez. Yalnızca JSON dizi döndür.
                """);
            return q.ToString();
        }
        var outp = Parse(await islev.ChatAsync(Prompt(todo, false), ct), todo, false);
        var again = todo.Where(kv => SplitKahin(texts.GetValueOrDefault(kv.Key)).Kahin != null)
            .Select(kv => (kv.Key, ps: kv.Value.Where(p => !outp.TryGetValue((kv.Key, p), out var o) || o.Uygulama == null).ToList()))
            .Where(x => x.ps.Count > 0).ToDictionary(x => x.Key, x => x.ps);
        if (again.Count > 0)
        {
            try
            {
                foreach (var (k, o) in Parse(await islev.ChatAsync(Prompt(again, true), ct), again, true))
                    if (o.Uygulama != null) outp[k] = o;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* 2. tur başarısızsa 1. tur sonucu kalır */ }
        }
        // Program son kontrol: hâlâ boş kalan portta portu kabul eden exe bilinen bir ürünse o (güven 55); portun exe'si yoksa ve sunucuda
        // tek bir bilinen ürün çalışıyorsa o (güven 45). SM kontrolünden sonra çalışır, SM varsa SM kazanır.
        void ProgramKontrol()
        {
            foreach (var (ip, ports) in todo)
            {
                string? ev = texts.GetValueOrDefault(ip);
                var sunucuUrun = ev?.Split('\n').FirstOrDefault(l => l.StartsWith("SUNUCUDA ÇALIŞAN İŞ PROGRAMLARI")) is { } pl
                    ? System.Text.RegularExpressions.Regex.Matches(pl, @"\] = ([^;]+)").Select(m => m.Groups[1].Value.Trim()).Distinct().ToList() : [];
                foreach (var port in ports)
                {
                    var o = outp.GetValueOrDefault((ip, port));
                    if (o?.Uygulama != null && o.Gerekce?.StartsWith("Kahin tahmini") != true) continue;
                    string? portUrun = PortLine(ev, port) is { } line ? line.Split('[').Skip(1).Select(x => BilinenUrun(x.Split(']')[0])).OfType<string>().FirstOrDefault() : null;
                    string? urun = portUrun ?? (sunucuUrun.Count == 1 ? sunucuUrun[0] : null);
                    if (urun == null) continue;
                    string ad = CatalogMatch(env, urun, out bool yeni, out var katNot);
                    if (DbPort(ev, port, out _)) ad += " Veritabanı";
                    outp[(ip, port)] = new EksikOneri(ad, yeni, portUrun != null ? 55 : 45,
                        (portUrun != null ? $"Portu kabul eden program bilinen ürün: {urun}" : $"Sunucuda çalışan tek bilinen iş programı: {urun}")
                        + (o != null ? $" (AI: {(o.Uygulama ?? "öneri yok")} — {Cut(o.Gerekce, 200)})" : "") + (katNot != null ? $" [{katNot}]" : ""), Ai, "AI", DateTime.Now);
                }
            }
        }
        // SM son kontrol (kullanıcı: "SM kanıtları en kritik"): AI boş bıraktıysa ya da yalnız Kahin'e dayandıysa ve SM açıklamalarında adı
        // geçen bir sistem varsa o yazılır (DB portunda "… Veritabanı"), gerekçede belirtilir
        foreach (var (ip, ports) in todo)
        {
            if (SmAdayOf(texts.GetValueOrDefault(ip)) is not { } sm) continue;
            foreach (var port in ports)
            {
                var o = outp.GetValueOrDefault((ip, port));
                bool kahin = o?.Gerekce?.StartsWith("Kahin tahmini") == true;
                if (o?.Uygulama != null && !kahin) continue;
                if (o?.Uygulama != null && Norm(o.Uygulama).Contains(Norm(sm))) continue;
                string ad = CatalogMatch(env, sm, out bool yeni, out var katNot);
                if (DbPort(texts.GetValueOrDefault(ip), port, out _)) ad += " Veritabanı";
                outp[(ip, port)] = new EksikOneri(Cut(ad, 200), yeni, 65,
                    $"SM talep açıklamalarında adı geçen sistem: {sm}" + (o != null ? $" (AI: {(o.Uygulama ?? "öneri yok")} — {Cut(o.Gerekce, 200)})" : "") + (katNot != null ? $" [{katNot}]" : ""),
                    Ai, "AI", DateTime.Now);
            }
        }
        ProgramKontrol();
        return outp;

        Dictionary<(string, string), EksikOneri> Parse(string answer, Dictionary<string, List<string>> want, bool kahinTur)
        {
        var res = new Dictionary<(string, string), EksikOneri>();
        if (IslevService.ExtractJson(answer) is not { } root) throw new InvalidOperationException("AI cevabı JSON değil: " + Cut(answer, 200));
        var arr = root.ValueKind == JsonValueKind.Array ? root : default;
        if (arr.ValueKind != JsonValueKind.Array) return res;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? ip = IslevService.Str(e, "ip") ?? (want.Count == 1 ? want.Keys.First() : null);
            if (ip == null || !want.ContainsKey(ip)) continue;
            string port = IslevService.Str(e, "port") ?? (want[ip].Count == 1 ? want[ip][0] : "");
            if (port is "(port yok)" or "null") port = "";
            if (!want[ip].Contains(port)) continue;
            string? app = IslevService.Str(e, "uygulama");
            if (app is "null" or "-") app = null;
            bool yeni = false;
            int guven = Math.Clamp(IslevService.Int(e, "guven"), 0, 100);
            string? gerekce = IslevService.Str(e, "gerekce");
            if (kahinTur && app != null)
            {
                guven = Math.Min(guven, 50);
                if (gerekce?.Contains("Kahin", StringComparison.OrdinalIgnoreCase) != true) gerekce = "Kahin tahmini (son çare): " + gerekce;
            }
            // Ekip / birim adı uygulama değildir (kullanıcı: AI işleten birim "KATILIM VERİ AMBARI"nı uygulama diye önerdi)
            if (app != null && EkipAdi(env, ip, app) is { } ekip)
            {
                gerekce = $"Yok sayıldı: AI '{app}' önerdi ama bu bir ekip / birim adı (ERT {ekip}), uygulama adı değil. AI gerekçesi: {gerekce}";
                app = null; guven = 0;
            }
            // Ajan / araç adı önerildiyse: portun exe'si o ürüne aitse "Altyapı: X", değilse öneri yok sayılır (gerekçe saklanır)
            if (app != null && AjanOf(app) is { } ajan)
            {
                if (AjanSunucusu(env, ip, ajan, texts.GetValueOrDefault(ip)))
                {
                    // ürünün kendi sunucusunda ürün iş uygulamasıdır: "Altyapı:" öneki atılır, AI'ın tam adı kalır
                    if (app.StartsWith("Altyapı:", StringComparison.OrdinalIgnoreCase)) app = app[8..].Trim();
                    gerekce = $"{gerekce} [sunucu {ajan} ürününün kendi sunucusu (ERT / sunucu adı / segment / SM / sanal sunucu notu / exe yolu)]";
                }
                else if (PortExeHas(texts.GetValueOrDefault(ip), port, ajan)) app = "Altyapı: " + app;   // AI'ın tam ürün adı (kullanıcı: "neden sadece Symantec")
                else
                {
                    string? diger = BaskaUrun(env, ip, ajan, texts.GetValueOrDefault(ip));
                    gerekce = $"Yok sayıldı: AI '{app}' önerdi ama {ajan} bir ajan / araç ürünü ve bu portu kabul eden exe ona ait değil"
                        + (diger != null ? $"; kanıtlara göre sunucu {diger} ürününün sunucusu (segment adı ürünü belirlemez)" : "") + $". AI gerekçesi: {gerekce}";
                    app = null; guven = 0;
                    // veritabanı portu ve sunucu başka ürünün sunucusuysa: aynı sunucuda ERT'de o ürünün adı kayıtlıysa "<o ad> Veritabanı"
                    if (diger != null && DbPort(texts.GetValueOrDefault(ip), port, out _)
                        && env.AppNamesOnIp(ip).FirstOrDefault(n => AjanAdlari(diger).Any(Norm(n).Contains)) is { } urunAd)
                    {
                        app = urunAd + " Veritabanı"; guven = 60; yeni = false;
                        gerekce += $" [veritabanı portu; sunucu {urunAd} sunucusu olduğu için '{app}' yazıldı]";
                    }
                }
            }
            if (app != null && !app.StartsWith("Altyapı", StringComparison.OrdinalIgnoreCase))
            {
                // Veritabanı: AI işaretlediyse ya da port DB portu / DB süreci ise "<uygulama> Veritabanı" (kullanıcı isteği)
                var dbRx = new System.Text.RegularExpressions.Regex(@"\s*[-–(]?\s*(veritaban[ıi]|database|db)\)?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                bool db = e.TryGetProperty("veritabani", out var vt) && vt.ValueKind == JsonValueKind.True || DbPort(texts.GetValueOrDefault(ip), port, out _)
                          || dbRx.IsMatch(app);
                app = CatalogMatch(env, dbRx.Replace(app, "").Trim() is { Length: > 0 } core ? core : app, out yeni, out var katNot);
                if (db) app += " Veritabanı";
                if (katNot != null) gerekce = $"{gerekce} [{katNot}]";
            }
            res[(ip, port)] = new EksikOneri(Cut(app, 200), yeni, guven, gerekce, Ai, "AI", DateTime.Now);
        }
        return res;
        }
    }

    // ---------------- Excel (CSV) ----------------
    // hepsi = false: yalnız onaylanan / elle girilenler; true: önerisi olan tüm satırlar
    public async Task<string> CsvAsync(bool hepsi, CancellationToken ct)
    {
        var rows = (await ListAsync(null, hepsi ? null : "onay", true, 1, 1000, false, ct, true)) is var first && first.Toplam > 1000
            ? (await Task.WhenAll(Enumerable.Range(1, (first.Toplam + 999) / 1000)
                .Select(p => ListAsync(null, hepsi ? null : "onay", true, p, 1000, false, ct, true)))).SelectMany(l => l.Satirlar).ToList()
            : first.Satirlar;
        var sb = new StringBuilder("\uFEFFIP;PORT;SUNUCU;SEGMENT;WEBSITE;SERVICENAME;MUHAFIZ;AYNI SUNUCUDAKİ UYGULAMALAR;ÖNERİLEN UYGULAMA;YENİ AD;GÜVEN;DURUM;KARAR VEREN;GEREKÇE\r\n");
        static string C(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var r in rows.Where(r => r.Oneri != null))
            sb.Append(string.Join(";", C(r.Ip), C(r.Port), C(r.Hostname), C(r.Segment), C(r.Website), C(r.Servis), C(r.Muhafiz),
                C(string.Join(", ", r.Diger)), C(r.Oneri!.Uygulama), C(r.Oneri.Yeni ? "evet" : ""), C(r.Oneri.Guven.ToString()),
                C(r.Oneri.Durum), C(r.Oneri.Kaynak), C(r.Oneri.Gerekce))).Append("\r\n");
        return sb.ToString();
    }
}
