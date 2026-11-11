using System.Text;

// ---------------------------------------------------------------------------
// HTTP.sys port → asıl exe tahmini (kullanıcı: "ntoskrnl olayı: exe gerçekte hangi portta çalışıyor bilememek çok sıkıntı").
// HTTP.sys kullanan uygulamalarda (IIS, HttpSys'li ASP.NET Core, WCF) gelen bağlantıyı çekirdek kabul eder; Carbon Black portun sahibini
// ntoskrnl.exe / System görür. Ek veri olmadan zaman korelasyonu: sunucunun Carbon Black olayları dakika dakika sayılır —
// ntoskrnl'nin porta aldığı gelen bağlantılar ve her exe'nin giden bağlantıları. İstek alan uygulama çoğu zaman hemen arkasından
// DB'ye / başka servise gittiği için gelen trafiğe en çok eşlik eden exe portun sahibi olma adayıdır. KESİN DEĞİL: hiç dışarı çıkmayan
// uygulamayı yakalamaz, aynı anda çalışan uygulamalar karışabilir. Kesin eşleme sunucuda `netsh http show servicestate view=requestq`.
// ---------------------------------------------------------------------------
record HttpSysAday(string Exe, string Yol, double R, int Eslik, int OrtakDakika, bool EsikAlti = false, int Istemci = 0);   // Istemci: aynı istemci + dakikada exe'nin "gelen port 0" olayı (en güçlü)   // EsikAlti: eşik altı, yalnız en yakın exe (aday yokken)
record HttpSysPort(string Port, int Dakika, long Baglanti, List<HttpSysAday> Adaylar, bool Zayif = false);   // Zayif: HttpSys:MinDakika'dan az dakikada trafik — aday zayıf ipucu
record HttpSysSonuc(string Ip, DateTimeOffset Baslangic, DateTimeOffset Bitis, int ToplamDakika, List<HttpSysPort> Portlar, string? Hata,
    List<HttpSysGiden>? Giden = null, List<string>? GenelAtilan = null);   // tanı: dışarı bağlanan iş exe'leri (dakika), ayıklanan genel süreçler
record HttpSysGiden(string Exe, string Yol, int Dakika, long Baglanti);
record HttpSysIisUyg(string Ad, long Istek);
record HttpSysIis(string Port, List<string> Siteler, List<string> Hostlar, long Istek, int Istemci, List<HttpSysIisUyg> Uygulamalar);
record HttpSysHavuz(string Ad, int? Pid, bool Bit32, DateTimeOffset? Baslangic, string? Guid, string? Link, string Bilgisayar, int Baslatma = 1);
record HttpSysSurec(string Guid, int? Pid, string Exe, string Yol, List<string> Domainler, string? Link, long Olay,
    string? KomutSatiri = null, string? Havuz = null, string? UstGuid = null, string? UstYol = null, int? UstPid = null, string? UstHavuz = null);

static class HttpSysEslesme
{
    // w3wp.exe (IIS uygulama havuzu) \windows\system32\inetsrv altında olduğu için genel sayılıyordu; HTTP.sys'in arkasındaki asıl uygulama o
    public static bool Genel(string ad, string yol) => !ad.Equals("w3wp.exe", StringComparison.OrdinalIgnoreCase) && SurecAi.GenelExe(ad, yol);
    static string Ad(string yol) => yol.Replace('/', '\\').Split('\\').Last();

    public static bool Cekirdek(string? yol)
    {
        string e = (yol ?? "").Replace('/', '\\').Split('\\').Last().Trim().ToLowerInvariant();
        return e is "ntoskrnl.exe" or "system" or "ntoskrnl";
    }

    // Korelasyon penceresi (kullanıcı: "2 dk trafik — çok az"): IP kartının 3 saatlik süreç penceresi seyrek portlarda yetmiyor →
    // istenen aralıktan bağımsız son HttpSys:Saat (varsayılan 72) saat, şimdiye kadar
    public static (DateTimeOffset Start, DateTimeOffset End) Aralik(IConfiguration cfg)
    {
        double saat = Math.Clamp(cfg.GetValue("HttpSys:Saat", 72.0), 1, 24 * 14);
        var end = DateTimeOffset.Now;
        return (end.AddHours(-saat), end);
    }

    public static async Task<HttpSysSonuc> HesaplaAsync(string ip, DateTimeOffset start, DateTimeOffset end, IConfiguration cfg, HttpClient http,
        CancellationToken ct, ICollection<string>? portlar = null)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        // gelen: yalnız çekirdeğin kabul ettikleri (port başına); giden: süreç başına. Dakika = floor(_time / 60)
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" TERM({ip}) local_ip="{ip}"
            | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, "-"), ly=lower(yol), lp=coalesce(local_port, "0")
            | eval pr=coalesce(Protocol, protocol, "")
            | where yon=="out" OR like(ly, "%ntoskrnl.exe") OR ly=="system" OR ly=="ntoskrnl" OR ((lp=="0" OR lp=="") AND pr=="6")
            | eval port=if(yon=="in", if(lp=="", "0", lp), ""), rip=if(yon=="in", remote_ip, ""), t=floor(_time/60)
            | stats count by t yon port yol rip
            """;
        int toplam = Math.Max(1, (int)Math.Ceiling((end - start).TotalMinutes));
        SplunkResult res;
        try { res = await SplunkService.ExportAsync(spl, new LookupQuery(ip, start, end), cfg, http, ct, "Splunk"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(ip, start, end, toplam, [], ex.Message); }

        var gelen = new Dictionary<string, Dictionary<long, long>>();   // port → dakika → bağlantı
        var giden = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase);   // exe yolu → dakika → bağlantı
        var genelAtilan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gelenIst = new Dictionary<string, HashSet<(string Ip, long T)>>();   // çekirdek portu → (istemci, dakika)
        // "gelen, local_port 0" olayları: canlıda bunların Protocol=1 (ICMP / ping) olduğu görüldü — HTTP isteği DEĞİL; yalnız TCP (6) olanlar sayılır
        var sifir = new Dictionary<string, HashSet<(string Ip, long T)>>(StringComparer.OrdinalIgnoreCase);   // exe yolu → (istemci, dakika)
        foreach (var r in res.Rows)
        {
            string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if (!long.TryParse(V("t"), out long t) || !long.TryParse(V("count"), out long n)) continue;
            string yol = V("yol") ?? "-";
            string? rip = V("rip");
            if (V("yon") == "in")
            {
                if (Cekirdek(yol))
                {
                    if (V("port") is not { } p || p == "0" || (portlar != null && !portlar.Contains(p))) continue;
                    if (!gelen.TryGetValue(p, out var d)) gelen[p] = d = [];
                    d[t] = d.GetValueOrDefault(t) + n;
                    if (rip != null) (gelenIst.TryGetValue(p, out var gi) ? gi : gelenIst[p] = []).Add((rip, t));
                }
                else if (yol != "-" && rip != null && (V("port") ?? "0") == "0")
                {
                    if (Genel(Ad(yol), yol)) { genelAtilan.Add(Ad(yol)); continue; }
                    (sifir.TryGetValue(yol, out var si) ? si : sifir[yol] = []).Add((rip, t));
                }
            }
            else
            {
                if (yol == "-" || Cekirdek(yol)) continue;
                if (Genel(Ad(yol), yol)) { genelAtilan.Add(Ad(yol)); continue; }   // svchost, ajanlar, izleme… (sürekli çalışır, eşlik sahte çıkar)
                if (!giden.TryGetValue(yol, out var d)) giden[yol] = d = [];
                d[t] = d.GetValueOrDefault(t) + n;
            }
        }

        int minDakika = cfg.GetValue("HttpSys:MinDakika", 5);
        double minR = cfg.GetValue("HttpSys:MinKorelasyon", 0.3);
        var outp = new List<HttpSysPort>();
        foreach (var (port, x) in gelen.OrderByDescending(kv => kv.Value.Values.Sum()))
        {
            var adaylar = new List<HttpSysAday>();
            // 1) AYNI İSTEMCİ: portun (istemci, dakika) olaylarından kaçını exe'nin "gelen port 0" olayı (aynı istemci, ±1 dk) karşılıyor
            var ist = new List<HttpSysAday>();
            if (gelenIst.TryGetValue(port, out var K) && K.Count > 0)
                foreach (var (yol, P) in sifir)
                {
                    int es = K.Count(k => P.Contains(k) || P.Contains((k.Ip, k.T - 1)) || P.Contains((k.Ip, k.T + 1)));
                    if (es == 0) continue;
                    var y = giden.GetValueOrDefault(yol);
                    ist.Add(new(Ad(yol), yol, y != null ? Math.Round(Pearson(x, y, toplam), 2) : 0, (int)Math.Round(100.0 * es / K.Count), es, false, es));
                }
            // az trafikte de aday aranır ama "zayıf" işaretlenir (kullanıcı: "aday yok" yerine en olası exe görünsün)
            bool zayif = x.Count < minDakika;
            int minOrtak = Math.Min(2, x.Count);
            foreach (var (yol, y) in giden)
                {
                    double r = Pearson(x, y, toplam);
                    int ortak = x.Keys.Count(y.ContainsKey);
                    if (r < minR || ortak < minOrtak) continue;
                    adaylar.Add(new(yol.Replace('/', '\\').Split('\\').Last(), yol, Math.Round(r, 2), (int)Math.Round(100.0 * ortak / x.Count), ortak));
                }
            // eşiği geçen yoksa (kullanıcı: "hiçbir şey bulamadı") aynı dakikalarda dışarı bağlanan en yakın exe'ler eşik altı olarak gösterilir
            if (adaylar.Count == 0)
                foreach (var (yol, y) in giden)
                {
                    int ortak = x.Keys.Count(y.ContainsKey);
                    if (ortak == 0) continue;
                    adaylar.Add(new(yol.Replace('/', '\\').Split('\\').Last(), yol, Math.Round(Pearson(x, y, toplam), 2), (int)Math.Round(100.0 * ortak / x.Count), ortak, true));
                }
            var liste = ist.OrderByDescending(a => a.Istemci).ThenByDescending(a => a.R).Take(3).ToList();
            liste.AddRange(adaylar.Where(a => !liste.Any(l => l.Yol.Equals(a.Yol, StringComparison.OrdinalIgnoreCase)) && (liste.Count == 0 || !a.EsikAlti))
                .OrderByDescending(a => a.R).ThenByDescending(a => a.Eslik).Take(Math.Max(0, 3 - liste.Count)));
            outp.Add(new(port, x.Count, x.Values.Sum(), liste, zayif));
        }
        var gidenOzet = giden.Select(kv => new HttpSysGiden(kv.Key.Replace('/', '\\').Split('\\').Last(), kv.Key, kv.Value.Count, kv.Value.Values.Sum()))
            .OrderByDescending(g => g.Dakika).Take(15).ToList();
        return new(ip, start, end, toplam, outp, null, gidenOzet, genelAtilan.Order().Take(30).ToList());
    }

    // IIS HAVUZLARI (kullanıcı: "w3wp.exe benim işimi görmüyor"): CB netconn olaylarında cmdline / parent yok ama process_guid, domain ve link_process var;
    // ingress.event.procstart olayında cmdline (w3wp -ap "HavuzAdi") ve üst süreç (parent_guid / parent_path / parent_pid) bulunur.
    // 1) Bu sunucunun netconn olaylarından süreç başına (process_guid): pid, yol, gittiği domain'ler, CB bağlantısı
    // 2) Bu guid'lerin procstart olayı (son HttpSys:SurecGun = 7 gün; havuzlar ~29 saatte bir yeniden başlar): komut satırı, üst süreç → havuz adı; alt süreç üstü w3wp ise o havuz
    public static async Task<List<HttpSysSurec>> SureclerAsync(string ip, DateTimeOffset start, DateTimeOffset end, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string splA = $"""
            search index={index} sourcetype="{sourcetype}" TERM({ip}) local_ip="{ip}" process_guid=*
            | eval yol=coalesce(process_path, "-")
            | stats count latest(pid) as pid latest(yol) as yol values(domain) as domain latest(link_process) as link by process_guid
            """;
        var resA = await SplunkService.ExportAsync(splA, new LookupQuery(ip, start, end), cfg, http, ct, "Splunk");
        var liste = new Dictionary<string, HttpSysSurec>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in resA.Rows)
        {
            string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if (V("process_guid") is not { } g || V("yol") is not { } yol || Cekirdek(yol)) continue;
            if (Genel(Ad(yol), yol)) continue;
            var dom = (V("domain") ?? "").Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().Take(10).ToList();
            liste[g] = new(g, int.TryParse(V("pid"), out int pid) ? pid : null, Ad(yol), yol, dom, V("link"), long.TryParse(V("count"), out long n) ? n : 0);
        }
        if (liste.Count == 0) return [];
        int gun = cfg.GetValue("HttpSys:SurecGun", 7);
        // Canlı alanlar: command_line, username ("IIS APPPOOL\\Havuz"), parent_path, parent_pid; parent_guid SAYISAL (guid biçiminde değil) →
        // üst sürecin guid'i link_parent içinden (…/#analyze/<guid>/<segment>)
        async Task<Dictionary<string, (string? Cmd, string? Kullanici, string? UstGuid, string? UstYol, int? UstPid)>> ProcAsync(IEnumerable<string> guidler)
        {
            var d = new Dictionary<string, (string?, string?, string?, string?, int?)>(StringComparer.OrdinalIgnoreCase);
            foreach (var parca in guidler.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(40))
            {
                string splB = $"""
                    search index={index} sourcetype="{sourcetype}" type="ingress.event.procstart" ({string.Join(" OR ", parca.Select(g => $"TERM({g})"))}) ({string.Join(" OR ", parca.Select(g => $"process_guid=\"{g}\""))})
                    | eval cmd=coalesce(command_line, cmdline, process_cmdline, "")
                    | stats latest(cmd) as cmd latest(username) as un latest(link_parent) as lp latest(parent_path) as uy latest(parent_pid) as up by process_guid
                    """;
                SplunkResult resB;
                try { resB = await SplunkService.ExportAsync(splB, new LookupQuery(ip, end.AddDays(-gun), end), cfg, http, ct, "Splunk"); }
                catch (Exception ex) when (ex is not OperationCanceledException) { break; }
                foreach (var r in resB.Rows)
                {
                    string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
                    var m = System.Text.RegularExpressions.Regex.Match(V("lp") ?? "", @"analyze/([0-9a-fA-F-]{20,})");
                    if (V("process_guid") is { } g) d[g] = (V("cmd"), V("un"), m.Success ? m.Groups[1].Value : null, V("uy"), int.TryParse(V("up"), out int up) ? up : null);
                }
            }
            return d;
        }
        var ust = await ProcAsync(liste.Keys);
        foreach (var g in liste.Keys.ToList())
            if (ust.TryGetValue(g, out var u))
                liste[g] = liste[g] with { KomutSatiri = u.Cmd, Havuz = HavuzAdi(u.Cmd) ?? HavuzKullanici(u.Kullanici), UstGuid = u.UstGuid, UstYol = u.UstYol, UstPid = u.UstPid };
        var havuzByGuid = liste.Values.Where(x => x.Havuz != null && x.Exe.Equals("w3wp.exe", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Guid, x => x.Havuz!, StringComparer.OrdinalIgnoreCase);
        // üstü w3wp olup o w3wp'nin bu aralıkta ağ olayı olmayanlar: üst sürecin procstart'ı ayrıca okunur
        var eksikUst = liste.Values.Where(x => x.UstGuid != null && (x.UstYol ?? "").Contains("w3wp", StringComparison.OrdinalIgnoreCase) && !havuzByGuid.ContainsKey(x.UstGuid))
            .Select(x => x.UstGuid!).ToList();
        if (eksikUst.Count > 0)
            foreach (var (g, u) in await ProcAsync(eksikUst))
                if ((HavuzAdi(u.Cmd) ?? HavuzKullanici(u.Kullanici)) is { } h) havuzByGuid[g] = h;
        foreach (var g in liste.Keys.ToList())
        {
            var x = liste[g];
            if (x.Havuz == null && x.UstGuid != null && havuzByGuid.TryGetValue(x.UstGuid, out var h)) liste[g] = x with { UstHavuz = h };
        }
        return liste.Values.OrderBy(x => x.Exe).ThenBy(x => x.Pid).ToList();
    }

    // username "IIS APPPOOL\Havuz" → Havuz (havuz kimliğiyle çalışan her süreçte)
    public static string? HavuzKullanici(string? un) =>
        un != null && un.StartsWith(@"IIS APPPOOL\", StringComparison.OrdinalIgnoreCase) ? un[12..].Trim() : null;

    // SUNUCUDAKİ TÜM IIS HAVUZLARI (kullanıcı: "havuz isimlerini getir, arattığımız yerde faydası olur"): sorgu penceresinde ağ olayı olmasa da
    // son HttpSys:SurecGun günde başlamış w3wp'ler. procstart'ta IP yok → önce bu IP'nin computer_name'i (netconn), sonra o bilgisayarın w3wp başlangıçları.
    public static async Task<List<HttpSysHavuz>> HavuzlarAsync(string ip, IConfiguration cfg, HttpClient http, CancellationToken ct) =>
        (await HavuzlarBilgiAsync(ip, cfg, http, ct)).Liste;
    // IP → Carbon Black bilgisayar adı (procstart'ta IP yok). Tek olaydan değil, en sık geçen ad (kullanıcı: IP'li alt sorgu 3 satır, hostname'li tam liste getirdi)
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, List<(string Ad, long Sayi)> L)> _pcOnbellek = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, List<HttpSysHavuz> L, string Bilgi)> _hvOnbellek = new(StringComparer.OrdinalIgnoreCase);
    public static async Task<List<(string Ad, long Sayi)>> BilgisayarlarAsync(string ip, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        if (_pcOnbellek.TryGetValue(ip, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromHours(12) && c.L.Count > 0) return c.L;
        var l = await BilgisayarlarAsync0(ip, cfg, http, ct);
        _pcOnbellek[ip] = (DateTime.UtcNow, l);
        return l;
    }
    static async Task<List<(string Ad, long Sayi)>> BilgisayarlarAsync0(string ip, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        var end = DateTimeOffset.Now;
        foreach (var saat in new[] { 24, 24 * 7 })
        {
            var r0 = await SplunkService.ExportAsync($"""
                search index={index} sourcetype="{sourcetype}" TERM({ip}) local_ip="{ip}" computer_name=*
                | stats count by computer_name | sort - count
                """, new LookupQuery(ip, end.AddHours(-saat), end), cfg, http, ct, "Splunk");
            var l = r0.Rows.Select(r => (Ad: r.GetValueOrDefault("computer_name")?.Trim() ?? "", Sayi: long.TryParse(r.GetValueOrDefault("count"), out long n) ? n : 0))
                .Where(x => x.Ad.Length > 0).ToList();
            if (l.Count > 0) return l;
        }
        return [];
    }
    // Bilgi: boş dönerse nedeni (arayüzde gösterilir)
    public static async Task<(List<HttpSysHavuz> Liste, string Bilgi, List<(string Ad, long Sayi)> Bilgisayarlar)> HavuzlarBilgiAsync(string ip, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        var end = DateTimeOffset.Now;
        var pcs = await BilgisayarlarAsync(ip, cfg, http, ct);
        string? pc = pcs.FirstOrDefault().Ad;
        if (string.IsNullOrEmpty(pc) || pc.Contains('"') || pc.Contains(' ')) return ([], "Carbon Black olaylarında bu IP'nin bilgisayar adı (computer_name) bulunamadı (son 7 gün)", pcs);
        int gun = cfg.GetValue("HttpSys:SurecGun", 7);
        // havuzlar seyrek değişir: bilgisayar başına HttpSys:HavuzOnbellekSaat (6) saat bellekte
        if (_hvOnbellek.TryGetValue(pc, out var hc) && DateTime.UtcNow - hc.At < TimeSpan.FromHours(cfg.GetValue("HttpSys:HavuzOnbellekSaat", 6.0)))
            return (hc.L, hc.Bilgi + $" · önbellekten ({hc.At.ToLocalTime():HH:mm})", pcs);
        var res = await SplunkService.ExportAsync($"""
            search index={index} sourcetype="{sourcetype}" TERM({pc}) procstart w3wp type="ingress.event.procstart" computer_name="{pc}"
            | where like(lower(process_path), "%w3wp.exe")
            | eval cmd=coalesce(command_line, cmdline, process_cmdline, "")
            | stats latest(cmd) as cmd latest(username) as un latest(pid) as pid latest(process_path) as yol latest(_time) as t latest(link_process) as link by process_guid
            """, new LookupQuery(ip, end.AddDays(-gun), end), cfg, http, ct, "Splunk");
        var l = new List<HttpSysHavuz>();
        int adsiz = 0;
        foreach (var r in res.Rows)
        {
            string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if ((HavuzAdi(V("cmd")) ?? HavuzKullanici(V("un"))) is not { } h) { adsiz++; continue; }
            l.Add(new(h, int.TryParse(V("pid"), out int pid) ? pid : null, (V("yol") ?? "").Contains("syswow64", StringComparison.OrdinalIgnoreCase),
                double.TryParse(V("t"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t)
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)(t * 1000)).ToLocalTime() : null,
                V("process_guid"), V("link"), pc));
        }
        // havuz başına en son başlayan süreç (eski PID'ler recycle ile kapanmıştır)
        var sonuc = l.GroupBy(x => x.Ad, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderByDescending(x => x.Baslangic).First() with { Baslatma = g.Count() })
            .OrderBy(x => x.Ad, StringComparer.OrdinalIgnoreCase).ToList();
        string bilgi = $"bilgisayar {pc}: son {gun} günde {res.Rows.Count} w3wp başlangıcı" + (adsiz > 0 ? $" ({adsiz} tanesinde havuz adı okunamadı)" : "")
            + (pcs.Count > 1 ? $" · bu IP'de başka bilgisayar adları da görüldü: {string.Join(", ", pcs.Skip(1).Take(3).Select(x => $"{x.Ad} ({x.Sayi})"))}" : "")
            + (res.Messages.Count > 0 ? " · " + string.Join(" | ", res.Messages.Take(2)) : "");
        if (res.Messages.All(m => !m.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))) _hvOnbellek[pc] = (DateTime.UtcNow, sonuc, bilgi);
        return (sonuc, bilgi, pcs);
    }

    // IIS ERİŞİM LOGLARI (kullanıcı: Splunk'ta index=iis_msy / iis_csy, sourcetype ms:iis:default, W3C): ilk 9 alan sabit sırada —
    // date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip (sonrası sunucuya göre değişir, kullanılmaz); source'ta …\W3SVC<n>\ = IIS site no.
    // Portta IIS logu varsa port KESİN bir IIS sitesinindir (HTTP.sys'i kendisi kullanan uygulama IIS loguna yazmaz); URI'nin ilk parçası uygulama / sanal dizin.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, List<HttpSysIis> L, string? Hata)> _iisOnbellek = new();
    public static async Task<(List<HttpSysIis> Liste, string? Hata)> IisAsync(string ip, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        if (_iisOnbellek.TryGetValue(ip, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(cfg.GetValue("HttpSys:IisOnbellekDakika", 60))) return (c.L, c.Hata);
        var idx = cfg.GetSection("HttpSys:IisIndexleri").Get<string[]>() is { Length: > 0 } a ? a : ["iis_msy", "iis_csy"];
        if (idx.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x, @"^[A-Za-z0-9_\-\*]+$"))) return ([], "HttpSys:IisIndexleri geçersiz");
        var end = DateTimeOffset.Now;
        string spl = $"""
            search ({string.Join(" OR ", idx.Select(x => "index=" + x))}) TERM({ip})
            | rex "^\S+ \S+ (?<iis_sip>\S+) (?<iis_m>\S+) (?<iis_uri>\S+) \S+ (?<iis_port>\d+) \S+ (?<iis_cip>\S+)"
            | where iis_sip="{ip}"
            | rex field=source "(?i)W3SVC(?<iis_site>\d+)"
            | eval iis_app=lower(mvindex(split(iis_uri, "/"), 1)), iis_app=if(isnull(iis_app) OR iis_app="", "/", iis_app)
            | eventstats dc(iis_cip) as pist by iis_port
            | stats count max(pist) as pist latest(_time) as son by iis_port iis_site host iis_app
            """;
        List<HttpSysIis> l = [];
        string? hata = null;
        try
        {
            var res = await SplunkService.ExportAsync(spl, new LookupQuery(ip, end.AddHours(-cfg.GetValue("HttpSys:IisSaat", 24.0)), end), cfg, http, ct, "Splunk");
            var satir = res.Rows.Select(r => (Port: r.GetValueOrDefault("iis_port") ?? "", Site: r.GetValueOrDefault("iis_site"), Host: r.GetValueOrDefault("host"),
                App: r.GetValueOrDefault("iis_app") ?? "/", N: long.TryParse(r.GetValueOrDefault("count"), out long n) ? n : 0,
                Ist: int.TryParse(r.GetValueOrDefault("pist"), out int pi) ? pi : 0)).Where(x => x.Port.Length > 0).ToList();
            l = satir.GroupBy(x => x.Port).Select(g => new HttpSysIis(g.Key,
                    g.Select(x => x.Site).Where(x => !string.IsNullOrEmpty(x)).Distinct().Select(x => "W3SVC" + x).ToList(),
                    g.Select(x => x.Host).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct().ToList(),
                    g.Sum(x => x.N), g.Max(x => x.Ist),
                    g.GroupBy(x => x.App).Select(a => (Uygulama: a.Key, Istek: a.Sum(x => x.N))).OrderByDescending(a => a.Istek).Take(8)
                        .Select(a => new HttpSysIisUyg(a.Uygulama, a.Istek)).ToList()))
                .OrderByDescending(x => x.Istek).ToList();
            if (res.Messages.Any(m => m.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))) hata = string.Join(" | ", res.Messages.Take(2));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { hata = ex.Message; }
        if (hata == null) _iisOnbellek[ip] = (DateTime.UtcNow, l, null);
        return (l, hata);
    }

    public static string IisMetin(List<HttpSysIis> l, ICollection<string>? portlar = null)
    {
        var x = l.Where(p => portlar == null || portlar.Contains(p.Port)).ToList();
        if (x.Count == 0) return "";
        var sb = new StringBuilder("IIS ERİŞİM LOGU (KESİN: bu portu bir IIS sitesi karşılıyor; URI'nin ilk parçası uygulama / sanal dizin adı):\n");
        foreach (var p in x)
            sb.AppendLine($"- port {p.Port} → IIS sitesi {string.Join(", ", p.Siteler)} · {p.Istek} istek, {p.Istemci} istemci · uygulamalar: "
                + string.Join(", ", p.Uygulamalar.Select(u => $"/{u.Ad.TrimStart('/')} ({u.Istek})")));
        return sb.ToString();
    }

    // w3wp.exe -ap "HavuzAdi" -v "v4.0" … → HavuzAdi
    public static string? HavuzAdi(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(cmd, @"-ap\s+(?:""([^""]+)""|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
    }

    // ERT eksikleri / AI kanıtı için kısa metin
    public static string SurecMetin(List<HttpSysSurec> l, List<HttpSysHavuz>? havuzlar = null)
    {
        var ilgili = l.Where(x => x.Havuz != null || x.UstHavuz != null || x.Domainler.Count > 0 && x.Exe.Equals("w3wp.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ilgili.Count == 0 && !(havuzlar?.Count > 0)) return "";
        var sb = new StringBuilder("IIS UYGULAMA HAVUZLARI (Carbon Black süreç başlangıcı: w3wp komut satırı, alt süreçler; domain = sürecin bağlandığı DNS adları):\n");
        if (havuzlar?.Count > 0)
            sb.AppendLine("- Sunucudaki havuzlar (son başlangıçlar): " + string.Join("; ", havuzlar.Select(h => $"\"{h.Ad}\" (PID {h.Pid}{(h.Bit32 ? ", 32 bit" : "")})")));
        foreach (var x in ilgili.Take(20))
            sb.AppendLine($"- {x.Exe} PID {x.Pid}{(x.Havuz != null ? $" · HAVUZ \"{x.Havuz}\"" : "")}{(x.UstHavuz != null ? $" · üst süreç w3wp, HAVUZ \"{x.UstHavuz}\"" : x.UstYol != null ? $" · üst süreç {Ad(x.UstYol)}" : "")} · {x.Yol}"
                + (x.Domainler.Count > 0 ? $" · bağlandığı: {string.Join(", ", x.Domainler.Take(5))}" : ""));
        return sb.ToString();
    }

    // TANI (kullanıcı: "kontrol verisi basarsan bir yere gönderebilirim"): korelasyonun gördüğü ham veri, düz metin.
    // Filtresiz SPL (tüm gelen süreçler de) → ntoskrnl'nin gerçekten gelen trafiği kabul edip etmediği, exe'lerin dakikaları, port × exe ortak dakika ve r.
    public static async Task<string> TaniAsync(string ip, DateTimeOffset start, DateTimeOffset end, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" TERM({ip}) local_ip="{ip}"
            | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, "-")
            | eval port=if(yon=="in", coalesce(local_port, "0"), ""), rip=if(yon=="in", remote_ip, ""), t=floor(_time/60), pr=coalesce(Protocol, protocol, "?")
            | eval port=if(port=="0", "0/" . pr, port)
            | stats count by t yon port yol rip
            """;
        var sb = new StringBuilder();
        int minDakika = cfg.GetValue("HttpSys:MinDakika", 5); double minR = cfg.GetValue("HttpSys:MinKorelasyon", 0.3);
        int toplam = Math.Max(1, (int)Math.Ceiling((end - start).TotalMinutes));
        sb.AppendLine($"DeltaFlow HTTP.sys korelasyon tanısı · {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"IP {ip} · aralık {start:yyyy-MM-dd HH:mm} – {end:yyyy-MM-dd HH:mm} ({toplam} dk) · MinDakika {minDakika} · MinKorelasyon {minR}");
        sb.AppendLine("SPL:").AppendLine(spl.Trim()).AppendLine();
        SplunkResult res;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { res = await SplunkService.ExportAsync(spl, new LookupQuery(ip, start, end), cfg, http, ct, "Splunk"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return sb.AppendLine("SPLUNK HATASI: " + ex.Message).ToString(); }
        sb.AppendLine($"Splunk: {res.Rows.Count} satır, {sw.ElapsedMilliseconds} ms{(res.Messages.Count > 0 ? " · mesajlar: " + string.Join(" | ", res.Messages) : "")}");
        if (res.Rows.Count > 0) sb.AppendLine("İlk satır alanları: " + string.Join(", ", res.Rows[0].Select(kv => $"{kv.Key}={kv.Value}")));
        sb.AppendLine();

        var gelen = new Dictionary<(string Port, string Yol), Dictionary<long, long>>();
        var giden = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase);
        var yonlar = new Dictionary<string, int>();
        var ipler = new Dictionary<(string Port, string Yol), HashSet<(string Ip, long T)>>();
        foreach (var r in res.Rows)
        {
            string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if (!long.TryParse(V("t"), out long t) || !long.TryParse(V("count"), out long n)) continue;
            string yon = V("yon") ?? "?", yol = V("yol") ?? "-";
            yonlar[yon] = yonlar.GetValueOrDefault(yon) + 1;
            var d = yon == "in" ? (gelen.TryGetValue((V("port") ?? "?", yol), out var g) ? g : gelen[(V("port") ?? "?", yol)] = [])
                : (giden.TryGetValue(yol, out var o) ? o : giden[yol] = []);
            d[t] = d.GetValueOrDefault(t) + n;
            if (yon == "in" && V("rip") is { } rp) (ipler.TryGetValue((V("port") ?? "?", yol), out var ii) ? ii : ipler[(V("port") ?? "?", yol)] = []).Add((rp, t));
        }
        sb.AppendLine("Yön dağılımı (satır): " + string.Join(", ", yonlar.Select(kv => $"{kv.Key}={kv.Value}")));
        string Dk(IEnumerable<long> ts) { var l = ts.Order().ToList(); return string.Join(" ", l.Take(40).Select(x => DateTimeOffset.FromUnixTimeSeconds(x * 60).ToLocalTime().ToString("dd HH:mm"))) + (l.Count > 40 ? $" … (+{l.Count - 40})" : ""); }

        sb.AppendLine().AppendLine("GELEN (port · süreç · dakika · bağlantı) — çekirdek = ntoskrnl / System; port 0 satırlarında /protokol: 1 = ICMP (ping), 6 = TCP, 17 = UDP:");
        foreach (var ((port, yol), d) in gelen.OrderBy(kv => int.TryParse(kv.Key.Port, out var pi) ? pi : 0).ThenByDescending(kv => kv.Value.Count))
            sb.AppendLine($"  {port,-6} {(Cekirdek(yol) ? "[ÇEKİRDEK] " : "")}{yol} · {d.Count} dk · {d.Values.Sum()} bağlantı"
                + (ipler.TryGetValue((port, yol), out var ii) ? $" · {ii.Select(v => v.Ip).Distinct().Count()} istemci ({string.Join(", ", ii.Select(v => v.Ip).Distinct().Take(3))})" : " · istemci IP yok"));

        sb.AppendLine().AppendLine("GİDEN (exe · dakika · bağlantı · durum):");
        foreach (var (yol, d) in giden.OrderByDescending(kv => kv.Value.Count))
            sb.AppendLine($"  {yol} · {d.Count} dk · {d.Values.Sum()} bağlantı · {(Cekirdek(yol) ? "çekirdek (atlanır)" : Genel(Ad(yol), yol) ? "GENEL SÜREÇ (atlanır)" : "iş exe'si")}");

        var cek = gelen.Where(kv => Cekirdek(kv.Key.Yol)).GroupBy(kv => kv.Key.Port)
            .ToDictionary(g => g.Key, g => g.SelectMany(kv => kv.Value).GroupBy(kv => kv.Key).ToDictionary(x => x.Key, x => x.Sum(y => y.Value)));
        if (cek.Count == 0) sb.AppendLine().AppendLine("ÇEKİRDEĞİN KABUL ETTİĞİ GELEN BAĞLANTI YOK (bu aralıkta ntoskrnl / System gelen olayı görülmedi).");
        foreach (var (port, x) in cek)
        {
            sb.AppendLine().AppendLine($"ÇEKİRDEK PORTU {port}: {x.Count} dk, {x.Values.Sum()} bağlantı{(x.Count < minDakika ? " (MinDakika altı → zayıf)" : "")}");
            sb.AppendLine("  dakikalar: " + Dk(x.Keys));
            var K = ipler.Where(kv => kv.Key.Port == port && Cekirdek(kv.Key.Yol)).SelectMany(kv => kv.Value).ToHashSet();
            sb.AppendLine($"  AYNI İSTEMCİ (exe'nin TCP 'gelen port 0' olayı, aynı istemci ±1 dk; {K.Count} istemci×dakika):");
            var es = ipler.Where(kv => kv.Key.Port == "0/6" && !Cekirdek(kv.Key.Yol))
                .Select(kv => (kv.Key.Yol, N: K.Count(k => kv.Value.Contains(k) || kv.Value.Contains((k.Ip, k.T - 1)) || kv.Value.Contains((k.Ip, k.T + 1)))))
                .Where(v => v.N > 0).OrderByDescending(v => v.N).Take(10).ToList();
            if (es.Count == 0) sb.AppendLine("    eşleşen yok");
            foreach (var (yol, n) in es) sb.AppendLine($"    {Ad(yol),-38} {n}/{K.Count}{(Genel(Ad(yol), yol) ? " · GENEL (atlanıyor)" : "")}");
            sb.AppendLine("  DIŞARI BAĞLANTI KORELASYONU:");
            foreach (var (yol, y) in giden.Where(kv => !Cekirdek(kv.Key)).Select(kv => (kv.Key, kv.Value))
                .Select(v => (v.Key, v.Value, Ortak: x.Keys.Count(v.Value.ContainsKey))).OrderByDescending(v => v.Ortak).Take(15).Select(v => (v.Key, v.Value)))
            {
                int ortak = x.Keys.Count(y.ContainsKey);
                // ±1 dakika kayma (olaylar dakika sınırına denk gelebilir)
                int yakin = x.Keys.Count(k => y.ContainsKey(k) || y.ContainsKey(k - 1) || y.ContainsKey(k + 1));
                sb.AppendLine($"  {Ad(yol),-40} ortak {ortak}/{x.Count} dk · ±1 dk {yakin} · r {Pearson(x, y, toplam):0.00}{(Genel(Ad(yol), yol) ? " · GENEL (atlanıyor)" : "")}");
            }
        }
        return sb.ToString();
    }

    // Dakika dizileri (eksik dakika = 0) üzerinde Pearson korelasyonu
    static double Pearson(Dictionary<long, long> x, Dictionary<long, long> y, int n)
    {
        double sx = x.Values.Sum(), sy = y.Values.Sum();
        double sxx = x.Values.Sum(v => (double)v * v), syy = y.Values.Sum(v => (double)v * v);
        double sxy = x.Sum(kv => y.TryGetValue(kv.Key, out var w) ? (double)kv.Value * w : 0);
        n = Math.Max(n, x.Keys.Union(y.Keys).Count());
        double cov = sxy - sx * sy / n, vx = sxx - sx * sx / n, vy = syy - sy * sy / n;
        return vx <= 0 || vy <= 0 ? 0 : cov / Math.Sqrt(vx * vy);
    }

    // AI kanıtı (ERT eksikleri)
    public static string Metin(HttpSysSonuc s)
    {
        if (s.Portlar.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"HTTP.SYS PORT → EXE TAHMİNİ (Carbon Black zaman korelasyonu, son {Math.Round(s.ToplamDakika / 60.0)} saat; KESİN DEĞİL — ntoskrnl'nin kabul ettiği "
            + "gelen trafikle aynı dakikalarda dışarı bağlanan exe, portun asıl sahibi olma adayıdır):");
        foreach (var p in s.Portlar)
            sb.AppendLine(p.Adaylar.Count == 0
                ? $"- port {p.Port}: aday yok ({p.Dakika} dakikada gelen trafik; eşlik eden iş exe'si bulunamadı)"
                : p.Adaylar.All(a => a.EsikAlti)
                ? $"- port {p.Port}: güçlü aday yok ({p.Dakika} dakikada gelen trafik); aynı dakikalarda dışarı bağlanan exe'ler (ÇOK ZAYIF İPUCU): "
                    + string.Join("; ", p.Adaylar.Select(a => $"{a.Exe} [{a.Yol}] (korelasyon {a.R:0.00}, %{a.Eslik} eşlik)"))
                : $"- port {p.Port} ({p.Dakika} dakikada gelen trafik{(p.Zayif ? "; AZ TRAFİK — ZAYIF İPUCU" : "")}) → " + string.Join("; ", p.Adaylar.Where(a => !a.EsikAlti).Select((a, i) =>
                        a.Istemci > 0
                    ? $"{(i == 0 ? "EN OLASI " : "")}{a.Exe} [{a.Yol}] (aynı istemciden aynı dakikada bu exe'ye de gelen bağlantı: {a.Istemci} olay, %{a.Eslik} — güçlü)"
                    : $"{(i == 0 ? "EN OLASI " : "")}{a.Exe} [{a.Yol}] (korelasyon {a.R:0.00}, gelen trafiğin olduğu dakikaların %{a.Eslik}'inde bu exe de dışarı bağlanıyor)")));
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// Kayıtlar (kullanıcı: "bulunan gerçek portları bir yere yazalım, önce oraya bakalım"; SUNUCUYA BAĞLANILMAZ):
//  - dbo.DF_HTTPSYS_KORELASYON: zaman korelasyonu sonuçları. IP sorgusunda / ERT eksiklerinde önce buraya bakılır; yoksa ya da
//    HttpSys:YenilemeSaat'ten (24) eskiyse Carbon Black'ten hesaplanır ve yazılır (DeltaFlow'un kendi DF_ tablosu).
//  - dbo.DF_HTTPSYS_URL (isteğe bağlı): birisi araclar/HttpSysTopla.ps1 ile netsh kaydı topladıysa KESİN kayıt; DeltaFlow yalnız OKUR.
// sql/httpsys_kayit.sql. Tablo yoksa sonuç bellekte tutulur.
// ---------------------------------------------------------------------------
record HttpSysKayit(string Ip, string? Hostname, int Port, string? Protokol, string Url, string? Kuyruk, int? Pid, string? ExeYol, DateTime Tarih)
{
    public string? Exe => ExeYol is { Length: > 0 } y ? y.Replace('/', '\\').Split('\\').Last() : null;
}

sealed class HttpSysKayitService(IConfiguration cfg, ILogger<HttpSysKayitService> log)
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, List<HttpSysKayit> Liste)> _url = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, HttpSysSonuc> _kor = new();   // tablo yoksa bellekte
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _kilit = new();
    static bool Ad(string t) => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$");
    string UrlTablo => cfg["HttpSys:Tablo"] is { Length: > 0 } t && Ad(t) ? t : "dbo.DF_HTTPSYS_URL";
    string KorTablo => cfg["HttpSys:KorelasyonTablosu"] is { Length: > 0 } t && Ad(t) ? t : "dbo.DF_HTTPSYS_KORELASYON";
    string? Cs => cfg["HttpSys:Baglanti"] is { Length: > 0 } b ? b : cfg.GetConnectionString("Envanter") is { Length: > 0 } e ? e : null;
    public string? Durum { get; private set; }

    // ---- Zaman korelasyonu: önce kayıt, yoksa / eskiyse hesapla + yaz ----
    public record KorSonuc(HttpSysSonuc Sonuc, DateTime? Tarih, string Kaynak);   // Kaynak: "kayıt" | "şimdi hesaplandı"
    public async Task<KorSonuc> KorelasyonAsync(string ip, DateTimeOffset start, DateTimeOffset end, HttpClient http, CancellationToken ct,
        ICollection<string>? portlar = null, bool yenile = false)
    {
        double saat = cfg.GetValue("HttpSys:YenilemeSaat", 24.0);
        var k0 = await KorOkuAsync(ip, ct);   // yenile'de de okunur: ISTEMCI kolonu var mı öğrenilsin
        if (!yenile && k0 is { } k && DateTime.Now - k.Tarih < TimeSpan.FromHours(saat)
            && (portlar == null || portlar.All(p => k.Sonuc.Portlar.Any(x => x.Port == p) || k.Bos.Contains(p))))
            return new(k.Sonuc, k.Tarih, "kayıt");
        var kilit = _kilit.GetOrAdd(ip, _ => new SemaphoreSlim(1, 1));
        await kilit.WaitAsync(ct);
        try
        {
            var res = await HttpSysEslesme.HesaplaAsync(ip, start, end, cfg, http, ct, portlar);
            if (res.Hata == null) await KorYazAsync(res, portlar, ct);
            return new(res, DateTime.Now, "şimdi hesaplandı");
        }
        finally { kilit.Release(); }
    }

    async Task<(HttpSysSonuc Sonuc, DateTime Tarih, HashSet<string> Bos)?> KorOkuAsync(string ip, CancellationToken ct)
    {
        if (Cs is not { } cs) return _kor.TryGetValue(ip, out var m0) ? (m0, m0.Bitis.LocalDateTime, []) : null;
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                $"SELECT * FROM {KorTablo} WITH (NOLOCK) WHERE IP = @ip ORDER BY PORT, SIRA", conn);   // ISTEMCI kolonu sonradan eklendi (yoksa 0)
            cmd.Parameters.AddWithValue("@ip", ip);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var portlar = new Dictionary<string, (int Dk, long Bag, List<HttpSysAday> A)>();
            var bos = new HashSet<string>();
            DateTime? tarih = null;
            var kol = Enumerable.Range(0, r.FieldCount).ToDictionary(i => r.GetName(i), i => i, StringComparer.OrdinalIgnoreCase);
            _istKolon = kol.ContainsKey("ISTEMCI");
            object? G(string k) => kol.TryGetValue(k, out var i) && !r.IsDBNull(i) ? r.GetValue(i) : null;
            while (await r.ReadAsync(ct))
            {
                string port = Convert.ToString(G("PORT"))!;
                var t = Convert.ToDateTime(G("TARIH"));
                if (tarih == null || t < tarih) tarih = t;
                if (!portlar.TryGetValue(port, out var p)) portlar[port] = p = (Convert.ToInt32(G("DAKIKA") ?? 0), Convert.ToInt64(G("BAGLANTI") ?? 0L), []);
                if (G("EXE") is not string exe) { bos.Add(port); continue; }   // aday yok satırı
                double rr = Convert.ToDouble(G("R") ?? 0.0); int od = Convert.ToInt32(G("ORTAK_DAKIKA") ?? 0), ist = Convert.ToInt32(G("ISTEMCI") ?? 0);
                p.A.Add(new(exe, G("EXE_YOL") as string ?? "", rr, Convert.ToInt32(G("ESLIK") ?? 0), od,
                    ist == 0 && (rr < cfg.GetValue("HttpSys:MinKorelasyon", 0.3) || od < Math.Min(2, p.Dk)), ist));
            }
            if (tarih == null) return null;
            int minDk = cfg.GetValue("HttpSys:MinDakika", 5);
            double pSaat = Math.Clamp(cfg.GetValue("HttpSys:Saat", 72.0), 1, 24 * 14);
            var sonuc = new HttpSysSonuc(ip, tarih.Value.AddHours(-pSaat), tarih.Value, (int)(pSaat * 60), portlar.Where(kv => kv.Key != "0")
                .Select(kv => new HttpSysPort(kv.Key, kv.Value.Dk, kv.Value.Bag, kv.Value.A, kv.Value.Dk < minDk)).ToList(), null);
            Durum = null;
            return (sonuc, tarih.Value, bos);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208)
        {
            Durum = $"{KorTablo} tablosu yok (sql/httpsys_kayit.sql) — sonuçlar yalnız bellekte";
            return _kor.TryGetValue(ip, out var m) ? (m, m.Bitis.LocalDateTime, []) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("HTTP.sys korelasyon kaydı okunamadı: {Hata}", ex.Message);
            return _kor.TryGetValue(ip, out var m2) ? (m2, m2.Bitis.LocalDateTime, []) : null;
        }
    }

    // IP'nin hesaplanan portları yeniden yazılır (aynı IP'nin başka portlarının kaydı korunur); aday yoksa EXE NULL satır, hiç ntoskrnl portu yoksa PORT 0
    bool _istKolon;   // DF_HTTPSYS_KORELASYON.ISTEMCI var mı (okurken öğrenilir; KorelasyonAsync her zaman önce okur)
    async Task KorYazAsync(HttpSysSonuc res, ICollection<string>? istenen, CancellationToken ct)
    {
        _kor[res.Ip] = res with { Bitis = DateTimeOffset.Now };
        if (Cs is not { } cs) return;
        var portlar = res.Portlar.Select(p => p.Port).Concat(istenen ?? []).Distinct().ToList();
        if (portlar.Count == 0) portlar.Add("0");
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
            foreach (var port in portlar)
            {
                await using (var del = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {KorTablo} WHERE IP = @ip AND PORT = @p", conn, tx))
                { del.Parameters.AddWithValue("@ip", res.Ip); del.Parameters.AddWithValue("@p", port); await del.ExecuteNonQueryAsync(ct); }
                var hp = res.Portlar.FirstOrDefault(x => x.Port == port);
                var adaylar = hp?.Adaylar.Count > 0 ? hp.Adaylar.Select((a, i) => ((HttpSysAday?)a, i + 1)).ToList() : [(null, 0)];
                foreach (var (a, sira) in adaylar)
                {
                    await using var ins = new Microsoft.Data.SqlClient.SqlCommand(_istKolon
                        ? $"INSERT INTO {KorTablo} (IP, PORT, SIRA, EXE, EXE_YOL, R, ESLIK, ORTAK_DAKIKA, DAKIKA, BAGLANTI, ISTEMCI) VALUES (@ip, @p, @s, @e, @y, @r, @es, @o, @d, @b, @i)"
                        : $"INSERT INTO {KorTablo} (IP, PORT, SIRA, EXE, EXE_YOL, R, ESLIK, ORTAK_DAKIKA, DAKIKA, BAGLANTI) VALUES (@ip, @p, @s, @e, @y, @r, @es, @o, @d, @b)", conn, tx);
                    if (_istKolon) ins.Parameters.AddWithValue("@i", (object?)a?.Istemci ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@ip", res.Ip); ins.Parameters.AddWithValue("@p", port); ins.Parameters.AddWithValue("@s", sira);
                    ins.Parameters.AddWithValue("@e", (object?)a?.Exe ?? DBNull.Value); ins.Parameters.AddWithValue("@y", (object?)a?.Yol ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@r", (object?)a?.R ?? DBNull.Value); ins.Parameters.AddWithValue("@es", (object?)a?.Eslik ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@o", (object?)a?.OrtakDakika ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@d", hp?.Dakika ?? 0); ins.Parameters.AddWithValue("@b", hp?.Baglanti ?? 0);
                    await ins.ExecuteNonQueryAsync(ct);
                }
            }
            await tx.CommitAsync(ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Durum = $"{KorTablo} tablosu yok — sonuçlar yalnız bellekte"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("HTTP.sys korelasyon kaydı yazılamadı: {Hata}", ex.Message); }
    }

    // ---- Kesin kayıt (isteğe bağlı, yalnız okunur): DF_HTTPSYS_URL ----
    public async Task<List<HttpSysKayit>> IpAsync(string ip, CancellationToken ct)
    {
        if (_url.TryGetValue(ip, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(cfg.GetValue("HttpSys:KayitDakika", 10))) return c.Liste;
        var liste = new List<HttpSysKayit>();
        if (Cs is not { } cs) return liste;
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                $"SELECT IP, HOSTNAME, PORT, PROTOKOL, URL, KUYRUK, PID, EXE_YOL, TOPLAMA_TARIHI FROM {UrlTablo} WITH (NOLOCK) WHERE IP = @ip AND PORT > 0 ORDER BY PORT", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i))?.Trim();
            while (await r.ReadAsync(ct))
                liste.Add(new(S(0) ?? ip, S(1), Convert.ToInt32(r.GetValue(2)), S(3), S(4) ?? "", S(5), r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)), S(7),
                    r.IsDBNull(8) ? DateTime.MinValue : Convert.ToDateTime(r.GetValue(8))));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { /* isteğe bağlı tablo yok */ }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("HTTP.sys kayıtları okunamadı: {Hata}", ex.Message); }
        _url[ip] = (DateTime.UtcNow, liste);
        return liste;
    }

    // AI kanıtı: boş portların kesin kaydı
    public static string Metin(IEnumerable<HttpSysKayit> kayitlar)
    {
        var g = kayitlar.GroupBy(k => k.Port).ToList();
        if (g.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("HTTP.SYS KAYDI (sunucunun kendisinden `netsh http show servicestate` ile toplandı — KESİN: bu portu hangi süreç dinliyor):");
        foreach (var p in g)
            sb.AppendLine($"- port {p.Key} → " + string.Join("; ", p.GroupBy(k => (k.ExeYol, k.Kuyruk)).Select(x =>
                (x.Key.ExeYol is { } y ? $"{y.Replace('/', '\\').Split('\\').Last()} [{y}]" : "çalışan süreç yok")
                + (x.Key.Kuyruk is { } q ? $" · istek kuyruğu / IIS havuzu '{q}'" : "")
                + " · URL " + string.Join(", ", x.Select(k => k.Url).Distinct().Take(4))
                + $" · {x.Max(k => k.Tarih):yyyy-MM-dd}")));
        return sb.ToString();
    }
}
