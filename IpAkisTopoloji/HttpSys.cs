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
// HTTP.sys URL kaydı (KESİN): araclar/HttpSysTopla.ps1 sunucularda `netsh http show servicestate` çalıştırıp dbo.DF_HTTPSYS_URL'e
// yazar (sql/httpsys_kayit.sql). DeltaFlow yalnız OKUR; önce buna bakılır, kayıt yoksa zaman korelasyonu (HttpSysEslesme).
// ---------------------------------------------------------------------------
record HttpSysKayit(string Ip, string? Hostname, int Port, string? Protokol, string Url, string? Kuyruk, int? Pid, string? ExeYol, DateTime Tarih)
{
    public string? Exe => ExeYol is { Length: > 0 } y ? y.Replace('/', '\\').Split('\\').Last() : null;
}

sealed class HttpSysKayitService(IConfiguration cfg, ILogger<HttpSysKayitService> log)
{
    // IP → (okuma zamanı, kayıtlar, son toplama tarihi, kaynak)
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, List<HttpSysKayit> Liste, DateTime? Toplama)> _cache = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _kilit = new();
    string Tablo => cfg["HttpSys:Tablo"] is { Length: > 0 } t && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")
        ? t : "dbo.DF_HTTPSYS_URL";
    string? Cs => cfg["HttpSys:Baglanti"] is { Length: > 0 } b ? b : cfg.GetConnectionString("Envanter");
    public string? Durum { get; private set; }   // tablo yoksa / okunamıyorsa açıklama
    bool CanliAcik => cfg.GetValue("HttpSys:CanliTopla", true) && OperatingSystem.IsWindows();

    public record Sonuc(List<HttpSysKayit> Kayitlar, DateTime? Toplama, string Kaynak, string? Hata);

    // IP'nin kayıtları. topla=true ise kayıt yoksa ya da `HttpSys:YenilemeSaat`ten (24) eskiyse sunucudan şimdi toplanır (WinRM + netsh),
    // tabloya yazılır. host: sunucu adı (ERT UNIXNAME; WinRM Kerberos IP ile çalışmaz). yenile=true → süreye bakmadan topla.
    public async Task<Sonuc> GetirAsync(string ip, string? host, bool topla, CancellationToken ct, bool yenile = false)
    {
        var (liste, tarih) = await OkuAsync(ip, ct);
        double saat = cfg.GetValue("HttpSys:YenilemeSaat", 24.0);
        bool taze = tarih is { } t && DateTime.Now - t < TimeSpan.FromHours(saat);
        if (!topla || !CanliAcik || (taze && !yenile)) return new(liste, tarih, tarih != null ? "tablo" : "yok", null);
        var kilit = _kilit.GetOrAdd(ip, _ => new SemaphoreSlim(1, 1));
        await kilit.WaitAsync(ct);
        try
        {
            if (!yenile && _cache.TryGetValue(ip, out var c) && c.Toplama is { } ct2 && DateTime.Now - ct2 < TimeSpan.FromHours(saat))
                return new(c.Liste, c.Toplama, "tablo", null);   // aynı anda gelen ikinci istek beklerken toplandı
            var (yeniKayit, hata) = await ToplaAsync(ip, host, ct);
            if (hata != null) return new(liste, tarih, tarih != null ? "tablo" : "yok", hata);
            await YazAsync(ip, yeniKayit!, ct);
            _cache[ip] = (DateTime.UtcNow, yeniKayit!, DateTime.Now);
            return new(yeniKayit!, DateTime.Now, "şimdi toplandı", null);
        }
        finally { kilit.Release(); }
    }
    public async Task<List<HttpSysKayit>> IpAsync(string ip, CancellationToken ct) => (await OkuAsync(ip, ct)).Liste;

    async Task<(List<HttpSysKayit> Liste, DateTime? Toplama)> OkuAsync(string ip, CancellationToken ct)
    {
        int dk = cfg.GetValue("HttpSys:KayitDakika", 10);
        if (_cache.TryGetValue(ip, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromMinutes(dk)) return (c.Liste, c.Toplama);
        var liste = new List<HttpSysKayit>();
        DateTime? toplama = null;
        if (Cs is not { } cs) return (liste, null);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                $"SELECT IP, HOSTNAME, PORT, PROTOKOL, URL, KUYRUK, PID, EXE_YOL, TOPLAMA_TARIHI FROM {Tablo} WITH (NOLOCK) WHERE IP = @ip ORDER BY PORT", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i))?.Trim();
            while (await r.ReadAsync(ct))
            {
                var t = r.IsDBNull(8) ? DateTime.MinValue : Convert.ToDateTime(r.GetValue(8));
                if (toplama == null || t > toplama) toplama = t;
                int port = Convert.ToInt32(r.GetValue(2));
                if (port <= 0) continue;   // "toplandı, HTTP.sys kaydı yok" işaret satırı
                liste.Add(new(S(0) ?? ip, S(1), port, S(3), S(4) ?? "", S(5), r.IsDBNull(6) ? null : Convert.ToInt32(r.GetValue(6)), S(7), t));
            }
            Durum = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208)
        {
            Durum = $"{Tablo} tablosu yok (sql/httpsys_kayit.sql) — sonuçlar yalnız bellekte tutulur";
            if (_cache.TryGetValue(ip, out var m)) return (m.Liste, m.Toplama);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Durum = "HTTP.sys kayıtları okunamadı: " + ex.Message;
            log.LogWarning("HTTP.sys kayıtları okunamadı: {Hata}", ex.Message);
        }
        _cache[ip] = (DateTime.UtcNow, liste, toplama);
        return (liste, toplama);
    }

    // Sunucudan canlı toplama: powershell.exe → Invoke-Command (WinRM) → netsh http show servicestate + Win32_Process. Komut SABİT; sunucu adı,
    // kullanıcı ve parola ortam değişkeniyle geçer (komut satırına girmez). Kimlik: HttpSys:Kullanici / HttpSys:Sifre (appsettings.Production.json),
    // yoksa uygulama havuzu hesabı. Hedefte WinRM açık ve hesap yerel yönetici olmalı.
    const string Betik = """
        $ErrorActionPreference = 'Stop'
        $sb = {
            $o = netsh http show servicestate view=requestq verbose=yes 2>$null
            $p = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ExecutablePath, CommandLine)
            [pscustomobject]@{ Netsh = ($o -join "`n"); Proc = $p; Ad = $env:COMPUTERNAME }
        }
        $a = @{ ComputerName = $env:DF_HS_HOST; ScriptBlock = $sb }
        if ($env:DF_HS_USER) {
            $sec = ConvertTo-SecureString $env:DF_HS_PASS -AsPlainText -Force
            $a.Credential = New-Object System.Management.Automation.PSCredential($env:DF_HS_USER, $sec)
        }
        Invoke-Command @a | Select-Object Netsh, Proc, Ad | ConvertTo-Json -Depth 4 -Compress
        """;

    async Task<(List<HttpSysKayit>? Liste, string? Hata)> ToplaAsync(string ip, string? host, CancellationToken ct)
    {
        string hedef = host is { Length: > 0 } h && !LookupQuery.TryParseIpv4(h, out _)
            ? (h.Contains('.') || cfg["HttpSys:AlanAdi"] is not { Length: > 0 } dom ? h : h + "." + dom.TrimStart('.')) : ip;
        if (!System.Text.RegularExpressions.Regex.IsMatch(hedef, @"^[A-Za-z0-9.\-]{1,255}$")) return (null, "geçersiz sunucu adı");
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(Betik)))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
          StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.Environment["DF_HS_HOST"] = hedef;
        if (cfg["HttpSys:Kullanici"] is { Length: > 0 } u) { psi.Environment["DF_HS_USER"] = u; psi.Environment["DF_HS_PASS"] = cfg["HttpSys:Sifre"] ?? ""; }
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromSeconds(cfg.GetValue("HttpSys:ToplaSaniye", 60)));
        try
        {
            using var pr = System.Diagnostics.Process.Start(psi)!;
            var outT = pr.StandardOutput.ReadToEndAsync(to.Token);
            var errT = pr.StandardError.ReadToEndAsync(to.Token);
            try { await pr.WaitForExitAsync(to.Token); }
            catch (OperationCanceledException) { try { pr.Kill(true); } catch { } throw; }
            string o = await outT, e = await errT;
            if (pr.ExitCode != 0 || o.Trim().Length == 0)
                return (null, $"{hedef}: " + (e.Trim() is { Length: > 0 } ee ? Cut1(System.Text.RegularExpressions.Regex.Replace(ee, @"\s+", " "), 300) : $"çıkış kodu {pr.ExitCode}"));
            using var doc = System.Text.Json.JsonDocument.Parse(o);
            var kok = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;
            string netsh = kok.TryGetProperty("Netsh", out var n) ? n.GetString() ?? "" : "";
            string? ad = kok.TryGetProperty("Ad", out var adEl) ? adEl.GetString() : hedef;
            var exe = new Dictionary<int, string?>();
            if (kok.TryGetProperty("Proc", out var pl) && pl.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var x in pl.EnumerateArray())
                    if (x.TryGetProperty("ProcessId", out var pid) && pid.TryGetInt32(out int pi))
                        exe[pi] = x.TryGetProperty("ExecutablePath", out var ep) && ep.ValueKind == System.Text.Json.JsonValueKind.String ? ep.GetString() : null;
            return (Ayristir(netsh, ip, ad, exe), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, $"{hedef}: {cfg.GetValue("HttpSys:ToplaSaniye", 60)} sn içinde cevap yok (WinRM)"); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (null, $"{hedef}: {ex.Message}"); }
    }
    static string Cut1(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    // netsh http show servicestate view=requestq verbose=yes çıktısı (araclar/HttpSysTopla.ps1 ile aynı kurallar): girintisiz satır = yeni blok
    // (kuyruk adı; 16 haneli onaltılık + süreçsiz = sunucu oturumu, atlanır), "  ID: n" süreç ("Controller process ID" sayılmaz), "http(s)://host:port/…" URL.
    // URL başka bir IP'ye bağlıysa o IP'nin kaydı değildir.
    public static List<HttpSysKayit> Ayristir(string netsh, string ip, string? ad, IReadOnlyDictionary<int, string?> exe)
    {
        var sonuc = new List<HttpSysKayit>();
        string? kuyruk = null; var pids = new List<int>(); var urls = new List<string>();
        void Bitir()
        {
            if (urls.Count == 0 || (kuyruk != null && System.Text.RegularExpressions.Regex.IsMatch(kuyruk, "^[0-9A-Fa-f]{16}$") && pids.Count == 0)) return;
            foreach (var u in urls)
            {
                var m = System.Text.RegularExpressions.Regex.Match(u, @"^(?i)(https?)://([^/\s]+):(\d+)(/\S*)?");
                if (!m.Success || !int.TryParse(m.Groups[3].Value, out int port)) continue;
                string h = m.Groups[2].Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(h, @"^\d{1,3}(\.\d{1,3}){3}$") && h != ip) continue;
                foreach (int? p in pids.Count > 0 ? pids.Select(x => (int?)x) : [null])
                    sonuc.Add(new(ip, ad, port, m.Groups[1].Value.ToLowerInvariant(), u, kuyruk, p, p is int pp ? exe.GetValueOrDefault(pp) : null, DateTime.Now));
            }
        }
        foreach (var raw in netsh.Replace("\r", "").Split('\n'))
        {
            if (raw.Length > 0 && !char.IsWhiteSpace(raw[0]) && !raw.StartsWith('-') && raw.Contains(':'))
            {
                Bitir();
                kuyruk = raw[(raw.IndexOf(':') + 1)..].Trim();
                if (kuyruk.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(kuyruk, "(?i)unnamed|adsız|adsiz")) kuyruk = null;
                pids.Clear(); urls.Clear();
                continue;
            }
            if (System.Text.RegularExpressions.Regex.Match(raw, @"^\s*ID:\s*(\d+)") is { Success: true } im) pids.Add(int.Parse(im.Groups[1].Value));
            else if (System.Text.RegularExpressions.Regex.Match(raw, @"^\s*((?i)https?://\S+)\s*$") is { Success: true } um) urls.Add(um.Groups[1].Value);
        }
        Bitir();
        return sonuc.DistinctBy(k => (k.Port, k.Url, k.Pid)).ToList();
    }

    // DeltaFlow'un kendi DF_ tablosuna yazar (IP'nin eski satırları silinir); kayıt yoksa PORT 0 işaret satırı ("toplandı, kayıt yok")
    async Task YazAsync(string ip, List<HttpSysKayit> liste, CancellationToken ct)
    {
        if (Cs is not { } cs) return;
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await conn.BeginTransactionAsync(ct);
            await using (var del = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {Tablo} WHERE IP = @ip", conn, tx))
            { del.Parameters.AddWithValue("@ip", ip); await del.ExecuteNonQueryAsync(ct); }
            var satirlar = liste.Count > 0 ? liste : [new HttpSysKayit(ip, null, 0, null, "", null, null, null, DateTime.Now)];
            foreach (var k in satirlar)
            {
                await using var ins = new Microsoft.Data.SqlClient.SqlCommand(
                    $"INSERT INTO {Tablo} (IP, HOSTNAME, PORT, PROTOKOL, URL, KUYRUK, PID, EXE_YOL, TOPLAYAN) VALUES (@ip, @h, @p, @pr, @u, @k, @pid, @e, @t)", conn, tx);
                ins.Parameters.AddWithValue("@ip", ip);
                ins.Parameters.AddWithValue("@h", (object?)k.Hostname ?? "");
                ins.Parameters.AddWithValue("@p", k.Port);
                ins.Parameters.AddWithValue("@pr", (object?)k.Protokol ?? DBNull.Value);
                ins.Parameters.AddWithValue("@u", k.Url);
                ins.Parameters.AddWithValue("@k", (object?)k.Kuyruk ?? DBNull.Value);
                ins.Parameters.AddWithValue("@pid", (object?)k.Pid ?? DBNull.Value);
                ins.Parameters.AddWithValue("@e", (object?)k.ExeYol ?? DBNull.Value);
                ins.Parameters.AddWithValue("@t", "DeltaFlow");
                await ins.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Durum = $"{Tablo} tablosu yok — toplanan kayıt yalnız bellekte"; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("HTTP.sys kaydı yazılamadı: {Hata}", ex.Message); }
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
