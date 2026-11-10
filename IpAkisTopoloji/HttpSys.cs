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
        if (!yenile && await KorOkuAsync(ip, ct) is { } k && DateTime.Now - k.Tarih < TimeSpan.FromHours(saat)
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
                $"SELECT PORT, SIRA, EXE, EXE_YOL, R, ESLIK, ORTAK_DAKIKA, DAKIKA, BAGLANTI, TARIH FROM {KorTablo} WITH (NOLOCK) WHERE IP = @ip ORDER BY PORT, SIRA", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var portlar = new Dictionary<string, (int Dk, long Bag, List<HttpSysAday> A)>();
            var bos = new HashSet<string>();
            DateTime? tarih = null;
            while (await r.ReadAsync(ct))
            {
                string port = Convert.ToString(r.GetValue(0))!;
                var t = Convert.ToDateTime(r.GetValue(9));
                if (tarih == null || t < tarih) tarih = t;
                if (!portlar.TryGetValue(port, out var p)) portlar[port] = p = (r.IsDBNull(7) ? 0 : Convert.ToInt32(r.GetValue(7)), r.IsDBNull(8) ? 0 : Convert.ToInt64(r.GetValue(8)), []);
                if (r.IsDBNull(2)) { bos.Add(port); continue; }   // aday yok satırı
                p.A.Add(new(r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3), Convert.ToDouble(r.GetValue(4)), Convert.ToInt32(r.GetValue(5)), Convert.ToInt32(r.GetValue(6))));
            }
            if (tarih == null) return null;
            var sonuc = new HttpSysSonuc(ip, tarih.Value, tarih.Value, 0, portlar.Where(kv => kv.Key != "0")
                .Select(kv => new HttpSysPort(kv.Key, kv.Value.Dk, kv.Value.Bag, kv.Value.A)).ToList(), null);
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
                    await using var ins = new Microsoft.Data.SqlClient.SqlCommand(
                        $"INSERT INTO {KorTablo} (IP, PORT, SIRA, EXE, EXE_YOL, R, ESLIK, ORTAK_DAKIKA, DAKIKA, BAGLANTI) VALUES (@ip, @p, @s, @e, @y, @r, @es, @o, @d, @b)", conn, tx);
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
