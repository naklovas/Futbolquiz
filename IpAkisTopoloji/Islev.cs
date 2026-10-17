using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// İşlev haritası: bir etiketin (ör. VERIAMBARI) sunucularını AI ile işlevlerine göre kümeler.
// 1) Her sunucu için bilgi özeti: ERT (uygulama/port, muhafız), segment, VIP, MSY (Envanter), ÇSY (CsyEnvanter),
//    Envanter Kahini (SONUCLAR), SM sunucu kurulum talepleri ve iş istekleri (REQUESTM1; kaynak/hedef IP, hostname,
//    çoklu erişim blob'u). Hepsi doku DB'sinde (ConnectionStrings:Envanter). Bağımlılık / trafik hesaplanmaz.
// 2) AI'a paketler halinde gönderilir: sunucu başına işlev, rol, güven, gerekçe.
// 3) Tüm işlevler tek istekte 8–15 kümeye birleştirilir.
// Sonuç ERT tablosunda: Islev:KumeKolonu (AI_KUME) ve Islev:DetayKolonu (AI_DETAY, JSON). Elle düzeltilenler AI'ca ezilmez.
// Arayüz: Topoloji sekmesi → küme → segment → VIP → sunucu.
// ---------------------------------------------------------------------------
// Takma / TakmaKaynak: kullanıcı kümeyi yeniden adlandırdıysa yeni ad ve AI'ın verdiği asıl küme adı (yeniden kümelemede korunur).
record IslevDetay(string? Islev, string? Rol, int Guven, string? Gerekce, string? KumeAciklama, string? Tarih, string? Kaynak,
    string? Takma = null, string? TakmaKaynak = null);
record IslevSunucu(string Ip, string? Hostname, List<string> Apps, string? Muhafiz, string? Rol, int? Guven, bool Elle);
record IslevVip(string Vip, string? Pool, string? App, List<IslevSunucu> Sunucular);
record IslevSegment(string Ad, string? Cidr, List<IslevVip> Vipler, List<IslevSunucu> Sunucular);
record IslevKume(string Ad, string? Aciklama, int Sunucu, List<IslevSegment> Segmentler);
record IslevDurum(bool Calisiyor, string? Asama, int Toplam, int Biten, int Hata, string? Mesaj, string? Etiket,
    string? Baslatan, DateTimeOffset? Baslangic, DateTimeOffset? Bitis, List<string> Hatalar);
record IslevHarita(string Etiket, int Toplam, int Siniflanan, List<IslevKume> Kumeler, IslevDurum Durum, bool Yazabilir, string? Uyari);
record IslevCalistir(string? Etiket, bool Hepsi, bool SadeceKumele);
record IslevDuzelt(List<string>? Ips, string? Kume, string? Aciklama = null);
record IslevAd(string? Etiket, string? Eski, string? Yeni);
record SmKayit(string No, string? Tarih, string? Tur, string? Baslik, string? Aciklama, string? Kaynak, string? Hedef,
    string? Birim, string? Yon, string? Link);
record IslevProfil(string Ip, string Metin, List<string> Msy, List<string> Csy, List<string> Kahin, List<SmKayit> Kurulum,
    List<SmKayit> Talepler, List<string> Hatalar, IslevDetay? Detay, string? Kume);

sealed class IslevService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ILogger<IslevService> log)
{
    static readonly Regex SafeName = new(@"^[A-Za-z_][A-Za-z0-9_]*$");
    static readonly Regex SafeTable = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    const string Sinifsiz = "Sınıflanmamış";

    readonly object _gate = new();
    IslevDurum _durum = new(false, null, 0, 0, 0, null, null, null, null, null, []);
    CancellationTokenSource? _cts;
    Dictionary<string, (string? Kume, IslevDetay? Detay)>? _sonuc;
    DateTimeOffset _sonucAt;

    public IslevDurum Durum { get { lock (_gate) return _durum; } }

    // Sabit işlev kataloğu: AI 1. aşamada bunlardan birini seçer, böylece "ETL / Veri entegrasyonu" ile "ETL ve Entegrasyon"
    // ayrı küme olmaz. Islev:Kategoriler ([{ "Ad": "…", "Aciklama": "…" }]) ile değiştirilebilir.
    static readonly (string Ad, string Aciklama)[] DefaultKategoriler =
    [
        ("Kaynak sistem veritabanı", "Veri ambarına veri sağlayan operasyonel sistemlerin (core banking, kart, kredi…) veritabanları."),
        ("ETL / Veri entegrasyonu", "Kaynaklardan veriyi çekip dönüştüren ve yükleyen ETL / ELT sunucuları (ODI, Informatica, DataStage, SSIS, GoldenGate)."),
        ("Staging / Ara katman", "Yükleme öncesi verinin geçici tutulduğu ara veritabanları ve dosya alanları."),
        ("Veri ambarı veritabanı", "Veri ambarının ana veritabanı düğümleri (Exadata, Oracle, Teradata, SQL Server)."),
        ("Raporlama / BI", "Rapor ve analiz sunucuları (OBIEE, Tableau, Power BI, Qlik, SSRS, Cognos)."),
        ("Analitik / Veri bilimi", "Analitik model, veri bilimi ve büyük veri sunucuları (SAS, Python/R, Hadoop, Spark)."),
        ("Dosya aktarımı", "Dosya alıp gönderen SFTP / FTP / MFT sunucuları."),
        ("Uygulama / Web", "Ekosistemdeki uygulama, web ve API sunucuları."),
        ("İş zamanlama / Orkestrasyon", "Batch işlerini zamanlayan ve tetikleyen sunucular (Control-M, Automic, cron)."),
        ("İzleme / Yönetim", "İzleme, yönetim, yedekleme ve altyapı hizmeti veren sunucular."),
        ("Test / Geliştirme", "Test, geliştirme ve ODM ortamı sunucuları."),
        ("Belirsiz", "Mevcut kanıtla işlevi belirlenemeyen sunucular.")
    ];

    List<(string Ad, string Aciklama)> Kategoriler()
    {
        var list = cfg.GetSection("Islev:Kategoriler").GetChildren()
            .Select(c => (Ad: c["Ad"] ?? c.Value, Aciklama: c["Aciklama"] ?? ""))
            .Where(k => !string.IsNullOrWhiteSpace(k.Ad)).Select(k => (k.Ad!.Trim(), k.Aciklama)).ToList();
        return list.Count > 0 ? list : [.. DefaultKategoriler];
    }

    static readonly HashSet<string> StopWords = ["ve", "ile", "veya", "icin", "sunucu", "sunucusu", "sunuculari", "sistem", "sistemi",
        "sistemleri", "katman", "katmani", "hizmet", "hizmeti", "hizmetleri", "islem", "islemleri", "ortam", "ortami", "ortamlari", "the", "and", "server", "servers"];

    // Yazım farkını yok eden anahtar: Türkçe harfler sadeleşir, noktalama ve bağlaçlar atılır, kelimeler 5 harfe kısalır
    // ("ETL ve Veri Entegrasyonu", "ETL / Veri entegrasyonu", "ETL Veri Entegrasyon" → {etl, veri, enteg}).
    static HashSet<string> Tokens(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        var sb = new StringBuilder();
        foreach (char ch in s.ToLower(new System.Globalization.CultureInfo("tr-TR")))
            sb.Append(ch switch { 'ı' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' => 'u', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', 'î' => 'i', _ => char.IsLetterOrDigit(ch) ? ch : ' ' });
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !StopWords.Contains(w)).SelectMany(w => Synonyms.TryGetValue(w, out var e) ? e : [w])
            .Select(w => w.Length > 5 ? w[..5] : w).ToHashSet();
    }

    // Kısaltmalar karşılıklarına açılır
    static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["db"] = ["veritabani"], ["database"] = ["veritabani"], ["dwh"] = ["veri", "ambari"], ["edw"] = ["veri", "ambari"],
        ["sftp"] = ["dosya", "aktarim"], ["ftp"] = ["dosya", "aktarim"], ["mft"] = ["dosya", "aktarim"],
        ["reporting"] = ["raporlama"], ["rapor"] = ["raporlama"], ["monitoring"] = ["izleme"], ["batch"] = ["zamanlama"]
    };

    // İki ad aynı işi mi anlatıyor? Aynı kelime kümesi, %60+ örtüşme ya da en az iki kelimelik adın tamamen diğerinin içinde olması.
    static double Similar(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int common = a.Count(b.Contains);
        if (common == 0) return 0;
        double jac = (double)common / (a.Count + b.Count - common);
        bool contained = common == Math.Min(a.Count, b.Count) && Math.Min(a.Count, b.Count) >= 2;
        return contained ? Math.Max(jac, 0.8) : jac;
    }

    // Adı listedeki en benzer ada eşler (eşik altıysa null). "Diğer: X" önekleri atılır.
    static string? Snap(string? name, IEnumerable<string> targets, double min = 0.6)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = Regex.Replace(name, @"^\s*di[gğ]er\s*[:\-]\s*", "", RegexOptions.IgnoreCase);
        var t = Tokens(name);
        var list = targets.ToList();
        string? best = null; double bs = 0;
        foreach (var x in list)
        {
            if (string.Equals(x, name.Trim(), StringComparison.OrdinalIgnoreCase)) return x;
            double sc = Similar(t, Tokens(x));
            if (sc > bs) { bs = sc; best = x; }
        }
        if (bs >= min) return best;
        // Tek anlamlı kelime ("Raporlama sunucusu" → rapor) yalnızca TEK hedefte geçiyorsa o hedefe gider
        if (t.Count == 1 && list.Where(x => Tokens(x).Overlaps(t)).Take(2).ToList() is [var only]) return only;
        return null;
    }

    string Col(string key, string def) => cfg[$"Islev:{key}"] is { } c && SafeName.IsMatch(c) ? c : def;
    string Tbl(string key, string def) => cfg[$"Islev:Tablolar:{key}"] is { } t && SafeTable.IsMatch(t) ? t : def;
    string HostTable => cfg["Envanter:HostTablosu"] ?? "dbo.ERT_HOSTIPADDRESS";
    string KumeCol => Col("KumeKolonu", "AI_KUME");
    string DetayCol => Col("DetayKolonu", "AI_DETAY");
    string ConnStr => cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
    int SqlTimeout => cfg.GetValue("Islev:SqlTimeoutSaniye", 180);

    // ---------------- Sonuçlar (ERT AI kolonları) ----------------
    public async Task<(Dictionary<string, (string? Kume, IslevDetay? Detay)> map, string? uyari)> ResultsAsync(bool force, CancellationToken ct)
    {
        if (!force && _sonuc != null && DateTimeOffset.Now - _sonucAt < TimeSpan.FromMinutes(2)) return (_sonuc, null);
        var map = new Dictionary<string, (string?, IslevDetay?)>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(
                $"SELECT IPADDRESS, MAX([{KumeCol}]), MAX(CAST([{DetayCol}] AS nvarchar(max))) FROM {HostTable} WHERE [{KumeCol}] IS NOT NULL OR [{DetayCol}] IS NOT NULL GROUP BY IPADDRESS", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string? ip = S(r, 0);
                if (ip == null) continue;
                IslevDetay? d = null;
                if (S(r, 2) is { } js) try { d = JsonSerializer.Deserialize<IslevDetay>(js, Json); } catch (JsonException) { }
                map[ip] = (S(r, 1), d);
            }
        }
        catch (SqlException ex) when (ex.Number == 207)
        {
            return (map, $"{HostTable} tablosunda {KumeCol} / {DetayCol} kolonları yok. Ekleyin: ALTER TABLE {HostTable} ADD {KumeCol} NVARCHAR(100) NULL, {DetayCol} NVARCHAR(MAX) NULL;");
        }
        _sonuc = map; _sonucAt = DateTimeOffset.Now;
        return (map, null);
    }

    async Task WriteAsync(SqlConnection conn, string ip, string? kume, IslevDetay? d, CancellationToken ct)
    {
        await using var cmd = new SqlCommand($"UPDATE {HostTable} SET [{KumeCol}] = @k, [{DetayCol}] = @d WHERE IPADDRESS = @ip", conn);
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@k", (object?)Cut(kume, 100) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@d", d == null ? DBNull.Value : JsonSerializer.Serialize(d, Json));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ---------------- Harita: küme → segment → VIP → sunucu ----------------
    public async Task<IslevHarita> MapAsync(string tag, bool yazabilir, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var (res, uyari) = await ResultsAsync(false, ct);
        var ips = env.TagIps(tag);
        IslevSunucu Row(string ip)
        {
            res.TryGetValue(ip, out var x);
            var hosts = env.AppsOnIp(ip);
            return new IslevSunucu(ip, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(), env.AppNamesOnIp(ip).Take(4).ToList(),
                hosts.Select(h => h.VarlikMuhafizi).OfType<string>().FirstOrDefault(), x.Detay?.Rol ?? x.Detay?.Islev, x.Detay?.Guven,
                x.Detay?.Kaynak?.StartsWith("Elle") == true);
        }
        string KumeOf(string ip) => res.TryGetValue(ip, out var x) && !string.IsNullOrWhiteSpace(x.Kume) ? x.Kume! : Sinifsiz;
        var cmp = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
        var kumeler = ips.GroupBy(KumeOf, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            string? aciklama = g.Select(ip => res.TryGetValue(ip, out var x) ? x.Detay?.KumeAciklama : null).OfType<string>().FirstOrDefault();
            var segs = g.GroupBy(ip => env.FindSegment(ip)?.Label ?? "Segment bilinmiyor").Select(sg =>
            {
                var vips = sg.SelectMany(ip => env.MemberOf(ip).Select(m => (ip, m)))
                    .GroupBy(t => t.m.LbIp + (t.m.LbPort is { } p ? ":" + p : ""))
                    .Select(vg => new IslevVip(vg.Key, vg.First().m.Pool,
                        env.Match(vg.First().m.LbIp, vg.First().m.LbPort).Apps.Select(a => a.AppName).OfType<string>().FirstOrDefault(),
                        vg.Select(t => t.ip).Distinct().Select(Row).ToList()))
                    .OrderBy(v => v.Vip, cmp).ToList();
                var free = sg.Where(ip => env.MemberOf(ip).Count == 0).Select(Row).OrderBy(s => s.Hostname ?? s.Ip, cmp).ToList();
                return new IslevSegment(sg.Key, env.FindSegment(sg.First())?.Cidr, vips, free);
            }).OrderByDescending(s => s.Vipler.Sum(v => v.Sunucular.Count) + s.Sunucular.Count).ToList();
            return new IslevKume(g.Key, aciklama, g.Count(), segs);
        })
        .OrderBy(k => k.Ad == Sinifsiz).ThenByDescending(k => k.Sunucu).ToList();
        return new IslevHarita(tag, ips.Count, ips.Count(ip => KumeOf(ip) != Sinifsiz), kumeler, Durum, yazabilir, uyari);
    }

    // ---------------- Elle düzeltme ----------------
    public async Task<int> FixAsync(List<string> ips, string? kume, string? aciklama, string user, CancellationToken ct)
    {
        var (res, uyari) = await ResultsAsync(true, ct);
        if (uyari != null) throw new InvalidOperationException(uyari);
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync(ct);
        foreach (var ip in ips)
        {
            res.TryGetValue(ip, out var x);
            var d = (x.Detay ?? new IslevDetay(null, null, 100, null, null, null, null)) with
            {
                Guven = 100, Tarih = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Kaynak = "Elle: " + user,
                Takma = null, TakmaKaynak = null,
                // Yeni kümeye açıklama verildiyse o; aynı adlı mevcut kümeye taşınıyorsa onun açıklaması korunur
                KumeAciklama = aciklama ?? res.Values.Where(v => string.Equals(v.Kume, kume, StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.Detay?.KumeAciklama).OfType<string>().FirstOrDefault()
            };
            // Küme boş = elle atamayı kaldır; sunucu bir sonraki çalıştırmada yeniden sınıflanır.
            await WriteAsync(conn, ip, kume, kume == null ? null : d, ct);
            log.LogInformation("İşlev: {User} {Ip} kümesi [{Old}] -> [{New}]", user, ip, x.Kume, kume);
        }
        await ResultsAsync(true, ct);
        return ips.Count;
    }

    // ---------------- Küme yeniden adlandırma ----------------
    // Kümedeki tüm sunucuların AI_KUME'si değişir. AI'ın atadığı sunucular AI'lı kalır (elle sayılmaz); asıl küme adı
    // TakmaKaynak'ta tutulur, böylece "Yeniden kümele" / yeni sınıflandırmada o kümeye düşen sunucular da yeni adı alır.
    public async Task<int> RenameAsync(string tag, string eski, string yeni, string user, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var (res, uyari) = await ResultsAsync(true, ct);
        if (uyari != null) throw new InvalidOperationException(uyari);
        var ips = env.TagIps(tag).Where(ip => res.TryGetValue(ip, out var x) && string.Equals(x.Kume, eski, StringComparison.OrdinalIgnoreCase)).ToList();
        if (ips.Count == 0) throw new InvalidOperationException($"\"{eski}\" kümesinde sunucu yok.");
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync(ct);
        foreach (var ip in ips)
        {
            var d = res[ip].Detay;
            if (d != null && d.Kaynak?.StartsWith("Elle") != true) d = d with { Takma = yeni, TakmaKaynak = d.TakmaKaynak ?? eski };
            await WriteAsync(conn, ip, yeni, d, ct);
        }
        log.LogInformation("İşlev: {User} {Tag} kümesi [{Old}] -> [{New}] ({N} sunucu)", user, tag, eski, yeni, ips.Count);
        await ResultsAsync(true, ct);
        return ips.Count;
    }

    // ---------------- Arka plan işi ----------------
    public bool Start(IslevCalistir req, string tag, string user)
    {
        lock (_gate)
        {
            if (_durum.Calisiyor) return false;
            _cts = new CancellationTokenSource();
            _durum = new(true, "Başlıyor", 0, 0, 0, null, tag, user, DateTimeOffset.Now, null, []);
        }
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(tag, req.Hepsi, req.SadeceKumele, ct); Set(d => d with { Calisiyor = false, Asama = "Bitti", Bitis = DateTimeOffset.Now }); }
            catch (OperationCanceledException) { Set(d => d with { Calisiyor = false, Asama = "Durduruldu", Bitis = DateTimeOffset.Now }); }
            catch (Exception ex)
            {
                log.LogError(ex, "İşlev sınıflandırması başarısız");
                Set(d => d with { Calisiyor = false, Asama = "Hata", Mesaj = ex.Message, Bitis = DateTimeOffset.Now });
            }
        });
        return true;
    }

    public void Stop() { lock (_gate) _cts?.Cancel(); }

    void Set(Func<IslevDurum, IslevDurum> f) { lock (_gate) _durum = f(_durum); }
    void Fail(string msg) => Set(d => d with { Hatalar = d.Hatalar.Count < 30 ? [.. d.Hatalar, msg] : d.Hatalar });

    async Task RunAsync(string tag, bool hepsi, bool sadeceKumele, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var (res, uyari) = await ResultsAsync(true, ct);
        if (uyari != null) throw new InvalidOperationException(uyari);
        var ips = env.TagIps(tag).ToList();
        if (ips.Count == 0) throw new InvalidOperationException($"\"{tag}\" etiketli sunucu yok.");

        if (!sadeceKumele)
        {
            // Elle düzeltilenler hiçbir zaman AI'a gönderilmez; "hepsi" değilse yalnız sınıflanmamışlar.
            var targets = ips.Where(ip => !(res.TryGetValue(ip, out var x) && x.Detay?.Kaynak?.StartsWith("Elle") == true))
                .Where(ip => hepsi || !res.TryGetValue(ip, out var x) || x.Detay?.Islev == null).ToList();
            Set(d => d with { Asama = "Veriler toplanıyor (ERT, MSY, ÇSY, Kahin, SM)", Toplam = targets.Count, Biten = 0 });
            var profiles = await ProfilesAsync(targets, env, ct);

            Set(d => d with { Asama = "AI sınıflandırıyor" });
            int paket = Math.Clamp(cfg.GetValue("Islev:Paket", 4), 1, 10);
            using var sem = new SemaphoreSlim(Math.Clamp(cfg.GetValue("Islev:Paralel", 2), 1, 8));
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            var writeLock = new SemaphoreSlim(1, 1);
            var tasks = targets.Chunk(paket).Select(async chunk =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var got = await ClassifyAsync(tag, chunk, profiles, ct);
                    // Pakette cevabı eksik gelen sunucular tek tek yeniden denenir.
                    foreach (var ip in chunk.Where(ip => !got.ContainsKey(ip)))
                        foreach (var kv in await ClassifyAsync(tag, [ip], profiles, ct)) got[kv.Key] = kv.Value;
                    await writeLock.WaitAsync(ct);
                    try { foreach (var kv in got) await WriteAsync(conn, kv.Key, kv.Value.Islev, kv.Value, ct); }
                    finally { writeLock.Release(); }
                    int miss = chunk.Count(ip => !got.ContainsKey(ip));
                    if (miss > 0) Fail($"{string.Join(", ", chunk.Where(ip => !got.ContainsKey(ip)))}: AI cevabı okunamadı");
                    Set(d => d with { Biten = d.Biten + chunk.Length, Hata = d.Hata + miss });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Fail($"{string.Join(", ", chunk)}: {ex.Message}");
                    Set(d => d with { Biten = d.Biten + chunk.Length, Hata = d.Hata + chunk.Length });
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        Set(d => d with { Asama = "Kümeleniyor" });
        await ClusterAsync(tag, ips, ct);
        await ResultsAsync(true, ct);
    }

    // ---------------- 1. aşama: sunucu başına işlev ----------------
    async Task<Dictionary<string, IslevDetay>> ClassifyAsync(string tag, string[] ips, Dictionary<string, IslevProfil> profiles, CancellationToken ct)
    {
        var kat = Kategoriler();
        string katalog = string.Join("\n", kat.Select(k => $"- {k.Ad}: {k.Aciklama}"));
        var sb = new StringBuilder();
        sb.AppendLine($$"""
            Bir bankanın veri merkezinde çalışan kıdemli bir altyapı mimarısın. Aşağıdaki sunucular kullanıcı tarafından "{{tag}}" olarak
            etiketlendi; hepsi bu ekosistemle ilişkili ama işlevleri farklı olabilir (ör. veri ambarı için: kaynak sistem veritabanı,
            ETL / veri entegrasyonu, staging, veri ambarı veritabanı (Exadata vb.), raporlama / BI, dosya aktarımı, uygulama / web,
            izleme / yönetim, yedekleme, test / geliştirme).

            Her sunucu için verilen kanıtlara (envanter, uygulama ve port kayıtları, VIP, sanal sunucu notları, kurulum talepleri,
            iş istekleri) bakarak sunucunun İŞLEVİNİ belirle. Port numarasından çok iş isteklerindeki açıklamalara ve uygulama
            adlarına ağırlık ver. Kanıt yetersizse islev "Belirsiz" ve güveni düşük yaz; uydurma.

            "islev" alanına AŞAĞIDAKİ LİSTEDEN BİRİNİ, yazımını HARFİ HARFİNE koruyarak yaz. Hiçbiri gerçekten uymuyorsa
            "Diğer: <2–3 kelimelik ad>" yaz. Ayrıntıyı "rol" alanına koy.
            {{katalog}}

            Yalnızca JSON dizi döndür, başka metin yazma. Her sunucu için bir eleman:
            {"ip":"…","islev":"listeden biri","rol":"daha özel rol (ör. Informatica PowerCenter iş sunucusu)","guven":0-100,"gerekce":"tek cümle; hangi kanıta dayandığın"}
            """);
        foreach (var ip in ips) sb.AppendLine().AppendLine(profiles.TryGetValue(ip, out var p) ? p.Metin : $"### Sunucu {ip}\n(bilgi yok)");
        string answer = await ChatAsync(sb.ToString(), ct);
        var outp = new Dictionary<string, IslevDetay>();
        if (ExtractJson(answer) is not { } root) return outp;
        var arr = root.ValueKind == JsonValueKind.Array ? root : root.ValueKind == JsonValueKind.Object && Prop(root, "sunucular") is { ValueKind: JsonValueKind.Array } a ? a : default;
        if (arr.ValueKind != JsonValueKind.Array) return outp;
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? ip = Str(e, "ip");
            if (ip == null) { if (ips.Length == 1) ip = ips[0]; else continue; }
            ip = ips.FirstOrDefault(x => x == ip.Trim());
            if (ip == null) continue;
            string islev = Cut(Str(e, "islev"), 80) ?? "Belirsiz";
            outp[ip] = new IslevDetay(Snap(islev, kat.Select(k => k.Ad)) ?? Regex.Replace(islev, @"^\s*di[gğ]er\s*[:\-]\s*", "", RegexOptions.IgnoreCase), Cut(Str(e, "rol"), 200), Math.Clamp(Int(e, "guven"), 0, 100),
                Cut(Str(e, "gerekce"), 600), null, now, "AI");
        }
        return outp;
    }

    // ---------------- 2. aşama: işlevleri kümelere birleştir ----------------
    // Önce kodda: katalogdaki ada (ve elle tanımlanmış kümelere) benzeyen işlevler doğrudan eşlenir. Kalanlar AI'a sorulur
    // (kataloğa mı girer, yeni küme mi). Sonunda benzer adlı kümeler yine kodda tek kümede toplanır.
    async Task ClusterAsync(string tag, List<string> ips, CancellationToken ct)
    {
        var (res, _) = await ResultsAsync(true, ct);
        var ai = ips.Where(ip => res.TryGetValue(ip, out var x) && x.Detay?.Islev != null && x.Detay.Kaynak?.StartsWith("Elle") != true).ToList();
        if (ai.Count == 0) return;
        var kat = Kategoriler();
        var aciklamalar = kat.ToDictionary(k => k.Ad, k => (string?)k.Aciklama, StringComparer.OrdinalIgnoreCase);
        var elle = ips.Where(ip => res.TryGetValue(ip, out var x) && x.Detay?.Kaynak?.StartsWith("Elle") == true)
            .Select(ip => res[ip].Kume).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Kullanıcının yeniden adlandırdığı kümeler: asıl ad → yeni ad
        var takma = res.Values.Where(x => x.Detay?.TakmaKaynak != null && x.Detay.Takma != null)
            .GroupBy(x => x.Detay!.TakmaKaynak!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Detay!.Takma!, StringComparer.OrdinalIgnoreCase);
        var hedefler = elle.Concat(kat.Select(k => k.Ad)).Concat(takma.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // işlev → küme
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var kalan = new List<(string Ad, int Sayi, List<string> Roller)>();
        foreach (var g in ai.GroupBy(ip => res[ip].Detay!.Islev!, StringComparer.OrdinalIgnoreCase))
        {
            if (Snap(g.Key, hedefler) is { } h) map[g.Key] = h;
            else kalan.Add((g.Key, g.Count(), g.Select(ip => res[ip].Detay!.Rol).OfType<string>().Distinct().Take(3).ToList()));
        }

        if (kalan.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($$"""
                "{{tag}}" etiketli sunucular sınıflandırıldı. Aşağıdaki işlevler standart küme listesine otomatik eşlenemedi.
                Her birini ya listedeki bir kümeye ata (adı HARFİ HARFİNE yaz) ya da gerçekten farklı bir işse yeni bir küme aç.
                Aynı işi anlatan işlevleri AYNI yeni kümeye koy; yeni küme açmaktan kaçın, yeni küme adları 2–4 kelime ve Türkçe olsun.

                Küme listesi:
                {{string.Join("\n", hedefler.Select(h => $"- {h}"))}}

                Yalnızca JSON döndür:
                {"eslesme":[{"islev":"…","kume":"…","aciklama":"yalnız yeni kümeyse tek cümle"}]}

                Eşlenecek işlevler:
                """);
            foreach (var x in kalan)
                sb.AppendLine($"- {x.Ad} ({x.Sayi} sunucu){(x.Roller.Count > 0 ? " — örn: " + string.Join("; ", x.Roller) : "")}");
            try
            {
                var root = ExtractJson(await ChatAsync(sb.ToString(), ct));
                var arr = root is { } r ? (r.ValueKind == JsonValueKind.Array ? r : Prop(r, "eslesme") ?? default) : default;
                if (arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray())
                    {
                        string? islev = Str(e, "islev"), kume = Cut(Str(e, "kume"), 100);
                        if (islev == null || kume == null) continue;
                        string? src = kalan.Select(k => k.Ad).FirstOrDefault(k => string.Equals(k, islev, StringComparison.OrdinalIgnoreCase))
                            ?? Snap(islev, kalan.Select(k => k.Ad), 0.75);
                        if (src == null) continue;
                        kume = Snap(kume, hedefler) ?? kume;
                        map[src] = kume;
                        if (!aciklamalar.ContainsKey(kume) && Cut(Str(e, "aciklama"), 300) is { } ac) aciklamalar[kume] = ac;
                    }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Fail("Kümeleme: " + ex.Message + " (eşlenemeyen işlevler kendi adlarıyla bırakıldı)");
            }
            // AI'ın eşlemediği işlevler kendi adıyla küme olur
            foreach (var x in kalan) map.TryAdd(x.Ad, x.Ad);
        }

        // Yeni küme adlarını da birbirine benzerse birleştir (büyük küme adını korur)
        var sayac = ai.GroupBy(ip => map[res[ip].Detay!.Islev!], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var kanonik = new List<string>(hedefler);
        var yeniden = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in sayac.OrderByDescending(x => x.Value).Select(x => x.Key))
        {
            string c = Snap(k, kanonik) ?? k;
            if (!kanonik.Contains(c, StringComparer.OrdinalIgnoreCase)) kanonik.Add(c);
            yeniden[k] = c;
        }

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync(ct);
        foreach (var ip in ai)
        {
            var d = res[ip].Detay!;
            string ad = yeniden[map[d.Islev!]];
            string? yeniAd = takma.GetValueOrDefault(ad);
            await WriteAsync(conn, ip, yeniAd ?? ad, d with
            {
                KumeAciklama = aciklamalar.GetValueOrDefault(ad), Takma = yeniAd, TakmaKaynak = yeniAd == null ? null : ad
            }, ct);
        }
    }

    // ---------------- AI çağrısı (OpenAI uyumlu) ----------------
    async Task<string> ChatAsync(string prompt, CancellationToken ct)
    {
        var s = cfg.GetSection("Ai");
        string url = s["Url"] is { Length: > 0 } u ? u : throw new InvalidOperationException("Ai:Url tanımlı değil.");
        var payload = new
        {
            model = s["Model"] is { Length: > 0 } m ? m : "zt-ga-small-0",
            messages = new[] { new { role = "user", content = prompt } },
            temperature = s.GetValue("Temperature", 0.1)
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (s["ApiKey"] is { Length: > 0 } key) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var resp = await factory.CreateClient("ai").SendAsync(req, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"AI servisi HTTP {(int)resp.StatusCode}: {Cut(body, 300)}");
        using var doc = JsonDocument.Parse(body);
        string answer = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        return Regex.Replace(answer, "<think>.*?</think>", "", RegexOptions.Singleline).Trim();
    }

    // Model cevabındaki ilk JSON dizi / nesnesi (``` blokları ve önündeki metin atlanır).
    static JsonElement? ExtractJson(string text)
    {
        int a = text.IndexOfAny(['[', '{']);
        if (a < 0) return null;
        char close = text[a] == '[' ? ']' : '}';
        int b = text.LastIndexOf(close);
        if (b <= a) return null;
        try { using var doc = JsonDocument.Parse(text[a..(b + 1)]); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
    static JsonElement? Prop(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in e.EnumerateObject()) if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
    static string? Str(JsonElement e, string name) => Prop(e, name) is { } v
        ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Number ? v.GetRawText() : null) is { Length: > 0 } s ? s.Trim() : null
        : null;
    static int Int(JsonElement e, string name) => Prop(e, name) is { } v
        ? v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? (int)Math.Round(d <= 1 && d > 0 ? d * 100 : d)
        : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString()?.Trim('%', ' '), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2) ? (int)d2 : 0
        : 0;

    // ---------------- Bilgi özeti ----------------
    public async Task<IslevProfil> ProfileAsync(string ip, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var p = (await ProfilesAsync([ip], env, ct))[ip];
        var (res, _) = await ResultsAsync(false, ct);
        return res.TryGetValue(ip, out var x) ? p with { Detay = x.Detay, Kume = x.Kume } : p;
    }

    sealed class Bag
    {
        public readonly List<string> Msy = [], Csy = [], Kahin = [], Hosts = [], Hatalar = [];
        public readonly List<SmKayit> Kurulum = [], Talepler = [];
    }

    async Task<Dictionary<string, IslevProfil>> ProfilesAsync(List<string> ips, EnvanterSnapshot env, CancellationToken ct)
    {
        var bags = ips.Distinct().ToDictionary(ip => ip, _ => new Bag());
        foreach (var (ip, b) in bags)
            b.Hosts.AddRange(env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>());
        var errors = new List<string>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync(ct);

        async Task Safe(string name, Func<Task> f)
        {
            try { await f(); }
            catch (SqlException ex) { errors.Add($"{name}: {ex.Message}"); log.LogWarning("İşlev özeti: {Name} okunamadı: {Msg}", name, ex.Message); }
        }

        // MSY sanal sunucu envanteri
        await Safe("MSY", () => InAsync(conn, $"SELECT Ip, Vm, DnsName, Cluster, Host, Os, Annotation FROM {Tbl("Msy", "dbo.Envanter")} WHERE Ip IN ({{0}})",
            bags.Keys, r =>
            {
                if (S(r, 0) is not { } ip || !bags.TryGetValue(ip.Trim(), out var b)) return;
                b.Msy.Add(Join("; ", ("VM", S(r, 1)), ("DNS", S(r, 2)), ("Cluster", S(r, 3)), ("Host", S(r, 4)), ("OS", S(r, 5)), ("Not", Cut(S(r, 6), 400))));
                AddHost(b, S(r, 1)); AddHost(b, S(r, 2));
            }, ct));
        // ÇSY sanal sunucu envanteri
        await Safe("ÇSY", () => InAsync(conn, $"SELECT Primary_IP_Address, Folder, VM, DNS_Name, CPUs, Memory, Total_disk_capacity_MiB FROM {Tbl("Csy", "dbo.CsyEnvanter")} WHERE Primary_IP_Address IN ({{0}})",
            bags.Keys, r =>
            {
                if (S(r, 0) is not { } ip || !bags.TryGetValue(ip.Trim(), out var b)) return;
                string? mem = double.TryParse(S(r, 5), out var mb) ? $"{mb / 1024:0.#} GB" : S(r, 5);
                string? disk = double.TryParse(S(r, 6), out var mib) ? $"{mib / 1024:0} GB" : S(r, 6);
                b.Csy.Add(Join("; ", ("Folder", S(r, 1)), ("VM", S(r, 2)), ("DNS", S(r, 3)), ("vCPU", S(r, 4)), ("RAM", mem), ("Disk", disk)));
                AddHost(b, S(r, 2)); AddHost(b, S(r, 3));
            }, ct));
        // Envanter Kahini tahminleri
        await Safe("Kahin", () => InAsync(conn, $"SELECT IP, PORT, TAHMIN, SAHIPLIK_ANALIZI FROM {Tbl("Kahin", "dbo.SONUCLAR")} WHERE IP IN ({{0}})",
            bags.Keys, r =>
            {
                if (S(r, 0) is not { } ip || !bags.TryGetValue(ip.Trim(), out var b) || b.Kahin.Count >= 5) return;
                b.Kahin.Add(Join("; ", ("Port", S(r, 1)), ("Tahmin", S(r, 2)), ("Sahiplik", Cut(S(r, 3), 200))));
            }, ct));

        string req = Tbl("Request", "dbo.REQUESTM1"), dept = Tbl("Dept", "dbo.DEPTM1"), cm3 = Tbl("Change", "dbo.CM3RM1"),
            kur = Tbl("Kurulum", "dbo.ZTSUNUCUKURULUMUOPTIONSM1");
        int paket = Math.Clamp(cfg.GetValue("Islev:SmPaket", 25), 1, 100);
        int maxKayit = Math.Clamp(cfg.GetValue("Islev:SmMaxKayit", 12), 1, 50);
        var since = DateTime.Today.AddYears(-Math.Clamp(cfg.GetValue("Islev:SmYil", 3), 1, 20));
        bool blob = cfg.GetValue("Islev:SmBlobAra", true);
        string? talepLink = cfg["Islev:SmTalepLinki"], gorevLink = cfg["Islev:SmGorevLinki"];

        foreach (var chunk in bags.Keys.Chunk(paket))
        {
            // Sunucu kurulum talepleri (kurulan sunucunun IP'si)
            await Safe("SM kurulum", async () =>
            {
                await using var cmd = new SqlCommand("", conn) { CommandTimeout = SqlTimeout };
                var likes = chunk.Select((ip, i) => { cmd.Parameters.AddWithValue($"@l{i}", $"%{ip}%"); return $"z.SUNUCU_IP LIKE @l{i}"; });
                cmd.CommandText = $"""
                    SELECT TOP 2000 z.CHANGE_ID, z.REQUEST_ID, r.SUBMIT_DATE, c.BRIEF_DESCRIPTION, r.BILL_TO_DEPT, c.APPROVAL_STATUS,
                           z.SUNUCU_IP, z.UYGULAMA_BILGISI, z.CMTASKID
                    FROM {kur} z
                    INNER JOIN {cm3} c ON c.NUMBER = z.CHANGE_ID
                    INNER JOIN {req} r ON r.NUMBER = z.REQUEST_ID
                    WHERE {string.Join(" OR ", likes)}
                    ORDER BY r.SUBMIT_DATE DESC
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    foreach (var ip in chunk.Where(ip => HasIp(S(r, 6), ip)))
                    {
                        var b = bags[ip];
                        if (b.Kurulum.Count >= 5) continue;
                        string? task = S(r, 8);
                        b.Kurulum.Add(new SmKayit(task ?? S(r, 0) ?? "", Date(r, 2), "Sunucu kurulumu", S(r, 3), S(r, 7), null, ip,
                            S(r, 4), S(r, 5), task != null && gorevLink is { Length: > 0 } ? gorevLink.Replace("{CMTASKID}", Uri.EscapeDataString(task)) : null));
                    }
            });

            // İş istekleri: kaynak / hedef IP, hostname ve çoklu erişim blob'ları
            // Blob kolonlarının tipi (varbinary / image / text) dönüşüme izin vermezse blob araması kapatılıp tekrar denenir.
            async Task Talepler(bool blob)
            {
                var hosts = chunk.SelectMany(ip => bags[ip].Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Take(400).ToList();
                await using var cmd = new SqlCommand("", conn) { CommandTimeout = SqlTimeout };
                cmd.Parameters.AddWithValue("@since", since);
                var conds = new List<string>();
                for (int i = 0; i < chunk.Length; i++)
                {
                    cmd.Parameters.AddWithValue($"@l{i}", $"%{chunk[i]}%");
                    conds.Add($"req.ZT_KAYNAK LIKE @l{i} OR req.ZT_HEDEF LIKE @l{i}");
                    if (blob) conds.Add($"CONVERT(varchar(max), CONVERT(varbinary(max), req.ZT_ERISIM_KAYNAK)) LIKE @l{i} OR CONVERT(varchar(max), CONVERT(varbinary(max), req.ZT_ERISIM_HEDEF)) LIKE @l{i}");
                }
                if (hosts.Count > 0)
                {
                    var hp = hosts.Select((h, i) => { cmd.Parameters.AddWithValue($"@h{i}", h); return $"@h{i}"; }).ToList();
                    conds.Add($"req.ZT_KAYNAKHOSTNAME IN ({string.Join(",", hp)}) OR req.ZT_HEDEFHOSTNAME IN ({string.Join(",", hp)})");
                }
                cmd.CommandText = $"""
                    SELECT TOP {Math.Clamp(cfg.GetValue("Islev:SmPaketMaxSatir", 3000), 100, 20000)}
                        req.NUMBER, req.MODEL, req.BRIEF_DESCRIPTION, LEFT(CAST(req.DESCRIPTION AS nvarchar(max)), 700), req.SUBMIT_DATE,
                        req.ZT_KAYNAK, req.ZT_KAYNAKHOSTNAME, req.ZT_KAYNAKACIKLAMA, req.ZT_HEDEF, req.ZT_HEDEFHOSTNAME, req.ZT_HEDEFACIKLAMA,
                        dep.DEPT, req.BILL_TO_DEPT, req.ZT_ERISIM_KAYNAK, req.ZT_ERISIM_HEDEF
                    FROM {req} req
                    LEFT JOIN {dept} dep ON dep.DEPT_ID = req.ZT_DEPT_ID
                    WHERE req.SUBMIT_DATE >= @since AND ({string.Join(" OR ", conds)})
                    ORDER BY req.SUBMIT_DATE DESC
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    string kaynak = Join(" ", ("", S(r, 5)), ("", S(r, 6)), ("", S(r, 7)), ("", Cut(ParseHexOrBinary(r.GetValue(13)), 300)));
                    string hedef = Join(" ", ("", S(r, 8)), ("", S(r, 9)), ("", S(r, 10)), ("", Cut(ParseHexOrBinary(r.GetValue(14)), 300)));
                    foreach (var ip in chunk)
                    {
                        var b = bags[ip];
                        bool k = HasIp(kaynak, ip) || b.Hosts.Any(h => HasWord(kaynak, h)), h2 = HasIp(hedef, ip) || b.Hosts.Any(h => HasWord(hedef, h));
                        if ((!k && !h2) || b.Talepler.Count >= maxKayit) continue;
                        string no = S(r, 0) ?? "";
                        b.Talepler.Add(new SmKayit(no, Date(r, 4), S(r, 1), S(r, 2), Cut(Squash(S(r, 3)), 400), Cut(kaynak, 250), Cut(hedef, 250),
                            S(r, 11) ?? S(r, 12), k && h2 ? "kaynak+hedef" : k ? "kaynak" : "hedef",
                            talepLink is { Length: > 0 } ? talepLink.Replace("{NUMBER}", Uri.EscapeDataString(no)) : null));
                    }
                }
            }
            await Safe("SM iş istekleri", async () =>
            {
                try { await Talepler(blob); }
                catch (SqlException ex) when (blob && !ct.IsCancellationRequested)
                {
                    log.LogWarning("SM çoklu erişim blob araması kapatıldı: {Msg}", ex.Message);
                    blob = false;
                    await Talepler(false);
                }
            });
        }

        int max = Math.Clamp(cfg.GetValue("Islev:MaxKarakter", 3500), 800, 20000);
        return bags.ToDictionary(kv => kv.Key, kv =>
            new IslevProfil(kv.Key, Text(kv.Key, env, kv.Value, max), kv.Value.Msy, kv.Value.Csy, kv.Value.Kahin, kv.Value.Kurulum, kv.Value.Talepler,
                errors, null, null));
    }

    string Text(string ip, EnvanterSnapshot env, Bag b, int max)
    {
        var sb = new StringBuilder();
        var hosts = env.AppsOnIp(ip);
        sb.AppendLine($"### Sunucu {ip}");
        if (b.Hosts.Count > 0) sb.AppendLine("Adlar: " + string.Join(", ", b.Hosts.Distinct(StringComparer.OrdinalIgnoreCase).Take(4)));
        if (env.TagsOf(ip) is { Count: > 0 } tags) sb.AppendLine("Etiketler: " + string.Join(", ", tags));
        if (env.FindSegment(ip) is { } seg)
            sb.AppendLine($"Segment: {seg.Label} ({seg.Cidr}{(seg.Domain != null ? ", " + seg.Domain : "")}{(seg.Description != null && seg.Description != seg.Label ? ", " + seg.Description : "")})");
        var ports = hosts.GroupBy(h => h.Port ?? "-").Take(15).Select(g =>
        {
            string apps = string.Join("/", g.Select(h => h.AppName).OfType<string>().Distinct().Take(3));
            string? pi = PortBilgisi.Find(g.Key, cfg)?.Name;
            string extra = string.Join(", ", g.Select(h => h.ServiceName).Concat(g.Select(h => h.Website)).OfType<string>().Distinct().Take(2));
            return $"{g.Key}{(pi != null ? $" [{pi}]" : "")} → {(apps == "" ? "uygulama yok" : apps)}{(extra != "" ? $" ({extra})" : "")}";
        }).ToList();
        if (ports.Count > 0) sb.AppendLine("Envanter port/uygulama: " + string.Join("; ", ports));
        string own = Join("; ", ("Varlık muhafızı", string.Join(", ", hosts.Select(h => h.VarlikMuhafizi).OfType<string>().Distinct().Take(3))),
            ("İşleten birim", string.Join(", ", hosts.Select(h => h.SunucuIsletenBirim).OfType<string>().Distinct().Take(2))),
            ("Uygulama sahibi", string.Join(", ", hosts.Select(h => h.UygulamaVarlikSahibi).OfType<string>().Distinct().Take(2))));
        if (own != "") sb.AppendLine(own);
        var vips = env.MemberOf(ip).Take(6).Select(m =>
        {
            string? app = env.Match(m.LbIp, m.LbPort).Apps.Select(a => a.AppName).OfType<string>().FirstOrDefault();
            return $"{m.LbIp}:{m.LbPort} → üye portu {m.Port}{(m.Pool != null ? ", havuz " + m.Pool : "")}{(app != null ? ", " + app : "")}";
        }).ToList();
        if (vips.Count > 0) sb.AppendLine("VIP üyelikleri: " + string.Join("; ", vips));
        foreach (var m in b.Msy) sb.AppendLine("MSY sanal sunucu: " + m);
        foreach (var c in b.Csy) sb.AppendLine("ÇSY sanal sunucu: " + c);
        foreach (var k in b.Kahin) sb.AppendLine("Envanter Kahini: " + k);
        foreach (var k in b.Kurulum) sb.AppendLine($"Sunucu kurulum talebi [{k.Tarih}]: {k.Baslik} | uygulama: {k.Aciklama} | birim: {k.Birim}");
        if (b.Talepler.Count > 0)
        {
            sb.AppendLine($"İş istekleri (son {b.Talepler.Count}):");
            foreach (var t in b.Talepler)
                sb.AppendLine($"- [{t.Tarih}] {t.Tur} (bu sunucu {t.Yon}): {t.Baslik} | kaynak: {t.Kaynak} | hedef: {t.Hedef} | {t.Birim} | {t.Aciklama}");
        }
        string s = sb.ToString();
        return s.Length > max ? s[..max] + "…" : s;
    }

    // ---------------- yardımcılar ----------------
    async Task InAsync(SqlConnection conn, string sqlTemplate, IEnumerable<string> values, Action<SqlDataReader> row, CancellationToken ct)
    {
        foreach (var chunk in values.Chunk(500))
        {
            await using var cmd = new SqlCommand("", conn) { CommandTimeout = SqlTimeout };
            var names = chunk.Select((v, i) => { cmd.Parameters.AddWithValue($"@p{i}", v); return $"@p{i}"; });
            cmd.CommandText = string.Format(sqlTemplate, string.Join(",", names));
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) row(r);
        }
    }

    static void AddHost(Bag b, string? h)
    {
        if (string.IsNullOrWhiteSpace(h)) return;
        h = h.Trim();
        foreach (var x in new[] { h, h.Split('.')[0] })
            if (x.Length >= 4 && !LookupQuery.TryParseIpv4(x, out _) && !b.Hosts.Contains(x, StringComparer.OrdinalIgnoreCase)) b.Hosts.Add(x);
    }

    static bool HasIp(string? text, string ip) =>
        text != null && text.Contains(ip) && Regex.IsMatch(text, $@"(?<![\d.]){Regex.Escape(ip)}(?!\.?\d)");
    static bool HasWord(string text, string word) =>
        text.Contains(word, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text, $@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", RegexOptions.IgnoreCase);

    static string? S(SqlDataReader r, int i) => r.IsDBNull(i) ? null : EnvanterSnapshot.Clean(r.GetValue(i) is byte[] ? null : Convert.ToString(r.GetValue(i)));
    static string? Date(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetValue(i) is DateTime d ? d.ToString("yyyy-MM-dd") : S(r, i);
    static string? Cut(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : (s = s.Trim()).Length > n ? s[..n] + "…" : s;
    static string? Squash(string? s) => s == null ? null : Regex.Replace(s, @"\s+", " ").Trim();
    static string Join(string sep, params (string Label, string? Value)[] parts) =>
        string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Label == "" ? p.Value!.Trim() : $"{p.Label}: {p.Value!.Trim()}"));

    // SM çok satırlı form alanları (ZT_ERISIM_KAYNAK / HEDEF) byte[] ya da "0x…" metni olarak gelir; okunur metne çevrilir.
    public static string ParseHexOrBinary(object? dbObj)
    {
        if (dbObj == null || dbObj == DBNull.Value) return "";
        byte[]? bytes;
        if (dbObj is byte[] arr) bytes = arr;
        else
        {
            string str = dbObj.ToString() ?? "";
            if (!str.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return str == "System.Byte[]" ? "" : str;
            try { bytes = Convert.FromHexString(str.AsSpan(2, (str.Length - 2) / 2 * 2)); }
            catch (FormatException) { return str; }
        }
        if (bytes.Length == 0) return "";
        var sb = new StringBuilder(bytes.Length);
        foreach (byte b in bytes) sb.Append(b is >= 32 and <= 126 ? (char)b : ' ');
        string cleaned = Regex.Replace(sb.ToString(), " {2,}", " ")
            .Replace(" ) - - ", " | ").Replace(" - - ", " | ").Replace(" )--", " |").Replace("--", "|").Replace("_RCFM*=", "").Replace(")|", "|");
        return cleaned.Trim();
    }
}
