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
