// ---------------------------------------------------------------------------
// TLS bağlantıları (AppResponse `tls_handshakes`; kullanıcı: "IP arama ekranında gelen ve giden TLS bağlantıları, yeni bir sekme").
// Nessus portun KABUL ETTİĞİ sürümleri verir; bu ise el sıkışmalarında FİİLEN kullanılan sürüm (istemci ↔ sunucu).
// Deneme aracı ArTlsTest ile canlıda doğrulanan kurallar:
//  - TLS bilgisi ayrı kaynakta: tls_handshakes (tls.version_name, tls.version, tls.sni, sum_tls.session_attempted …).
//  - tls.version_name ile group_by olmuyor ("invalid group by"); değer kolonu tls.version (771 = TLS 1.2) ile denenir.
//  - Gruplamasız rapor her el sıkışmasını ayrı satır döndürür, cihaz başına 100.000 satırda kesilir (istemciler eksik kalır).
//  - Sorgulanan IP hem sunucu (gelen) hem istemci (giden) olabilir; IP biçimi (::ffff:) farkı karşılaştırmada yok sayılır.
//  - Tüm kutular sorgulanır (DeltaFlow "Tüm kutular" gibi); bir kutunun hatası diğerlerini durdurmaz.
// Ayarlar `Tls:*` (kolon adları, saat, bekleme); sonuç IP + aralık başına `Tls:CacheDakika` önbellekte.
// ---------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

record TlsSatir(string Yon, string Karsi, string Port, string Surum, long Sayi, int KarsiSayisi, List<TlsKarsi> Karsilar, List<string> Cihazlar, List<string> Sni);
record TlsKarsi(string Ip, long Sayi, string? Uygulama);
record TlsCihaz(string Ad, int Satir, string? Yontem, string? Hata);
record TlsSonuc(string Ip, DateTime Bas, DateTime Bit, string? Yontem, List<TlsSatir> Gelen, List<TlsSatir> Giden,
    List<TlsCihaz> Cihazlar, List<string> Mesajlar, bool Kesik, long Ms);

static class TlsAkis
{
    static readonly ConcurrentDictionary<string, (DateTime At, TlsSonuc S)> _cache = new();

    // tls.version değeri → ad (0x0300 SSL 3.0 … 0x0304 TLS 1.3); ad kolonu "TLSv1.2" gibi gelirse "TLS 1.2"
    public static string SurumAd(string v)
    {
        v = (v ?? "").Trim();
        string s = v switch
        {
            "768" or "0x0300" => "SSL 3.0", "769" or "0x0301" => "TLS 1.0", "770" or "0x0302" => "TLS 1.1",
            "771" or "0x0303" => "TLS 1.2", "772" or "0x0304" => "TLS 1.3", "" => "bilinmiyor", _ => v
        };
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(SSL|TLS)\s?v?(\d)(?:\.(\d))?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return s;
        string tur = m.Groups[1].Value.ToUpperInvariant(), ana = m.Groups[2].Value, alt = m.Groups[3].Success ? m.Groups[3].Value : "0";
        return tur == "SSL" ? $"SSL {ana}.0" : $"TLS {ana}.{alt}";
    }

    static bool AyniIp(string a, string b)
    {
        static IPAddress? P(string x) => IPAddress.TryParse(x.Trim(), out var i) ? (i.IsIPv4MappedToIPv6 ? i.MapToIPv4() : i) : null;
        var (x, y) = (P(a), P(b));
        return x != null && y != null ? x.Equals(y) : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
    static string Ip(string a) => IPAddress.TryParse(a.Trim(), out var i) && i.IsIPv4MappedToIPv6 ? i.MapToIPv4().ToString() : a.Trim();

    // Ham el sıkışması satırı (kutu adıyla)
    internal record Ham(string Cihaz, string Cli, string Srv, string Port, string Surum, string? Sni, long Sayi);

    // Ortak çekirdek: verilen IP'ler için tüm kutularda tls_handshakes raporu. gelenYalniz: yalnız IP'lerin sunucu olduğu
    // el sıkışmaları (toplu sorgu); değilse sunucu ya da istemci. Kutu başına sırayla: gruplu + metrik → gruplu → ham.
    internal static async Task<(List<Ham> Satirlar, List<TlsCihaz> Cihazlar, List<string> Mesajlar, bool Kesik)> CekAsync(
        IReadOnlyList<string> ips, DateTime bas, DateTime bit, IReadOnlyList<string> portlar, bool gelenYalniz,
        IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var t = cfg.GetSection("Tls");
        string kaynak = t["Kaynak"] ?? "tls_handshakes";
        string cliIp = t["IstemciIp"] ?? "cli_tcp.ip", srvIp = t["SunucuIp"] ?? "srv_tcp.ip", srvPort = t["SunucuPort"] ?? "srv_tcp.port";
        string surum = t["Surum"] ?? "tls.version", surumAd = t["SurumAd"] ?? "tls.version_name", metrik = t["Metrik"] ?? "sum_tls.session_attempted";
        string sni = t["Sni"] ?? "";   // boş: SNI istenmez (gruplamayı bozabilir)
        int bekle = t.GetValue("BeklemeSaniye", 300);

        string PortF(string k) => portlar.Count > 0 ? " and (" + string.Join(" or ", portlar.Select(p => $"{k} == {p}")) + ")" : "";
        string srvF = "(" + string.Join(" or ", ips.Select(ip => $"{srvIp} == {ip}")) + ")" + PortF(srvPort);
        string filtre = gelenYalniz ? srvF : $"({srvF}) or ((" + string.Join(" or ", ips.Select(ip => $"{cliIp} == {ip}")) + ")" + PortF(srvPort) + ")";
        string pathType = cfg["SourcePathType"] ?? "jobs";
        var vifgler = cfg.GetSection("VifgIds").GetChildren().Select(x => (x.Value ?? "").Trim()).Where(x => x != "").ToList();
        if (vifgler.Count == 0) vifgler.Add("");
        bool sil = cfg.GetValue("DeleteReportInstances", true);
        long sBas = new DateTimeOffset(bas).ToUnixTimeSeconds(), sBit = new DateTimeOffset(bit).ToUnixTimeSeconds();

        var tlsKol = new List<string> { surum }; if (sni != "") tlsKol.Add(sni);
        var tlsKolAd = new List<string> { surumAd }; if (sni != "") tlsKolAd.Add(sni);
        string[] anahtar = [cliIp, srvIp, srvPort];
        var denemeler = new List<(string Ad, string[] Kol, string[]? Grup, bool Metrik)>
        {
            ("gruplu (tls.version)", [.. anahtar, .. tlsKol, metrik], [.. anahtar, .. tlsKol], true),
            ("gruplu, metriksiz", [.. anahtar, .. tlsKol], [.. anahtar, .. tlsKol], false),
            ("ham satırlar", [.. anahtar, .. tlsKolAd], null, false),
        };

        var names = cfg.GetSection("Servers").GetChildren().Select((s, i) => (Ad: s["Name"] ?? $"Cihaz {i + 1}", I: i)).ToList();
        var sonuc = await Task.WhenAll(names.Select(async n =>
        {
            var satirlar = new List<Ham>();
            string? yontem = null, hata = null;
            try
            {
                var (_, baseUrl, jwt) = await AppResponseService.ConnectAsync(n.I, cfg, http, ct);
                foreach (var d in denemeler)
                {
                    try
                    {
                        var tmp = new List<Ham>();
                        foreach (var vifg in vifgler)
                        {
                            var dd = JsonSerializer.SerializeToNode(new
                            {
                                source = vifg == "" ? (object)new { name = kaynak } : new { name = kaynak, path = $"{pathType}/{vifg}" },
                                columns = d.Kol,
                                time = new { start = sBas.ToString(), end = sBit.ToString() },
                                filters = new object[] { new { id = "tls", type = "STEELFILTER", value = filtre } },
                                topn = 100000
                            })!.AsObject();
                            if (d.Grup != null) dd["group_by"] = JsonSerializer.SerializeToNode(d.Grup);
                            var data = await AppResponseService.RunSingleAsync(baseUrl, jwt, dd, sil, http, ct, bekle);
                            foreach (var a in data.OfType<JsonArray>())
                            {
                                string C(int i) => i < a.Count ? a[i]?.ToString() ?? "" : "";
                                long sayi = d.Metrik && double.TryParse(C(3 + tlsKol.Count), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? (long)v : 1;
                                tmp.Add(new Ham(n.Ad, Ip(C(0)), Ip(C(1)), C(2), SurumAd(C(3)), sni != "" ? C(4) : null, sayi));
                            }
                        }
                        satirlar = tmp; yontem = d.Ad; hata = null;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { hata = ex.Message; }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { hata = ex is TaskCanceledException ? "Zaman aşımı" : ex.Message; }
            return (n.Ad, satirlar, yontem, hata);
        }));

        var cihazlar = sonuc.Select(s => new TlsCihaz(s.Ad, s.satirlar.Count, s.yontem, s.yontem == null ? Kisalt(s.hata, 300) : null)).ToList();
        var mesajlar = sonuc.Where(s => s.yontem == null).Select(s => $"[{s.Ad}] {Kisalt(s.hata, 300)}").ToList();
        bool kesik = sonuc.Any(s => s.yontem == "ham satırlar" && s.satirlar.Count >= 100000);
        if (kesik) mesajlar.Add("Bazı kutularda 100.000 satır sınırına ulaşıldı (gruplama olmadı): sayılar eksik olabilir, port süzün ya da aralığı kısaltın.");
        if (sonuc.Any(s => s.yontem == "ham satırlar")) mesajlar.Add("Gruplu rapor alınamadı, ham satırlar sayıldı (Tls:Surum / Tls:Metrik ayarlarını kontrol edin).");
        return (sonuc.SelectMany(s => s.satirlar).ToList(), cihazlar, mesajlar, kesik);
    }

    // Aralık: şimdiden geriye saat; tam dakikaya yuvarlanır (önbellek tutsun)
    internal static (DateTime Bas, DateTime Bit) Aralik(int saat)
    {
        var bit = DateTime.UtcNow; bit = new DateTime(bit.Year, bit.Month, bit.Day, bit.Hour, bit.Minute, 0, DateTimeKind.Utc);
        return (bit.AddHours(-saat), bit);
    }

    public static async Task<TlsSonuc> RunAsync(string ip, int saat, string? port, IConfiguration cfg, HttpClient http, EnvanterSnapshot? env, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        saat = Math.Clamp(saat, 1, cfg.GetValue("Tls:MaxSaat", 24));
        var (bas, bit) = Aralik(saat);
        var portlar = (port ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => int.TryParse(p, out _)).ToList();
        string key = $"{ip}|{saat}|{string.Join(",", portlar)}";
        if (_cache.TryGetValue(key, out var c) && (DateTime.UtcNow - c.At).TotalMinutes < cfg.GetValue("Tls:CacheDakika", 30)) return c.S;

        var (tum, cihazlar, mesajlar, kesik) = await CekAsync([ip], bas, bit, portlar, false, cfg, http, ct);
        string? Uyg(string karsi) => env == null ? null : env.AppNamesOnIp(karsi).FirstOrDefault(a => !env.IsSystemApp(a))
            ?? (env.IsVipGw(karsi, out _) ? "LB GW" : env.FindSegment(karsi)?.Label);
        List<TlsSatir> Yap(bool gelen)
        {
            var sec = tum.Where(x => gelen ? AyniIp(x.Srv, ip) : AyniIp(x.Cli, ip) && !AyniIp(x.Srv, ip)).ToList();
            return sec.GroupBy(x => gelen ? (Karsi: "", x.Port, x.Surum) : (Karsi: x.Srv, x.Port, x.Surum))
                .Select(g =>
                {
                    var karsilar = g.GroupBy(x => gelen ? x.Cli : x.Srv).Select(k => new TlsKarsi(k.Key, k.Sum(x => x.Sayi), Uyg(k.Key)))
                        .OrderByDescending(k => k.Sayi).ToList();
                    return new TlsSatir(gelen ? "gelen" : "giden", g.Key.Karsi, g.Key.Port, g.Key.Surum, g.Sum(x => x.Sayi), karsilar.Count,
                        karsilar.Take(50).ToList(), g.Select(x => x.Cihaz).Distinct().ToList(),
                        g.Select(x => x.Sni).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(10).ToList()!);
                })
                .OrderBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ThenBy(x => x.Karsi).ThenByDescending(x => x.Sayi).ToList();
        }
        var yontemler = cihazlar.Select(s => s.Yontem).OfType<string>().Distinct().ToList();
        if (yontemler.Count == 0) throw new InvalidOperationException("Hiçbir AppResponse kutusundan TLS verisi alınamadı: " + string.Join(" | ", mesajlar));
        var r = new TlsSonuc(ip, bas, bit, string.Join(", ", yontemler), Yap(true), Yap(false), cihazlar, mesajlar, kesik, sw.ElapsedMilliseconds);
        _cache[key] = (DateTime.UtcNow, r);
        return r;
    }

    internal static bool AyniIpDis(string a, string b) => AyniIp(a, b);

    // flow_tcp: IP'lerin SUNUCU olduğu TCP trafiği, (sunucu IP, port) → bağlantı / satır sayısı. "Trafik var ama TLS el sıkışması yok"
    // (olası şifresiz) ayrımı için. Kutu başına sırayla: gruplu + metrik (Tls:AkisMetrikleri), gruplu metriksiz, ham satır sayımı.
    internal static async Task<(Dictionary<(string Ip, string Port), long> Portlar, bool Ok, List<string> Mesajlar)> AkisAsync(
        IReadOnlyList<string> ips, DateTime bas, DateTime bit, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        string kaynak = cfg["SourceL4"] ?? "flow_tcp", pathType = cfg["SourcePathType"] ?? "jobs";
        string srvIp = cfg["Tls:SunucuIp"] ?? "srv_tcp.ip", srvPort = cfg["Tls:SunucuPort"] ?? "srv_tcp.port";
        var metrikler = cfg.GetSection("Tls:AkisMetrikleri").GetChildren().Select(x => (x.Value ?? "").Trim()).Where(x => x != "").ToList();
        if (metrikler.Count == 0) metrikler = ["sum_tcp.new_connections", "sum_traffic.total_bytes"];
        var vifgler = cfg.GetSection("VifgIds").GetChildren().Select(x => (x.Value ?? "").Trim()).Where(x => x != "").ToList();
        if (vifgler.Count == 0) vifgler.Add("");
        bool sil = cfg.GetValue("DeleteReportInstances", true);
        int bekle = cfg.GetValue("Tls:BeklemeSaniye", 300);
        string filtre = "(" + string.Join(" or ", ips.Select(ip => $"{srvIp} == {ip}")) + ")";
        long sBas = new DateTimeOffset(bas).ToUnixTimeSeconds(), sBit = new DateTimeOffset(bit).ToUnixTimeSeconds();
        var denemeler = metrikler.Select(m => (Kol: new[] { srvIp, srvPort, m }, Grup: (string[]?)new[] { srvIp, srvPort }, Metrik: true))
            .Append((Kol: new[] { srvIp, srvPort }, Grup: new[] { srvIp, srvPort }, Metrik: false))
            .Append((Kol: new[] { srvIp, srvPort }, Grup: null, Metrik: false)).ToList();

        var names = cfg.GetSection("Servers").GetChildren().Select((s, i) => (Ad: s["Name"] ?? $"Cihaz {i + 1}", I: i)).ToList();
        var sonuc = await Task.WhenAll(names.Select(async n =>
        {
            var d0 = new Dictionary<(string, string), long>();
            string? hata = null; bool ok = false;
            try
            {
                var (_, baseUrl, jwt) = await AppResponseService.ConnectAsync(n.I, cfg, http, ct);
                foreach (var d in denemeler)
                {
                    try
                    {
                        var tmp = new Dictionary<(string, string), long>();
                        foreach (var vifg in vifgler)
                        {
                            var dd = JsonSerializer.SerializeToNode(new
                            {
                                source = vifg == "" ? (object)new { name = kaynak } : new { name = kaynak, path = $"{pathType}/{vifg}" },
                                columns = d.Kol,
                                time = new { start = sBas.ToString(), end = sBit.ToString() },
                                filters = new object[] { new { id = "akis", type = "STEELFILTER", value = filtre } },
                                topn = 100000
                            })!.AsObject();
                            if (d.Grup != null) dd["group_by"] = JsonSerializer.SerializeToNode(d.Grup);
                            var data = await AppResponseService.RunSingleAsync(baseUrl, jwt, dd, sil, http, ct, bekle);
                            foreach (var a in data.OfType<JsonArray>())
                            {
                                string C(int i) => i < a.Count ? a[i]?.ToString() ?? "" : "";
                                long v = d.Metrik && double.TryParse(C(2), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? (long)x : 1;
                                var k = (Ip(C(0)), C(1));
                                tmp[k] = tmp.GetValueOrDefault(k) + Math.Max(v, 1);
                            }
                        }
                        d0 = tmp; ok = true; hata = null;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { hata = ex.Message; }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { hata = ex is TaskCanceledException ? "Zaman aşımı" : ex.Message; }
            return (n.Ad, d0, ok, hata);
        }));
        var tum = new Dictionary<(string, string), long>();
        foreach (var s in sonuc) foreach (var kv in s.d0) tum[kv.Key] = tum.GetValueOrDefault(kv.Key) + kv.Value;
        var mesajlar = sonuc.Where(s => !s.ok).Select(s => $"[{s.Ad}] flow_tcp (şifresiz kontrolü): {Kisalt(s.hata, 200)}").ToList();
        return (tum, sonuc.Any(s => s.ok), mesajlar);
    }
    static string Kisalt(string? s, int n) => s == null ? "" : s.Length <= n ? s : s[..n] + "…";
}

// ---------------------------------------------------------------------------
// Toplu TLS sorgusu (kullanıcı: "Excel'den bir kolon IP girip karşılığı olan port – uygulama – TLS versiyonu"): arka plan işi
// (IIS istek zaman aşımı), `Tls:TopluParca` IP'lik parçalar sırayla; yalnız GELEN el sıkışmaları (IP sunucu, port onun portu).
// Satır = IP × port: ERT'deki portlar + TLS görülen portlar birleşimi; uygulama ERT'den (IP + port, yoksa IP'nin uygulamaları).
// ---------------------------------------------------------------------------
record TlsTopluSurum(string Surum, long Sayi, double Yuzde);
// Sifreleme: "tls" (el sıkışması görüldü) | "sifresiz" (TCP trafiği var, TLS yok — olası şifresiz) | "protokol" (trafik var, şifreleme
// protokolün içinde, AppResponse'ta TLS olarak görünmez: WinRM, SQL, RDP…) | "trafikyok" (AppResponse bu portta trafik görmedi) | "bilinmiyor"
record TlsTopluSatir(string Ip, string? Sunucu, string Port, string? Uygulama, bool ErtPortu, List<TlsTopluSurum> Surumler,
    long Sayi, int Istemci, List<string> Kutular, string? Not, string Sifreleme = "bilinmiyor", long Trafik = 0, string? Ipucu = null);

class TlsTopluIs
{
    public string Id = Guid.NewGuid().ToString("N")[..12];
    public string Kullanici = "";
    public int Saat;
    public List<string> Ips = [];
    public int Parca, ToplamParca;
    public bool Bitti;
    public bool Sifresiz;   // ek flow_tcp sorgusu yapıldı mı (trafik var / TLS yok ayrımı)
    public string? Hata;
    public DateTime Bas, Bit, Basladi = DateTime.UtcNow;
    public List<TlsTopluSatir> Satirlar = [];
    public List<string> Mesajlar = [];
    public HashSet<string> Yontemler = [];
    public CancellationTokenSource Cts = new();
}

static class TlsToplu
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TlsTopluIs> _isler = new();

    public static TlsTopluIs? Get(string id) => _isler.TryGetValue(id, out var j) ? j : null;

    public static TlsTopluIs Start(List<string> ips, int saat, bool sifresiz, string kullanici, IConfiguration cfg, HttpClient http, EnvanterService envanter)
    {
        // eski işler (1 saat) temizlenir
        foreach (var kv in _isler.Where(kv => (DateTime.UtcNow - kv.Value.Basladi).TotalHours > 1).ToList()) _isler.TryRemove(kv.Key, out _);
        int parca = Math.Max(1, cfg.GetValue("Tls:TopluParca", 20));
        var (bas, bit) = TlsAkis.Aralik(saat);
        var j = new TlsTopluIs { Kullanici = kullanici, Saat = saat, Ips = ips, Bas = bas, Bit = bit, ToplamParca = (ips.Count + parca - 1) / parca, Sifresiz = sifresiz };
        _isler[j.Id] = j;
        _ = Task.Run(async () =>
        {
            try
            {
                var env = await envanter.GetAsync(false, j.Cts.Token);
                foreach (var grup in ips.Chunk(parca))
                {
                    j.Cts.Token.ThrowIfCancellationRequested();
                    List<TlsAkis.Ham> ham = [];
                    try
                    {
                        var (s, cihazlar, mesajlar, _) = await TlsAkis.CekAsync(grup, bas, bit, [], true, cfg, http, j.Cts.Token);
                        ham = s;
                        foreach (var c in cihazlar.Where(c => c.Yontem != null)) lock (j) j.Yontemler.Add(c.Yontem!);
                        lock (j) foreach (var m in mesajlar) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                        if (cihazlar.All(c => c.Yontem == null)) throw new InvalidOperationException("Hiçbir kutudan veri alınamadı: " + string.Join(" | ", mesajlar));
                    }
                    catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        lock (j) j.Mesajlar.Add($"{grup.First()}…{grup.Last()} ({grup.Length} IP) alınamadı: {ex.Message}");
                        var hatali = grup.Select(ip => new TlsTopluSatir(ip, Host(env, ip), "", null, false, [], 0, 0, [], "TLS verisi alınamadı (hata)")).ToList();
                        lock (j) { j.Satirlar.AddRange(hatali); j.Parca++; }
                        continue;
                    }
                    // TCP trafiği (şifresiz kontrolü): hata TLS sonucunu bozmaz, satırlar "bilinmiyor" kalır
                    Dictionary<(string Ip, string Port), long>? akis = null;
                    if (sifresiz) try
                    {
                        var (ap, aok, amsg) = await TlsAkis.AkisAsync(grup, bas, bit, cfg, http, j.Cts.Token);
                        if (aok) akis = ap;
                        lock (j) foreach (var m in amsg) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                    }
                    catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                    catch (Exception ex) { lock (j) if (j.Mesajlar.Count < 30) j.Mesajlar.Add("flow_tcp (şifresiz kontrolü) alınamadı: " + ex.Message); }
                    var yeni = new List<TlsTopluSatir>();
                    foreach (var ip in grup)
                    {
                        var ipAkis = akis?.Where(kv => TlsAkis.AyniIpDis(kv.Key.Ip, ip)).ToDictionary(kv => kv.Key.Port, kv => kv.Value);
                        yeni.AddRange(Satirlar(ip, ham.Where(h => TlsAkis.AyniIpDis(h.Srv, ip)).ToList(), ipAkis, sifresiz, env, cfg));
                    }
                    lock (j) { j.Satirlar.AddRange(yeni); j.Parca++; }
                }
            }
            catch (OperationCanceledException) { lock (j) j.Hata = "Durduruldu."; }
            catch (Exception ex) { lock (j) j.Hata = ex.Message; }
            finally { j.Bitti = true; }
        });
        return j;
    }

    static string? Host(EnvanterSnapshot env, string ip) => env.AppsOnIp(ip).Select(h => h.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    // Şifrelemesi protokolün içinde olan / TLS el sıkışması olarak görünmeyen bilinen portlar
    static readonly Dictionary<string, string> Protokol = new()
    {
        ["5985"] = "WinRM (HTTP): içerik varsayılan olarak Kerberos / NTLM ile mesaj düzeyinde şifreli — AllowUnencrypted ve Basic kapalı olmalı",
        ["47001"] = "WinRM (HTTP): içerik varsayılan olarak mesaj düzeyinde şifreli — AllowUnencrypted ve Basic kapalı olmalı",
        ["1433"] = "SQL Server: TLS TDS paketinin içinde, ayrı el sıkışması görünmez (Force Encryption ayarına bakın)",
        ["3389"] = "RDP: TLS CredSSP / X.224 içinde",
        ["389"] = "LDAP: StartTLS ya da düz (LDAP signing / channel binding)", ["3268"] = "LDAP GC: StartTLS ya da düz",
        ["88"] = "Kerberos: kendi şifrelemesi", ["135"] = "RPC: kimlik doğrulama düzeyine göre şifreli olabilir",
        ["445"] = "SMB: TLS değil, SMB3 şifrelemesi olabilir", ["139"] = "NetBIOS / SMB1: şifresiz olabilir",
        ["1521"] = "Oracle: TLS değil, native network encryption olabilir", ["1522"] = "Oracle: native network encryption olabilir",
        ["25"] = "SMTP: StartTLS ile düz başlar", ["587"] = "SMTP: StartTLS ile düz başlar", ["21"] = "FTP: düz ya da FTPS (StartTLS)",
        ["22"] = "SSH: kendi şifrelemesi (TLS değil)", ["53"] = "DNS: şifresiz (UDP/TCP)", ["161"] = "SNMP: v3 değilse şifresiz",
    };

    static IEnumerable<TlsTopluSatir> Satirlar(string ip, List<TlsAkis.Ham> ham, Dictionary<string, long>? akis, bool sifresiz, EnvanterSnapshot env, IConfiguration cfg)
    {
        var ert = env.AppsOnIp(ip);
        string? host = Host(env, ip);
        int dinamik = cfg.GetValue("Tls:DinamikPortBaslangic", 49152);
        var ertPort = ert.Where(h => !string.IsNullOrWhiteSpace(h.Port)).GroupBy(h => h.Port!.Trim())
            .ToDictionary(g => g.Key, g => g.Select(h => h.AppName).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList());
        string? genel = env.AppNamesOnIp(ip).FirstOrDefault(a => !env.IsSystemApp(a));
        // TCP trafiğinden gelen portlar da satır olur (dinamik / geçici portlar hariç; TLS görülmüşse yine de girer)
        var akisPort = (akis?.Keys ?? Enumerable.Empty<string>()).Where(p => !(int.TryParse(p, out int n) && n >= dinamik));
        var portlar = ertPort.Keys.Union(ham.Select(h => h.Port)).Union(akisPort).Where(p => p != "" && p != "0")
            .OrderBy(p => int.TryParse(p, out int n) ? n : 99999).ToList();
        if (portlar.Count == 0)
        {
            yield return new TlsTopluSatir(ip, host, "", genel, false, [], 0, 0, [],
                ert.Count == 0 ? "ERT'de kayıt yok, AppResponse'ta trafik görülmedi" : "AppResponse'ta bu IP'ye gelen trafik görülmedi",
                akis == null ? "bilinmiyor" : "trafikyok");
            yield break;
        }
        foreach (var p in portlar)
        {
            var hs = ham.Where(h => h.Port == p).ToList();
            long top = hs.Sum(h => h.Sayi);
            var surumler = hs.GroupBy(h => h.Surum).Select(g => (S: g.Key, N: g.Sum(h => h.Sayi)))
                .OrderByDescending(x => x.N).Select(x => new TlsTopluSurum(x.S, x.N, top > 0 ? Math.Round(x.N * 100.0 / top, 1) : 0)).ToList();
            bool ertte = ertPort.TryGetValue(p, out var appler);
            string? uyg = ertte && appler!.Count > 0 ? string.Join(", ", appler) : null;
            long trafik = akis?.GetValueOrDefault(p) ?? 0;
            string sif; string? ipucu = null, not;
            if (hs.Count > 0) { sif = "tls"; not = !ertte ? "ERT'de bu port yok" : null; }
            else if (akis == null) { sif = "bilinmiyor"; not = sifresiz ? "TLS el sıkışması görülmedi (TCP trafiği bilgisi alınamadı)" : "TLS el sıkışması görülmedi"; }
            else if (trafik == 0) { sif = "trafikyok"; not = "AppResponse bu portta trafik görmedi"; }
            else if (Protokol.TryGetValue(p, out var pr)) { sif = "protokol"; ipucu = pr; not = "Trafik var, TLS el sıkışması yok — şifreleme protokolün içinde olabilir"; }
            else { sif = "sifresiz"; not = "Trafik var ama TLS el sıkışması yok — olası şifresiz iletişim"; }
            if (!ertte && sif != "tls") not += " · ERT'de bu port yok";
            yield return new TlsTopluSatir(ip, host, p, uyg ?? (ertte ? null : genel), ertte, surumler, top, hs.Select(h => h.Cli).Distinct().Count(),
                hs.Select(h => h.Cihaz).Distinct().ToList(), not, sif, trafik, ipucu);
        }
    }
}
