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

    public static async Task<TlsSonuc> RunAsync(string ip, int saat, string? port, IConfiguration cfg, HttpClient http, EnvanterSnapshot? env, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t = cfg.GetSection("Tls");
        string kaynak = t["Kaynak"] ?? "tls_handshakes";
        string cliIp = t["IstemciIp"] ?? "cli_tcp.ip", srvIp = t["SunucuIp"] ?? "srv_tcp.ip", srvPort = t["SunucuPort"] ?? "srv_tcp.port";
        string surum = t["Surum"] ?? "tls.version", surumAd = t["SurumAd"] ?? "tls.version_name", metrik = t["Metrik"] ?? "sum_tls.session_attempted";
        string sni = t["Sni"] ?? "";   // boş: SNI istenmez (gruplamayı bozabilir)
        int bekle = t.GetValue("BeklemeSaniye", 300), cacheDk = t.GetValue("CacheDakika", 30);
        saat = Math.Clamp(saat, 1, t.GetValue("MaxSaat", 24));

        // aralık: tam dakikaya yuvarlanır (önbellek tutsun)
        var bit = DateTime.UtcNow; bit = new DateTime(bit.Year, bit.Month, bit.Day, bit.Hour, bit.Minute, 0, DateTimeKind.Utc);
        var bas = bit.AddHours(-saat);
        var portlar = (port ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => int.TryParse(p, out _)).ToList();
        string key = $"{ip}|{saat}|{string.Join(",", portlar)}";
        if (_cache.TryGetValue(key, out var c) && (DateTime.UtcNow - c.At).TotalMinutes < cacheDk) return c.S;

        string PortF(string k) => portlar.Count > 0 ? " and (" + string.Join(" or ", portlar.Select(p => $"{k} == {p}")) + ")" : "";
        string filtre = $"({srvIp} == {ip}{PortF(srvPort)}) or ({cliIp} == {ip}{PortF(srvPort)})";
        string pathType = cfg["SourcePathType"] ?? "jobs";
        var vifgler = cfg.GetSection("VifgIds").GetChildren().Select(x => (x.Value ?? "").Trim()).Where(x => x != "").ToList();
        if (vifgler.Count == 0) vifgler.Add("");
        bool sil = cfg.GetValue("DeleteReportInstances", true);
        long sBas = new DateTimeOffset(bas).ToUnixTimeSeconds(), sBit = new DateTimeOffset(bit).ToUnixTimeSeconds();

        // denemeler: kolonlar (istemci, sunucu, port, sürüm[, sni][, metrik]) + group_by
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
        var mesajlar = new List<string>();
        var sonuc = await Task.WhenAll(names.Select(async n =>
        {
            var satirlar = new List<(string Cli, string Srv, string Port, string Surum, string? Sni, long Sayi)>();
            string? yontem = null, hata = null;
            try
            {
                var (_, baseUrl, jwt) = await AppResponseService.ConnectAsync(n.I, cfg, http, ct);
                foreach (var d in denemeler)
                {
                    try
                    {
                        var tmp = new List<(string, string, string, string, string?, long)>();
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
                                tmp.Add((Ip(C(0)), Ip(C(1)), C(2), SurumAd(C(3)), sni != "" ? C(4) : null, sayi));
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
        foreach (var s in sonuc.Where(s => s.yontem == null)) mesajlar.Add($"[{s.Ad}] {Kisalt(s.hata, 300)}");
        bool kesik = sonuc.Any(s => s.yontem == "ham satırlar" && s.satirlar.Count >= 100000);
        if (kesik) mesajlar.Add("Bazı kutularda 100.000 satır sınırına ulaşıldı (gruplama olmadı): sayılar eksik olabilir, port süzün ya da aralığı kısaltın.");
        if (sonuc.Any(s => s.yontem == "ham satırlar")) mesajlar.Add("Gruplu rapor alınamadı, ham satırlar sayıldı (Tls:Surum / Tls:Metrik ayarlarını kontrol edin).");

        string? Uyg(string karsi) => env == null ? null : env.AppNamesOnIp(karsi).FirstOrDefault(a => !env.IsSystemApp(a))
            ?? (env.IsVipGw(karsi, out _) ? "LB GW" : env.FindSegment(karsi)?.Label);
        var tum = sonuc.SelectMany(s => s.satirlar.Select(r => (Cihaz: s.Ad, R: r))).ToList();
        List<TlsSatir> Yap(bool gelen)
        {
            var sec = tum.Where(x => gelen ? AyniIp(x.R.Srv, ip) : AyniIp(x.R.Cli, ip) && !AyniIp(x.R.Srv, ip)).ToList();
            return sec.GroupBy(x => gelen ? (Karsi: "", x.R.Port, x.R.Surum) : (Karsi: x.R.Srv, x.R.Port, x.R.Surum))
                .Select(g =>
                {
                    var karsilar = g.GroupBy(x => gelen ? x.R.Cli : x.R.Srv).Select(k => new TlsKarsi(k.Key, k.Sum(x => x.R.Sayi), Uyg(k.Key)))
                        .OrderByDescending(k => k.Sayi).ToList();
                    return new TlsSatir(gelen ? "gelen" : "giden", g.Key.Karsi, g.Key.Port, g.Key.Surum, g.Sum(x => x.R.Sayi), karsilar.Count,
                        karsilar.Take(50).ToList(), g.Select(x => x.Cihaz).Distinct().ToList(),
                        g.Select(x => x.R.Sni).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(10).ToList()!);
                })
                .OrderBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ThenBy(x => x.Karsi).ThenByDescending(x => x.Sayi).ToList();
        }
        var yontemler = sonuc.Select(s => s.yontem).OfType<string>().Distinct().ToList();
        var r = new TlsSonuc(ip, bas, bit, yontemler.Count > 0 ? string.Join(", ", yontemler) : null, Yap(true), Yap(false), cihazlar, mesajlar, kesik, sw.ElapsedMilliseconds);
        if (yontemler.Count > 0) _cache[key] = (DateTime.UtcNow, r);
        if (yontemler.Count == 0) throw new InvalidOperationException("Hiçbir AppResponse kutusundan TLS verisi alınamadı: " + string.Join(" | ", mesajlar));
        return r;
    }

    static string Kisalt(string? s, int n) => s == null ? "" : s.Length <= n ? s : s[..n] + "…";
}
