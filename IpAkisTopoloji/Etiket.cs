using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Sunucu etiketleme: ERT_HOSTIPADDRESS tablosunun etiket kolonu (Envanter:EtiketKolonu, varsayılan TAG) okunur ve yazılır.
// Bir IP'nin tüm satırları aynı etiket metnini taşır ("VERIAMBARI,ETL"). Yazma: önce IP'nin mevcut etiketleri DB'den okunur,
// ekle / kaldır uygulanır, IP'nin bütün satırları güncellenir; ardından envanter yeniden yüklenir.
// Yetki: Yetki.cs (Yetki:Yazma, DokuPanel site kaydı). Her değişiklik loglanır.
// Alt etiket: "ANA/ALT" (ör. MOBILBANKACILIK/MOBIL APP) aynı TAG kolonunda; ana etiket seçilince alt etiketli sunucular da gelir.
// Açıklamalar (ana ve alt etiket): ayrı küçük tablo Etiket:AciklamaTablosu (varsayılan dbo.DF_ETIKET_ACIKLAMA, sql/etiket_versiyon.sql).
// ---------------------------------------------------------------------------
record EtiketSunucu(string Ip, string? Hostname, List<string> Apps, string? Muhafiz, string? Segment, List<string> Tags, string? Cidr = null, bool ErtYok = false);
record EtiketSayi(string Ad, int Sunucu, string? Aciklama = null);
// Sunucular = istenen sayfa; Toplam = filtreye uyan; Tum / Etiketli = envanterin geneli; Etiketler = etiket başına sunucu sayısı
record EtiketListe(List<EtiketSunucu> Sunucular, int Toplam, int Tum, int Etiketli, List<EtiketSayi> Etiketler, string? AciklamaUyari = null);
record EtiketFiltre(string? Ip, string? Sunucu, string? Uygulama, string? Muhafiz, string? Segment, string? Etiket,
    string? Sirala, bool Azalan, int Sayfa, int Boyut);
record EtiketIstek(List<string>? Ips, string? Ekle, string? Kaldir, Dictionary<string, string?>? Adlar = null);   // Adlar: ERT dışı IP → sunucu adı (DF_ETIKET_EK)
record EtiketSonuc(int Guncellenen, List<EtiketSunucu> Sunucular);
record EtiketAciklamaIstek(string? Etiket, string? Aciklama);

static class EtiketService
{
    static readonly Regex SafeName = new(@"^[A-Za-z_][A-Za-z0-9_]*$");
    static readonly Regex TagRx = new(@"^[\p{L}\p{N}_\-. ]{1,50}(/[\p{L}\p{N}_\-. ]{1,50})?$");

    public static string Column(IConfiguration cfg)
    {
        string c = cfg["Envanter:EtiketKolonu"] ?? "TAG";
        return SafeName.IsMatch(c) ? c : "TAG";
    }

    public static IEnumerable<string> Split(string raw) =>
        raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? NormalizeTag(string? t)
    {
        // "ana / alt" → "ana/alt"
        t = t == null ? null : string.Join("/", t.Split('/').Select(x => x.Trim()));
        return string.IsNullOrEmpty(t) || !TagRx.IsMatch(t) ? null : t.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
    }

    // Ana etiket: "MOBILBANKACILIK/MOBIL APP" → "MOBILBANKACILIK"
    public static string Parent(string tag) => tag.Split('/')[0];
    // Sunucunun etiketi seçilen etikete uyuyor mu (ana etiket seçiliyse alt etiketleri de)
    public static bool Matches(string serverTag, string wanted) =>
        string.Equals(serverTag, wanted, StringComparison.OrdinalIgnoreCase)
        || (!wanted.Contains('/') && serverTag.StartsWith(wanted + "/", StringComparison.OrdinalIgnoreCase));

    // Yazma (etiketleme, işlev haritası düzenleme, AI çalıştırma, ERT eksikleri kararları) yetkisi: Yetki.cs (DokuPanel site kaydı
    // Yetki:Yazma, boşsa Etiket:YazmaSiteAdi, o da boşsa Etiket:Yazanlar listesi).
    public static bool CanWrite(IConfiguration cfg, HttpContext ctx) => Yetki.Var(cfg, ctx, Yetki.Yazma);

    // Envanterdeki tüm sunucular (IP başına bir satır); envanter anlık görüntüsü başına bir kez hesaplanır.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EnvanterSnapshot, List<EtiketSunucu>> Cache = new();
    static List<EtiketSunucu> All(EnvanterSnapshot env) => Cache.GetValue(env, e => e.HostIps
        .Select(ip =>
        {
            var hosts = e.AppsOnIp(ip);
            string? muh = string.Join(", ", hosts.Select(h => h.VarlikMuhafizi).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            return new EtiketSunucu(ip, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(),
                e.AppNamesOnIp(ip).Take(5).ToList(), muh == "" ? null : muh, e.FindSegment(ip)?.Label, e.TagsOf(ip), e.FindSegment(ip)?.Cidr);
        })
        // ERT'de olmayan, DF_ETIKET_EK'te etiketi olan IP'ler (segment getir'den etiketlenenler)
        .Concat(e.EkSunucular.Where(kv => e.TagsOf(kv.Key).Count > 0).Select(kv =>
            new EtiketSunucu(kv.Key, kv.Value, [], null, e.FindSegment(kv.Key)?.Label, e.TagsOf(kv.Key), e.FindSegment(kv.Key)?.Cidr, true)))
        .ToList());

    public static EtiketSunucu? Row(EnvanterSnapshot env, string ip) => All(env).FirstOrDefault(s => s.Ip == ip)
        ?? (env.ErtteVar(ip) ? null : new EtiketSunucu(ip, env.EkSunucular.GetValueOrDefault(ip), [], null, env.FindSegment(ip)?.Label, env.TagsOf(ip), env.FindSegment(ip)?.Cidr, true));

    static bool Has(string? value, string? q) => string.IsNullOrWhiteSpace(q) || (value?.Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false);

    // Segment süzgeci (kullanıcı: "segmenti CIDR olarak filtreleyemiyoruz"): "10.71.51.0/24" → IP bu aralıkta mı (segment kaydından bağımsız; birden çok
    // aralık virgül / boşlukla), "10.71.51." gibi IP öneki → IP bununla başlıyor mu; diğer metin → segment adında ya da segmentin CIDR'ında geçiyor mu
    static readonly System.Text.RegularExpressions.Regex _cidr = new(@"^(\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})$");
    static bool SegmentUyar(EtiketSunucu s, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return true;
        var parca = q.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parca.All(p => _cidr.IsMatch(p)))
            return EnvanterSnapshot.TryToUInt(s.Ip, out uint ipv) && parca.Any(p =>
            {
                var m = _cidr.Match(p);
                if (!EnvanterSnapshot.TryToUInt(m.Groups[1].Value, out uint ag) || !int.TryParse(m.Groups[2].Value, out int n) || n > 32) return false;
                uint mask = n == 0 ? 0u : uint.MaxValue << (32 - n);
                return (ipv & mask) == (ag & mask);
            });
        string t = q.Trim();
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d{1,3}(\.\d{1,3}){1,3}\.?$")) return s.Ip.StartsWith(t) || (s.Cidr?.StartsWith(t) ?? false);
        return Has(s.Segment, t) || Has(s.Cidr, t);
    }

    // Kolon filtreleri (içinde geçen, büyük/küçük harf duyarsız), etiket filtresi ("" tümü, "-" etiketsiz, "*" etiketli, ad),
    // sıralama ve sayfalama.
    public static EtiketListe List(EnvanterSnapshot env, EtiketFiltre f)
    {
        var all = All(env);
        IEnumerable<EtiketSunucu> q = all
            .Where(s => Has(s.Ip, f.Ip) && Has(s.Hostname, f.Sunucu) && Has(string.Join(" ", s.Apps), f.Uygulama)
                        && Has(s.Muhafiz, f.Muhafiz) && SegmentUyar(s, f.Segment))
            .Where(s => f.Etiket switch
            {
                null or "" => true,
                "-" => s.Tags.Count == 0,
                "*" => s.Tags.Count > 0,
                var t => s.Tags.Any(x => Matches(x, t))
            });
        var cmp = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
        Func<EtiketSunucu, string> key = f.Sirala switch
        {
            "sunucu" => s => s.Hostname ?? "\uffff",
            "uygulama" => s => s.Apps.FirstOrDefault() ?? "\uffff",
            "muhafiz" => s => s.Muhafiz ?? "\uffff",
            "segment" => s => s.Segment ?? "\uffff",
            "etiket" => s => s.Tags.Count == 0 ? "\uffff" : string.Join(",", s.Tags),
            _ => s => IpKey(s.Ip)
        };
        var sorted = (f.Azalan ? q.OrderByDescending(key, cmp) : q.OrderBy(key, cmp)).ToList();
        int size = Math.Clamp(f.Boyut, 10, 500), page = Math.Max(1, f.Sayfa);
        return new EtiketListe(sorted.Skip((page - 1) * size).Take(size).ToList(), sorted.Count, all.Count, all.Count(s => s.Tags.Count > 0), []);
    }

    // Etiket kataloğu (ana etiketler ve altları, sunucu sayısı) + açıklamalar
    public static List<EtiketSayi> Catalog(EnvanterSnapshot env, IReadOnlyDictionary<string, string> desc) =>
        env.TagCatalog().Select(t => new EtiketSayi(t.Tag, t.Count, desc.TryGetValue(t.Tag, out var a) ? a : null)).ToList();

    // IP'ler sayısal sırayla: "010.001.002.003"
    static string IpKey(string ip) => string.Join(".", ip.Split('.').Select(p => p.PadLeft(3, '0')));

    // Seçilen IP'lere etiket ekler / kaldırır. Döner: güncellenen satır sayısı.
    public static async Task<int> ApplyAsync(IConfiguration cfg, List<string> ips, string? add, string? remove, string user,
        ILogger log, CancellationToken ct)
    {
        string connStr = cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
        string table = cfg["Envanter:HostTablosu"] ?? "dbo.ERT_HOSTIPADDRESS";
        string col = Column(cfg);
        int total = 0;
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        foreach (var ip in ips)
        {
            var current = new List<string>();
            try
            {
                await using (var cmd = new SqlCommand($"SELECT DISTINCT [{col}] FROM {table} WHERE IPADDRESS = @ip AND [{col}] IS NOT NULL", conn))
                {
                    cmd.Parameters.AddWithValue("@ip", ip);
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct))
                        foreach (var t in Split(Convert.ToString(r.GetValue(0)) ?? ""))
                            if (!current.Contains(t, StringComparer.OrdinalIgnoreCase)) current.Add(t);
                }
            }
            catch (SqlException ex) when (ex.Number == 207)
            {
                throw new InvalidOperationException($"{table} tablosunda {col} kolonu yok. Önce ekleyin: ALTER TABLE {table} ADD {col} NVARCHAR(200) NULL;");
            }
            var next = current.Where(t => remove == null || !string.Equals(t, remove, StringComparison.OrdinalIgnoreCase)).ToList();
            if (add != null && !next.Contains(add, StringComparer.OrdinalIgnoreCase)) next.Add(add);
            if (next.SequenceEqual(current)) continue;
            string? value = next.Count == 0 ? null : string.Join(",", next);
            int max = cfg.GetValue("Etiket:MaxUzunluk", 200);   // TAG kolonunun uzunluğu
            if (value != null && value.Length > max)
                throw new InvalidOperationException($"{ip}: etiketler {max} karakteri geçiyor (kolonu büyütüp Etiket:MaxUzunluk ayarını artırın).");
            await using (var cmd = new SqlCommand($"UPDATE {table} SET [{col}] = @v WHERE IPADDRESS = @ip", conn))
            {
                cmd.Parameters.AddWithValue("@ip", ip);
                cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
                int n = await cmd.ExecuteNonQueryAsync(ct);
                total += n;
                log.LogInformation("Etiket: {User} {Ip} [{Old}] -> [{New}] ({Rows} satır)", user, ip, string.Join(",", current), value ?? "", n);
            }
        }
        return total;
    }
}

// Etiket açıklamaları: Etiket:AciklamaTablosu (ETIKET NVARCHAR(120) PK, ACIKLAMA NVARCHAR(2000), GUNCELLEYEN, TARIH).
// Bellekte 5 dk tutulur; yazınca tazelenir. Tablo yoksa açıklamalar boş döner, Uyari dolu olur (yazma hata verir).
static class EtiketAciklama
{
    static readonly Regex SafeTable = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$");
    static Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    static DateTime _at = DateTime.MinValue;
    static string? _uyari;
    static readonly SemaphoreSlim Gate = new(1, 1);

    static string Table(IConfiguration cfg)
    {
        string t = cfg["Etiket:AciklamaTablosu"] ?? "dbo.DF_ETIKET_ACIKLAMA";
        return SafeTable.IsMatch(t) ? t : "dbo.DF_ETIKET_ACIKLAMA";
    }
    static string Missing(IConfiguration cfg) => $"Etiket açıklama tablosu ({Table(cfg)}) yok; sql/etiket_versiyon.sql betiğini çalıştırın.";

    public static async Task<(IReadOnlyDictionary<string, string> Map, string? Uyari)> GetAsync(IConfiguration cfg, CancellationToken ct, bool force = false)
    {
        if (!force && DateTime.UtcNow - _at < TimeSpan.FromMinutes(5)) return (_cache, _uyari);
        await Gate.WaitAsync(ct);
        try
        {
            if (!force && DateTime.UtcNow - _at < TimeSpan.FromMinutes(5)) return (_cache, _uyari);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? uyari = null;
            try
            {
                await using var conn = new SqlConnection(cfg.GetConnectionString("Envanter"));
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand($"SELECT ETIKET, ACIKLAMA FROM {Table(cfg)} WHERE ACIKLAMA IS NOT NULL", conn);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) map[r.GetString(0)] = r.GetString(1);
            }
            catch (SqlException ex) when (ex.Number == 208) { uyari = Missing(cfg); }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException) { uyari = "Etiket açıklamaları okunamadı: " + ex.Message; }
            (_cache, _uyari, _at) = (map, uyari, DateTime.UtcNow);
            return (map, uyari);
        }
        finally { Gate.Release(); }
    }

    public static async Task SetAsync(IConfiguration cfg, string tag, string? text, string user, ILogger log, CancellationToken ct)
    {
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (text is { Length: > 2000 }) throw new InvalidOperationException("Açıklama en fazla 2000 karakter olabilir.");
        try
        {
            await using var conn = new SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            string t = Table(cfg);
            await using var cmd = new SqlCommand(text == null ? $"DELETE FROM {t} WHERE ETIKET = @e" : $"""
                UPDATE {t} SET ACIKLAMA = @a, GUNCELLEYEN = @u, TARIH = SYSDATETIME() WHERE ETIKET = @e;
                IF @@ROWCOUNT = 0 INSERT INTO {t} (ETIKET, ACIKLAMA, GUNCELLEYEN, TARIH) VALUES (@e, @a, @u, SYSDATETIME());
                """, conn);
            cmd.Parameters.AddWithValue("@e", tag);
            cmd.Parameters.AddWithValue("@a", (object?)text ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@u", user);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 208) { throw new InvalidOperationException(Missing(cfg)); }
        log.LogInformation("Etiket açıklaması: {User} {Tag} -> {Text}", user, tag, text ?? "(silindi)");
        await GetAsync(cfg, ct, true);
    }
}

// ERT'de olmayan IP'lerin etiketleri (kullanıcı: "ERT'de olmayan satırlara veri giremiyorum tamam ama en azından etiket ekleyelim"):
// ERT_HOSTIPADDRESS'e satır EKLENMEZ; DeltaFlow'un kendi tablosu Etiket:EkTablosu (dbo.DF_ETIKET_EK, sql/etiket_ek.sql):
// IPADDRESS PK, TAG (ERT TAG'iyle aynı biçim "A,B/ALT"), HOSTNAME, GUNCELLEYEN, TARIH. Envanter yüklenirken ERT'de olmayan IP'lerin
// etiketleri envanterin etiket listesine katılır (topoloji, etiket taraması, işlev haritası da görür). IP sonradan ERT'ye girerse ERT TAG'i geçerli.
static class EtiketEk
{
    static readonly Regex SafeTable = new(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$");
    public static string Table(IConfiguration cfg)
    {
        string t = cfg["Etiket:EkTablosu"] ?? "dbo.DF_ETIKET_EK";
        return SafeTable.IsMatch(t) ? t : "dbo.DF_ETIKET_EK";
    }

    public static async Task<Dictionary<string, (List<string> Tags, string? Host)>> OkuAsync(SqlConnection conn, IConfiguration cfg, ILogger log, CancellationToken ct)
    {
        var map = new Dictionary<string, (List<string>, string?)>();
        try
        {
            await using var cmd = new SqlCommand($"SELECT IPADDRESS, TAG, HOSTNAME FROM {Table(cfg)} WHERE TAG IS NOT NULL", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string? ip = r.IsDBNull(0) ? null : r.GetString(0).Trim();
                if (string.IsNullOrEmpty(ip) || r.IsDBNull(1)) continue;
                map[ip] = (EtiketService.Split(r.GetString(1)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), r.IsDBNull(2) ? null : r.GetString(2));
            }
        }
        catch (SqlException ex) when (ex.Number == 208) { log.LogInformation("ERT dışı etiket tablosu yok ({T}); sql/etiket_ek.sql", Table(cfg)); }
        catch (SqlException ex) { log.LogInformation("ERT dışı etiketler okunamadı ({T}): {M}", Table(cfg), ex.Message); }
        return map;
    }

    public static async Task<int> ApplyAsync(IConfiguration cfg, List<string> ips, string? add, string? remove, IReadOnlyDictionary<string, string?>? adlar,
        string user, ILogger log, CancellationToken ct)
    {
        string t = Table(cfg);
        int total = 0, max = cfg.GetValue("Etiket:MaxUzunluk", 200);
        await using var conn = new SqlConnection(cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil."));
        await conn.OpenAsync(ct);
        try
        {
            foreach (var ip in ips)
            {
                var current = new List<string>();
                string? host = null;
                await using (var cmd = new SqlCommand($"SELECT TAG, HOSTNAME FROM {t} WHERE IPADDRESS = @ip", conn))
                {
                    cmd.Parameters.AddWithValue("@ip", ip);
                    await using var r = await cmd.ExecuteReaderAsync(ct);
                    if (await r.ReadAsync(ct))
                    {
                        if (!r.IsDBNull(0)) current = EtiketService.Split(r.GetString(0)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (!r.IsDBNull(1)) host = r.GetString(1);
                    }
                }
                var next = current.Where(x => remove == null || !string.Equals(x, remove, StringComparison.OrdinalIgnoreCase)).ToList();
                if (add != null && !next.Contains(add, StringComparer.OrdinalIgnoreCase)) next.Add(add);
                if (next.SequenceEqual(current)) continue;
                string? value = next.Count == 0 ? null : string.Join(",", next);
                if (value != null && value.Length > max) throw new InvalidOperationException($"{ip}: etiketler {max} karakteri geçiyor.");
                string? ad = adlar != null && adlar.TryGetValue(ip, out var a) && !string.IsNullOrWhiteSpace(a) ? a.Trim() : host;
                if (ad is { Length: > 200 }) ad = ad[..200];
                await using (var cmd = new SqlCommand(value == null ? $"DELETE FROM {t} WHERE IPADDRESS = @ip" : $"""
                    UPDATE {t} SET TAG = @v, HOSTNAME = @h, GUNCELLEYEN = @u, TARIH = SYSDATETIME() WHERE IPADDRESS = @ip;
                    IF @@ROWCOUNT = 0 INSERT INTO {t} (IPADDRESS, TAG, HOSTNAME, GUNCELLEYEN, TARIH) VALUES (@ip, @v, @h, @u, SYSDATETIME());
                    """, conn))
                {
                    cmd.Parameters.AddWithValue("@ip", ip);
                    cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@h", (object?)ad ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@u", user);
                    total += await cmd.ExecuteNonQueryAsync(ct) > 0 ? 1 : 0;
                }
                log.LogInformation("Etiket (ERT dışı): {User} {Ip} [{Old}] -> [{New}]", user, ip, string.Join(",", current), value ?? "");
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException($"ERT'de olmayan sunucuların etiket tablosu ({t}) yok; sql/etiket_ek.sql betiğini çalıştırın.");
        }
        return total;
    }
}
