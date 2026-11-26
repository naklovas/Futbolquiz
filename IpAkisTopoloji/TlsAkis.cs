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
using System.Text.RegularExpressions;

record TlsSatir(string Yon, string Karsi, string Port, string Surum, long Sayi, int KarsiSayisi, List<TlsKarsi> Karsilar, List<string> Cihazlar, List<string> Sni);
record TlsKarsi(string Ip, long Sayi, string? Uygulama);
record TlsCihaz(string Ad, int Satir, string? Yontem, string? Hata);
record TlsSonuc(string Ip, DateTime Bas, DateTime Bit, string? Yontem, List<TlsSatir> Gelen, List<TlsSatir> Giden,
    List<TlsCihaz> Cihazlar, List<string> Mesajlar, bool Kesik, long Ms);

static class TlsAkis
{
    static readonly ConcurrentDictionary<string, (DateTime At, TlsSonuc S)> _cache = new();
    static volatile bool _sniYok;   // kutularda SNI kolonu yok / gruplanamıyor: toplu sorguda bir daha denenmez
    internal static bool SniYok => _sniYok;

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
        IConfiguration cfg, HttpClient http, CancellationToken ct, string? sniKol = null, Action<string, string, bool>? ilerleme = null)
    {
        var t = cfg.GetSection("Tls");
        string kaynak = t["Kaynak"] ?? "tls_handshakes";
        string cliIp = t["IstemciIp"] ?? "cli_tcp.ip", srvIp = t["SunucuIp"] ?? "srv_tcp.ip", srvPort = t["SunucuPort"] ?? "srv_tcp.port";
        string surum = t["Surum"] ?? "tls.version", surumAd = t["SurumAd"] ?? "tls.version_name", metrik = t["Metrik"] ?? "sum_tls.session_attempted";
        // SNI (TLS ClientHello'da istenen sunucu adı): Tls:Sni ya da toplu sorgunun FQDN / giden kipi (sniKol). Kolon kutuda yoksa
        // ya da gruplama bozulursa SNI'siz denemelere düşülür; bir kez hiç alınamazsa süreç boyunca denenmez (_sniYok)
        string sni = sniKol ?? t["Sni"] ?? "";
        if (sniKol != null && _sniYok) sni = "";
        int bekle = t.GetValue("BeklemeSaniye", 300);
        // SNI'li denemeler daha kısa beklenir: kolon / gruplama sorunluysa rapor uzun sürüp zaman aşımına düşebiliyor, ardından SNI'siz deneme yine tam süre bekliyordu
        int bekleSni = Math.Clamp(t.GetValue("SniBeklemeSaniye", 120), 10, Math.Max(10, bekle));

        string PortF(string k) => portlar.Count > 0 ? " and (" + string.Join(" or ", portlar.Select(p => $"{k} == {p}")) + ")" : "";
        string srvF = "(" + string.Join(" or ", ips.Select(ip => $"{srvIp} == {ip}")) + ")" + PortF(srvPort);
        string filtre = gelenYalniz ? srvF : $"({srvF}) or ((" + string.Join(" or ", ips.Select(ip => $"{cliIp} == {ip}")) + ")" + PortF(srvPort) + ")";
        string pathType = cfg["SourcePathType"] ?? "jobs";
        var vifgler = cfg.GetSection("VifgIds").GetChildren().Select(x => (x.Value ?? "").Trim()).Where(x => x != "").ToList();
        if (vifgler.Count == 0) vifgler.Add("");
        bool sil = cfg.GetValue("DeleteReportInstances", true);
        long sBas = new DateTimeOffset(bas).ToUnixTimeSeconds(), sBit = new DateTimeOffset(bit).ToUnixTimeSeconds();

        string[] anahtar = [cliIp, srvIp, srvPort];
        var denemeler = new List<(string Ad, string[] Kol, string[]? Grup, bool Metrik, bool Sni)>();
        if (sni != "")
        {
            denemeler.Add(("gruplu (tls.version) + SNI", [.. anahtar, surum, sni, metrik], [.. anahtar, surum, sni], true, true));
            denemeler.Add(("gruplu, metriksiz + SNI", [.. anahtar, surum, sni], [.. anahtar, surum, sni], false, true));
        }
        if (sniKol == null || sni == "")   // Tls:Sni ayarıyla eski davranış: SNI'li ham satırlar
        {
            var tlsKol = new List<string> { surum }; if (sni != "") tlsKol.Add(sni);
            var tlsKolAd = new List<string> { surumAd }; if (sni != "") tlsKolAd.Add(sni);
            denemeler.Add(("gruplu (tls.version)", [.. anahtar, .. tlsKol, metrik], [.. anahtar, .. tlsKol], true, sni != ""));
            denemeler.Add(("gruplu, metriksiz", [.. anahtar, .. tlsKol], [.. anahtar, .. tlsKol], false, sni != ""));
            denemeler.Add(("ham satırlar", [.. anahtar, .. tlsKolAd], null, false, sni != ""));
        }
        else
        {
            denemeler.Add(("gruplu (tls.version)", [.. anahtar, surum, metrik], [.. anahtar, surum], true, false));
            denemeler.Add(("gruplu, metriksiz", [.. anahtar, surum], [.. anahtar, surum], false, false));
            denemeler.Add(("ham satırlar", [.. anahtar, surumAd], null, false, false));
        }

        var names = cfg.GetSection("Servers").GetChildren().Select((s, i) => (Ad: s["Name"] ?? $"Cihaz {i + 1}", I: i)).ToList();
        var sonuc = await Task.WhenAll(names.Select(async n =>
        {
            var satirlar = new List<Ham>();
            string? yontem = null, hata = null;
            bool sniZaman = false;   // SNI'li deneme zaman aşımına düştü (kolon yok değil, yavaş) → kalan SNI denemeleri atlanır
            try
            {
                ilerleme?.Invoke(n.Ad, "bağlanıyor", false);
                var (_, baseUrl, jwt) = await AppResponseService.ConnectAsync(n.I, cfg, http, ct);
                int no = 0;
                foreach (var d in denemeler)
                {
                    no++;
                    if (d.Sni && sniZaman) continue;
                    ilerleme?.Invoke(n.Ad, $"{no}/{denemeler.Count}. deneme: {d.Ad} (en çok {(d.Sni ? bekleSni : bekle)} sn)", false);
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
                            var data = await AppResponseService.RunSingleAsync(baseUrl, jwt, dd, sil, http, ct, d.Sni ? bekleSni : bekle);
                            foreach (var a in data.OfType<JsonArray>())
                            {
                                string C(int i) => i < a.Count ? a[i]?.ToString() ?? "" : "";
                                long sayi = d.Metrik && double.TryParse(C(d.Sni ? 5 : 4), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? (long)v : 1;
                                tmp.Add(new Ham(n.Ad, Ip(C(0)), Ip(C(1)), C(2), SurumAd(C(3)), d.Sni ? C(4) : null, sayi));
                            }
                        }
                        satirlar = tmp; yontem = d.Ad; hata = null;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { hata = ex.Message; if (d.Sni && ex is TimeoutException) sniZaman = true; }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { hata = ex is TaskCanceledException ? "Zaman aşımı" : ex.Message; }
            ilerleme?.Invoke(n.Ad, yontem != null ? $"tamam · {yontem} · {satirlar.Count} satır" : "alınamadı: " + Kisalt(hata, 120), true);
            return (n.Ad, satirlar, yontem, hata, sniZaman);
        }));

        if (sniKol != null && sni != "" && sonuc.Any(s => s.yontem != null) && !sonuc.Any(s => s.sniZaman) && sonuc.All(s => s.yontem == null || !s.yontem.EndsWith("+ SNI")))
            _sniYok = true;
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
    long Sayi, int Istemci, List<string> Kutular, string? Not, string Sifreleme = "bilinmiyor", long Trafik = 0, string? Ipucu = null,
    // FQDN / endpoint (kullanıcı: mailde "ilgili servislerin FQDN / endpoint URL bilgileri" istendi, ~1000 sunucu toplu)
    List<TlsFqdn>? Fqdn = null, List<TlsUc>? Uclar = null, string? Url = null, string? Website = null, string? Vip = null, bool Istenen = false,
    // Yön (kullanıcı: "tek IP yazarsam giden gelen ne varsa baksın"): "gelen" = IP sunucu (Port onun portu); "giden" = IP istemci, Hedef:Port çağrılan servis,
    // Uygulama hedefin ERT uygulaması
    string Yon = "gelen", string? Hedef = null, string? HedefSunucu = null,
    // Giden hedef bir VIP ise havuz üyeleri = "dış bacak" (kullanıcı: "dış kurum tahsilatlarında gidenlerde URL yok, muhtemelen VIP üzerinden gidiyor"):
    // F5'in arkasındaki gerçek hedefler, o bacağın TLS sürümü ve SNI'si (AppResponse, istemci F5), Palo Alto'daki hedef adı / URL
    List<TlsUye>? Uyeler = null, string? DisUrl = null,
    // Segment (KonsolideSegmentler4; kullanıcı: "segment, VLAN bilgisi de kolon olsun"): satır IP'sinin, giden satırda ayrıca hedefin
    string? Segment = null, string? Vlan = null, string? Cidr = null, string? HedefSegment = null, string? HedefVlan = null);
record TlsUye(string Ip, string? Port, bool Dis, List<TlsTopluSurum> Surumler, List<string> Sni, List<string> Adlar);
// Servise erişilen ad: Kaynaklar = "DNS (Carbon Black)" (istemcilerin çözdüğü ad), "DNS · VIP ip:port", "IIS Host", "Sertifika (Nessus)", "Sunucu DNS (Nessus)"
record TlsFqdn(string Ad, List<string> Kaynaklar, long Sayi, int Istemci);
// IIS logunda görülen yol (sayı / GUID parçaları {n} / {guid}), istek ve istemci sayısı, örnek metot, site
record TlsUc(string Yol, long Istek, int Istemci, string? Metot, string? Site);

class TlsTopluIs
{
    public string Id = Guid.NewGuid().ToString("N")[..12];
    public string Kullanici = "";
    public int Saat;
    public List<string> Ips = [];
    public int Parca, ToplamParca;
    public bool Bitti;
    public bool Sifresiz;   // ek flow_tcp sorgusu yapıldı mı (trafik var / TLS yok ayrımı)
    public bool Fqdn;       // FQDN / endpoint araması yapıldı mı (Carbon Black DNS, IIS logu, Nessus sertifika)
    public bool Giden;      // giden el sıkışmaları da alındı mı (IP istemci)
    public int IstenenPort; // girişte IP + port verilen satır sayısı (yalnız o portlar)
    public string? Hata;
    public DateTime Bas, Bit, Basladi = DateTime.UtcNow;
    public List<TlsTopluSatir> Satirlar = [];
    public List<string> Mesajlar = [];
    public HashSet<string> Yontemler = [];
    public CancellationTokenSource Cts = new();
    // Sürmekte olan grubun adımları (AppResponse kutuları, Carbon Black, IIS, Nessus, dış bacak…): ekranda "%0"da ne beklendiği görünsün
    public List<TlsAdim> Adimlar = [];
    public DateTime GrupBas = DateTime.UtcNow;
    public void Adim(string ad, string durum, bool bitti = false)
    {
        lock (this)
        {
            int i = Adimlar.FindIndex(a => a.Ad == ad);
            var bas = i >= 0 && Adimlar[i].Bit == null ? Adimlar[i].Bas : DateTime.UtcNow;
            var yeni = new TlsAdim(ad, durum, bas, bitti ? DateTime.UtcNow : null);
            if (i >= 0) Adimlar[i] = yeni; else Adimlar.Add(yeni);
        }
    }
}

record TlsAdim(string Ad, string Durum, DateTime Bas, DateTime? Bit);

static class TlsToplu
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TlsTopluIs> _isler = new();

    public static TlsTopluIs? Get(string id) => _isler.TryGetValue(id, out var j) ? j : null;

    public static TlsTopluIs Start(List<string> ips, int saat, bool sifresiz, string kullanici, IConfiguration cfg, HttpClient http, EnvanterService envanter,
        bool fqdn = false, Dictionary<string, HashSet<string>>? istenen = null, HttpClient? splunk = null, NessusService? nessus = null, bool giden = false)
    {
        // eski işler (1 saat) temizlenir
        foreach (var kv in _isler.Where(kv => (DateTime.UtcNow - kv.Value.Basladi).TotalHours > 1).ToList()) _isler.TryRemove(kv.Key, out _);
        int parca = Math.Max(1, cfg.GetValue("Tls:TopluParca", 20));
        var (bas, bit) = TlsAkis.Aralik(saat);
        var j = new TlsTopluIs { Kullanici = kullanici, Saat = saat, Ips = ips, Bas = bas, Bit = bit, ToplamParca = (ips.Count + parca - 1) / parca, Sifresiz = sifresiz,
            Fqdn = fqdn && splunk != null, IstenenPort = istenen?.Sum(x => x.Value.Count) ?? 0, Giden = giden };
        _isler[j.Id] = j;
        // SNI (TLS'te istenen sunucu adı) FQDN / giden kipinde istenir (Tls:TopluSni, varsayılan tls.sni; boş = istenmez)
        string? sniKol = (j.Fqdn || giden) && (cfg["Tls:TopluSni"] ?? "tls.sni") is { Length: > 0 } sk ? sk : null;
        _ = Task.Run(async () =>
        {
            try
            {
                var env = await envanter.GetAsync(false, j.Cts.Token);
                foreach (var grup in ips.Chunk(parca))
                {
                    j.Cts.Token.ThrowIfCancellationRequested();
                    lock (j) { j.Adimlar.Clear(); j.GrupBas = DateTime.UtcNow; }
                    List<TlsAkis.Ham> ham = [];
                    bool tlsHata = false;
                    // TCP trafiği (şifresiz kontrolü) TLS sorgusuyla AYNI ANDA başlar (sırayla beklemek süreyi ikiye katlıyordu)
                    var akisTask = sifresiz ? Izle(j, "TCP trafiği (AppResponse flow_tcp)", TlsAkis.AkisAsync(grup, bas, bit, cfg, http, j.Cts.Token)) : null;
                    // FQDN / endpoint: Carbon Black DNS adları + IIS logu + Nessus, TLS sorgusuyla aynı anda
                    var ucTask = j.Fqdn ? UcNoktaAsync(grup, env, cfg, splunk!, nessus, j.Cts.Token, giden, j) : null;
                    try
                    {
                        var (s, cihazlar, mesajlar, _) = await TlsAkis.CekAsync(grup, bas, bit, [], !giden, cfg, http, j.Cts.Token, sniKol,
                            (c, d, b) => j.Adim("AppResponse TLS · " + c, d, b));
                        ham = s;
                        foreach (var c in cihazlar.Where(c => c.Yontem != null)) lock (j) j.Yontemler.Add(c.Yontem!);
                        lock (j) foreach (var m in mesajlar) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                        if (cihazlar.All(c => c.Yontem == null)) throw new InvalidOperationException("Hiçbir kutudan veri alınamadı: " + string.Join(" | ", mesajlar));
                    }
                    catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        lock (j) j.Mesajlar.Add($"{grup.First()}…{grup.Last()} ({grup.Length} IP) alınamadı: {ex.Message}");
                        if (ucTask == null)
                        {
                            var hatali = grup.Select(ip => SegmentEkle(new TlsTopluSatir(ip, Host(env, ip), "", null, false, [], 0, 0, [], "TLS verisi alınamadı (hata)"), env)).ToList();
                            lock (j) { j.Satirlar.AddRange(hatali); j.Parca++; }
                            _ = akisTask?.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // sonucu kullanılmayacak; hata gözlenmiş sayılsın
                            continue;
                        }
                        tlsHata = true;   // FQDN / endpoint araması varsa satırlar yine üretilir (TLS kolonu "alınamadı")
                    }
                    // TCP trafiği (şifresiz kontrolü): hata TLS sonucunu bozmaz, satırlar "bilinmiyor" kalır
                    Dictionary<(string Ip, string Port), long>? akis = null;
                    if (akisTask != null) try
                    {
                        var (ap, aok, amsg) = await akisTask;
                        if (aok) akis = ap;
                        lock (j) foreach (var m in amsg) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                    }
                    catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                    catch (Exception ex) { lock (j) if (j.Mesajlar.Count < 30) j.Mesajlar.Add("flow_tcp (şifresiz kontrolü) alınamadı: " + ex.Message); }
                    UcVeri? uc = null;
                    if (ucTask != null) try
                    {
                        uc = await ucTask;
                        lock (j) foreach (var m in uc.Mesajlar) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                    }
                    catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                    catch (Exception ex) { lock (j) if (j.Mesajlar.Count < 30) j.Mesajlar.Add("FQDN / endpoint bilgisi alınamadı: " + ex.Message); }
                    var yeni = new List<TlsTopluSatir>();
                    var gidenTum = new List<TlsTopluSatir>();
                    foreach (var ip in grup)
                    {
                        var ipAkis = akis?.Where(kv => TlsAkis.AyniIpDis(kv.Key.Ip, ip)).ToDictionary(kv => kv.Key.Port, kv => kv.Value);
                        HashSet<string>? sadece = istenen != null && istenen.TryGetValue(ip, out var ist) && ist.Count > 0 ? ist : null;
                        var ek = uc == null ? [] : uc.Portlar(ip, env);
                        var satirlar = Satirlar(ip, ham.Where(h => TlsAkis.AyniIpDis(h.Srv, ip)).ToList(), ipAkis, sifresiz, env, cfg, ek, sadece);
                        var hamGelen = ham.Where(h => TlsAkis.AyniIpDis(h.Srv, ip)).ToList();
                        yeni.AddRange(satirlar.Select(r => Zenginlestir(tlsHata ? r with { Not = "TLS verisi alınamadı (hata)", Sifreleme = "bilinmiyor" } : r, uc, env, sadece != null, hamGelen)));
                        if (giden) gidenTum.AddRange(GidenSatirlar(ip, ham.Where(h => TlsAkis.AyniIpDis(h.Cli, ip) && !TlsAkis.AyniIpDis(h.Srv, ip)).ToList(), uc, env, tlsHata));
                    }
                    // giden hedefi VIP olanların havuz üyeleri (dış bacak) + dış hedeflerin Palo Alto adları: grup başına tek ek sorgu
                    if (gidenTum.Count > 0)
                        try { gidenTum = await DisBacakAsync(gidenTum, env, cfg, http, splunk, bas, bit, sniKol, j); }
                        catch (OperationCanceledException) when (j.Cts.IsCancellationRequested) { throw; }
                        catch (Exception ex) { lock (j) if (j.Mesajlar.Count < 30) j.Mesajlar.Add("VIP dış bacağı / firewall adları alınamadı: " + ex.Message); }
                    yeni.AddRange(gidenTum);
                    yeni = yeni.Select(r => SegmentEkle(r, env)).ToList();
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

    static IEnumerable<TlsTopluSatir> Satirlar(string ip, List<TlsAkis.Ham> ham, Dictionary<string, long>? akis, bool sifresiz, EnvanterSnapshot env, IConfiguration cfg,
        IEnumerable<string>? ekPort = null, HashSet<string>? sadece = null)
    {
        var ert = env.AppsOnIp(ip);
        string? host = Host(env, ip);
        int dinamik = cfg.GetValue("Tls:DinamikPortBaslangic", 49152);
        var ertPort = ert.Where(h => !string.IsNullOrWhiteSpace(h.Port)).GroupBy(h => h.Port!.Trim())
            .ToDictionary(g => g.Key, g => g.Select(h => h.AppName).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList());
        string? genel = env.AppNamesOnIp(ip).FirstOrDefault(a => !env.IsSystemApp(a));
        // TCP trafiğinden gelen portlar da satır olur (dinamik / geçici portlar hariç; TLS görülmüşse yine de girer)
        var akisPort = (akis?.Keys ?? Enumerable.Empty<string>()).Where(p => !(int.TryParse(p, out int n) && n >= dinamik));
        // FQDN / endpoint aramasında görülen portlar (CB DNS adı, IIS logu) da satır olur; girişte IP + port verildiyse YALNIZ o portlar (veri olmasa da satır)
        var portlar = ertPort.Keys.Union(ham.Select(h => h.Port)).Union(akisPort).Union((ekPort ?? []).Where(p => !(int.TryParse(p, out int n) && n >= dinamik)))
            .Union(sadece ?? []).Where(p => p != "" && p != "0" && (sadece == null || sadece.Contains(p)))
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

    // ---------------- FQDN / endpoint URL (kullanıcı: "ilgili servislerin FQDN / endpoint URL bilgileri", 1000 sunucu toplu) ----------------
    // Kaynaklar: (1) Carbon Black: istemcilerin bu IP:port'a (ya da sunucunun üyesi olduğu VIP ip:port'a) bağlanırken çözdüğü DNS adı (`domain`, giden olay);
    // (2) IIS logu: Host başlığı (cs_host alanı çıkarılıyorsa) + istek yolları (sayı / GUID parçaları sadeleşir, tarama yolları atılır);
    // (3) Nessus: portun sertifika adları (CN / SAN) ve sunucunun DNS adı. IIS dışı (Java / nginx…) servislerin yolu ağ verisinde yok.
    internal sealed class UcVeri
    {
        public Dictionary<(string Ip, string Port), List<(string Ad, long N, int Ist)>> Dns = new();
        public Dictionary<(string Ip, string Port), List<(string Yol, long N, int Ist, string? M, string? Site, List<string> Host)>> Iis = new();
        public Dictionary<string, NessusSunucu> Nessus = new();
        // giden: bu sunucunun (istemci) bağlandığı hedef ip:port → çözdüğü DNS adları (Carbon Black, local_ip = sunucu)
        public Dictionary<(string Ip, string Hedef, string Port), List<(string Ad, long N)>> GidenDns = new();
        public List<string> Mesajlar = [];
        // satır olacak portlar: CB'de DNS adıyla bağlanılan ve IIS logunda görülen portlar (VIP üzerinden gelenler üye portuna çevrilir)
        public IEnumerable<string> Portlar(string ip, EnvanterSnapshot env) =>
            Dns.Keys.Where(k => k.Ip == ip).Select(k => k.Port)
                .Concat(env.MemberOf(ip).Where(v => v.Port != null && Dns.Keys.Any(k => k.Ip == v.LbIp && k.Port == v.LbPort)).Select(v => v.Port!))
                .Concat(Iis.Keys.Where(k => k.Ip == ip).Select(k => k.Port)).Distinct();
    }

    static readonly Regex _ipRx = new(@"^\d{1,3}(\.\d{1,3}){3}$");
    // HTTP olmayan bilinen portlar: FQDN gösterilir ama "önerilen URL" üretilmez (veritabanı, SSH, RDP, LDAP, MQ…)
    static readonly HashSet<string> _httpDegil = ["21", "22", "23", "25", "53", "88", "110", "111", "123", "135", "139", "143", "161", "389", "445", "514", "587", "636",
        "1433", "1434", "1521", "1522", "1523", "1524", "1525", "1526", "1527", "1528", "1529", "1530", "2049", "2483", "2484", "3268", "3269", "3306", "3389",
        "5432", "5672", "5985", "5986", "6379", "9092", "11211", "27017", "47001", "50000", "61616"];
    static string? UrlYap(string? ad, bool https, string port) =>
        ad == null || _httpDegil.Contains(port) ? null : $"{(https ? "https" : "http")}://{ad}" + ((https && port == "443") || (!https && port == "80") ? "" : ":" + port);
    // Bir alt sorguyu işin adım listesine yazar (başladı / bitti / hata); j yoksa yalnız bekler
    static async Task<T> Izle<T>(TlsTopluIs? j, string ad, Task<T> t)
    {
        if (j == null) return await t;
        j.Adim(ad, "sürüyor");
        try { var r = await t; j.Adim(ad, "tamam", true); return r; }
        catch (OperationCanceledException) { j.Adim(ad, "iptal", true); throw; }
        catch (Exception ex) { j.Adim(ad, "hata: " + (ex.Message.Length > 100 ? ex.Message[..100] + "…" : ex.Message), true); throw; }
    }

    static async Task<UcVeri> UcNoktaAsync(string[] grup, EnvanterSnapshot env, IConfiguration cfg, HttpClient http, NessusService? nessus, CancellationToken ct, bool giden = false, TlsTopluIs? j = null)
    {
        var v = new UcVeri();
        var end = DateTimeOffset.Now;
        var sure = TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Tls:UcSureSaniye", 180.0), 10, 900));
        // VIP adresleri: istemciler havuz üyesine değil VIP'e bağlanır, DNS adı VIP ip:port olaylarında görünür
        var vipIp = grup.SelectMany(ip => env.MemberOf(ip)).Select(x => x.LbIp).Where(x => _ipRx.IsMatch(x ?? "")).Distinct().ToList();
        var hedef = grup.Concat(vipIp).Distinct().ToList();
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", st = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string terms = string.Join(" OR ", hedef.Select(x => $"TERM({x})")), inList = string.Join(",", hedef.Select(x => $"\"{x}\""));
        var cbSaat = Math.Clamp(cfg.GetValue("Tls:UcCbSaat", 24.0), 1, 168);
        var cbTask = Izle(j, "Carbon Black DNS adları (gelen)", SplunkService.ExportAsync($"""
            search index={index} sourcetype="{st}" ({terms}) remote_ip IN ({inList}) direction=outbound domain=*
            | stats count dc(local_ip) as ist by remote_ip remote_port domain
            """, new LookupQuery(grup[0], end.AddHours(-cbSaat), end), cfg, http, ct, "Carbon Black (DNS adları)", sure));

        var idx = cfg.GetSection("HttpSys:IisIndexleri").Get<string[]>() is { Length: > 0 } a ? a : ["iis_msy", "iis_csy"];
        Task<SplunkResult>? iisTask = null;
        if (idx.All(x => Regex.IsMatch(x, @"^[A-Za-z0-9_\-\*]+$")))
        {
            string gterms = string.Join(" OR ", grup.Select(x => $"TERM({x})")), gin = string.Join(",", grup.Select(x => $"\"{x}\""));
            var iisSaat = Math.Clamp(cfg.GetValue("Tls:UcIisSaat", cfg.GetValue("HttpSys:IisSaat", 1.0)), 0.25, 72);
            iisTask = Izle(j, "IIS logu (endpoint yolları)", SplunkService.ExportAsync($$"""
                search ({{string.Join(" OR ", idx.Select(x => "index=" + x))}}) ({{gterms}})
                | fields host source _raw cs_host cs_Host
                | rex "^\S+ \S+ (?<iis_sip>\S+) (?<iis_m>\S+) (?<iis_uri>\S+) \S+ (?<iis_port>\d+) \S+ (?<iis_cip>\S+)"
                | search iis_sip IN ({{gin}})
                | rex field=source "(?i)W3SVC(?<iis_site>\d+)"
                | eval iis_yol=lower(iis_uri)
                | eval iis_yol=replace(iis_yol, "/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "/{guid}")
                | eval iis_yol=replace(iis_yol, "/\d+(/|$)", "/{n}\1")
                | eval hh=lower(coalesce(cs_host, cs_Host, ""))
                | stats count dc(iis_cip) as ist values(hh) as hh values(iis_site) as site latest(iis_m) as m by iis_sip iis_port iis_yol
                | eval hh=mvjoin(mvindex(hh, 0, 4), "|||"), site=mvjoin(mvindex(site, 0, 2), ",")
                """, new LookupQuery(grup[0], end.AddHours(-iisSaat), end), cfg, http, ct, "IIS logu (endpoint)", sure));
        }
        else v.Mesajlar.Add("HttpSys:IisIndexleri geçersiz: IIS endpoint araması yapılmadı.");
        // giden: sunucunun kendi giden olaylarındaki hedef ad (domain) — hangi servislere hangi adla bağlanıyor
        string gin0 = string.Join(",", grup.Select(x => $"\"{x}\""));
        var gidenTask = giden ? Izle(j, "Carbon Black DNS adları (giden)", SplunkService.ExportAsync($"""
            search index={index} sourcetype="{st}" ({string.Join(" OR ", grup.Select(x => $"TERM({x})"))}) local_ip IN ({gin0}) direction=outbound domain=*
            | stats count by local_ip remote_ip remote_port domain
            """, new LookupQuery(grup[0], end.AddHours(-cbSaat), end), cfg, http, ct, "Carbon Black (giden DNS adları)", sure)) : null;
        var nesTask = nessus is { Acik: true } && cfg.GetValue("Tls:UcNessus", true) ? Izle(j, "Nessus (sertifika / DNS adları)", nessus.SunucularAsync(grup, null, ct)) : null;

        static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var x) && !string.IsNullOrWhiteSpace(x) ? x.Trim() : null;
        static long L(string? x) => long.TryParse(x, out var n) ? n : 0;
        try
        {
            var r = await cbTask;
            foreach (var m in r.Messages) v.Mesajlar.Add(m);
            foreach (var row in r.Rows)
            {
                if (V(row, "remote_ip") is not { } ip || V(row, "remote_port") is not { } port || V(row, "domain") is not { } d) continue;
                d = d.TrimEnd('.').ToLowerInvariant();
                if (_ipRx.IsMatch(d) || d.Length < 3) continue;
                var key = (ip, port);
                if (!v.Dns.TryGetValue(key, out var l)) v.Dns[key] = l = [];
                l.Add((d, L(V(row, "count")), (int)L(V(row, "ist"))));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { v.Mesajlar.Add("Carbon Black DNS adları alınamadı: " + ex.Message); }
        if (iisTask != null) try
        {
            var r = await iisTask;
            foreach (var m in r.Messages) v.Mesajlar.Add(m);
            foreach (var row in r.Rows)
            {
                if (V(row, "iis_sip") is not { } ip || V(row, "iis_port") is not { } port || V(row, "iis_yol") is not { } yol) continue;
                var ilk = yol.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                if (ilk.Length > 0 && HttpSysEslesme.IisTarama(ilk)) continue;   // tarayıcı / sağlık kontrolü gürültüsü
                var key = (ip, port);
                if (!v.Iis.TryGetValue(key, out var l)) v.Iis[key] = l = [];
                l.Add((yol, L(V(row, "count")), (int)L(V(row, "ist")), V(row, "m"), V(row, "site"),
                    (V(row, "hh") ?? "").Split("|||", StringSplitOptions.RemoveEmptyEntries).Where(h => h != "-").ToList()));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { v.Mesajlar.Add("IIS endpoint bilgisi alınamadı: " + ex.Message); }
        if (gidenTask != null) try
        {
            var r = await gidenTask;
            foreach (var m in r.Messages) v.Mesajlar.Add(m);
            foreach (var row in r.Rows)
            {
                if (V(row, "local_ip") is not { } ip || V(row, "remote_ip") is not { } h || V(row, "remote_port") is not { } port || V(row, "domain") is not { } d) continue;
                d = d.TrimEnd('.').ToLowerInvariant();
                if (_ipRx.IsMatch(d) || d.Length < 3 || h == ip || h.StartsWith("127.")) continue;
                var key = (ip, h, port);
                if (!v.GidenDns.TryGetValue(key, out var l)) v.GidenDns[key] = l = [];
                l.Add((d, L(V(row, "count"))));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { v.Mesajlar.Add("Carbon Black giden DNS adları alınamadı: " + ex.Message); }
        if (nesTask != null) try { v.Nessus = await nesTask; }
        catch (Exception ex) when (ex is not OperationCanceledException) { v.Mesajlar.Add("Nessus (sertifika adları) alınamadı: " + ex.Message); }
        return v;
    }

    // Satıra FQDN adayları, endpoint yolları, önerilen temel URL, IIS website (ERT) ve VIP adresi eklenir
    static TlsTopluSatir Zenginlestir(TlsTopluSatir r, UcVeri? uc, EnvanterSnapshot env, bool istenen, List<TlsAkis.Ham>? ham = null)
    {
        if (r.Port == "") return r with { Istenen = istenen };
        string ip = r.Ip, port = r.Port;
        var vipler = env.MemberOf(ip).Where(x => x.Port == port).ToList();
        string? vip = vipler.Count == 0 ? null : string.Join(", ", vipler.Select(x => $"{x.LbIp}:{x.LbPort}" + (string.IsNullOrWhiteSpace(x.Pool) ? "" : $" ({x.Pool!.Replace("/Common/", "")})")).Distinct());
        string? web = env.AppsOnIp(ip).Where(h => h.Port == port).Select(h => h.Website).FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));
        if (uc == null) return r with { Vip = vip, Website = web, Istenen = istenen };
        var ad = new Dictionary<string, (List<string> K, long N, int I)>(StringComparer.OrdinalIgnoreCase);
        void Ekle(string a, string kaynak, long n, int ist)
        {
            a = a.Trim().TrimEnd('.').ToLowerInvariant();
            if (a.Length < 3 || _ipRx.IsMatch(a)) return;
            var e = ad.TryGetValue(a, out var x) ? x : ([], 0, 0);
            if (!e.K.Contains(kaynak)) e.K.Add(kaynak);
            ad[a] = (e.K, e.N + n, e.I + ist);
        }
        if (uc.Dns.TryGetValue((ip, port), out var dl)) foreach (var d in dl) Ekle(d.Ad, "DNS (Carbon Black)", d.N, d.Ist);
        // SNI: istemcilerin TLS el sıkışmasında istediği ad (AppResponse) — servise hangi adla erişildiğinin en doğrudan kanıtı
        foreach (var g in (ham ?? []).Where(h => h.Port == port && !string.IsNullOrWhiteSpace(h.Sni)).GroupBy(h => h.Sni!))
            Ekle(g.Key, "SNI (AppResponse)", g.Sum(h => h.Sayi), g.Select(h => h.Cli).Distinct().Count());
        foreach (var vm in vipler)
            if (vm.LbPort != null && uc.Dns.TryGetValue((vm.LbIp, vm.LbPort), out var vl))
                foreach (var d in vl) Ekle(d.Ad, $"DNS · VIP {vm.LbIp}:{vm.LbPort}", d.N, d.Ist);
        List<TlsUc>? uclar = null;
        if (uc.Iis.TryGetValue((ip, port), out var il))
        {
            foreach (var h in il.SelectMany(x => x.Host).Distinct()) Ekle(h.Split(':')[0], "IIS Host", 0, 0);
            uclar = il.OrderByDescending(x => x.N).Take(25).Select(x => new TlsUc(x.Yol, x.N, x.Ist, x.M, x.Site)).ToList();
        }
        bool https = r.Surumler.Count > 0 || port is "443" or "8443" or "9443";
        if (uc.Nessus.TryGetValue(ip, out var ns))
        {
            var np = ns.Portlar.FirstOrDefault(x => x.Port == port);
            if (np != null)
            {
                foreach (var c in np.Sertifika) Ekle(c.Replace("DNS:", "").Replace("CN=", ""), "Sertifika (Nessus)", 0, 0);
                if (np.Tls is { Count: > 0 } || np.Sertifika.Count > 0) https = true;
            }
            if (!string.IsNullOrWhiteSpace(ns.Dns)) Ekle(ns.Dns, "Sunucu DNS (Nessus)", 0, 0);
        }
        // sıra: istemcilerin kullandığı (CB, bağlantı sayısı) > IIS Host > sertifika > sunucu DNS adı
        int Oncelik(List<string> k) => k.Any(x => x.StartsWith("DNS") || x.StartsWith("SNI")) ? 0 : k.Contains("IIS Host") ? 1 : k.Contains("Sertifika (Nessus)") ? 2 : 3;
        var fq = ad.Select(kv => new TlsFqdn(kv.Key, kv.Value.K, kv.Value.N, kv.Value.I))
            .OrderBy(x => Oncelik(x.Kaynaklar)).ThenByDescending(x => x.Sayi).ThenBy(x => x.Ad).Take(12).ToList();
        string? host = fq.FirstOrDefault(x => !x.Ad.StartsWith("*") && Oncelik(x.Kaynaklar) < 3)?.Ad ?? fq.FirstOrDefault(x => !x.Ad.StartsWith("*"))?.Ad;
        string? url = UrlYap(host, https, port);
        return r with { Fqdn = fq.Count > 0 ? fq : null, Uclar = uclar, Url = url, Website = web, Vip = vip, Istenen = istenen };
    }

    // GİDEN satırlar: IP'nin istemci olduğu el sıkışmaları hedef ip:port başına (sürüm yüzdeleri); FQDN açıksa CB'de bu sunucunun DNS adıyla bağlandığı
    // ama TLS el sıkışması görülmeyen hedefler de satır olur ("TLS el sıkışması görülmedi"). Uygulama = hedefin ERT uygulaması.
    static IEnumerable<TlsTopluSatir> GidenSatirlar(string ip, List<TlsAkis.Ham> ham, UcVeri? uc, EnvanterSnapshot env, bool tlsHata)
    {
        string? host = Host(env, ip);
        var anahtar = ham.Select(h => (H: h.Srv, P: h.Port))
            .Concat(uc?.GidenDns.Keys.Where(k => k.Ip == ip).Select(k => (H: k.Hedef, P: k.Port)) ?? [])
            .Where(k => k.P != "" && k.P != "0").Distinct().ToList();
        foreach (var (h, p) in anahtar.OrderBy(k => k.H).ThenBy(k => int.TryParse(k.P, out int n) ? n : 99999))
        {
            var hs = ham.Where(x => x.Srv == h && x.Port == p).ToList();
            long top = hs.Sum(x => x.Sayi);
            var surumler = hs.GroupBy(x => x.Surum).Select(g => (S: g.Key, N: g.Sum(x => x.Sayi))).OrderByDescending(x => x.N)
                .Select(x => new TlsTopluSurum(x.S, x.N, top > 0 ? Math.Round(x.N * 100.0 / top, 1) : 0)).ToList();
            var happ = env.AppsOnIp(h);
            string? uyg = happ.Where(a => a.Port == p).Select(a => a.AppName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
            bool ertte = uyg != null;
            uyg ??= env.AppNamesOnIp(h).FirstOrDefault(a => !env.IsSystemApp(a));
            string? web = happ.Where(a => a.Port == p).Select(a => a.Website).FirstOrDefault(w => !string.IsNullOrWhiteSpace(w));
            var fqL = new List<TlsFqdn>();
            foreach (var g in hs.Where(x => !string.IsNullOrWhiteSpace(x.Sni)).GroupBy(x => x.Sni!.Trim().TrimEnd('.').ToLowerInvariant()))
                fqL.Add(new TlsFqdn(g.Key, ["SNI (AppResponse)"], g.Sum(x => x.Sayi), 0));
            if (uc != null && uc.GidenDns.TryGetValue((ip, h, p), out var dl))
                foreach (var g in dl.GroupBy(x => x.Ad))
                {
                    int i = fqL.FindIndex(f => f.Ad == g.Key);
                    if (i >= 0) fqL[i] = fqL[i] with { Kaynaklar = [.. fqL[i].Kaynaklar, "DNS (Carbon Black)"] };
                    else fqL.Add(new TlsFqdn(g.Key, ["DNS (Carbon Black)"], g.Sum(x => x.N), 0));
                }
            List<TlsFqdn>? fq = fqL.Count > 0 ? fqL.OrderByDescending(x => x.Sayi).Take(8).ToList() : null;
            // hedef bir VIP ise: havuz adı ve üyeleri (dış bacak; TLS / SNI / firewall adları DisBacakAsync'te)
            var vu = env.VipMembers(h, p);
            string? vipTxt = vu.Count == 0 ? null : "VIP" + (vu.Select(x => x.Pool).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) is { } pool ? " · havuz " + pool.Replace("/Common/", "") : "");
            List<TlsUye>? uyeler = vu.Count == 0 ? null : vu.Select(x => new TlsUye(x.Ip, x.Port, !Ozel(x.Ip), [], [], [])).DistinctBy(x => (x.Ip, x.Port)).Take(30).ToList();
            bool https = hs.Count > 0 || p is "443" or "8443" or "9443";
            string? ad = fq?.FirstOrDefault()?.Ad;
            string? url = UrlYap(ad, https, p);
            string? not = hs.Count > 0 ? null : tlsHata ? "TLS verisi alınamadı (hata)" : "TLS el sıkışması görülmedi (Carbon Black'te bağlantı var)";
            yield return new TlsTopluSatir(ip, host, p, uyg, ertte, surumler, top, 0, hs.Select(x => x.Cihaz).Distinct().ToList(), not,
                hs.Count > 0 ? "tls" : "bilinmiyor", Fqdn: fq, Url: url, Website: web, Vip: vipTxt, Yon: "giden", Hedef: h, HedefSunucu: Host(env, h), Uyeler: uyeler);
        }
    }

    // Segment adı (EPG > ApplicationProfile > açıklama > CIDR), VLAN, CIDR; giden satırda hedef IP'nin segmenti de
    static TlsTopluSatir SegmentEkle(TlsTopluSatir r, EnvanterSnapshot env)
    {
        var s = env.FindSegment(r.Ip);
        var h = r.Hedef != null && _ipRx.IsMatch(r.Hedef) ? env.FindSegment(r.Hedef) : null;
        static string? V(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        return r with { Segment = s?.Label, Vlan = V(s?.Vlan), Cidr = s?.Cidr, HedefSegment = h?.Label, HedefVlan = V(h?.Vlan) };
    }

    static bool Ozel(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return true;
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 169 && b[1] == 254);
    }

    // DIŞ BACAK: giden hedefi VIP olan satırlarda havuz üyelerinin (F5 → gerçek hedef) TLS sürümü ve SNI'si (AppResponse, sunucu = üye; istemci F5
    // olduğu için sunucuya bağlanamayan bacak) + üyelerin ve doğrudan dış hedeflerin Palo Alto loglarındaki adı / URL'si (Tls:FwAdAlanlari).
    static async Task<List<TlsTopluSatir>> DisBacakAsync(List<TlsTopluSatir> satirlar, EnvanterSnapshot env, IConfiguration cfg, HttpClient http, HttpClient? splunk,
        DateTime bas, DateTime bit, string? sniKol, TlsTopluIs j)
    {
        var ct = j.Cts.Token;
        var uyeIp = satirlar.SelectMany(r => r.Uyeler ?? []).Select(u => u.Ip).Distinct().Take(cfg.GetValue("Tls:DisBacakMaxIp", 200)).ToList();
        var disHedef = satirlar.Where(r => r.Uyeler == null && r.Hedef != null && !Ozel(r.Hedef)).Select(r => r.Hedef!).Distinct().ToList();
        // (1) üyelerin TLS el sıkışmaları (gelen = üye sunucu)
        List<TlsAkis.Ham> ham = [];
        if (uyeIp.Count > 0)
        {
            var (h, cihazlar, mesajlar, _) = await TlsAkis.CekAsync(uyeIp, bas, bit, [], true, cfg, http, ct, sniKol,
                (c, d, b) => j.Adim($"VIP dış bacağı TLS ({uyeIp.Count} üye) · " + c, d, b));
            ham = h;
            lock (j) foreach (var m in mesajlar) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add("Dış bacak: " + m);
        }
        // (2) Palo Alto: hedef IP'ye giden oturumlarda ad / URL alanları (URL filtreleme / SSL inceleme logu varsa)
        var fwAd = new Dictionary<(string Ip, string Port), List<(string Ad, long N)>>();
        var fwUrl = new Dictionary<(string Ip, string Port), List<(string Yol, long N)>>();
        var fwHedef = uyeIp.Concat(disHedef).Distinct().Take(300).ToList();
        if (splunk != null && fwHedef.Count > 0 && FirewallService.Enabled(cfg) && cfg.GetValue("Tls:DisBacakFirewall", true))
        {
            var f = cfg.GetSection("Firewall");
            string idx = f["Index"] ?? "fw_paloalto", dIp = f["Alanlar:DestIp"] ?? "dest_ip", dPort = f["Alanlar:DestPort"] ?? "dest_port";
            var alanlar = cfg.GetSection("Tls:FwAdAlanlari").Get<string[]>() is { Length: > 0 } fa ? fa : ["url", "dest_hostname", "dest_name", "server_name", "sni", "hostname"];
            if (new[] { idx, dIp, dPort }.Concat(alanlar).All(x => Regex.IsMatch(x, @"^[A-Za-z0-9_.\-]+$")))
                try
                {
                    var end = DateTimeOffset.Now;
                    double saat = Math.Clamp(cfg.GetValue("Tls:UcCbSaat", 24.0), 1, 168);
                    var r = await Izle(j, $"Palo Alto hedef adları ({fwHedef.Count} IP)", SplunkService.ExportAsync($"""
                        search index={idx} ({string.Join(" OR ", fwHedef.Select(x => $"TERM({x})"))}) {dIp} IN ({string.Join(",", fwHedef.Select(x => $"\"{x}\""))})
                        | eval ad=coalesce({string.Join(", ", alanlar)})
                        | where isnotnull(ad) AND ad!="" AND ad!="-"
                        | stats count by {dIp} {dPort} ad
                        """, new LookupQuery(fwHedef[0], end.AddHours(-saat), end), cfg, splunk, ct, "Palo Alto (hedef adı / URL)",
                        TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Tls:UcSureSaniye", 180.0), 10, 900))));
                    lock (j) foreach (var m in r.Messages) if (!j.Mesajlar.Contains(m) && j.Mesajlar.Count < 30) j.Mesajlar.Add(m);
                    foreach (var row in r.Rows)
                    {
                        string? ip = row.GetValueOrDefault(dIp)?.Trim(), port = row.GetValueOrDefault(dPort)?.Trim() ?? "", ad = row.GetValueOrDefault("ad")?.Trim();
                        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(ad)) continue;
                        long n = long.TryParse(row.GetValueOrDefault("count"), out var c) ? c : 0;
                        // "host/yol" ya da "https://host:port/yol" → ad = host, yol ayrıca (endpoint)
                        var m2 = Regex.Match(ad, @"^(?:[a-z]+://)?([^/:?\s]+)(?::\d+)?(/[^?\s]*)?", RegexOptions.IgnoreCase);
                        if (!m2.Success) continue;
                        string host = m2.Groups[1].Value.TrimEnd('.').ToLowerInvariant();
                        if (_ipRx.IsMatch(host) || host.Length < 3) continue;
                        var key = (ip, port);
                        if (!fwAd.TryGetValue(key, out var l)) fwAd[key] = l = [];
                        l.Add((host, n));
                        if (m2.Groups[2].Success && m2.Groups[2].Value.Length > 1)
                        {
                            if (!fwUrl.TryGetValue(key, out var u)) fwUrl[key] = u = [];
                            u.Add((m2.Groups[2].Value, n));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { lock (j) if (j.Mesajlar.Count < 30) j.Mesajlar.Add("Palo Alto hedef adları alınamadı: " + ex.Message); }
        }
        List<(string Ad, long N)> FwAd(string ip, string? port) =>
            fwAd.Where(kv => kv.Key.Ip == ip && (port == null || kv.Key.Port == port || kv.Key.Port == "")).SelectMany(kv => kv.Value).ToList();

        return satirlar.Select(r =>
        {
            var fq = (r.Fqdn ?? []).ToList();
            void Ekle(string ad, string kaynak, long n)
            {
                int i = fq.FindIndex(x => x.Ad == ad);
                if (i >= 0) { if (!fq[i].Kaynaklar.Contains(kaynak)) fq[i] = fq[i] with { Kaynaklar = [.. fq[i].Kaynaklar, kaynak], Sayi = fq[i].Sayi + n }; }
                else fq.Add(new TlsFqdn(ad, [kaynak], n, 0));
            }
            List<TlsUye>? uyeler = null;
            bool https = r.Surumler.Count > 0 || r.Port is "443" or "8443" or "9443";
            if (r.Uyeler != null)
            {
                uyeler = r.Uyeler.Select(u =>
                {
                    var hs = ham.Where(x => x.Srv == u.Ip && (u.Port == null || x.Port == u.Port)).ToList();
                    long top = hs.Sum(x => x.Sayi);
                    var sur = hs.GroupBy(x => x.Surum).Select(g => (S: g.Key, N: g.Sum(x => x.Sayi))).OrderByDescending(x => x.N)
                        .Select(x => new TlsTopluSurum(x.S, x.N, top > 0 ? Math.Round(x.N * 100.0 / top, 1) : 0)).ToList();
                    var sni = hs.Select(x => x.Sni).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim().TrimEnd('.').ToLowerInvariant()).Distinct().Take(5).ToList();
                    var adlar = FwAd(u.Ip, u.Port).GroupBy(x => x.Ad).OrderByDescending(g => g.Sum(x => x.N)).Select(g => g.Key).Take(5).ToList();
                    foreach (var s in sni) Ekle(s, "SNI · dış bacak", hs.Where(x => x.Sni != null && x.Sni.Trim().TrimEnd('.').ToLowerInvariant() == s).Sum(x => x.Sayi));
                    foreach (var a in adlar) Ekle(a, "Firewall (Palo Alto)", 0);
                    if (sur.Count > 0) https = true;
                    return u with { Surumler = sur, Sni = sni, Adlar = adlar };
                }).ToList();
            }
            else if (r.Hedef != null && !Ozel(r.Hedef))
                foreach (var g in FwAd(r.Hedef, r.Port).GroupBy(x => x.Ad)) Ekle(g.Key, "Firewall (Palo Alto)", g.Sum(x => x.N));
            // Palo Alto URL yolları giden satırın endpoint'i olur
            var yollar = (r.Uyeler?.Select(u => (u.Ip, u.Port)) ?? (r.Hedef != null ? [(r.Hedef, (string?)r.Port)] : []))
                .SelectMany(k => fwUrl.Where(kv => kv.Key.Ip == k.Item1 && (k.Item2 == null || kv.Key.Port == k.Item2)).SelectMany(kv => kv.Value))
                .GroupBy(x => x.Yol).Select(g => new TlsUc(g.Key, g.Sum(x => x.N), 0, null, "Palo Alto")).OrderByDescending(x => x.Istek).Take(25).ToList();
            var fqS = fq.Count > 0 ? fq.OrderBy(x => x.Kaynaklar.Any(k => k.StartsWith("SNI")) ? 0 : x.Kaynaklar.Any(k => k.StartsWith("Firewall")) ? 1 : 2)
                .ThenByDescending(x => x.Sayi).Take(10).ToList() : null;
            string? url = r.Url ?? UrlYap(fqS?.FirstOrDefault()?.Ad, https, r.Port);
            // dış kurum adresi: dış bacaktaki (üye) ilk ad + üyenin portu; üyenin TLS'i görüldüyse https
            var du = uyeler?.FirstOrDefault(u => u.Sni.Count > 0 || u.Adlar.Count > 0);
            string? disUrl = du == null ? null : UrlYap(du.Sni.FirstOrDefault() ?? du.Adlar.First(), du.Surumler.Count > 0 || du.Port is "443" or "8443" or "9443", du.Port ?? r.Port);
            return r with { Fqdn = fqS, Uyeler = uyeler, Url = url, DisUrl = disUrl, Uclar = yollar.Count > 0 ? yollar : r.Uclar };
        }).ToList();
    }
}
