using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// ERT eksikleri: ERT_HOSTIPADDRESS'te APPNAME'i boş (IP, port) satırları için AI'dan uygulama önerisi.
// Kanıtlar (sunucu başına): işlev haritasının bilgi özeti (ERT diğer portlar / WEBSITE / SERVICENAME, segment, VIP, MSY, ÇSY,
// Kahin, SM kurulum ve iş istekleri; IslevService.ProfilesAsync), Carbon Black (son Eksik:Saat saat: hangi portu hangi exe
// kabul ediyor, kimler bağlanıyor, exe'ler nereye gidiyor) ve aday uygulamalar (aynı sunucu, VIP, muhafızın uygulamaları,
// adı kanıtlarla örtüşen ERT uygulamaları). AI adayı harfi harfine seçer ya da "yeni" ad önerir.
// Sonuçlar ERT'ye YAZILMAZ: Eksik:OneriTablosu (dbo.DF_ERT_UYGULAMA_ONERI, sql/ert_oneri.sql); onay / ret / elle düzeltme,
// Excel'e (CSV) aktarım. Tablo yoksa öneriler bellekte tutulur (uyarı gösterilir).
// ---------------------------------------------------------------------------
record EksikOneri(string? Uygulama, bool Yeni, int Guven, string? Gerekce, string Durum, string? Kaynak, DateTime Tarih);
record EksikSatir(string Ip, string Port, string? Hostname, string? Segment, string? Website, string? Servis, string? Muhafiz,
    string? Birim, List<string> Diger, bool Altyapi, EksikOneri? Oneri);
record EksikListe(List<EksikSatir> Satirlar, int Toplam, int TumSatir, int TumIp, int Onerili, int Onayli, IslevDurum Durum,
    bool Yazabilir, string? Uyari);
record EksikCalistir(List<string>? Ips, bool Hepsi, bool Yeniden, int? Saat);
record EksikKarar(string? Ip, string? Port, string? Karar, string? Uygulama);

sealed class EksikService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, IslevService islev,
    ILogger<EksikService> log)
{
    public const string Onay = "Onaylandı", Red = "Reddedildi", Ai = "AI", Elle = "Elle";
    readonly object _gate = new();
    IslevDurum _durum = new(false, null, 0, 0, 0, null, null, null, null, null, []);
    CancellationTokenSource? _cts;
    // (ip, port) → öneri; tablo yoksa yalnız bellekte
    readonly Dictionary<(string, string), EksikOneri> _mem = new();
    readonly Dictionary<string, string> _kanit = new();   // ip → AI'a giden metin (bu çalışmada)
    Dictionary<(string, string), EksikOneri>? _cache;
    string? _uyari;

    public IslevDurum Durum { get { lock (_gate) return _durum; } }
    string Table => cfg["Eksik:OneriTablosu"] is { Length: > 0 } t && System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")
        ? t : "dbo.DF_ERT_UYGULAMA_ONERI";
    string ConnStr => cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");

    // ---------------- Eksik satırlar ----------------
    static string Key(string? port) => port ?? "";

    public static List<(string Ip, string Port, HostApp Row)> Missing(EnvanterSnapshot env) =>
        env.HostIps.SelectMany(ip => env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName))
                .GroupBy(h => Key(h.Port)).Select(g => (ip, g.Key, g.First())))
            .ToList();

    public async Task<EksikListe> ListAsync(string? ara, string? durum, bool altyapi, int sayfa, int boyut, bool yazabilir, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var infra = AppTopology.InfraPorts(cfg);
        var oneri = await LoadAsync(false, ct);
        var all = Missing(env);
        var rows = all.Select(m =>
        {
            var hosts = env.AppsOnIp(m.Ip);
            oneri.TryGetValue((m.Ip, m.Port), out var o);
            return new EksikSatir(m.Ip, m.Port, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(), env.FindSegment(m.Ip)?.Label,
                m.Row.Website, m.Row.ServiceName, m.Row.VarlikMuhafizi ?? hosts.Select(h => h.VarlikMuhafizi).OfType<string>().FirstOrDefault(),
                m.Row.SunucuIsletenBirim, env.AppNamesOnIp(m.Ip).Take(4).ToList(), infra.Contains(m.Port), o);
        }).ToList();
        string? q = ara?.Trim();
        bool Has(string? v) => v != null && v.Contains(q!, StringComparison.CurrentCultureIgnoreCase);
        var f = rows.Where(r => altyapi || !r.Altyapi)
            .Where(r => string.IsNullOrEmpty(q) || Has(r.Ip) || Has(r.Port) || Has(r.Hostname) || Has(r.Website) || Has(r.Servis)
                        || Has(r.Muhafiz) || Has(r.Segment) || Has(r.Oneri?.Uygulama) || r.Diger.Any(Has))
            .Where(r => durum switch
            {
                "yok" => r.Oneri == null,
                "ai" => r.Oneri?.Durum == Ai,
                "onay" => r.Oneri?.Durum is Onay or Elle,
                "red" => r.Oneri?.Durum == Red,
                _ => true
            })
            .OrderBy(r => r.Oneri == null ? 1 : 0).ThenByDescending(r => r.Oneri?.Guven ?? 0).ThenBy(r => IpKey(r.Ip)).ThenBy(r => int.TryParse(r.Port, out var n) ? n : 0)
            .ToList();
        int size = Math.Clamp(boyut, 20, 1000), page = Math.Max(1, sayfa);
        return new EksikListe(f.Skip((page - 1) * size).Take(size).ToList(), f.Count, rows.Count, rows.Select(r => r.Ip).Distinct().Count(),
            rows.Count(r => r.Oneri != null), rows.Count(r => r.Oneri?.Durum is Onay or Elle), Durum, yazabilir, _uyari);
    }

    static string IpKey(string ip) => string.Join(".", ip.Split('.').Select(p => p.PadLeft(3, '0')));

    // ---------------- Öneri tablosu ----------------
    async Task<Dictionary<(string, string), EksikOneri>> LoadAsync(bool force, CancellationToken ct)
    {
        if (_cache != null && !force) return _cache;
        var map = new Dictionary<(string, string), EksikOneri>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT IP, PORT, UYGULAMA, YENI, GUVEN, GEREKCE, DURUM, KAYNAK, TARIH FROM {Table}", conn);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            while (await r.ReadAsync(ct))
                map[(r.GetString(0), S(1) ?? "")] = new EksikOneri(S(2), !r.IsDBNull(3) && r.GetBoolean(3), r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    S(5), S(6) ?? Ai, S(7), r.IsDBNull(8) ? DateTime.Now : r.GetDateTime(8));
            _uyari = null;
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            _uyari = $"Öneri tablosu ({Table}) yok; öneriler yalnız bellekte tutuluyor (uygulama yeniden başlayınca kaybolur). sql/ert_oneri.sql betiğini çalıştırın.";
            lock (_gate) foreach (var kv in _mem) map[kv.Key] = kv.Value;
        }
        return _cache = map;
    }

    async Task SaveAsync(string ip, string port, EksikOneri o, string? kanit, CancellationToken ct)
    {
        lock (_gate) _mem[(ip, port)] = o;
        if (_cache != null) lock (_gate) _cache[(ip, port)] = o;
        if (_uyari != null) return;
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"""
                UPDATE {Table} SET UYGULAMA = @u, YENI = @y, GUVEN = @g, GEREKCE = @r, DURUM = @d, KAYNAK = @k, TARIH = SYSDATETIME(),
                       KANIT = COALESCE(@kn, KANIT)
                WHERE IP = @ip AND PORT = @p;
                IF @@ROWCOUNT = 0
                  INSERT INTO {Table} (IP, PORT, UYGULAMA, YENI, GUVEN, GEREKCE, DURUM, KAYNAK, TARIH, KANIT)
                  VALUES (@ip, @p, @u, @y, @g, @r, @d, @k, SYSDATETIME(), @kn);
                """, conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            cmd.Parameters.AddWithValue("@p", port);
            cmd.Parameters.AddWithValue("@u", (object?)o.Uygulama ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@y", o.Yeni);
            cmd.Parameters.AddWithValue("@g", o.Guven);
            cmd.Parameters.AddWithValue("@r", (object?)Cut(o.Gerekce, 1000) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", o.Durum);
            cmd.Parameters.AddWithValue("@k", (object?)o.Kaynak ?? DBNull.Value);
            cmd.Parameters.Add("@kn", System.Data.SqlDbType.NVarChar, -1).Value = (object?)kanit ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 208) { _uyari = $"Öneri tablosu ({Table}) yok; sql/ert_oneri.sql betiğini çalıştırın."; }
    }

    static string? Cut(string? s, int n) => s == null ? null : s.Length > n ? s[..n] : s;

    // Onay / ret / elle düzeltme
    public async Task DecideAsync(EksikKarar k, string user, CancellationToken ct)
    {
        string ip = k.Ip?.Trim() ?? throw new InvalidOperationException("IP gerekli."), port = k.Port?.Trim() ?? "";
        var all = await LoadAsync(false, ct);
        all.TryGetValue((ip, port), out var cur);
        var env = await envanter.GetAsync(false, ct);
        EksikOneri o = k.Karar switch
        {
            "onay" when cur?.Uygulama != null => cur with { Durum = Onay, Kaynak = user, Tarih = DateTime.Now },
            "red" => (cur ?? new EksikOneri(null, false, 0, null, Red, user, DateTime.Now)) with { Durum = Red, Kaynak = user, Tarih = DateTime.Now },
            "elle" when !string.IsNullOrWhiteSpace(k.Uygulama) => new EksikOneri(Snap(env, k.Uygulama!.Trim(), out bool yeni), yeni, 100,
                cur?.Gerekce, Elle, user, DateTime.Now),
            "sil" => new EksikOneri(null, false, 0, null, "", user, DateTime.Now),
            _ => throw new InvalidOperationException("Geçersiz karar ya da öneri yok.")
        };
        if (k.Karar == "sil")
        {
            lock (_gate) { _mem.Remove((ip, port)); _cache?.Remove((ip, port)); }
            if (_uyari == null)
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand($"DELETE FROM {Table} WHERE IP = @ip AND PORT = @p", conn);
                cmd.Parameters.AddWithValue("@ip", ip); cmd.Parameters.AddWithValue("@p", port);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        else await SaveAsync(ip, port, o, null, ct);
        log.LogInformation("ERT önerisi: {User} {Ip}:{Port} {Karar} [{App}]", user, ip, port, k.Karar, o.Uygulama);
    }

    // Ad ERT kataloğunda varsa kataloğun yazımı; yoksa olduğu gibi (yeni)
    static string Snap(EnvanterSnapshot env, string name, out bool yeni)
    {
        var hit = env.AppNames.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                  ?? env.AppNames.FirstOrDefault(a => Norm(a) == Norm(name));
        yeni = hit == null;
        return hit ?? name;
    }
    static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    // ---------------- Kanıt (AI'a giden metin) ----------------
    public async Task<string> EvidenceAsync(string ip, CancellationToken ct)
    {
        lock (_gate) if (_kanit.TryGetValue(ip, out var k)) return k;
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT TOP 1 KANIT FROM {Table} WHERE IP = @ip AND KANIT IS NOT NULL ORDER BY TARIH DESC", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            if (await cmd.ExecuteScalarAsync(ct) is string s) return s;
        }
        catch (SqlException) { /* tablo yoksa yeniden üretilir */ }
        var env = await envanter.GetAsync(false, ct);
        var miss = Missing(env).Where(m => m.Ip == ip).Select(m => m.Port).ToList();
        var texts = await BuildAsync([ip], miss.Count > 0 ? new() { [ip] = miss } : new() { [ip] = [] }, env, Hours(null), ct);
        return texts.GetValueOrDefault(ip) ?? "";
    }

    int Hours(int? h) => Math.Clamp(h ?? cfg.GetValue("Eksik:Saat", 24), 1, Math.Max(1, cfg.GetValue("MaxRangeHours", 24 * 7)));

    // Sunucu başına kanıt metni: işlev haritası bilgi özeti + Carbon Black + aday uygulamalar
    async Task<Dictionary<string, string>> BuildAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env,
        int hours, CancellationToken ct)
    {
        var profiles = await islev.ProfilesAsync(ips, env, ct);
        var cb = await CarbonBlackAsync(ips, hours, ct);
        var sm = await SmPortAsync(ips, ports, env, ct);
        var outp = new Dictionary<string, string>();
        foreach (var ip in ips)
        {
            var sb = new StringBuilder();
            var miss = ports.GetValueOrDefault(ip) ?? [];
            sb.AppendLine(profiles.TryGetValue(ip, out var p) ? p.Metin.TrimEnd() : $"### Sunucu {ip}");
            var empty = env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName) && miss.Contains(Key(h.Port))).GroupBy(h => Key(h.Port));
            sb.AppendLine("UYGULAMA ADI BOŞ PORTLAR (ERT satırı):");
            foreach (var g in empty)
            {
                var h = g.First();
                sb.AppendLine($"- port {(g.Key == "" ? "(yok)" : g.Key)}: " + string.Join(", ", new[]
                {
                    h.Website is { } w ? "WEBSITE=" + w : null, h.ServiceName is { } sv ? "SERVICENAME=" + sv : null,
                    h.VipIp is { } vi ? $"VIP={vi}:{h.VipPort}" : null, h.UnixName is { } un ? "UNIXNAME=" + un : null,
                    h.VarlikMuhafizi is { } vm ? "muhafız=" + vm : null, h.SunucuIsletenBirim is { } sb2 ? "işleten=" + sb2 : null
                }.OfType<string>().DefaultIfEmpty("başka bilgi yok")));
            }
            sb.Append(Neighbors(env, ip, miss));
            if (sm.TryGetValue(ip, out var smText)) sb.Append(smText);
            if (cb.TryGetValue(ip, out var c)) sb.Append(c.Text(env, ip, miss));
            else sb.AppendLine($"Carbon Black (son {hours} saat): bu sunucuda ağ olayı yok (ajan yok ya da trafik yok).");
            var cands = Candidates(env, ip, sb.ToString());
            if (cands.Count > 0) sb.AppendLine("ADAY UYGULAMALAR (ERT'deki adlar): " + string.Join(" | ", cands));
            outp[ip] = sb.ToString();
        }
        return outp;
    }

    // SM talepleri (IP + port): açıklamasında sunucunun IP'si ya da adı VE boş portlardan biri geçen en yeni talepler
    // (kullanıcının diğer projesindeki sorgu). SM verisi ERT ile aynı doku DB'sinde: Eksik:SmTablolari (varsayılan dbo.REQUESTM1).
    // Tablo büyük: tek sorgu tüm grup (≤20 sunucu) için, yalnız son Eksik:SmYil (varsayılan Islev:SmYil = 3) yıl; hangi talebin
    // hangi sunucu / porta ait olduğu C#'ta ayrılır (port tam sayı olarak). ZT_*ACIKLAMA yoksa yalnız DESCRIPTION / BRIEF_DESCRIPTION.
    async Task<Dictionary<string, string>> SmPortAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env, CancellationToken ct)
    {
        var want = ips.Select(ip => (ip, host: env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault(),
                ps: (ports.GetValueOrDefault(ip) ?? []).Where(p => p.Length > 0 && p.All(char.IsDigit)).ToList()))
            .Where(x => x.ps.Count > 0).ToList();
        var outp = new Dictionary<string, string>();
        if (want.Count == 0) return outp;
        var tables = cfg.GetSection("Eksik:SmTablolari").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (tables.Count == 0) tables = ["dbo.REQUESTM1"];
        var since = DateTime.Today.AddYears(-Math.Clamp(cfg.GetValue("Eksik:SmYil", cfg.GetValue("Islev:SmYil", 3)), 1, 20));
        var rows = new List<(string No, DateTime? T, string Who, string Desc, string Text)>();
        string? err = null;
        foreach (var table in tables.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")))
        {
            async Task Query(bool zt)
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand("", conn) { CommandTimeout = cfg.GetValue("Islev:SqlTimeoutSaniye", 180) };
                var cols = zt ? new[] { "req.DESCRIPTION", "req.BRIEF_DESCRIPTION", "req.ZT_KAYNAKACIKLAMA", "req.ZT_HEDEFACIKLAMA" }
                              : new[] { "req.DESCRIPTION", "req.BRIEF_DESCRIPTION" };
                int n = 0;
                string P(string v) { string name = "@w" + n++; cmd.Parameters.AddWithValue(name, $"%{v}%"); return name; }
                var who = want.SelectMany(x => new[] { x.ip }.Concat(x.host is { Length: >= 4 } h ? [h] : []))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Select(P).SelectMany(p => cols.Select(c => $"{c} LIKE {p}")).ToList();
                var port = want.SelectMany(x => x.ps).Distinct().Select(P).Select(p => $"req.DESCRIPTION LIKE {p} OR req.BRIEF_DESCRIPTION LIKE {p}").ToList();
                cmd.Parameters.AddWithValue("@since", since);
                cmd.CommandText = $"""
                    SELECT TOP (500) req.NUMBER, req.OPEN_TIME, req.BRIEF_DESCRIPTION, LEFT(CAST(req.DESCRIPTION AS nvarchar(max)), 800)
                        {(zt ? ", req.ZT_KAYNAKACIKLAMA, req.ZT_HEDEFACIKLAMA" : "")}
                    FROM {table} req WITH (NOLOCK)
                    WHERE req.OPEN_TIME >= @since AND ({string.Join(" OR ", who)}) AND ({string.Join(" OR ", port)})
                    ORDER BY req.OPEN_TIME DESC
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    string Txt(int i) => i < r.FieldCount && !r.IsDBNull(i) ? Convert.ToString(r.GetValue(i)) ?? "" : "";
                    string all = string.Join(" · ", new[] { Txt(2), Txt(3), zt ? Txt(4) : "", zt ? Txt(5) : "" }.Where(x => x.Length > 0));
                    rows.Add((Txt(0), r.IsDBNull(1) ? null : r.GetDateTime(1), all, Txt(2) + " " + Txt(3),
                        System.Text.RegularExpressions.Regex.Replace(all, @"\s+", " ")));
                }
            }
            try { await Query(true); }
            catch (SqlException ex) when (ex.Number == 207)
            {
                try { await Query(false); } catch (SqlException ex2) { err = $"{table}: {ex2.Message}"; }
            }
            catch (SqlException ex) { err = $"{table}: {ex.Message}"; }
        }
        foreach (var (ip, host, ps) in want)
        {
            // IP tam (10.1.1.1, 10.1.1.10'a uymasın), sunucu adı içinde; port tam sayı olarak açıklamada
            var mine = rows.Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.Who, $@"(?<![\d.]){System.Text.RegularExpressions.Regex.Escape(ip)}(?![\d])")
                                       || (host is { Length: >= 4 } && r.Who.Contains(host, StringComparison.OrdinalIgnoreCase)))
                .Select(r => (r, found: ps.Where(p => System.Text.RegularExpressions.Regex.IsMatch(r.Desc, $@"(?<!\d){p}(?!\d)")).ToList()))
                .Where(x => x.found.Count > 0).DistinctBy(x => x.r.No).OrderByDescending(x => x.r.T).Take(10).ToList();
            if (mine.Count > 0)
            {
                var sb = new StringBuilder("SM TALEPLERİ (açıklamasında bu sunucu VE boş port geçen, en yeni):\n");
                foreach (var (r, found) in mine) sb.AppendLine($"- {r.No} {r.T:yyyy-MM-dd} [port {string.Join(",", found)}]: {Cut(r.Text, 400)}");
                outp[ip] = sb.ToString();
            }
            else if (err != null) outp[ip] = $"SM TALEPLERİ (IP + port): okunamadı ({Cut(err, 150)})\n";
        }
        return outp;
    }

    // Komşu ipuçları: benzer adlı sunucular (ad sonundaki sayı atılınca aynı: zb1pwatmvzsra01/02…), aynı segmentte aynı portu
    // kullanan sunucular ve segmentin yaygın uygulamaları (hepsi ERT'de uygulama adı dolu satırlardan)
    Dictionary<string, Dictionary<string, int>>? _byStem, _bySegPort, _bySeg;
    EnvanterSnapshot? _nbEnv;
    static string? Stem(string? host) => host == null ? null
        : System.Text.RegularExpressions.Regex.Replace(host.Split('.')[0].ToLowerInvariant(), @"[-_]?\d+$", "") is { Length: >= 4 } st ? st : null;
    static void Count(Dictionary<string, Dictionary<string, int>> d, string? k, string app)
    {
        if (k == null) return;
        if (!d.TryGetValue(k, out var m)) d[k] = m = new(StringComparer.OrdinalIgnoreCase);
        m[app] = m.GetValueOrDefault(app) + 1;
    }
    static string Top(Dictionary<string, Dictionary<string, int>> d, string? k, int n = 5) =>
        k != null && d.TryGetValue(k, out var m) ? string.Join(", ", m.OrderByDescending(x => x.Value).Take(n).Select(x => $"{x.Key} ({x.Value})")) : "";
    string Neighbors(EnvanterSnapshot env, string ip, List<string> miss)
    {
        if (!ReferenceEquals(_nbEnv, env))
        {
            _byStem = new(); _bySegPort = new(); _bySeg = new();
            foreach (var i in env.HostIps)
            {
                var rows = env.AppsOnIp(i).Where(h => h.AppName != null && !env.IsSystemApp(h.AppName)).ToList();
                if (rows.Count == 0) continue;
                string? seg = env.FindSegment(i)?.Label, stem = Stem(env.AppsOnIp(i).Select(h => h.UnixName).OfType<string>().FirstOrDefault());
                foreach (var app in rows.Select(h => h.AppName!).Distinct(StringComparer.OrdinalIgnoreCase)) { Count(_byStem, stem, app); Count(_bySeg, seg, app); }
                foreach (var h in rows.Where(h => h.Port != null)) Count(_bySegPort, seg + "|" + h.Port, h.AppName!);
            }
            _nbEnv = env;
        }
        var sb = new StringBuilder();
        string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault(), segL = env.FindSegment(ip)?.Label, st = Stem(host);
        if (Top(_byStem!, st) is { Length: > 0 } a) sb.AppendLine($"BENZER ADLI SUNUCULAR ({st}*) ERT'de: {a}");
        foreach (var p in miss.Where(p => p != ""))
            if (Top(_bySegPort!, segL + "|" + p) is { Length: > 0 } b) sb.AppendLine($"AYNI SEGMENTTE ({segL}) {p} PORTUNU KULLANAN SUNUCULAR ERT'de: {b}");
        if (Top(_bySeg!, segL, 6) is { Length: > 0 } c) sb.AppendLine($"SEGMENTİN ({segL}) YAYGIN UYGULAMALARI: {c}");
        return sb.ToString();
    }

    // Aday uygulamalar: aynı sunucunun diğer portları, VIP'leri ve havuz komşuları, aynı muhafızın uygulamaları,
    // adı kanıt metniyle (exe yolu, WEBSITE, sunucu adı…) kelime düzeyinde örtüşen ERT uygulamaları
    Dictionary<string, List<string>>? _byMuhafiz;
    List<(string App, HashSet<string> Tok)>? _catTok;
    EnvanterSnapshot? _candEnv;
    List<string> Candidates(EnvanterSnapshot env, string ip, string evidence)
    {
        if (!ReferenceEquals(_candEnv, env))
        {
            _byMuhafiz = env.HostIps.SelectMany(i => env.AppsOnIp(i)).Where(h => h.VarlikMuhafizi != null && h.AppName != null && !env.IsSystemApp(h.AppName))
                .GroupBy(h => h.VarlikMuhafizi!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.GroupBy(h => h.AppName!).OrderByDescending(x => x.Count()).Select(x => x.Key).Take(8).ToList(), StringComparer.OrdinalIgnoreCase);
            _catTok = env.AppNames.Select(a => (a, IslevService.Tokens(a))).Where(x => x.Item2.Count > 0).ToList();
            _candEnv = env;
        }
        var list = new List<string>();
        void Add(IEnumerable<string?> xs) { foreach (var x in xs) if (x != null && !env.IsSystemApp(x) && !list.Contains(x, StringComparer.OrdinalIgnoreCase)) list.Add(x); }
        Add(env.AppNamesOnIp(ip));
        foreach (var m in env.MemberOf(ip))
        {
            Add(env.Match(m.LbIp, m.LbPort).Apps.Select(a => a.AppName));
            Add(env.VipMembers(m.LbIp, m.LbPort).Where(x => x.Ip != ip).SelectMany(x => env.AppNamesOnIp(x.Ip)).Take(6));
        }
        string? host = env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault(), segL = env.FindSegment(ip)?.Label;
        if (_byStem != null && Stem(host) is { } st && _byStem.TryGetValue(st, out var sm)) Add(sm.OrderByDescending(x => x.Value).Take(5).Select(x => (string?)x.Key));
        if (_bySegPort != null)
            foreach (var h in env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName) && h.Port != null))
                if (_bySegPort.TryGetValue(segL + "|" + h.Port, out var spm)) Add(spm.OrderByDescending(x => x.Value).Take(3).Select(x => (string?)x.Key));
        foreach (var mu in env.AppsOnIp(ip).Select(h => h.VarlikMuhafizi).OfType<string>().Distinct())
            if (_byMuhafiz!.TryGetValue(mu, out var apps)) Add(apps);
        var tok = IslevService.Tokens(evidence);
        Add(_catTok!.Select(c => (c.App, n: c.Tok.Count(t => t.Length >= 3 && tok.Contains(t)), r: (double)c.Tok.Count(tok.Contains) / c.Tok.Count))
            .Where(x => x.n >= 1 && x.r >= 0.5).OrderByDescending(x => x.r).ThenByDescending(x => x.n).Take(12).Select(x => (string?)x.App));
        return list.Take(30).ToList();
    }

    // ---------------- Carbon Black ----------------
    sealed class Cb
    {
        public readonly List<(string Yon, string Port, string Yol, long Hits, int Peers, List<string> Ornek)> Rows = [];
        public string Text(EnvanterSnapshot env, string ip, List<string> missing)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CARBON BLACK (bu sunucudaki süreçlerin ağ bağlantıları):");
            string Exe(string yol) => yol.Split('\\', '/').Last();
            string Peers(List<string> ips) => string.Join(", ", ips.Select(p =>
                env.IsVipGw(p, out _) ? "LB GW" : env.AppNamesOnIp(p).FirstOrDefault() is { } a ? a : env.VipMembers(p).Count > 0 ? "VIP " + p : p).Distinct().Take(5));
            foreach (var g in Rows.Where(r => r.Yon == "in").GroupBy(r => r.Port).OrderBy(g => missing.Contains(g.Key) ? 0 : 1).ThenByDescending(g => g.Sum(r => r.Hits)).Take(15))
            {
                var procs = g.OrderByDescending(r => r.Hits).Take(3).Select(r =>
                    Exe(r.Yol) is var e && (e.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase) || e.Equals("System", StringComparison.OrdinalIgnoreCase))
                        ? "ntoskrnl.exe (HTTP.sys: asıl uygulama HTTP.sys'e kayıtlı ayrı bir exe)" : $"{e} [{r.Yol}]");
                sb.AppendLine($"- gelen port {g.Key}{(missing.Contains(g.Key) ? " (ERT'de uygulama adı boş)" : "")}: {string.Join("; ", procs)} · {g.Sum(r => r.Hits)} bağlantı, {g.Max(r => r.Peers)} istemci · istemciler: {Peers(g.SelectMany(r => r.Ornek).ToList())}");
            }
            foreach (var g in Rows.Where(r => r.Yon == "out").GroupBy(r => r.Yol).OrderByDescending(g => g.Sum(r => r.Hits)).Take(10))
                sb.AppendLine($"- {Exe(g.Key)} [{g.Key}] dışarı bağlanıyor: " + string.Join(", ",
                    g.OrderByDescending(r => r.Hits).Take(5).Select(r => $"port {r.Port} → {Peers(r.Ornek)}")));
            return sb.ToString();
        }
    }

    async Task<Dictionary<string, Cb>> CarbonBlackAsync(List<string> ips, int hours, CancellationToken ct)
    {
        var outp = new Dictionary<string, Cb>();
        if (ips.Count == 0 || string.IsNullOrEmpty(cfg["Splunk:BaseUrl"])) return outp;
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        var valid = ips.Where(ip => LookupQuery.TryParseIpv4(ip, out _)).ToList();
        foreach (var part in valid.Chunk(20))
        {
            string terms = string.Join(" OR ", part.Select(ip => $"TERM({ip})"));
            string inList = string.Join(",", part.Select(ip => $"\"{ip}\""));
            string spl = $"""
                search index={index} sourcetype="{sourcetype}" ({terms}) local_ip IN ({inList})
                | eval yon=if(lower(direction)=="outbound", "out", "in"),
                       yol=coalesce(process_path, "-"),
                       port=if(yon=="out", remote_port, local_port)
                | stats count dc(remote_ip) as peers values(remote_ip) as ornek by local_ip yon port yol
                | eval ornek=mvjoin(mvindex(ornek, 0, 7), " ")
                """;
            var q = new LookupQuery(part[0], DateTimeOffset.Now.AddHours(-hours), DateTimeOffset.Now);
            SplunkResult res;
            try { res = await SplunkService.ExportAsync(spl, q, cfg, factory.CreateClient("splunk"), ct, "Splunk"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "ERT eksikleri: Carbon Black sorgusu başarısız"); continue; }
            foreach (var r in res.Rows)
            {
                string? V(string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
                if (V("local_ip") is not { } ip) continue;
                if (!outp.TryGetValue(ip, out var c)) outp[ip] = c = new Cb();
                c.Rows.Add((V("yon") ?? "in", V("port") ?? "", V("yol") ?? "-", long.TryParse(V("count"), out var n) ? n : 1,
                    int.TryParse(V("peers"), out var pe) ? pe : 0,
                    (V("ornek") ?? "").Split(new[] { ' ', '|' }, StringSplitOptions.RemoveEmptyEntries).Where(x => x != ip && x != "127.0.0.1").ToList()));
            }
        }
        return outp;
    }

    // ---------------- Arka plan işi ----------------
    public bool Start(EksikCalistir req, string user)
    {
        lock (_gate)
        {
            if (_durum.Calisiyor) return false;
            _cts = new CancellationTokenSource();
            _durum = new(true, "Başlıyor", 0, 0, 0, null, null, user, DateTimeOffset.Now, null, []);
        }
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(req, ct); Set(d => d with { Calisiyor = false, Asama = "Bitti", Bitis = DateTimeOffset.Now }); }
            catch (OperationCanceledException) { Set(d => d with { Calisiyor = false, Asama = "Durduruldu", Bitis = DateTimeOffset.Now }); }
            catch (Exception ex)
            {
                log.LogError(ex, "ERT eksikleri: AI önerisi başarısız");
                Set(d => d with { Calisiyor = false, Asama = "Hata", Mesaj = ex.Message, Bitis = DateTimeOffset.Now });
            }
        });
        return true;
    }

    public void Stop() { lock (_gate) _cts?.Cancel(); }
    void Set(Func<IslevDurum, IslevDurum> f) { lock (_gate) _durum = f(_durum); }
    void Fail(string msg) => Set(d => d with { Hata = d.Hata + 1, Hatalar = d.Hatalar.Count < 30 ? [.. d.Hatalar, msg] : d.Hatalar });

    async Task RunAsync(EksikCalistir req, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var infra = AppTopology.InfraPorts(cfg);
        var have = await LoadAsync(true, ct);
        var wanted = req.Ips is { Count: > 0 } ? req.Ips.ToHashSet() : null;
        // Hedef: istenen IP'ler ya da önerisi olmayanlar; altyapı portları ve onaylanmış / elle girilmiş olanlar atlanır
        var todo = Missing(env).Where(m => !infra.Contains(m.Port))
            .Where(m => wanted == null || wanted.Contains(m.Ip))
            .Where(m => !have.TryGetValue((m.Ip, m.Port), out var o) || (o.Durum is not (Onay or Elle) && (req.Yeniden || wanted != null)))
            .GroupBy(m => m.Ip).OrderByDescending(g => g.Count())
            .Take(Math.Clamp(cfg.GetValue("Eksik:MaxIp", 200), 1, 5000))
            .ToDictionary(g => g.Key, g => g.Select(m => m.Port).ToList());
        if (todo.Count == 0) throw new InvalidOperationException("Öneri bekleyen satır yok (altyapı portları ve onaylananlar hariç).");
        int total = todo.Values.Sum(v => v.Count), hours = Hours(req.Saat);
        Set(d => d with { Asama = "Kanıtlar toplanıyor", Toplam = total });
        int paket = Math.Clamp(cfg.GetValue("Eksik:Paket", 3), 1, 10), paralel = Math.Clamp(cfg.GetValue("Eksik:Paralel", 2), 1, 6);
        using var gate = new SemaphoreSlim(paralel);
        // Kanıtlar 20'şer IP'lik gruplarla (Carbon Black tek sorgu), AI paketleri paralel
        foreach (var grp in todo.Keys.Chunk(20))
        {
            ct.ThrowIfCancellationRequested();
            Set(d => d with { Asama = "Kanıtlar toplanıyor" });
            var texts = await BuildAsync(grp.ToList(), todo, env, hours, ct);
            lock (_gate) foreach (var kv in texts) _kanit[kv.Key] = kv.Value;
            Set(d => d with { Asama = "AI önerileri" });
            var tasks = grp.Chunk(paket).Select(async chunk =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var got = await AskAsync(chunk, todo, texts, env, ct);
                    foreach (var ip in chunk)
                        foreach (var port in todo[ip])
                        {
                            var o = got.GetValueOrDefault((ip, port)) ?? new EksikOneri(null, false, 0, "AI bu port için cevap vermedi.", Ai, "AI", DateTime.Now);
                            await SaveAsync(ip, port, o, texts.GetValueOrDefault(ip), ct);
                        }
                    Set(d => d with { Biten = d.Biten + chunk.Sum(ip => todo[ip].Count) });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Fail($"{string.Join(", ", chunk)}: {ex.Message}");
                    Set(d => d with { Biten = d.Biten + chunk.Sum(ip => todo[ip].Count) });
                }
                finally { gate.Release(); }
            });
            await Task.WhenAll(tasks);
        }
    }

    async Task<Dictionary<(string, string), EksikOneri>> AskAsync(string[] ips, Dictionary<string, List<string>> todo,
        Dictionary<string, string> texts, EnvanterSnapshot env, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            Bir bankanın veri merkezinde çalışan kıdemli bir altyapı mimarısın. Kurumun uygulama envanterinde (ERT) aşağıdaki
            sunucuların bazı portlarında UYGULAMA ADI BOŞ. Verilen kanıtlara bakarak her boş port için o portta hangi uygulamanın
            çalıştığını bul.

            Kanıtların gücü (güçlüden zayıfa): o portu kabul eden exe'nin adı ve klasör yolu (ör. d:\apps\odeme.api\odeme.exe);
            aynı satırdaki WEBSITE (IIS site adı) ve SERVICENAME; o IP ve portun geçtiği SM talepleri
            (erişim / kurulum talebinde uygulama adı çoğu zaman yazar); aynı sunucunun diğer portlarındaki uygulamalar; VIP ve havuz
            komşuları; BENZER ADLI SUNUCULARIN (aynı ad, farklı numara) uygulamaları; aynı segmentte aynı portu kullanan
            sunucuların uygulamaları; bu porta bağlanan istemci uygulamalar ve exe'nin bağlandığı yerler; sanal sunucu notları;
            iş istekleri; sunucu adındaki kısaltmalar (ör. "atm", "sql", "web"). ntoskrnl.exe görünüyorsa port HTTP.sys üzerindendir; asıl uygulama aynı sunucudaki ve adı WEBSITE ile
            örtüşen exe'dir.

            Kurallar:
            - Uygulama adını mümkünse "ADAY UYGULAMALAR" listesinden HARFİ HARFİNE seç ("yeni": false).
            - Hiçbir aday uymuyor ama kanıt açıkça bir uygulamayı gösteriyorsa kısa bir ad öner ("yeni": true).
            - Port bir altyapı / araç hizmetiyse (izleme ajanı, yedekleme, antivirüs, yönetim) "uygulama": "Altyapı: <ne>" yaz.
            - Kanıt yetersizse "uygulama": null ve güven düşük yaz. Uydurma.

            Yalnızca JSON dizi döndür, başka metin yazma. Her boş port için bir eleman:
            [{"ip":"…","port":"…","uygulama":"… ya da null","yeni":false,"guven":0-100,"gerekce":"tek cümle; hangi kanıta dayandığın"}]
            """);
        foreach (var ip in ips)
            sb.AppendLine().AppendLine($"=== Sunucu {ip} · boş portlar: {string.Join(", ", todo[ip].Select(p => p == "" ? "(port yok)" : p))}")
              .AppendLine(Cut(texts.GetValueOrDefault(ip), cfg.GetValue("Eksik:MaxKarakter", 6000)));
        string answer = await islev.ChatAsync(sb.ToString(), ct);
        var outp = new Dictionary<(string, string), EksikOneri>();
        if (IslevService.ExtractJson(answer) is not { } root) throw new InvalidOperationException("AI cevabı JSON değil: " + Cut(answer, 200));
        var arr = root.ValueKind == JsonValueKind.Array ? root : default;
        if (arr.ValueKind != JsonValueKind.Array) return outp;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? ip = IslevService.Str(e, "ip") ?? (ips.Length == 1 ? ips[0] : null);
            if (ip == null || !todo.ContainsKey(ip)) continue;
            string port = IslevService.Str(e, "port") ?? (todo[ip].Count == 1 ? todo[ip][0] : "");
            if (port is "(port yok)" or "null") port = "";
            if (!todo[ip].Contains(port)) continue;
            string? app = IslevService.Str(e, "uygulama");
            if (app is "null" or "-") app = null;
            bool yeni = false;
            if (app != null && !app.StartsWith("Altyapı", StringComparison.OrdinalIgnoreCase)) app = Snap(env, app, out yeni);
            outp[(ip, port)] = new EksikOneri(Cut(app, 200), yeni, Math.Clamp(IslevService.Int(e, "guven"), 0, 100), IslevService.Str(e, "gerekce"),
                Ai, "AI", DateTime.Now);
        }
        return outp;
    }

    // ---------------- Excel (CSV) ----------------
    // hepsi = false: yalnız onaylanan / elle girilenler; true: önerisi olan tüm satırlar
    public async Task<string> CsvAsync(bool hepsi, CancellationToken ct)
    {
        var rows = (await ListAsync(null, hepsi ? null : "onay", true, 1, 1000, false, ct)) is var first && first.Toplam > 1000
            ? (await Task.WhenAll(Enumerable.Range(1, (first.Toplam + 999) / 1000)
                .Select(p => ListAsync(null, hepsi ? null : "onay", true, p, 1000, false, ct)))).SelectMany(l => l.Satirlar).ToList()
            : first.Satirlar;
        var sb = new StringBuilder("\uFEFFIP;PORT;SUNUCU;SEGMENT;WEBSITE;SERVICENAME;MUHAFIZ;AYNI SUNUCUDAKİ UYGULAMALAR;ÖNERİLEN UYGULAMA;YENİ AD;GÜVEN;DURUM;KARAR VEREN;GEREKÇE\r\n");
        static string C(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var r in rows.Where(r => r.Oneri != null))
            sb.Append(string.Join(";", C(r.Ip), C(r.Port), C(r.Hostname), C(r.Segment), C(r.Website), C(r.Servis), C(r.Muhafiz),
                C(string.Join(", ", r.Diger)), C(r.Oneri!.Uygulama), C(r.Oneri.Yeni ? "evet" : ""), C(r.Oneri.Guven.ToString()),
                C(r.Oneri.Durum), C(r.Oneri.Kaynak), C(r.Oneri.Gerekce))).Append("\r\n");
        return sb.ToString();
    }
}
