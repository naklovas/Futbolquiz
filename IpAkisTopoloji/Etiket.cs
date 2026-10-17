using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Sunucu etiketleme: ERT_HOSTIPADDRESS tablosunun etiket kolonu (Envanter:EtiketKolonu, varsayılan TAG) okunur ve yazılır.
// Bir IP'nin tüm satırları aynı etiket metnini taşır ("VERIAMBARI,ETL"). Yazma: önce IP'nin mevcut etiketleri DB'den okunur,
// ekle / kaldır uygulanır, IP'nin bütün satırları güncellenir; ardından envanter yeniden yüklenir.
// Yetki: Etiket:Yazanlar listesi doluysa yalnız o kullanıcılar yazar (boş = sayfaya girebilen herkes). Her değişiklik loglanır.
// ---------------------------------------------------------------------------
record EtiketSunucu(string Ip, string? Hostname, List<string> Apps, string? Segment, List<string> Tags);
record EtiketListe(List<EtiketSunucu> Sunucular, int Toplam, List<string> Etiketler);
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

    // Envanterdeki sunucular: arama (IP / hostname / uygulama içinde geçen), etiket filtresi, yalnız etiketsizler.
    public static EtiketListe List(EnvanterSnapshot env, string? q, string? tag, bool untagged, int max)
    {
        q = q?.Trim();
        var rows = env.HostIps
            .Select(ip =>
            {
                var hosts = env.AppsOnIp(ip);
                return new EtiketSunucu(ip, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(),
                    env.AppNamesOnIp(ip).Take(4).ToList(), env.FindSegment(ip)?.Label, env.TagsOf(ip));
            })
            .Where(s => string.IsNullOrEmpty(q)
                        || s.Ip.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || (s.Hostname?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                        || s.Apps.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Where(s => string.IsNullOrEmpty(tag) || s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Where(s => !untagged || s.Tags.Count == 0)
            .OrderBy(s => s.Tags.Count == 0).ThenBy(s => s.Ip, StringComparer.Ordinal)
            .ToList();
        return new EtiketListe(rows.Take(max).ToList(), rows.Count, env.TagCatalog().Select(t => t.Tag).ToList());
    }

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
