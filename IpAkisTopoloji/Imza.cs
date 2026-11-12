using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

// SERVİS İMZASI (kullanıcı: "Entropi'deki envanteri alalım, IP port karşılığında burada bulduklarımızı getirelim, bir tabloya basalım;
// boş olanlarda aynı servis çalışıyorsa karşısına tespit ettiğimiz uygulamayı basarız"; "exe – IIS uygulaması – ERT adı, bir yol bulmamız lazım").
// Tüm ERT (ERT_HOSTIPADDRESS) arka planda taranır: IP'ler parça parça Carbon Black (son Imza:CbSaat saat, port → exe) + IIS logu
// (son Imza:IisDakika dakika, port → IIS uygulamaları). Ham tarama gzip'li JSON olarak DF_SERVIS_IMZA'ya yazılır (sql/servis_imza.sql);
// eşleşme her açılışta ham veriden hesaplanır (kural değişince yeniden tarama gerekmez). ERT'ye hiçbir şey yazılmaz.
// İmza = exe (HTTP.sys / ntoskrnl portunda ERT WEBSITE / APPNAME adıyla yolu eşleşen asıl exe) + port + IIS uygulama kümesi.
// Sözlük: ERT'de adı dolu satırların imzası → ERT adı. Boş satır: aynı exe + port (+ IIS uygulamaları örtüşüyorsa güçlü), yalnız IIS kümesi (zayıf).

record ImzaPort(string Port, string Yol, long Baglanti, int Istemci);
record ImzaIis(string Port, List<string> Uygulamalar, long Istek);
record ImzaSunucu(string Ip, List<ImzaPort> Gelen, List<string> Giden, List<ImzaIis> Iis);
record ImzaTarama(DateTime Baslangic, DateTime? Bitis, double CbSaat, double IisDakika, List<ImzaSunucu> Sunucular, List<string> Taranan,
    List<string> Mesajlar, int ToplamIp, string? Kaydeden);
record ImzaIs(string Durum, int Toplam, int Biten, string Asama, DateTime Baslangic, DateTime? Bitis, string? Hata, string? Kaydeden, int Sunucu);
record ImzaAday(string Ad, int Sunucu);
record ImzaSatir(string Ip, string Port, string? Sunucu, string? Ert, string? Website, string? Exe, string? ExeYol, bool HttpSys,
    long Baglanti, int Istemci, List<string> Iis, string Durum, string? Oneri, string? Guven, string? Not, List<ImzaAday>? Adaylar, bool Altyapi);
record ImzaSozluk(string Exe, string Port, List<ImzaAday> Adlar, List<string> Iis, int Sunucu, bool Celiski);
record ImzaSonuc(ImzaIs? Is, object? Kayit, List<ImzaSatir> Satirlar, List<ImzaSozluk> Sozluk, List<string> Mesajlar, string? Uyari);

sealed class ServisImzaService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ILogger<ServisImzaService> log)
{
    static readonly System.Text.Json.JsonSerializerOptions Js = new(System.Text.Json.JsonSerializerDefaults.Web);
    readonly object _gate = new();
    ImzaIs? _is;
    CancellationTokenSource? _cts;
    ImzaTarama? _son;                       // son (bellekteki) tarama
    (ImzaTarama T, DateTimeOffset Env, ImzaSonuc S)? _hesap;   // hesap önbelleği (tarama + envanter sürümü)
    public string? Uyari { get; private set; }

    string Tablo => cfg["Imza:Tablo"] is { Length: > 0 } t && Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$") ? t : "dbo.DF_SERVIS_IMZA";
    double CbSaat => Math.Clamp(cfg.GetValue("Imza:CbSaat", 72.0), 1, 24 * 14);
    double IisDakika => Math.Clamp(cfg.GetValue("Imza:IisDakika", 30.0), 5, 24 * 60);
    int Parca => Math.Clamp(cfg.GetValue("Imza:ParcaIp", 40), 1, 200);
    TimeSpan Sure => TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Imza:SureSaniye", 120.0), 10, 900));
    int Dyn => cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);

    public ImzaIs? Durum { get { lock (_gate) return _is; } }
    public void Durdur() { lock (_gate) _cts?.Cancel(); }

    // ---------------- Tarama ----------------
    public async Task<ImzaIs> BaslatAsync(string mod, string user, CancellationToken ct)
    {
        lock (_gate) if (_is?.Durum == "calisiyor") throw new InvalidOperationException("Tarama zaten çalışıyor.");
        var env = await envanter.GetAsync(false, ct);
        var tum = env.HostIps.Where(ip => EnvanterSnapshot.TryToUInt(ip, out _)).Distinct()
            .OrderBy(ip => EnvanterSnapshot.TryToUInt(ip, out uint u) ? u : 0).ToList();
        ImzaTarama t;
        if (mod == "devam" && (_son ?? await OkuAsync(ct)) is { } eski && eski.Bitis == null)
            t = eski with { ToplamIp = tum.Count };
        else
            t = new ImzaTarama(DateTime.Now, null, CbSaat, IisDakika, [], [], [], tum.Count, user);
        var yapilan = t.Taranan.ToHashSet();
        var kalan = tum.Where(ip => !yapilan.Contains(ip)).ToList();
        var parcalar = kalan.Chunk(Parca).ToList();
        var cts = new CancellationTokenSource();
        var is_ = new ImzaIs("calisiyor", parcalar.Count, 0, kalan.Count == 0 ? "Taranacak sunucu kalmadı" : $"{kalan.Count} sunucu sırada",
            DateTime.Now, null, null, user, t.Sunucular.Count);
        lock (_gate) { _is = is_; _cts = cts; _son = t; }
        _ = Task.Run(() => CalisAsync(t, parcalar, user, cts.Token));
        return is_;
    }

    void Set(Func<ImzaIs, ImzaIs> f) { lock (_gate) if (_is != null) _is = f(_is); }

    async Task CalisAsync(ImzaTarama t, List<string[]> parcalar, string user, CancellationToken ct)
    {
        var sunucular = t.Sunucular.ToDictionary(s => s.Ip);
        var taranan = t.Taranan.ToList();
        var mesaj = t.Mesajlar.ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ImzaTarama Anlik(DateTime? bitis) => t with { Sunucular = sunucular.Values.ToList(), Taranan = taranan.ToList(), Mesajlar = mesaj.Distinct().TakeLast(40).ToList(), Bitis = bitis };
        try
        {
            for (int i = 0; i < parcalar.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = parcalar[i];
                string kalanSure = i > 0 ? $" · tahmini kalan {TimeSpan.FromSeconds(sw.Elapsed.TotalSeconds / i * (parcalar.Count - i)):hh\\:mm}" : "";
                Set(x => x with { Biten = i, Asama = $"{i + 1} / {parcalar.Count}. grup: Carbon Black ({p[0]} … {p.Length} sunucu){kalanSure}" });
                var cb = await CbAsync(p, mesaj, ct);
                Set(x => x with { Asama = $"{i + 1} / {parcalar.Count}. grup: IIS logu ({p.Length} sunucu){kalanSure}" });
                var iis = await IisAsync(p, mesaj, ct);
                foreach (var ip in p)
                {
                    var gelen = cb.Gelen.GetValueOrDefault(ip) ?? [];
                    var giden = cb.Giden.GetValueOrDefault(ip) ?? [];
                    var ii = iis.GetValueOrDefault(ip) ?? [];
                    if (gelen.Count + giden.Count + ii.Count > 0) sunucular[ip] = new ImzaSunucu(ip, gelen, giden, ii);
                    else sunucular.Remove(ip);
                    taranan.Add(ip);
                }
                Set(x => x with { Biten = i + 1, Sunucu = sunucular.Count });
                lock (_gate) { _son = Anlik(null); _hesap = null; }
                if ((i + 1) % 10 == 0) await YazAsync(Anlik(null), user);   // uzun işte ara kayıt (uygulama yeniden başlarsa "devam et")
            }
            var son = Anlik(DateTime.Now);
            lock (_gate) { _son = son; _hesap = null; }
            await YazAsync(son, user);
            Set(x => x with { Durum = "bitti", Biten = x.Toplam, Asama = $"Bitti · {sunucular.Count} sunucuda veri", Bitis = DateTime.Now });
        }
        catch (OperationCanceledException)
        {
            await YazAsync(Anlik(null), user);
            Set(x => x with { Durum = "durduruldu", Asama = "Durduruldu (kaldığı yerden devam edilebilir)", Bitis = DateTime.Now });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Servis imzası taraması hata verdi");
            await YazAsync(Anlik(null), user);
            Set(x => x with { Durum = "hata", Hata = ex.Message, Asama = "Hata", Bitis = DateTime.Now });
        }
    }

    static string Terimler(IEnumerable<string> ips) => "(" + string.Join(" OR ", ips.Select(ip => $"TERM({ip})")) + ")";
    static string Liste(IEnumerable<string> ips) => string.Join(",", ips.Select(ip => $"\"{ip}\""));

    async Task<(Dictionary<string, List<ImzaPort>> Gelen, Dictionary<string, List<string>> Giden)> CbAsync(string[] ips, List<string> mesaj, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" {Terimler(ips)} local_ip IN ({Liste(ips)})
            | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, process_name, "-"), lp=if(yon=="in", coalesce(local_port, "0"), "-")
            | stats count dc(remote_ip) as ist by local_ip yon lp yol
            """;
        var gelen = new Dictionary<string, List<ImzaPort>>(); var giden = new Dictionary<string, List<string>>();
        var end = DateTimeOffset.Now;
        try
        {
            var res = await SplunkService.ExportAsync(spl, new LookupQuery(ips[0], end.AddHours(-CbSaat), end), cfg, factory.CreateClient("splunk"), ct, "Splunk", Sure);
            if (res.Messages.Any(m => m.Contains("sınırında") || m.Contains("cevap vermedi"))) mesaj.Add($"Carbon Black {ips[0]} … grubunda süre sınırı (sonuç kısmi)");
            foreach (var r in res.Rows)
            {
                string ip = r.GetValueOrDefault("local_ip") ?? "", yol = r.GetValueOrDefault("yol") ?? "-", lp = r.GetValueOrDefault("lp") ?? "-";
                if (ip.Length == 0 || yol == "-") continue;
                long.TryParse(r.GetValueOrDefault("count"), out long n); int.TryParse(r.GetValueOrDefault("ist"), out int ist);
                if (r.GetValueOrDefault("yon") == "out") { (giden.TryGetValue(ip, out var l) ? l : giden[ip] = []).Add(yol); continue; }
                (gelen.TryGetValue(ip, out var g) ? g : gelen[ip] = []).Add(new ImzaPort(lp == "" ? "0" : lp, yol, n, ist));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add($"Carbon Black {ips[0]} … grubu: {ex.Message}"); }
        foreach (var k in giden.Keys.ToList()) giden[k] = giden[k].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (gelen, giden);
    }

    async Task<Dictionary<string, List<ImzaIis>>> IisAsync(string[] ips, List<string> mesaj, CancellationToken ct)
    {
        var idx = cfg.GetSection("HttpSys:IisIndexleri").Get<string[]>() is { Length: > 0 } a ? a : ["iis_msy", "iis_csy"];
        if (idx.Any(x => !Regex.IsMatch(x, @"^[A-Za-z0-9_\-\*]+$"))) return [];
        string spl = $"""
            search ({string.Join(" OR ", idx.Select(x => "index=" + x))}) {Terimler(ips)}
            | fields _raw
            | rex "^\S+ \S+ (?<iis_sip>\S+) \S+ (?<iis_uri>\S+) \S+ (?<iis_port>\d+) \S+ (?<iis_cip>\S+)"
            | search iis_sip IN ({Liste(ips)})
            | eval iis_seg=split(lower(iis_uri), "/")
            | eval iis_seg=mvfilter(NOT match(iis_seg, "^(|api|apis|v\d+(\.\d+)*|rest|services?|ws|odata|public|internal)$"))
            | eval iis_app=mvindex(iis_seg, 0), iis_app=if(isnull(iis_app) OR iis_app="", "/", iis_app)
            | stats count dc(iis_cip) as aist by iis_sip iis_port iis_app
            """;
        var sonuc = new Dictionary<string, List<ImzaIis>>();
        var end = DateTimeOffset.Now;
        try
        {
            var res = await SplunkService.ExportAsync(spl, new LookupQuery(ips[0], end.AddMinutes(-IisDakika), end), cfg, factory.CreateClient("splunk"), ct, "Splunk", Sure);
            if (res.Messages.Any(m => m.Contains("sınırında") || m.Contains("cevap vermedi"))) mesaj.Add($"IIS logu {ips[0]} … grubunda süre sınırı (sonuç kısmi)");
            var satir = res.Rows.Select(r => (Ip: r.GetValueOrDefault("iis_sip") ?? "", Port: r.GetValueOrDefault("iis_port") ?? "", App: r.GetValueOrDefault("iis_app") ?? "/",
                N: long.TryParse(r.GetValueOrDefault("count"), out long n) ? n : 0, AIst: int.TryParse(r.GetValueOrDefault("aist"), out int ai) ? ai : 0))
                .Where(x => x.Ip.Length > 0 && x.Port.Length > 0).ToList();
            foreach (var gi in satir.GroupBy(x => x.Ip))
            {
                // gürültü: tarama yolları + sunucunun portlarının yarısından çoğunda görünen, en çok 2 istemcili adlar (IP kartıyla aynı kural)
                int portSay = gi.Select(x => x.Port).Distinct().Count();
                var yaygin = gi.GroupBy(x => x.App).Where(g => portSay >= 3 && g.Select(x => x.Port).Distinct().Count() * 2 > portSay && g.Max(x => x.AIst) <= 2).Select(g => g.Key).ToHashSet();
                sonuc[gi.Key] = gi.GroupBy(x => x.Port).Select(gp =>
                {
                    var u = HttpSysEslesme.IisUyg(gp.Select(x => (x.App, "", x.N, x.AIst, (string?)null)), a => yaygin.Contains(a) || HttpSysEslesme.IisTarama(a), out _);
                    return new ImzaIis(gp.Key, u.Where(x => x.Ad != "/").Select(x => x.Ad).Take(15).ToList(), gp.Sum(x => x.N));
                }).Where(x => x.Uygulamalar.Count > 0).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add($"IIS logu {ips[0]} … grubu: {ex.Message}"); }
        return sonuc;
    }

    // ---------------- Kayıt (gzip'li JSON, tek satır) ----------------
    static byte[] Sikistir(ImzaTarama t)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) System.Text.Json.JsonSerializer.Serialize(gz, t, Js);
        return ms.ToArray();
    }
    static ImzaTarama? Ac(byte[] b)
    {
        using var gz = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
        return System.Text.Json.JsonSerializer.Deserialize<ImzaTarama>(gz, Js);
    }
    async Task YazAsync(ImzaTarama t, string user)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {Tablo} AS t USING (SELECT @a AS AD) AS s ON t.AD = s.AD
                WHEN MATCHED THEN UPDATE SET SONUC = @s, SUNUCU = @n, TAMAM = @b, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (AD, SONUC, SUNUCU, TAMAM, TARIH, KAYDEDEN) VALUES (@a, @s, @n, @b, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@a", "TUM");
            cmd.Parameters.Add("@s", System.Data.SqlDbType.VarBinary, -1).Value = Sikistir(t);
            cmd.Parameters.AddWithValue("@n", t.Sunucular.Count); cmd.Parameters.AddWithValue("@b", t.Bitis != null); cmd.Parameters.AddWithValue("@u", user);
            await cmd.ExecuteNonQueryAsync();
            Uyari = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Uyari = $"Kayıt tablosu ({Tablo}) yok; tarama yalnız bellekte tutuluyor (sql/servis_imza.sql)."; }
        catch (Exception ex) { log.LogWarning(ex, "Servis imzası taraması kaydedilemedi"); Uyari = "Tarama kaydedilemedi: " + ex.Message; }
    }
    async Task<ImzaTarama?> OkuAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT SONUC FROM {Tablo} WHERE AD = 'TUM'", conn);
            if (await cmd.ExecuteScalarAsync(ct) is byte[] b && Ac(b) is { } t) { lock (_gate) _son ??= t; return t; }
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Uyari = $"Kayıt tablosu ({Tablo}) yok; tarama yalnız bellekte tutuluyor (sql/servis_imza.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Servis imzası kaydı okunamadı"); }
        return null;
    }

    // ---------------- Eşleşme ----------------
    public async Task<ImzaSonuc> SonucAsync(CancellationToken ct)
    {
        ImzaTarama? t; lock (_gate) t = _son;
        t ??= await OkuAsync(ct);
        var env = await envanter.GetAsync(false, ct);
        lock (_gate) if (_hesap is { } h && ReferenceEquals(h.T, t) && h.Env == env.LoadedAt) return h.S with { Is = _is, Uyari = Uyari };
        var s = Hesapla(t, env);
        lock (_gate) if (t != null) _hesap = (t, env.LoadedAt, s);
        return s with { Is = Durum, Uyari = Uyari };
    }

    static string Norm(string? x) => Regex.Replace((x ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
    static string ExeAd(string yol) => yol.Replace('/', '\\').Split('\\').Last().Trim().ToLowerInvariant();
    static readonly HashSet<string> GenelKelime = ["exe", "dll", "app", "core", "web", "api", "service", "services", "zb", "zk", "zt"];
    static List<string> Kelime(string? x) => Regex.Split((x ?? "").ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length >= 2 && !GenelKelime.Contains(w)).Distinct().ToList();
    static double Jaccard(ICollection<string> a, ICollection<string> b) =>
        a.Count == 0 || b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / a.Union(b).Count();

    // HTTP.sys portu: ERT'deki WEBSITE / APPNAME / SERVICENAME adı bu sunucudaki tek bir iş exe'sinin yolunda geçiyorsa asıl exe odur (IP kartı httpSysApp ile aynı)
    static string? HttpSysExe(IEnumerable<string?> adlar, List<string> yollar)
    {
        var nm = adlar.Where(x => Norm(x).Length >= 4).Distinct().ToList();
        foreach (var a in nm)
        {
            var hit = yollar.Where(y => Norm(y).Contains(Norm(a))).Select(ExeAd).Distinct().ToList();
            if (hit.Count == 1) return yollar.First(y => ExeAd(y) == hit[0]);
        }
        foreach (var a in nm)
        {
            var ws = Kelime(a); if (ws.Count < 2) continue;
            var hit = yollar.Where(y => { var ew = Kelime(y).ToHashSet(); int n = ws.Count(ew.Contains); return n >= 2 && n >= ws.Count * 2.0 / 3; }).Select(ExeAd).Distinct().ToList();
            if (hit.Count == 1) return yollar.First(y => ExeAd(y) == hit[0]);
        }
        return null;
    }

    ImzaSonuc Hesapla(ImzaTarama? t, EnvanterSnapshot env)
    {
        var infra = AppTopology.InfraPorts(cfg);
        var veri = (t?.Sunucular ?? []).ToDictionary(s => s.Ip);
        var taranan = (t?.Taranan ?? []).ToHashSet();
        var satirlar = new List<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)>();
        bool Sistem(string? a) => a != null && a.StartsWith("Sistem Portudur", StringComparison.OrdinalIgnoreCase);

        foreach (var ip in env.HostIps.Where(ip => EnvanterSnapshot.TryToUInt(ip, out _)).Distinct())
        {
            var ert = env.AppsOnIp(ip).Where(h => !(int.TryParse(h.Port, out int pn) && pn >= Dyn)).GroupBy(h => h.Port ?? "").ToDictionary(g => g.Key, g => g.ToList());
            veri.TryGetValue(ip, out var v);
            var yollar = (v?.Gelen.Select(x => x.Yol) ?? []).Concat(v?.Giden ?? []).Where(y => !HttpSysEslesme.Cekirdek(y) && !SurecAi.GenelExe(ExeAd(y), y))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var portlar = ert.Keys.Where(p => p.Length > 0).ToList();
            foreach (var p in v?.Gelen.Select(x => x.Port) ?? [])
                if (p != "0" && !(int.TryParse(p, out int pn) && pn >= Dyn) && !portlar.Contains(p)) portlar.Add(p);
            foreach (var iisP in v?.Iis.Select(x => x.Port) ?? []) if (!portlar.Contains(iisP)) portlar.Add(iisP);
            string? sunucuAd = ert.Values.SelectMany(x => x).Select(h => h.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            foreach (var port in portlar)
            {
                var rows = ert.GetValueOrDefault(port) ?? [];
                var adlar = rows.Select(h => h.AppName).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string? website = rows.Select(h => h.Website).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                var gel = (v?.Gelen ?? []).Where(x => x.Port == port).OrderByDescending(x => x.Baglanti).ToList();
                var is_ = gel.Where(x => !HttpSysEslesme.Cekirdek(x.Yol)).ToList();
                string? yol = is_.FirstOrDefault()?.Yol; bool hs = false;
                if (yol == null && gel.Count > 0)
                {
                    yol = HttpSysExe(rows.SelectMany(h => new[] { h.Website, h.AppName, h.ServiceName }), yollar);
                    hs = true;
                    yol ??= gel[0].Yol;   // çözülemedi: ntoskrnl kalır
                }
                var iis = v?.Iis.FirstOrDefault(x => x.Port == port)?.Uygulamalar ?? [];
                string? exe = yol == null ? null : HttpSysEslesme.Cekirdek(yol) ? "ntoskrnl.exe" : ExeAd(yol);
                bool genel = exe != null && exe != "ntoskrnl.exe" && SurecAi.GenelExe(exe, yol);
                bool alt = infra.Contains(port);
                string? anahtar = exe == null || exe == "ntoskrnl.exe" || genel || alt ? null : exe + "|" + port;
                string durum = rows.Count == 0 ? "ertyok" : adlar.Count > 0 ? "kayitli" : "bos";
                if (durum != "kayitli" && exe == null && iis.Count == 0) durum = taranan.Contains(ip) ? (rows.Count == 0 ? "ertyok" : "veriyok") : "taranmadi";
                var s = new ImzaSatir(ip, port, sunucuAd, adlar.Count > 0 ? string.Join(" | ", adlar) : null, website, exe, yol, hs && exe != "ntoskrnl.exe",
                    gel.Sum(x => x.Baglanti), gel.Count > 0 ? gel.Max(x => x.Istemci) : 0, iis, durum, null, null, null, null, alt);
                satirlar.Add((s, iis.ToHashSet(StringComparer.OrdinalIgnoreCase), anahtar));
            }
        }

        // SÖZLÜK: ERT'de tek ve sistem dışı adı olan satırlar
        var kaynak = satirlar.Where(x => x.S.Durum == "kayitli" && !x.S.Ert!.Contains(" | ") && !Sistem(x.S.Ert) && !x.S.Altyapi).ToList();
        var exeSoz = kaynak.Where(x => x.Anahtar != null).GroupBy(x => x.Anahtar!).ToDictionary(g => g.Key, g => g.ToList());
        var iisSoz = kaynak.Where(x => x.IisSet.Count > 0).ToList();
        static List<ImzaAday> Say(IEnumerable<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)> l) =>
            l.GroupBy(x => x.S.Ert!, StringComparer.OrdinalIgnoreCase).Select(g => new ImzaAday(g.Key, g.Select(x => x.S.Ip).Distinct().Count()))
                .OrderByDescending(a => a.Sunucu).ToList();

        var sonuc = new List<ImzaSatir>(satirlar.Count);
        foreach (var (s, iisSet, anahtar) in satirlar)
        {
            if (s.Altyapi || (s.Durum is not ("bos" or "ertyok" or "kayitli"))) { sonuc.Add(s); continue; }
            List<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)> c = [];
            string? guven = null, nasil = null;
            if (anahtar != null && exeSoz.TryGetValue(anahtar, out var ec))
            {
                c = ec.Where(x => x.S.Ip != s.Ip).ToList();
                if (c.Count > 0 && iisSet.Count > 0)
                {
                    var g = c.Where(x => Jaccard(iisSet, x.IisSet) >= 0.5).ToList();
                    if (g.Count > 0) { c = g; guven = "güçlü"; nasil = $"{s.Exe}:{s.Port} + IIS uygulamaları ortak"; }
                }
                if (c.Count > 0 && guven == null) { guven = "orta"; nasil = $"aynı exe + port ({s.Exe}:{s.Port})"; }
            }
            if (c.Count == 0 && iisSet.Count > 0 && s.Durum != "kayitli")
            {
                c = iisSoz.Where(x => x.S.Ip != s.Ip && Jaccard(iisSet, x.IisSet) >= 0.6).ToList();
                if (c.Count > 0) { guven = "zayıf"; nasil = "yalnız IIS uygulamaları ortak (" + string.Join(", ", iisSet.Take(4)) + ")"; }
            }
            if (c.Count == 0) { sonuc.Add(s); continue; }
            var ad = Say(c);
            int top = ad.Sum(a => a.Sunucu);
            var en = ad[0];
            bool karisik = ad.Count > 1 && en.Sunucu < top * 0.6;
            if (s.Durum == "kayitli")
            {
                // ERT dolu ama aynı servis başka sunucularda farklı adla (≥ 2 sunucu, açık çoğunluk) → uyumsuzluk
                if (!ad.Any(a => string.Equals(a.Ad, s.Ert, StringComparison.OrdinalIgnoreCase)) && en.Sunucu >= 2 && !karisik)
                    sonuc.Add(s with { Durum = "uyumsuz", Oneri = en.Ad, Guven = guven, Not = $"{nasil}: başka {en.Sunucu} sunucuda '{en.Ad}' olarak kayıtlı", Adaylar = ad.Take(8).ToList() });
                else sonuc.Add(s);
                continue;
            }
            if (karisik && guven == "güçlü") guven = "orta"; else if (karisik) guven = "zayıf";
            sonuc.Add(s with
            {
                Oneri = en.Ad, Guven = guven, Adaylar = ad.Take(8).ToList(),
                Not = $"{nasil} → {en.Sunucu} sunucuda '{en.Ad}'" + (ad.Count > 1 ? $" (toplam {top} sunucu, {ad.Count} farklı ad{(karisik ? " — net çoğunluk yok" : "")})" : "")
            });
        }

        var sozluk = exeSoz.Select(kv =>
        {
            var ad = Say(kv.Value);
            var iis = kv.Value.SelectMany(x => x.IisSet).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(8).ToList();
            var p = kv.Key.Split('|');
            return new ImzaSozluk(p[0], p[1], ad, iis, ad.Sum(a => a.Sunucu), ad.Count > 1);
        }).OrderByDescending(x => x.Sunucu).ThenBy(x => x.Exe).Take(5000).ToList();

        var kayit = t == null ? null : (object)new
        {
            baslangic = t.Baslangic, bitis = t.Bitis, cbSaat = t.CbSaat, iisDakika = t.IisDakika, taranan = t.Taranan.Count, toplamIp = t.ToplamIp,
            sunucu = t.Sunucular.Count, kaydeden = t.Kaydeden
        };
        return new ImzaSonuc(null, kayit, sonuc.OrderBy(x => EnvanterSnapshot.TryToUInt(x.Ip, out uint u) ? u : 0)
            .ThenBy(x => int.TryParse(x.Port, out int p) ? p : 999999).ToList(), sozluk, t?.Mesajlar ?? [], null);
    }
}
