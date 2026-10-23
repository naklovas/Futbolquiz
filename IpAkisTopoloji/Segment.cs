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
record SegmentIstek(string? Segment, int? Saat, string? Etiket = null, string? Mod = null);   // Mod: boş = kayıtlıysa aç, "yeniden" = baştan tara, "ekle" = yalnız yeni sunucular
record SegmentKayit(string Tur, string Ad, DateTime Tarih, int Saat, int Sunucu, int Satir, string? Kaydeden);
record SegmentAiIstek(List<string>? Ips, bool Yeniden);
record SegmentSatir(string Ip, string? Sunucu, string? Segment, string Port, string Exe, string Yol, string? Urun, bool Genel, bool Altyapi, bool Dinamik,
    long Baglanti, int Istemci, List<string> Istemciler, List<string> ErtUygulama, string ErtDurum, string? Son);
record SegmentGiden(string Ip, string? Sunucu, string Exe, string Yol, string? Urun, bool Genel, long Baglanti, int Hedef, List<string> Hedefler);
record SegmentSunucu(string Ip, string? Sunucu, string? Segment, List<string> ErtUygulamalar, int ErtPort, int CbPort, bool CbVar);
record SegmentSonuc(string Tur, string Segment, List<string> Cidrler, int Saat, DateTimeOffset Baslangic, DateTimeOffset Bitis, List<SegmentSunucu> Sunucular,
    List<SegmentSatir> Satirlar, List<SegmentGiden> Giden, List<string> Mesajlar, long Ms, Dictionary<string, NessusSunucu>? Nessus = null, string? NessusDurum = null);
record SegmentIs(string Id, string Segment, bool Calisiyor, string Asama, int Toplam, int Biten, string? Hata, string Kullanici,
    DateTimeOffset Basladi, SegmentSonuc? Sonuc, SegmentKayit? Kayit = null, string? Not = null);

// AI ile iş uygulaması tahmini (kullanıcı: "sunucu sunucu AI'a gönderelim, exe'lerden çalışan iş uygulamalarını tahmin etsin, bir kolona"):
// tarama sonucundaki exe'ler sunucu başına SurecAi'ye (IP kartındaki "Bu sunucuda hangi uygulama çalışıyor?" ile aynı prompt) gider;
// aynı anda Segment:AiParalel (3) sunucu; sonuç tarama işiyle birlikte bellekte, kaydedilmez.
record SegmentAiDurum(bool Calisiyor, int Toplam, int Biten, Dictionary<string, SurecAiSonuc> Sonuclar, Dictionary<string, string> Hatalar,
    int Kayitli = 0, Dictionary<string, string>? Tarihler = null, string? Uyari = null);

sealed class SegmentTaramaService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, IslevService islev, EksikService eksik,
    NessusService nessus, ILogger<SegmentTaramaService> log)
{
    sealed class AiIs
    {
        public ConcurrentDictionary<string, SurecAiSonuc> Sonuc = new();
        public ConcurrentDictionary<string, string> Hata = new(), Tarih = new();
        public int Toplam, Biten, Kayitli; public volatile bool Calisiyor; public string? Uyari;
        public CancellationTokenSource Cts = new();
    }
    readonly ConcurrentDictionary<string, AiIs> _ai = new();

    public SegmentAiDurum? AiDurum(string id) => _ai.TryGetValue(id, out var a)
        ? new(a.Calisiyor, a.Toplam, a.Biten, new(a.Sonuc), new(a.Hata), a.Kayitli, new(a.Tarih), a.Uyari) : null;

    // ---- Kayıt (Segment:AiTablosu, sql/segment_ai.sql; tablo yoksa bellekte) ----
    readonly ConcurrentDictionary<string, (SurecAiSonuc Sonuc, string Exeler, DateTime Tarih)> _aiBellek = new();
    string AiTablo => cfg["Segment:AiTablosu"] is { Length: > 0 } t && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")
        ? t : "dbo.DF_SEGMENT_AI_TAHMIN";
    static readonly System.Text.Json.JsonSerializerOptions Js = new(System.Text.Json.JsonSerializerDefaults.Web);
    async Task<(Dictionary<string, (SurecAiSonuc Sonuc, string Exeler, DateTime Tarih)> Kayit, string? Uyari)> AiOkuAsync(List<string> ips, CancellationToken ct)
    {
        var d = new Dictionary<string, (SurecAiSonuc, string, DateTime)>();
        foreach (var ip in ips) if (_aiBellek.TryGetValue(ip, out var m)) d[ip] = m;
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            foreach (var part in ips.Chunk(500))
            {
                await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("", conn);
                cmd.CommandText = $"SELECT IP, SONUC, EXELER, TARIH FROM {AiTablo} WHERE IP IN ({string.Join(",", part.Select((ip, i) => { cmd.Parameters.AddWithValue("@p" + i, ip); return "@p" + i; }))})";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    if (System.Text.Json.JsonSerializer.Deserialize<SurecAiSonuc>(r.GetString(1), Js) is { } sn)
                        d[r.GetString(0)] = (sn, r.IsDBNull(2) ? "" : r.GetString(2), r.GetDateTime(3));
            }
            return (d, null);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208)
        { return (d, $"AI tahmin tablosu ({AiTablo}) yok; tahminler yalnız bellekte tutuluyor (sql/segment_ai.sql)."); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { log.LogWarning(ex, "Segment AI tahminleri okunamadı"); return (d, "AI tahminleri okunamadı: " + ex.Message); }
    }
    async Task AiYazAsync(SurecAiSonuc sonuc, string exeler, string user)
    {
        _aiBellek[sonuc.Ip] = (sonuc, exeler, DateTime.Now);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {AiTablo} AS t USING (SELECT @ip AS IP) AS s ON t.IP = s.IP
                WHEN MATCHED THEN UPDATE SET SONUC = @s, EXELER = @e, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (IP, SONUC, EXELER, TARIH, KAYDEDEN) VALUES (@ip, @s, @e, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@ip", sonuc.Ip);
            cmd.Parameters.Add("@s", System.Data.SqlDbType.NVarChar, -1).Value = System.Text.Json.JsonSerializer.Serialize(sonuc, Js);
            cmd.Parameters.Add("@e", System.Data.SqlDbType.NVarChar, -1).Value = exeler;
            cmd.Parameters.AddWithValue("@u", user);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) when (ex is not Microsoft.Data.SqlClient.SqlException { Number: 208 }) { log.LogWarning(ex, "Segment AI tahmini kaydedilemedi: {Ip}", sonuc.Ip); }
        catch (Microsoft.Data.SqlClient.SqlException) { }
    }
    public void AiDurdur(string id) { if (_ai.TryGetValue(id, out var a)) a.Cts.Cancel(); }

    // Tarama bitince kendiliğinden çalışır (kullanıcı: "düğme olmasın, segmenti seçeyim, satır satır tahmin etsin, kendi kaydetsin; tekrar
    // tekrar tahmin etmeyeyim"): kayıtlı tahmini olan ve iş exe'leri kayıttakilerin içinde kalan sunucular AI'a GİTMEZ (kayıt gösterilir);
    // yeni sunucu ya da yeni iş exe'si görülen sunucu sorulur ve kaydedilir. yeniden = kayıtlı olanları da yeniden sor.
    public async Task<SegmentAiDurum> AiBaslatAsync(string id, List<string>? ips, bool yeniden, string user, CancellationToken ct)
    {
        if (Durum(id)?.Sonuc is not { } r) throw new InvalidOperationException("Önce tarama bitmeli (ya da sonucun süresi dolmuş olabilir).");
        if (_ai.TryGetValue(id, out var eski) && eski.Calisiyor) throw new InvalidOperationException("AI tahmini zaten çalışıyor.");
        // iş hemen görünür olsun (arayüz "başlamadı" sanmasın); kayıtlar okununca doldurulur
        var a = new AiIs { Calisiyor = true, Uyari = "Kayıtlı tahminler okunuyor…" };
        if (eski != null) foreach (var (k2, v) in eski.Sonuc) a.Sonuc[k2] = v;
        _ai[id] = a;
        var env = await envanter.GetAsync(false, ct);
        // sunucu başına exe'ler: gelen satırlar (port, bağlantı, istemci) + yalnız giden
        var tum = r.Satirlar.Select(x => x.Ip).Concat(r.Giden.Select(g => g.Ip)).Distinct()
            .Where(ip => ips is not { Count: > 0 } || ips.Contains(ip))
            .Select(ip => new SurecAiIstek(ip,
                r.Satirlar.Where(x => x.Ip == ip).GroupBy(x => x.Yol, StringComparer.OrdinalIgnoreCase).Select(g => new SurecAiExe(g.First().Exe, g.Key, null,
                        g.Select(x => new ProcPort(x.Port, x.Baglanti, x.Istemci)).ToList(), 0, g.SelectMany(x => x.Istemciler).Distinct().Take(8).ToList(), null))
                    .Concat(r.Giden.Where(x => x.Ip == ip).Select(x => new SurecAiExe(x.Exe, x.Yol, null, null, x.Baglanti, null, x.Hedefler)))
                    .ToList()))
            .Where(q => q.Surecler!.Any(e => !SurecAi.Genel(e)))   // yalnız genel süreçli sunucu AI'a gitmez
            .ToList();
        static string Exeler(SurecAiIstek q) => string.Join("|", q.Surecler!.Where(e => !SurecAi.Genel(e)).Select(e => (e.Yol ?? e.Ad).ToLowerInvariant()).Distinct().OrderBy(x => x));
        var (kayit, uyari) = await AiOkuAsync(tum.Select(q => q.Ip!).ToList(), ct);
        a.Uyari = uyari ?? (tum.Count == 0 ? "AI'a gönderilecek iş programı yok (yalnız genel Windows / ajan süreçleri görüldü)." : null);
        var istek = new List<SurecAiIstek>();
        foreach (var q in tum)
        {
            if (!yeniden && kayit.TryGetValue(q.Ip!, out var k) && Exeler(q).Split('|').All(e => k.Exeler.Split('|').Contains(e)))
            { a.Sonuc[q.Ip!] = SurecAi.Katalogla(k.Sonuc, env, eksik); a.Tarih[q.Ip!] = k.Tarih.ToString("yyyy-MM-dd HH:mm"); a.Kayitli++; }   // eski kayıtlar da katalogla eşlenir
            else istek.Add(q);
        }
        a.Toplam = istek.Count; a.Calisiyor = istek.Count > 0;
        log.LogInformation("Segment tarama AI: {Id} {N} sunucu sorulacak, {K} kayıtlı", id, istek.Count, a.Kayitli);
        if (istek.Count > 0)
            _ = Task.Run(async () =>
            {
                using var gate = new SemaphoreSlim(Math.Clamp(cfg.GetValue("Segment:AiParalel", 3), 1, 8));
                await Task.WhenAll(istek.Select(async q =>
                {
                    try { await gate.WaitAsync(a.Cts.Token); } catch (OperationCanceledException) { return; }
                    try
                    {
                        var sonuc = await SurecAi.RunAsync(q, env, islev, eksik, a.Cts.Token,
                            r.Nessus?.TryGetValue(q.Ip!, out var ns) == true ? NessusService.Metin(ns) : null);
                        a.Sonuc[q.Ip!] = sonuc; a.Tarih[q.Ip!] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); a.Hata.TryRemove(q.Ip!, out _);
                        await AiYazAsync(sonuc, Exeler(q), user);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { a.Hata[q.Ip!] = ex.Message; }
                    finally { Interlocked.Increment(ref a.Biten); gate.Release(); }
                }));
                a.Calisiyor = false;
            });
        return AiDurum(id)!;
    }

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
            sorgular = IpSorgulari(list);
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
        string tur = ipler != null ? "etiket" : "segment", gorunen = (ipler != null ? "Etiket " : "") + ad;
        // Kayıtlı tarama (kullanıcı: "her seferinde tekrar sorgulamayayım, kaydetsin; istersem yeniden çek, ya da yeni sunucuları ekle")
        string mod = (req.Mod ?? "").Trim().ToLowerInvariant();
        var kayit = mod == "yeniden" ? null : await KayitOkuAsync(tur, ad, ct);
        if (kayit != null && mod != "ekle")
        {
            _isler[id] = (new SegmentIs(id, gorunen, false, "Kayıttan açıldı", 0, 0, null, user, DateTimeOffset.Now, kayit.Value.Sonuc, kayit.Value.Bilgi), cts);
            log.LogInformation("Segment tarama kayıttan: {User} {Tur} {Ad} ({Tarih})", user, tur, ad, kayit.Value.Bilgi.Tarih);
            _ = Task.Run(async () => { try { await AiBaslatAsync(id, null, false, user, CancellationToken.None); } catch (Exception ex) { _ai[id] = new AiIs { Uyari = "AI tahmini başlatılamadı: " + ex.Message }; } });
            return _isler[id].Is;
        }
        SegmentSonuc? eski = null;
        if (kayit != null)   // mod == "ekle": kayıtta olmayan ya da geçen sefer Carbon Black verisi gelmeyen sunucular
        {
            eski = kayit.Value.Sonuc;
            var bilinen = eski.Sunucular.Where(x => x.CbVar).Select(x => x.Ip).ToHashSet();
            var hepsi = ipler ?? env.HostIps.Where(ip => InCidrs(ip, cidrs.Select(c => { EnvanterSnapshot.TryParseCidr(c, out uint n, out int pp); return (n, pp); }).ToList())).ToHashSet();
            var yeni = hepsi.Where(ip => !bilinen.Contains(ip) && LookupQuery.TryParseIpv4(ip, out _)).ToList();
            if (yeni.Count == 0)
            {
                _isler[id] = (new SegmentIs(id, gorunen, false, "Kayıttan açıldı", 0, 0, null, user, DateTimeOffset.Now, eski, kayit.Value.Bilgi,
                    "Eklenecek yeni sunucu yok: ERT'deki tüm sunucular kayıtlı taramada var (Carbon Black verisi gelmeyenler dahil yeniden denenecek sunucu yok)."), cts);
                _ = Task.Run(async () => { try { await AiBaslatAsync(id, null, false, user, CancellationToken.None); } catch { } });
                return _isler[id].Is;
            }
            ipler = yeni.ToHashSet();
            sorgular = IpSorgulari(yeni);
            saat = eski.Saat;   // birleşen sonuç aynı aralık uzunluğunda kalsın
        }
        _isler[id] = (new SegmentIs(id, gorunen, true, eski != null ? $"Yeni sunucular Carbon Black'te sorgulanıyor ({ipler!.Count} IP)" : "Carbon Black sorgulanıyor",
            sorgular.Count, 0, null, user, DateTimeOffset.Now, null), cts);
        log.LogInformation("Segment tarama: {User} {Tur} {Ad} ({Cidr}), {Saat} saat, {Parca} sorgu", user, tur, ad, string.Join(",", cidrs), saat, sorgular.Count);
        _ = Task.Run(async () =>
        {
            try
            {
                var sonuc = await TaraAsync(id, env, tur, ad, cidrs, ipler, sorgular, saat, cts.Token);
                sonuc = await NessusEkleAsync(id, sonuc, cts.Token);
                string? not = null;
                if (eski != null)
                {
                    int bulunan = sonuc.Sunucular.Count(x => x.CbVar);
                    not = $"{ipler!.Count} yeni / verisiz sunucu tarandı, {bulunan} tanesinde Carbon Black verisi bulundu ve kayda eklendi.";
                    sonuc = Birlestir(eski, sonuc, ipler!);
                }
                var bilgi = await KayitYazAsync(tur, ad, sonuc, user);
                Set(id, i => i with { Calisiyor = false, Asama = "Bitti", Sonuc = sonuc, Kayit = bilgi, Not = not });
                try { await AiBaslatAsync(id, null, false, user, CancellationToken.None); }   // kendiliğinden AI tahmini (kayıtlılar sorulmaz)
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Segment tarama AI başlatılamadı: {Segment}", ad);
                    if (_ai.TryGetValue(id, out var ai)) { ai.Calisiyor = false; ai.Uyari = "AI tahmini başlatılamadı: " + ex.Message; }
                    else _ai[id] = new AiIs { Uyari = "AI tahmini başlatılamadı: " + ex.Message };
                }
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

    // Nessus / Tenable.sc (Nessus.cs): taranan IP'lerin repository'deki son tarama bilgisi; hata taramayı bozmaz, NessusDurum'a yazılır
    async Task<SegmentSonuc> NessusEkleAsync(string id, SegmentSonuc s, CancellationToken ct)
    {
        if (!nessus.Acik) return s;
        var ips = s.Sunucular.Select(x => x.Ip).ToList();
        Set(id, i => i with { Asama = $"Nessus (Tenable.sc) sorgulanıyor ({ips.Count} IP)" });
        try
        {
            var n = await nessus.SunucularAsync(ips, (b, t) => Set(id, i => i with { Asama = $"Nessus (Tenable.sc) sorgulanıyor ({b + 1} / {t})" }), ct);
            return s with { Nessus = n, NessusDurum = $"Nessus: {n.Count} / {ips.Count} IP'de tarama kaydı" };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Segment tarama Nessus sorgusu başarısız");
            return s with { NessusDurum = "Nessus sorgulanamadı: " + ex.Message };
        }
    }

    List<(string Arama, string? Kosul, string Etiket)> IpSorgulari(IEnumerable<string> list) =>
        list.Chunk(Math.Clamp(cfg.GetValue("Segment:ParcaIp", 40), 1, 200)).Select(c => (
            $"({string.Join(" OR ", c.Select(ip => $"TERM({ip})"))}) local_ip IN ({string.Join(",", c.Select(ip => $"\"{ip}\""))})",
            (string?)null, $"{c[0]} … ({c.Length} IP)")).ToList();

    // "Yeni sunucuları ekle": kayıtlı sonuçtan yeni taranan IP'lerin eski satırları çıkar, yenileri girer (Nessus da birleşir)
    static SegmentSonuc Birlestir(SegmentSonuc eski, SegmentSonuc yeni, HashSet<string> ips)
    {
        static uint U(string ip) => EnvanterSnapshot.TryToUInt(ip, out uint u) ? u : 0;
        var nes = new Dictionary<string, NessusSunucu>(eski.Nessus ?? []);
        foreach (var (k, v) in yeni.Nessus ?? []) nes[k] = v;
        return eski with
        {
            Sunucular = eski.Sunucular.Where(x => !ips.Contains(x.Ip)).Concat(yeni.Sunucular).OrderBy(x => U(x.Ip)).ToList(),
            Satirlar = eski.Satirlar.Where(x => !ips.Contains(x.Ip)).Concat(yeni.Satirlar).OrderBy(x => U(x.Ip)).ThenBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ToList(),
            Giden = eski.Giden.Where(x => !ips.Contains(x.Ip)).Concat(yeni.Giden).OrderBy(x => U(x.Ip)).ToList(),
            Mesajlar = eski.Mesajlar.Concat(yeni.Mesajlar).Distinct().Take(20).ToList(),
            Nessus = nes.Count > 0 ? nes : null, NessusDurum = yeni.NessusDurum ?? eski.NessusDurum,
            Bitis = yeni.Bitis, Ms = eski.Ms + yeni.Ms
        };
    }

    // ---- Kayıtlı taramalar (Segment:TaramaTablosu, sql/segment_tarama.sql; tür + ad başına son tarama, gzip'li JSON; tablo yoksa bellekte) ----
    readonly ConcurrentDictionary<string, (SegmentSonuc Sonuc, SegmentKayit Bilgi)> _kayitBellek = new(StringComparer.OrdinalIgnoreCase);
    string TaramaTablo => cfg["Segment:TaramaTablosu"] is { Length: > 0 } t && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")
        ? t : "dbo.DF_SEGMENT_TARAMA";
    public string? KayitUyari { get; private set; }
    static byte[] Sikistir(SegmentSonuc s)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) System.Text.Json.JsonSerializer.Serialize(gz, s, Js);
        return ms.ToArray();
    }
    static SegmentSonuc? Ac(byte[] b)
    {
        using var gz = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
        return System.Text.Json.JsonSerializer.Deserialize<SegmentSonuc>(gz, Js);
    }

    async Task<(SegmentSonuc Sonuc, SegmentKayit Bilgi)?> KayitOkuAsync(string tur, string ad, CancellationToken ct)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT SONUC, TARIH, SAAT, SUNUCU, SATIR, KAYDEDEN FROM {TaramaTablo} WHERE TUR = @t AND AD = @a", conn);
            cmd.Parameters.AddWithValue("@t", tur); cmd.Parameters.AddWithValue("@a", ad);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && Ac((byte[])r[0]) is { } s)
                return (s, new SegmentKayit(tur, ad, r.GetDateTime(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.IsDBNull(5) ? null : r.GetString(5)));
            return _kayitBellek.TryGetValue(tur + "|" + ad, out var m) ? m : null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208)
        {
            KayitUyari = $"Tarama kayıt tablosu ({TaramaTablo}) yok; taramalar yalnız bellekte tutuluyor (sql/segment_tarama.sql).";
            return _kayitBellek.TryGetValue(tur + "|" + ad, out var m) ? m : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Kayıtlı segment taraması okunamadı: {Tur} {Ad}", tur, ad);
            return _kayitBellek.TryGetValue(tur + "|" + ad, out var m) ? m : null;
        }
    }

    async Task<SegmentKayit> KayitYazAsync(string tur, string ad, SegmentSonuc s, string user)
    {
        var bilgi = new SegmentKayit(tur, ad, DateTime.Now, s.Saat, s.Sunucular.Count, s.Satirlar.Count, user);
        _kayitBellek[tur + "|" + ad] = (s, bilgi);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {TaramaTablo} AS t USING (SELECT @t AS TUR, @a AS AD) AS s ON t.TUR = s.TUR AND t.AD = s.AD
                WHEN MATCHED THEN UPDATE SET SONUC = @s, SAAT = @h, SUNUCU = @n, SATIR = @r, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (TUR, AD, SONUC, SAAT, SUNUCU, SATIR, TARIH, KAYDEDEN) VALUES (@t, @a, @s, @h, @n, @r, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@t", tur); cmd.Parameters.AddWithValue("@a", ad);
            cmd.Parameters.Add("@s", System.Data.SqlDbType.VarBinary, -1).Value = Sikistir(s);
            cmd.Parameters.AddWithValue("@h", s.Saat); cmd.Parameters.AddWithValue("@n", s.Sunucular.Count); cmd.Parameters.AddWithValue("@r", s.Satirlar.Count);
            cmd.Parameters.AddWithValue("@u", user);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { KayitUyari = $"Tarama kayıt tablosu ({TaramaTablo}) yok; taramalar yalnız bellekte (sql/segment_tarama.sql)."; }
        catch (Exception ex) { log.LogWarning(ex, "Segment taraması kaydedilemedi: {Tur} {Ad}", tur, ad); }
        return bilgi;
    }

    // Sayfadaki seçici için kayıtlı taramaların listesi (sonuç gövdesi okunmaz)
    public async Task<List<SegmentKayit>> KayitlarAsync(CancellationToken ct)
    {
        var list = _kayitBellek.Values.Select(v => v.Bilgi).ToDictionary(k => k.Tur + "|" + k.Ad, StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT TUR, AD, TARIH, SAAT, SUNUCU, SATIR, KAYDEDEN FROM {TaramaTablo}", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list[r.GetString(0) + "|" + r.GetString(1)] = new SegmentKayit(r.GetString(0), r.GetString(1), r.GetDateTime(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6));
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { KayitUyari = $"Tarama kayıt tablosu ({TaramaTablo}) yok; taramalar yalnız bellekte (sql/segment_tarama.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Kayıtlı segment taramaları listelenemedi"); }
        return list.Values.OrderByDescending(k => k.Tarih).ToList();
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
