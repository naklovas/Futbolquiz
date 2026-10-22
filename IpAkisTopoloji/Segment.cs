using System.Collections.Concurrent;
using System.Text;

// ---------------------------------------------------------------------------
// Segment tarama (wwwroot/segment.html, ayrı sayfa): verilen segmentteki (segment adı ya da CIDR) tüm IP'lerin Carbon Black'te görülen
// gelen portları ve o portu kabul eden exe'ler — ERT'den bağımsız, "full Carbon Black" (kullanıcı isteği). ERT yalnız karşılaştırma için:
// port ERT'de kayıtlı / uygulama adı boş / hiç yok. Gelen bağlantısı olmayan exe'ler "yalnız giden" listesinde.
// Sorgu ağır olabilir (24 saat × /24): arka plan işi + durum sorgulama (IIS istek zaman aşımına takılmasın). Sorgu IP deseni
// (local_ip="10.1.2.*") ile daraltılır, cidrmatch ile kesinleştirilir; /24'ten büyük segmentler /24 parçalara bölünür.
// Ayarlar: Segment:MaxAdres (65536 = /16), Segment:MaxSaat (168), Segment:ParcaDesen (16 desen / sorgu), Segment:SonucDakika (60).
// ---------------------------------------------------------------------------
record SegmentOzet(string Ad, List<string> Cidrler, string? Kurum, int ErtSunucu);
record SegmentIstek(string? Segment, int? Saat, string? Etiket = null);
record SegmentSatir(string Ip, string? Sunucu, string? Segment, string Port, string Exe, string Yol, string? Urun, bool Genel, bool Altyapi, bool Dinamik,
    long Baglanti, int Istemci, List<string> Istemciler, List<string> ErtUygulama, string ErtDurum, string? Son);
record SegmentGiden(string Ip, string? Sunucu, string Exe, string Yol, string? Urun, bool Genel, long Baglanti, int Hedef, List<string> Hedefler);
record SegmentSunucu(string Ip, string? Sunucu, string? Segment, List<string> ErtUygulamalar, int ErtPort, int CbPort, bool CbVar);
record SegmentSonuc(string Tur, string Segment, List<string> Cidrler, int Saat, DateTimeOffset Baslangic, DateTimeOffset Bitis, List<SegmentSunucu> Sunucular,
    List<SegmentSatir> Satirlar, List<SegmentGiden> Giden, List<string> Mesajlar, long Ms);
record SegmentIs(string Id, string Segment, bool Calisiyor, string Asama, int Toplam, int Biten, string? Hata, string Kullanici,
    DateTimeOffset Basladi, SegmentSonuc? Sonuc);

sealed class SegmentTaramaService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ILogger<SegmentTaramaService> log)
{
    readonly ConcurrentDictionary<string, (SegmentIs Is, CancellationTokenSource Cts)> _isler = new();

    // Segment seçicisi: aynı adlı segmentler tek kayıt (proje kuralı), ERT'deki sunucu sayısıyla
    List<SegmentOzet>? _liste; EnvanterSnapshot? _listeEnv;
    public async Task<List<SegmentOzet>> ListeAsync(CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        if (ReferenceEquals(env, _listeEnv) && _liste != null) return _liste;
        var say = env.HostIps.Select(ip => env.FindSegment(ip)?.Label).OfType<string>()
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        _liste = env.Segments.GroupBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SegmentOzet(g.Key, g.Select(s => s.Cidr).Distinct().OrderBy(c => c).ToList(), g.Select(s => s.Domain).FirstOrDefault(d => d != null),
                say.GetValueOrDefault(g.Key)))
            .OrderByDescending(s => s.ErtSunucu).ThenBy(s => s.Ad, StringComparer.CurrentCultureIgnoreCase).ToList();
        _listeEnv = env;
        return _liste;
    }

    public SegmentIs? Durum(string id) => _isler.TryGetValue(id, out var x) ? x.Is : null;
    public void Durdur(string id) { if (_isler.TryGetValue(id, out var x)) x.Cts.Cancel(); }

    public async Task<SegmentIs> BaslatAsync(SegmentIstek req, string user, CancellationToken ct)
    {
        foreach (var (k, v) in _isler)   // eski sonuçları temizle
            if (!v.Is.Calisiyor && v.Is.Basladi < DateTimeOffset.Now.AddMinutes(-cfg.GetValue("Segment:SonucDakika", 60))) _isler.TryRemove(k, out _);
        if (_isler.Values.Count(v => v.Is.Calisiyor) >= Math.Max(1, cfg.GetValue("Segment:AyniAnda", 2)))
            throw new InvalidOperationException("Şu an çalışan segment taramaları var; biri bitince yeniden deneyin.");
        var env = await envanter.GetAsync(false, ct);
        int saat = Math.Clamp(req.Saat ?? 24, 1, cfg.GetValue("Segment:MaxSaat", 168));
        string ad; List<string> cidrs = []; HashSet<string>? ipler = null;
        List<(string Arama, string? Kosul, string Etiket)> sorgular;
        if (!string.IsNullOrWhiteSpace(req.Etiket))
        {
            // Etiket (kullanıcı: "segment değil kayıtlı etiketler üzerinden de"): etiketli sunucular (ana etiket alt etiketleri de kapsar),
            // TERM(ip) + local_ip IN (...) ile Segment:ParcaIp (40) IP'lik sorgular
            ad = req.Etiket.Trim();
            var list = env.TagIps(ad).Where(ip => LookupQuery.TryParseIpv4(ip, out _)).Distinct().ToList();
            if (list.Count == 0) throw new InvalidOperationException($"Bu etikette sunucu yok: {ad}");
            int maxIp = cfg.GetValue("Segment:EtiketMaxIp", 2000);
            if (list.Count > maxIp) throw new InvalidOperationException($"Etikette {list.Count:N0} sunucu var; en çok {maxIp:N0} (Segment:EtiketMaxIp).");
            ipler = list.ToHashSet();
            sorgular = list.Chunk(Math.Clamp(cfg.GetValue("Segment:ParcaIp", 40), 1, 200)).Select(c => (
                $"({string.Join(" OR ", c.Select(ip => $"TERM({ip})"))}) local_ip IN ({string.Join(",", c.Select(ip => $"\"{ip}\""))})",
                (string?)null, $"{c[0]} … ({c.Length} IP)")).ToList();
        }
        else
        {
            (ad, cidrs) = Coz(env, req.Segment);
            long adres = cidrs.Sum(c => { EnvanterSnapshot.TryParseCidr(c, out _, out int p); return 1L << (32 - p); });
            long max = cfg.GetValue("Segment:MaxAdres", 65536L);
            if (adres > max) throw new InvalidOperationException($"Segment çok büyük ({adres:N0} adres; en çok {max:N0} — Segment:MaxAdres). Daha küçük bir CIDR girin.");
            sorgular = Desenler(cidrs).Chunk(Math.Clamp(cfg.GetValue("Segment:ParcaDesen", 16), 1, 256)).Select(g => (
                $"({string.Join(" OR ", g.Select(x => $"local_ip=\"{x.Desen}\""))})",
                (string?)string.Join(" OR ", g.SelectMany(x => x.Cidrler).Distinct().Select(c => $"cidrmatch(\"{c}\", local_ip)")),
                string.Join(", ", g.Select(x => x.Desen)))).ToList();
        }
        var id = Guid.NewGuid().ToString("N")[..12];
        var cts = new CancellationTokenSource();
        string tur = ipler != null ? "etiket" : "segment";
        _isler[id] = (new SegmentIs(id, (ipler != null ? "Etiket " : "") + ad, true, "Carbon Black sorgulanıyor", sorgular.Count, 0, null, user, DateTimeOffset.Now, null), cts);
        log.LogInformation("Segment tarama: {User} {Tur} {Ad} ({Cidr}), {Saat} saat, {Parca} sorgu", user, tur, ad, string.Join(",", cidrs), saat, sorgular.Count);
        _ = Task.Run(async () =>
        {
            try
            {
                var sonuc = await TaraAsync(id, env, tur, ad, cidrs, ipler, sorgular, saat, cts.Token);
                Set(id, i => i with { Calisiyor = false, Asama = "Bitti", Sonuc = sonuc });
            }
            catch (OperationCanceledException) { Set(id, i => i with { Calisiyor = false, Asama = "Durduruldu" }); }
            catch (Exception ex)
            {
                log.LogError(ex, "Segment tarama başarısız: {Segment}", ad);
                Set(id, i => i with { Calisiyor = false, Asama = "Hata", Hata = ex.Message });
            }
        });
        return _isler[id].Is;
    }

    void Set(string id, Func<SegmentIs, SegmentIs> f)
    {
        if (_isler.TryGetValue(id, out var x)) _isler[id] = (f(x.Is), x.Cts);
    }

    // Girdi CIDR ise o; değilse aynı adlı segmentlerin CIDR'ları
    static (string Ad, List<string> Cidrler) Coz(EnvanterSnapshot env, string? girdi)
    {
        string g = (girdi ?? "").Trim();
        if (g.Length == 0) throw new InvalidOperationException("Segment adı ya da CIDR girin.");
        if (g.Contains('/') && EnvanterSnapshot.TryParseCidr(g, out uint net, out int p))
        {
            string cidr = $"{net >> 24}.{(net >> 16) & 255}.{(net >> 8) & 255}.{net & 255}/{p}";
            return (env.Segments.FirstOrDefault(s => s.Cidr == cidr)?.Label ?? cidr, [cidr]);
        }
        var cidrs = env.Segments.Where(s => string.Equals(s.Label, g, StringComparison.OrdinalIgnoreCase)).Select(s => s.Cidr)
            .Where(c => EnvanterSnapshot.TryParseCidr(c, out _, out _)).Distinct().ToList();
        if (cidrs.Count == 0) throw new InvalidOperationException($"Segment bulunamadı: {g} (segment adı ya da 10.1.2.0/24 gibi CIDR girin).");
        return (env.Segments.First(s => string.Equals(s.Label, g, StringComparison.OrdinalIgnoreCase)).Label, cidrs);
    }

    // Splunk arama deseni: /24 ve küçüğü → a.b.c.*; /16–/23 → /24 parçalar (16'dan çoksa a.b.*)
    static List<(string Desen, List<string> Cidrler)> Desenler(List<string> cidrs)
    {
        var d = new Dictionary<string, List<string>>();
        void Add(string k, string c) { if (!d.TryGetValue(k, out var l)) d[k] = l = []; if (!l.Contains(c)) l.Add(c); }
        foreach (var c in cidrs)
        {
            EnvanterSnapshot.TryParseCidr(c, out uint net, out int p);
            if (p >= 24) Add($"{net >> 24}.{(net >> 16) & 255}.{(net >> 8) & 255}.*", c);
            else if (p >= 16 && (1 << (24 - p)) <= 16)
                for (uint i = 0; i < (1u << (24 - p)); i++) { uint n = net + (i << 8); Add($"{n >> 24}.{(n >> 16) & 255}.{(n >> 8) & 255}.*", c); }
            else if (p >= 16) Add($"{net >> 24}.{(net >> 16) & 255}.*", c);
            else for (uint i = 0; i < (1u << (16 - p)); i++) { uint n = net + (i << 16); Add($"{n >> 24}.{(n >> 16) & 255}.*", c); }
        }
        return d.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    // Ekranda teknoloji etiketi (yalnız bu sayfa; ERT eksikleri bunları uygulama adı saymaz): veritabanı / web sunucusu süreçleri
    static readonly (string Exe, string Ad)[] Teknolojiler = [("sqlservr", "SQL Server"), ("tnslsnr", "Oracle"), ("oracle", "Oracle"),
        ("postgres", "PostgreSQL"), ("mysqld", "MySQL"), ("mariadbd", "MariaDB"), ("db2sysc", "DB2"), ("mongod", "MongoDB"), ("redis-server", "Redis"),
        ("w3wp", "IIS"), ("httpd", "Apache"), ("nginx", "nginx"), ("ntoskrnl", "HTTP.sys")];
    static string? Urun(string yol, string exe) => EksikService.BilinenUrun(yol)
        ?? Teknolojiler.FirstOrDefault(t => exe.StartsWith(t.Exe, StringComparison.OrdinalIgnoreCase)).Ad;

    static bool InCidrs(string ip, List<(uint Net, int P)> cs) =>
        EnvanterSnapshot.TryToUInt(ip, out uint a) && cs.Any(c => (a & EnvanterSnapshot.Mask(c.P)) == c.Net);

    async Task<SegmentSonuc> TaraAsync(string id, EnvanterSnapshot env, string tur, string ad, List<string> cidrs, HashSet<string>? ipler,
        List<(string Arama, string? Kosul, string Etiket)> sorgular, int saat, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        var bas = DateTimeOffset.Now.AddHours(-saat); var bit = DateTimeOffset.Now;
        var cs = cidrs.Select(c => { EnvanterSnapshot.TryParseCidr(c, out uint n, out int p); return (n, p); }).ToList();
        var mesaj = new List<string>();
        var rows = new List<Dictionary<string, string>>();
        bool Icinde(string ip) => ipler?.Contains(ip) ?? InCidrs(ip, cs);
        foreach (var (arama, kosul, etiket) in sorgular)
        {
            ct.ThrowIfCancellationRequested();
            string spl = $"""
                search index={index} sourcetype="{sourcetype}" {arama}
                {(kosul != null ? "| where " + kosul : "")}
                | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, process_name, "-"),
                       port=if(yon=="out", "", local_port)
                | stats count dc(remote_ip) as peers values(remote_ip) as ornek max(_time) as son by local_ip yon port yol
                | eval ornek=mvjoin(mvindex(ornek, 0, 9), " ")
                """;
            try
            {
                var res = await SplunkService.ExportAsync(spl, new LookupQuery(ipler?.First() ?? cidrs[0].Split('/')[0], bas, bit), cfg, factory.CreateClient("splunk"), ct, "Splunk");
                rows.AddRange(res.Rows);
                mesaj.AddRange(res.Messages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                mesaj.Add($"{etiket}: {ex.Message}");
            }
            Set(id, i => i with { Biten = i.Biten + 1, Asama = $"Carbon Black sorgulanıyor ({i.Biten + 1} / {i.Toplam})" });
        }

        var infra = AppTopology.InfraPorts(cfg);
        int dyn = cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);
        string? Host(string ip) => env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault();
        string Ad(string ip) => env.AppNamesOnIp(ip).FirstOrDefault() is { } a ? $"{ip} ({a})" : env.IsVipGw(ip, out _) ? $"{ip} (LB GW)" : ip;
        static string Exe(string yol) => yol.Split('\\', '/').Last();
        string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        string? Zaman(string? t) => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var u)
            ? DateTimeOffset.FromUnixTimeSeconds((long)u).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : t;
        List<string> Ornek(Dictionary<string, string> r, string ip) => (V(r, "ornek") ?? "").Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x != ip && x != "127.0.0.1").ToList();

        var satir = new List<SegmentSatir>(); var giden = new List<SegmentGiden>();
        foreach (var r in rows)
        {
            if (V(r, "local_ip") is not { } ip || !Icinde(ip)) continue;
            string yol = V(r, "yol") ?? "-", exe = Exe(yol);
            long n = long.TryParse(V(r, "count"), out var c) ? c : 1; int peers = int.TryParse(V(r, "peers"), out var pe) ? pe : 0;
            var ornek = Ornek(r, ip);
            if (V(r, "yon") == "out") { giden.Add(new(ip, Host(ip), exe, yol, Urun(yol, exe), SurecAi.GenelExe(exe, yol), n, peers, ornek.Take(5).Select(Ad).ToList())); continue; }
            string port = V(r, "port") ?? "";
            var ert = env.AppsOnIp(ip).Where(h => h.Port == port).ToList();
            var ertAd = ert.Select(h => h.AppName).OfType<string>().Distinct().ToList();
            string durum = ert.Count == 0 ? "yok" : ertAd.Count == 0 ? "bos" : "kayitli";
            satir.Add(new(ip, Host(ip), env.FindSegment(ip)?.Label, port, exe, yol, Urun(yol, exe), SurecAi.GenelExe(exe, yol), infra.Contains(port),
                int.TryParse(port, out int pn) && pn >= dyn, n, peers, ornek.Take(6).Select(Ad).ToList(), ertAd, durum, Zaman(V(r, "son"))));
        }
        // aynı IP + exe'de gelen satırı olan giden kayıtları "yalnız giden" listesinden çıkar; giden aynı exe birleştirilir
        var gelenExe = satir.Select(x => (x.Ip, x.Yol)).ToHashSet();
        giden = giden.Where(g => !gelenExe.Contains((g.Ip, g.Yol))).GroupBy(g => (g.Ip, g.Yol))
            .Select(g => g.First() with { Baglanti = g.Sum(x => x.Baglanti), Hedef = g.Max(x => x.Hedef) }).ToList();

        var tum = (ipler ?? env.HostIps.Where(ip => InCidrs(ip, cs))).Concat(satir.Select(x => x.Ip)).Concat(giden.Select(x => x.Ip)).Distinct().ToList();
        var sunucular = tum.Select(ip => new SegmentSunucu(ip, Host(ip), env.FindSegment(ip)?.Label,
                env.AppNamesOnIp(ip).Where(a => !env.IsSystemApp(a)).Take(6).ToList(),
                env.AppsOnIp(ip).Select(h => h.Port).OfType<string>().Distinct().Count(),
                satir.Where(x => x.Ip == ip).Select(x => x.Port).Distinct().Count(),
                satir.Any(x => x.Ip == ip) || giden.Any(x => x.Ip == ip)))
            .OrderBy(x => EnvanterSnapshot.TryToUInt(x.Ip, out uint u) ? u : 0).ToList();
        return new SegmentSonuc(tur, ad, cidrs, saat, bas, bit, sunucular,
            satir.OrderBy(x => EnvanterSnapshot.TryToUInt(x.Ip, out uint u) ? u : 0).ThenBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ToList(),
            giden.OrderBy(x => EnvanterSnapshot.TryToUInt(x.Ip, out uint u) ? u : 0).ThenByDescending(x => x.Baglanti).ToList(),
            mesaj.Distinct().Take(20).ToList(), sw.ElapsedMilliseconds);
    }
}
