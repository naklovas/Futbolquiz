// ---------------------------------------------------------------------------
// DB'ye gelenler (Dataskope): seçilen aralıkta veritabanı oturum kayıtları veritabanı bazında toplanır.
//  - Veritabanı = sunucu IP + port + instance (db_type, makine adı, DB adları, kayıt / istemci sayısı, son görülme).
//  - Her veritabanı için gelen istemciler (client_ip): makine adı, OS / DB kullanıcıları, programlar, kayıt sayısı,
//    ilk / son; envanterden segment ve uygulama. Veritabanı sunucusunun kendi segmenti ve uygulamaları da eklenir.
// Kayıt hacmi yüksek olabildiği için en çok Dataskope:DbGelenMaxKayit (varsayılan 20000) kayıt okunur; filtre ile daraltılır.
// ---------------------------------------------------------------------------
record DbIstemci(string Ip, string? Host, List<string> OsUsers, List<string> DbUsers, List<string> Programs, long Count,
    string? First, string? Last, SegmentInfo? Segment, List<string> Apps);
record DbOzet(string Key, string? ServerIp, string? Port, string? DbType, string? Instance, string? Machine, List<string> DbNames,
    long Count, int ClientCount, string? Last, SegmentInfo? Segment, List<string> Apps, List<DbIstemci> Clients);
record DbGelenSonuc(string Pencere, string Sorgu, long Toplam, int Okunan, bool Eksik, List<DbOzet> Veritabanlari,
    SourceStatus Dataskope, SourceStatus Envanter, long ElapsedMs);

static class DbGelen
{
    // Serbest filtre: boşsa yalnız DB türü; IP ise server_ip; alan:değer içeriyorsa olduğu gibi; değilse instance / DB / makine adı.
    public static string Query(string? filtre, string? tur)
    {
        var parts = new List<string>();
        string f = (filtre ?? "").Trim();
        if (f != "")
        {
            if (LookupQuery.TryParseIpv4(f, out var ip)) parts.Add($"server_ip:\"{ip}\"");
            else if (f.Contains(':')) parts.Add(f);
            else
            {
                string v = f.Replace("\"", "");
                parts.Add($"(instance_name:\"{v}\" OR db_name:\"{v}\" OR MachineName:\"{v}\")");
            }
        }
        string t = (tur ?? "").Trim().Replace("\"", "");
        if (t != "") parts.Add($"db_type:\"{t}\"");
        return string.Join(" AND ", parts);  // boş = filtresiz (parametre hiç gönderilmez)
    }

    public static DbGelenSonuc Build(LookupQuery q, string sorgu, DataskopeSonuc? ds, EnvanterSnapshot? env,
        SourceStatus dsSt, SourceStatus envSt, long ms)
    {
        static string? T(string? t) => DateTimeOffset.TryParse(t, out var d) ? d.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : t;
        static List<string> D(IEnumerable<string?> xs) => xs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct().Order().ToList();
        var recs = ds?.Kayitlar ?? [];
        var dbs = recs.GroupBy(k => $"{k.ServerIp}|{k.ServerPort}|{k.Instance}")
            .Select(g =>
            {
                var f = g.First();
                var clients = g.Where(k => !string.IsNullOrEmpty(k.ClientIp)).GroupBy(k => k.ClientIp!)
                    .Select(c => new DbIstemci(c.Key, c.Select(k => k.ClientHost).FirstOrDefault(h => !string.IsNullOrEmpty(h)),
                        D(c.Select(k => k.OsUser)), D(c.Select(k => k.DbUser)), D(c.Select(k => k.ClientApp)), c.Count(),
                        c.Select(k => T(k.Time)).Min(StringComparer.Ordinal), c.Select(k => T(k.Time)).Max(StringComparer.Ordinal),
                        env?.FindSegment(c.Key), env?.AppNamesOnIp(c.Key) ?? []))
                    .OrderByDescending(c => c.Count).ToList();
                return new DbOzet(g.Key, f.ServerIp, f.ServerPort, f.DbType, f.Instance, f.Machine, D(g.Select(k => k.DbName)),
                    g.Count(), clients.Count, g.Select(k => T(k.Time)).Max(StringComparer.Ordinal),
                    f.ServerIp == null ? null : env?.FindSegment(f.ServerIp), f.ServerIp == null ? [] : env?.AppNamesOnIp(f.ServerIp) ?? [], clients);
            })
            .OrderByDescending(d => d.Count).ToList();
        return new DbGelenSonuc($"{q.Start:dd.MM.yyyy HH:mm} - {q.End:dd.MM.yyyy HH:mm}", sorgu, ds?.Toplam ?? 0, recs.Count, ds?.Eksik ?? false,
            dbs, dsSt, envSt, ms);
    }
}
