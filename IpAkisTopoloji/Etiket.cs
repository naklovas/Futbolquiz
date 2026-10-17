using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Sunucu etiketleme: ERT_HOSTIPADDRESS tablosunun etiket kolonu (Envanter:EtiketKolonu, varsayılan TAG) okunur ve yazılır.
// Bir IP'nin tüm satırları aynı etiket metnini taşır ("VERIAMBARI,ETL"). Yazma: önce IP'nin mevcut etiketleri DB'den okunur,
// ekle / kaldır uygulanır, IP'nin bütün satırları güncellenir; ardından envanter yeniden yüklenir.
// Yetki: Etiket:Yazanlar listesi doluysa yalnız o kullanıcılar yazar (boş = sayfaya girebilen herkes). Her değişiklik loglanır.
// ---------------------------------------------------------------------------
record EtiketSunucu(string Ip, string? Hostname, List<string> Apps, string? Muhafiz, string? Segment, List<string> Tags);
record EtiketSayi(string Ad, int Sunucu);
// Sunucular = istenen sayfa; Toplam = filtreye uyan; Tum / Etiketli = envanterin geneli; Etiketler = etiket başına sunucu sayısı
record EtiketListe(List<EtiketSunucu> Sunucular, int Toplam, int Tum, int Etiketli, List<EtiketSayi> Etiketler);
record EtiketFiltre(string? Ip, string? Sunucu, string? Uygulama, string? Muhafiz, string? Segment, string? Etiket,
    string? Sirala, bool Azalan, int Sayfa, int Boyut);
record EtiketIstek(List<string>? Ips, string? Ekle, string? Kaldir);
record EtiketSonuc(int Guncellenen, List<EtiketSunucu> Sunucular);

static class EtiketService
{
    static readonly Regex SafeName = new(@"^[A-Za-z_][A-Za-z0-9_]*$");
    static readonly Regex TagRx = new(@"^[\p{L}\p{N}_\-. ]{1,50}$");

    public static string Column(IConfiguration cfg)
    {
        string c = cfg["Envanter:EtiketKolonu"] ?? "TAG";
        return SafeName.IsMatch(c) ? c : "TAG";
    }

    public static IEnumerable<string> Split(string raw) =>
        raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? NormalizeTag(string? t)
    {
        t = t?.Trim();
        return string.IsNullOrEmpty(t) || !TagRx.IsMatch(t) ? null : t.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
    }

    public static bool CanWrite(IConfiguration cfg, string? user)
    {
        var allowed = cfg.GetSection("Etiket:Yazanlar").Get<string[]>() ?? [];
        if (allowed.Length == 0) return true;
        string u = user ?? "";
        return allowed.Any(a => string.Equals(a, u, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(a, u.Split('\\').Last(), StringComparison.OrdinalIgnoreCase));
    }

    // Envanterdeki tüm sunucular (IP başına bir satır); envanter anlık görüntüsü başına bir kez hesaplanır.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EnvanterSnapshot, List<EtiketSunucu>> Cache = new();
    static List<EtiketSunucu> All(EnvanterSnapshot env) => Cache.GetValue(env, e => e.HostIps
        .Select(ip =>
        {
            var hosts = e.AppsOnIp(ip);
            string? muh = string.Join(", ", hosts.Select(h => h.VarlikMuhafizi).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            return new EtiketSunucu(ip, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(),
                e.AppNamesOnIp(ip).Take(5).ToList(), muh == "" ? null : muh, e.FindSegment(ip)?.Label, e.TagsOf(ip));
        })
        .ToList());

    public static EtiketSunucu? Row(EnvanterSnapshot env, string ip) => All(env).FirstOrDefault(s => s.Ip == ip);

    static bool Has(string? value, string? q) => string.IsNullOrWhiteSpace(q) || (value?.Contains(q.Trim(), StringComparison.CurrentCultureIgnoreCase) ?? false);

    // Kolon filtreleri (içinde geçen, büyük/küçük harf duyarsız), etiket filtresi ("" tümü, "-" etiketsiz, "*" etiketli, ad),
    // sıralama ve sayfalama.
    public static EtiketListe List(EnvanterSnapshot env, EtiketFiltre f)
    {
        var all = All(env);
        IEnumerable<EtiketSunucu> q = all
            .Where(s => Has(s.Ip, f.Ip) && Has(s.Hostname, f.Sunucu) && Has(string.Join(" ", s.Apps), f.Uygulama)
                        && Has(s.Muhafiz, f.Muhafiz) && Has(s.Segment, f.Segment))
            .Where(s => f.Etiket switch
            {
                null or "" => true,
                "-" => s.Tags.Count == 0,
                "*" => s.Tags.Count > 0,
                var t => s.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)
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
        var tags = env.TagCatalog().Select(t => new EtiketSayi(t.Tag, t.Count)).ToList();
        return new EtiketListe(sorted.Skip((page - 1) * size).Take(size).ToList(), sorted.Count, all.Count, all.Count(s => s.Tags.Count > 0), tags);
    }

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
            if (value is { Length: > 200 }) throw new InvalidOperationException($"{ip}: etiketler 200 karakteri geçiyor.");
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
