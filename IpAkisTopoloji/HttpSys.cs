using System.Text;

// ---------------------------------------------------------------------------
// HTTP.sys port → asıl exe tahmini (kullanıcı: "ntoskrnl olayı: exe gerçekte hangi portta çalışıyor bilememek çok sıkıntı").
// HTTP.sys kullanan uygulamalarda (IIS, HttpSys'li ASP.NET Core, WCF) gelen bağlantıyı çekirdek kabul eder; Carbon Black portun sahibini
// ntoskrnl.exe / System görür. Ek veri olmadan zaman korelasyonu: sunucunun Carbon Black olayları dakika dakika sayılır —
// ntoskrnl'nin porta aldığı gelen bağlantılar ve her exe'nin giden bağlantıları. İstek alan uygulama çoğu zaman hemen arkasından
// DB'ye / başka servise gittiği için gelen trafiğe en çok eşlik eden exe portun sahibi olma adayıdır. KESİN DEĞİL: hiç dışarı çıkmayan
// uygulamayı yakalamaz, aynı anda çalışan uygulamalar karışabilir. Kesin eşleme sunucuda `netsh http show servicestate view=requestq`.
// ---------------------------------------------------------------------------
record HttpSysAday(string Exe, string Yol, double R, int Eslik, int OrtakDakika);
record HttpSysPort(string Port, int Dakika, long Baglanti, List<HttpSysAday> Adaylar);
record HttpSysSonuc(string Ip, DateTimeOffset Baslangic, DateTimeOffset Bitis, int ToplamDakika, List<HttpSysPort> Portlar, string? Hata);

static class HttpSysEslesme
{
    public static bool Cekirdek(string? yol)
    {
        string e = (yol ?? "").Replace('/', '\\').Split('\\').Last().Trim().ToLowerInvariant();
        return e is "ntoskrnl.exe" or "system" or "ntoskrnl";
    }

    public static async Task<HttpSysSonuc> HesaplaAsync(string ip, DateTimeOffset start, DateTimeOffset end, IConfiguration cfg, HttpClient http,
        CancellationToken ct, ICollection<string>? portlar = null)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        // gelen: yalnız çekirdeğin kabul ettikleri (port başına); giden: süreç başına. Dakika = floor(_time / 60)
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" TERM({ip}) local_ip="{ip}"
            | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, "-"), ly=lower(yol)
            | where yon=="out" OR like(ly, "%ntoskrnl.exe") OR ly=="system" OR ly=="ntoskrnl"
            | eval port=if(yon=="in", local_port, ""), t=floor(_time/60)
            | stats count by t yon port yol
            """;
        int toplam = Math.Max(1, (int)Math.Ceiling((end - start).TotalMinutes));
        SplunkResult res;
        try { res = await SplunkService.ExportAsync(spl, new LookupQuery(ip, start, end), cfg, http, ct, "Splunk"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(ip, start, end, toplam, [], ex.Message); }

        var gelen = new Dictionary<string, Dictionary<long, long>>();   // port → dakika → bağlantı
        var giden = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase);   // exe yolu → dakika → bağlantı
        foreach (var r in res.Rows)
        {
            string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            if (!long.TryParse(V("t"), out long t) || !long.TryParse(V("count"), out long n)) continue;
            string yol = V("yol") ?? "-";
            if (V("yon") == "in")
            {
                if (!Cekirdek(yol) || V("port") is not { } p || (portlar != null && !portlar.Contains(p))) continue;
                if (!gelen.TryGetValue(p, out var d)) gelen[p] = d = [];
                d[t] = d.GetValueOrDefault(t) + n;
            }
            else
            {
                if (yol == "-" || Cekirdek(yol)) continue;
                string ad = yol.Replace('/', '\\').Split('\\').Last();
                if (SurecAi.GenelExe(ad, yol)) continue;   // svchost, ajanlar, izleme… (sürekli çalışır, eşlik sahte çıkar)
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
            if (x.Count >= minDakika)
                foreach (var (yol, y) in giden)
                {
                    double r = Pearson(x, y, toplam);
                    int ortak = x.Keys.Count(y.ContainsKey);
                    if (r < minR || ortak < 2) continue;
                    adaylar.Add(new(yol.Replace('/', '\\').Split('\\').Last(), yol, Math.Round(r, 2), (int)Math.Round(100.0 * ortak / x.Count), ortak));
                }
            outp.Add(new(port, x.Count, x.Values.Sum(), adaylar.OrderByDescending(a => a.R).ThenByDescending(a => a.Eslik).Take(3).ToList()));
        }
        return new(ip, start, end, toplam, outp, null);
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
                : $"- port {p.Port} ({p.Dakika} dakikada gelen trafik) → " + string.Join("; ", p.Adaylar.Select((a, i) =>
                    $"{(i == 0 ? "EN OLASI " : "")}{a.Exe} [{a.Yol}] (korelasyon {a.R:0.00}, gelen trafiğin olduğu dakikaların %{a.Eslik}'inde bu exe de dışarı bağlanıyor)")));
        return sb.ToString();
    }
}
