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
    string? Birim, List<string> Diger, bool Altyapi, EksikOneri? Oneri, bool Test = false);
record EksikListe(List<EksikSatir> Satirlar, int Toplam, int TumSatir, int TumIp, int Onerili, int Onayli, IslevDurum Durum,
    bool Yazabilir, string? Uyari, bool TopluYetki = false, string? Bilgi = null, string? Surum = null);
// Max: bu çalıştırmada en çok kaç sunucu (boş = Eksik:MaxIp; 0 = sınırsız)
record EksikCalistir(List<string>? Ips, bool Hepsi, bool Yeniden, int? Saat, bool Test = false, int? Max = null);
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

    // Otomatik (dinamik) portlar hiç listelenmez: Eksik:DinamikPortBaslangic (varsayılan 49152) ve üstü (kullanıcı kararı)
    public static List<(string Ip, string Port, HostApp Row)> Missing(EnvanterSnapshot env, int dyn = 49152) =>
        env.HostIps.SelectMany(ip => env.AppsOnIp(ip).Where(h => string.IsNullOrWhiteSpace(h.AppName))
                .Where(h => !(int.TryParse(h.Port, out int n) && n >= dyn))
                .GroupBy(h => Key(h.Port)).Select(g => (ip, g.Key, g.First())))
            .ToList();
    int Dyn => cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);

    // Test segmenti: KonsolideSegmentler4.Domain'inde Eksik:TestIfadeleri (varsayılan "test") geçiyor; varsayılan olarak gizli, AI'a gitmez
    bool IsTest(EnvanterSnapshot env, string ip)
    {
        var words = cfg.GetSection("Eksik:TestIfadeleri").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (words.Count == 0) words = ["test"];
        string? d = env.FindSegment(ip)?.Domain;
        return d != null && words.Any(w => d.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    // IP sorgulama kartı: bu IP'nin port başına önerisi (ERT tablosundaki "AI önerisi" kolonu)
    public async Task<Dictionary<string, EksikOneri>> ForIpAsync(string ip, CancellationToken ct) =>
        (await LoadAsync(false, ct)).Where(kv => kv.Key.Item1 == ip && kv.Value.Durum != "").ToDictionary(kv => kv.Key.Item2, kv => kv.Value);

    // sirala: ip | port | sunucu | segment | oneri | guven | durum (yon: azalan)
    public async Task<EksikListe> ListAsync(string? ara, string? durum, bool altyapi, int sayfa, int boyut, bool yazabilir, CancellationToken ct,
        bool test = false, string? sirala = null, bool azalan = false)
    {
        var env = await envanter.GetAsync(false, ct);
        var infra = AppTopology.InfraPorts(cfg);
        var oneri = await LoadAsync(false, ct);
        var all = Missing(env, Dyn);
        var rows = all.Select(m =>
        {
            var hosts = env.AppsOnIp(m.Ip);
            oneri.TryGetValue((m.Ip, m.Port), out var o);
            return new EksikSatir(m.Ip, m.Port, hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault(), env.FindSegment(m.Ip)?.Label,
                m.Row.Website, m.Row.ServiceName, m.Row.VarlikMuhafizi ?? hosts.Select(h => h.VarlikMuhafizi).OfType<string>().FirstOrDefault(),
                m.Row.SunucuIsletenBirim, env.AppNamesOnIp(m.Ip).Take(4).ToList(), infra.Contains(m.Port), o, IsTest(env, m.Ip));
        }).ToList();
        string? q = ara?.Trim();
        bool Has(string? v) => v != null && v.Contains(q!, StringComparison.CurrentCultureIgnoreCase);
        var f = rows.Where(r => altyapi || !r.Altyapi).Where(r => test || !r.Test)
            .Where(r => string.IsNullOrEmpty(q) || Has(r.Ip) || Has(r.Port) || Has(r.Hostname) || Has(r.Website) || Has(r.Servis)
                        || Has(r.Muhafiz) || Has(r.Segment) || Has(r.Oneri?.Uygulama) || Has(r.Oneri?.Gerekce) || r.Diger.Any(Has))
            .Where(r => durum switch
            {
                "yok" => r.Oneri == null,
                "ai" => r.Oneri?.Durum == Ai,
                "onay" => r.Oneri?.Durum is Onay or Elle,
                "red" => r.Oneri?.Durum == Red,
                _ => true
            })
            .ToList();
        var cmp = StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true);
        static int PortN(string p) => int.TryParse(p, out var n) ? n : -1;
        IOrderedEnumerable<EksikSatir> Ord<T>(Func<EksikSatir, T> k, IComparer<T>? c = null) => azalan ? f.OrderByDescending(k, c) : f.OrderBy(k, c);
        f = (sirala switch
        {
            "ip" => Ord(r => IpKey(r.Ip)).ThenBy(r => PortN(r.Port)),
            "port" => Ord(r => PortN(r.Port)).ThenBy(r => IpKey(r.Ip)),
            "sunucu" => Ord(r => r.Hostname ?? "\uffff", cmp).ThenBy(r => PortN(r.Port)),
            "segment" => Ord(r => r.Segment ?? "\uffff", cmp).ThenBy(r => IpKey(r.Ip)),
            // öneri: önerisi olmayanlar hep sonda
            "oneri" => (azalan ? f.OrderBy(r => r.Oneri?.Uygulama == null).ThenByDescending(r => r.Oneri?.Uygulama ?? "", cmp)
                               : f.OrderBy(r => r.Oneri?.Uygulama == null).ThenBy(r => r.Oneri?.Uygulama ?? "", cmp)).ThenBy(r => IpKey(r.Ip)),
            "guven" => (azalan ? f.OrderByDescending(r => r.Oneri?.Guven ?? -1) : f.OrderBy(r => r.Oneri == null).ThenBy(r => r.Oneri?.Guven ?? 0)).ThenBy(r => IpKey(r.Ip)),
            "durum" => Ord(r => r.Oneri?.Durum ?? "\uffff", cmp).ThenByDescending(r => r.Oneri?.Guven ?? 0),
            // varsayılan: IP + port (sabit; kullanıcı: tahmin bitince satır öneriye göre başka sayfaya kayıyordu)
            _ => f.OrderBy(r => IpKey(r.Ip)).ThenBy(r => PortN(r.Port))
        }).ToList();
        int size = Math.Clamp(boyut, 20, 1000), page = Math.Max(1, sayfa);
        string bilgi;
        try { bilgi = Bilgi().Durum; } catch (Exception ex) { bilgi = "genel bilgi dosyası okunamadı: " + ex.Message; }
        return new EksikListe(f.Skip((page - 1) * size).Take(size).ToList(), f.Count, rows.Count, rows.Select(r => r.Ip).Distinct().Count(),
            rows.Count(r => r.Oneri != null), rows.Count(r => r.Oneri?.Durum is Onay or Elle), Durum, yazabilir, _uyari, false, bilgi, Surum);
    }

    static string IpKey(string ip) => string.Join(".", ip.Split('.').Select(p => p.PadLeft(3, '0')));

    // Çalışan sürüm (yayınlanan dll'in tarihi + SM tarih kolonu): eski dll ile yeni dosyalar karışınca sayfada görülsün
    static readonly string Surum = $"sunucu sürümü {File.GetLastWriteTime(typeof(EksikService).Assembly.Location):yyyy-MM-dd HH:mm}";

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
    // yeni = false: son AI isteğinde giden (kayıtlı) metin; true: kanıtlar şimdi yeniden toplanır (AI'a sorulmaz)
    public async Task<string> EvidenceAsync(string ip, CancellationToken ct, bool yeni = false)
    {
        if (!yeni) lock (_gate) if (_kanit.TryGetValue(ip, out var k)) return k;
        if (!yeni) try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT TOP 1 KANIT FROM {Table} WHERE IP = @ip AND KANIT IS NOT NULL ORDER BY TARIH DESC", conn);
            cmd.Parameters.AddWithValue("@ip", ip);
            if (await cmd.ExecuteScalarAsync(ct) is string s) return s;
        }
        catch (SqlException) { /* tablo yoksa yeniden üretilir */ }
        var env = await envanter.GetAsync(false, ct);
        var miss = Missing(env, Dyn).Where(m => m.Ip == ip).Select(m => m.Port).ToList();
        var texts = await BuildAsync([ip], miss.Count > 0 ? new() { [ip] = miss } : new() { [ip] = [] }, env, Hours(null), ct);
        return texts.GetValueOrDefault(ip) ?? "";
    }

    int Hours(int? h) => Math.Clamp(h ?? cfg.GetValue("Eksik:Saat", 24), 1, Math.Max(1, cfg.GetValue("MaxRangeHours", 24 * 7)));

    // Sunucu başına kanıt metni: işlev haritası bilgi özeti + Carbon Black + aday uygulamalar
    // Ajan / araç ürünleri (kullanıcı: Dataskope ajanı kuruldu diye sunucu Dataskope uygulaması OLMAZ): SM talebinde, aday listesinde ya da
    // AI önerisinde geçerse sunucunun iş uygulaması sayılmaz; yalnız boş portu kabul eden exe o ürüne aitse "Altyapı: <ürün>" olabilir.
    static readonly string[] DefaultAjanlar = ["Dataskope", "Infraskope", "Carbon Black", "Splunk", "SiteScope", "Trend Micro", "Deep Security",
        "McAfee", "Trellix", "Symantec", "Tanium", "Qualys", "Tenable", "Nessus", "CyberArk", "SCCM", "Zabbix", "Dynatrace", "AppDynamics",
        "AppInternals", "Commvault", "Veeam", "NetBackup", "Guardium", "Imperva", "Varonis", "Forcepoint", "Ansible", "Nagios", "PRTG", "SolarWinds"];
    string[] Ajanlar() => cfg.GetSection("Eksik:AjanUygulamalari").GetChildren().Select(c => c.Value).OfType<string>().Where(v => v.Trim() != "") is var l && l.Any()
        ? l.ToArray() : DefaultAjanlar;
    string? AjanOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string n = Norm(text);
        return Ajanlar().FirstOrDefault(a => Norm(a) is { Length: >= 4 } na && n.Contains(na));
    }
    // Boş portu kabul eden exe (kanıttaki Carbon Black "- gelen port X" satırı) bu ajana mı ait
    static bool PortExeHas(string? evidence, string port, string ajan) => evidence != null && port.Length > 0 &&
        evidence.Split('\n').Any(l => (l.StartsWith($"- gelen port {port}:") || l.StartsWith($"- gelen port {port} (")) && Norm(l).Contains(Norm(ajan)));

    async Task<Dictionary<string, string>> BuildAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env,
        int hours, CancellationToken ct)
    {
        var profiles = await islev.ProfilesAsync(ips, env, ct);
        var cb = await CarbonBlackAsync(ips, hours, ct);
        var smf = await SmFieldsAsync(ips, ports, env, ct);
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
                string pi = PortBilgisi.Find(g.Key, cfg) is { } info ? $" (bilinen kullanım: {info.Name})" : "";
                sb.AppendLine($"- port {(g.Key == "" ? "(yok)" : g.Key)}{pi}: " + string.Join(", ", new[]
                {
                    h.Website is { } w ? "WEBSITE=" + w : null, h.ServiceName is { } sv ? "SERVICENAME=" + sv : null,
                    h.VipIp is { } vi ? $"VIP={vi}:{h.VipPort}" : null, h.UnixName is { } un ? "UNIXNAME=" + un : null,
                    h.VarlikMuhafizi is { } vm ? "muhafız=" + vm : null, h.SunucuIsletenBirim is { } sb2 ? "işleten=" + sb2 : null
                }.OfType<string>().DefaultIfEmpty("başka bilgi yok")));
            }
            sb.Append(Neighbors(env, ip, miss));
            if (smf.TryGetValue(ip, out var smfText)) sb.Append(smfText);
            if (cb.TryGetValue(ip, out var c)) sb.Append(c.Text(env, ip, miss));
            else sb.AppendLine($"Carbon Black (son {hours} saat): bu sunucuda ağ olayı yok (ajan yok ya da trafik yok).");
            string sofar = sb.ToString();
            var cands = Candidates(env, ip, sofar)
                .Where(c => AjanOf(c) is not { } a || miss.Any(p => PortExeHas(sofar, p, a))).ToList();   // ajan adları aday değil
            if (cands.Count > 0) sb.AppendLine("ADAY UYGULAMALAR (ERT'deki adlar): " + string.Join(" | ", cands));
            outp[ip] = sb.ToString();
        }
        return outp;
    }

    // SM talepleri (kullanıcı tarifi): sunucu ZT_HEDEF / ZT_KAYNAK'ta (IP) ya da ZT_HEDEFHOSTNAME / ZT_KAYNAKHOSTNAME'de (UNIXNAME) aranır;
    // bulunan talebin DESCRIPTION, BRIEF_DESCRIPTION, ZT_KAYNAKACIKLAMA, ZT_HEDEFACIKLAMA alanları AI'a gider. Sunucunun talepte HEDEF mi
    // KAYNAK mı olduğu yazılır: hedefse ZT_HEDEFACIKLAMA bu sunucuyu, ZT_KAYNAKACIKLAMA karşı tarafı anlatır (kaynaksa tersi). Hedef olup
    // ZT_HEDEFPORT'u boş portu içeren talepler başa; en yeni 10. Talepte doluysa ZT_HEDEFUYGULAMA / ZT_KAYNAKUYGULAMA
    // ek bilgi. Tek sorgu / sunucu, tarih Eksik:SmTarihKolonu (SUBMIT_DATE; canlıda OPEN_TIME yok) ≥ SmYil.
    async Task<Dictionary<string, string>> SmFieldsAsync(List<string> ips, Dictionary<string, List<string>> ports, EnvanterSnapshot env, CancellationToken ct)
    {
        var outp = new Dictionary<string, string>();
        var want = ips.Select(ip => (ip, host: env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault())).ToList();
        if (want.Count == 0) return outp;
        var tables = cfg.GetSection("Eksik:SmTablolari").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (tables.Count == 0) tables = ["dbo.REQUESTM1"];
        string dateCol = cfg["Eksik:SmTarihKolonu"] is { Length: > 0 } dc && System.Text.RegularExpressions.Regex.IsMatch(dc, "^[A-Za-z_][A-Za-z0-9_]*$") ? dc : "SUBMIT_DATE";
        var since = DateTime.Today.AddYears(-Math.Clamp(cfg.GetValue("Eksik:SmYil", cfg.GetValue("Islev:SmYil", 3)), 1, 20));
        var rows = new List<Dictionary<string, string>>();
        string? err = null;
        string[] names = ["NUMBER", "TARIH", "ZT_HEDEF", "ZT_KAYNAK", "ZT_HEDEFHOSTNAME", "ZT_KAYNAKHOSTNAME", "ZT_HEDEFPORT", "ZT_PORT",
            "BRIEF_DESCRIPTION", "DESCRIPTION", "ZT_HEDEFACIKLAMA", "ZT_KAYNAKACIKLAMA", "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA", "ZT_KAYNAKUYGULAMA"];
        foreach (var table in tables.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$")))
        {
            try
            {
                await using var conn = new SqlConnection(ConnStr);
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand("", conn) { CommandTimeout = cfg.GetValue("Islev:SqlTimeoutSaniye", 180) };
                int n = 0;
                string P(string v, bool like) { string name = "@f" + n++; cmd.Parameters.AddWithValue(name, like ? $"%{v}%" : v); return name; }
                var conds = new List<string>();
                foreach (var (ip, host) in want)
                {
                    string l = P(ip, true);
                    conds.Add($"req.ZT_HEDEF LIKE {l} OR req.ZT_KAYNAK LIKE {l}");
                    if (host is { Length: >= 4 }) { string h = P(host, false); conds.Add($"req.ZT_HEDEFHOSTNAME = {h} OR req.ZT_KAYNAKHOSTNAME = {h}"); }
                }
                cmd.Parameters.AddWithValue("@since", since);
                string sel = string.Join(", ", names.Select(c => c switch
                {
                    "TARIH" => $"req.[{dateCol}]",
                    "DESCRIPTION" => "LEFT(CAST(req.[DESCRIPTION] AS nvarchar(max)), 3000)",
                    _ => $"req.[{c}]"
                }));
                cmd.CommandText = $"""
                    SELECT TOP (300) {sel}
                    FROM {table} req WITH (NOLOCK)
                    WHERE req.[{dateCol}] >= @since AND ({string.Join(" OR ", conds)})
                    ORDER BY req.[{dateCol}] DESC
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (r.IsDBNull(i)) continue;
                        object v = r.GetValue(i);
                        string s = v is DateTime dt ? dt.ToString("yyyy-MM-dd") : v is byte[] ? IslevService.ParseHexOrBinary(v) : Convert.ToString(v) ?? "";
                        if (!string.IsNullOrWhiteSpace(s)) row[names[i]] = System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");
                    }
                    rows.Add(row);
                }
            }
            catch (SqlException ex) { err = $"{table}: {ex.Message}"; }
        }
        static string? G(Dictionary<string, string> r, params string[] keys) => keys.Select(k => r.GetValueOrDefault(k)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        static bool HasIp(string? text, string ip) => text != null && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\d.]){System.Text.RegularExpressions.Regex.Escape(ip)}(?![\d])");
        static bool HasPort(string? text, string port) => text != null && port.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<!\d){port}(?!\d)");
        foreach (var (ip, host) in want)
        {
            bool Same(string? h) => host is { Length: >= 4 } && h != null && h.Split(',', ';', ' ').Any(x => string.Equals(x.Trim().Split('.')[0], host.Split('.')[0], StringComparison.OrdinalIgnoreCase));
            var miss = (ports.GetValueOrDefault(ip) ?? []).Where(p => p.Length > 0).ToList();
            var all = rows.Select(r =>
                {
                    bool hedef = HasIp(G(r, "ZT_HEDEF"), ip) || Same(G(r, "ZT_HEDEFHOSTNAME"));
                    bool kaynak = HasIp(G(r, "ZT_KAYNAK"), ip) || Same(G(r, "ZT_KAYNAKHOSTNAME"));
                    // port eşleşmesi yalnız ZT_HEDEFPORT'ta (kullanıcı)
                    var ps = hedef ? miss.Where(p => HasPort(G(r, "ZT_HEDEFPORT"), p)).ToList() : [];
                    string? aj = AjanOf(string.Join(" ", new[] { "BRIEF_DESCRIPTION", "DESCRIPTION", "ZT_HEDEFACIKLAMA", "ZT_KAYNAKACIKLAMA",
                        "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA", "ZT_KAYNAKUYGULAMA" }.Select(k => G(r, k))));
                    return (r, hedef, kaynak, ps, aj);
                })
                .Where(x => x.hedef || x.kaynak).DistinctBy(x => G(x.r, "NUMBER")).ToList();
            var mine = all
                .OrderBy(x => x.aj != null).ThenByDescending(x => x.ps.Count > 0).ThenByDescending(x => x.hedef).ThenByDescending(x => G(x.r, "TARIH")).Take(10).ToList();
            if (mine.Count == 0)
            {
                outp[ip] = err != null ? $"SM TALEPLERİ: okunamadı ({Cut(err, 150)})\n"
                    : $"SM TALEPLERİ: son {DateTime.Today.Year - since.Year} yılda bu sunucunun (IP {ip}{(host != null ? ", ad " + host : "")}) ZT_HEDEF / ZT_KAYNAK olduğu talep bulunamadı.\n";
                continue;
            }
            var sb = new StringBuilder();
            // Aranan (boş) port başına açık özet: bu porta erişim talebi var mı (tüm eşleşen talepler üzerinden, yalnız ilk 10 değil)
            foreach (var p in miss)
            {
                var hitAll = all.Where(x => x.ps.Contains(p)).OrderByDescending(x => G(x.r, "TARIH")).ToList();
                var hit = hitAll.Where(x => x.aj == null).ToList();
                var ajHit = hitAll.Where(x => x.aj != null).ToList();
                if (ajHit.Count > 0)
                    sb.AppendLine($"PORT {p}: {ajHit.Count} talep AJAN / ARAÇ talebi ({string.Join(", ", ajHit.Select(x => x.aj).Distinct())}; {string.Join(", ", ajHit.Take(3).Select(x => G(x.r, "NUMBER")))}) — " +
                        "bu ürünün sunucuya kurulumu / erişimi içindir, sunucunun iş uygulamasını GÖSTERMEZ.");
                sb.AppendLine(hit.Count > 0
                    ? $"PORT {p}'E ERİŞİM TALEBİ VAR: {hit.Count} talepte bu sunucu hedef ve ZT_HEDEFPORT {p} içeriyor (son {G(hit[0].r, "TARIH")}; {string.Join(", ", hit.Take(5).Select(x => G(x.r, "NUMBER")))})"
                      + (hit.Select(x => G(x.r, "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA")).OfType<string>().GroupBy(a => a, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault() is { } ga
                          ? $"; talepteki hedef uygulama: {ga.Key} ({ga.Count()})" : "")
                    : ajHit.Count > 0 ? $"PORT {p}'E İŞ UYGULAMASI İÇİN ERİŞİM TALEBİ YOK (yalnız ajan / araç talepleri var)"
                    : $"PORT {p}'E ERİŞİM TALEBİ YOK (son {DateTime.Today.Year - since.Year} yılda bu sunucuya ZT_HEDEFPORT {p} olan talep bulunamadı)");
            }
            sb.AppendLine($"SM TALEPLERİ (bu sunucunun hedef ya da kaynak olduğu erişim talepleri, en yeni; {mine.Count(x => x.hedef)} hedef, {mine.Count(x => x.kaynak && !x.hedef)} kaynak):");
            foreach (var (r, hedef, kaynak, ps, aj) in mine)
            {
                string rol = hedef && kaynak ? "bu sunucu HEM HEDEF HEM KAYNAK" : hedef ? $"bu sunucu HEDEF (kaynak: {Cut(G(r, "ZT_KAYNAK", "ZT_KAYNAKHOSTNAME"), 60)})"
                    : $"bu sunucu KAYNAK (hedef: {Cut(G(r, "ZT_HEDEF", "ZT_HEDEFHOSTNAME"), 60)})";
                string port = G(r, "ZT_HEDEFPORT") ?? "";
                if (aj != null) sb.AppendLine($"- [AJAN / ARAÇ TALEBİ: {aj} — sunucunun uygulaması DEĞİL, uygulama kanıtı olarak kullanma]");
                sb.AppendLine($"- {G(r, "NUMBER")} {G(r, "TARIH")} · {rol}{(port.Length > 0 ? $" · hedef port {Cut(port, 40)}" : "")}{(ps.Count > 0 ? $" · BOŞ PORT {string.Join(",", ps)}" : "")}");
                if (G(r, "BRIEF_DESCRIPTION") is { } bd) sb.AppendLine($"    Kısa açıklama: {Cut(bd, 200)}");
                // açıklamaları sunucunun rolüne göre etiketle
                if (G(r, hedef ? "ZT_HEDEFACIKLAMA" : "ZT_KAYNAKACIKLAMA") is { } own) sb.AppendLine($"    {(hedef ? "Hedef" : "Kaynak")} açıklaması (BU SUNUCU): {Cut(own, 250)}");
                if (G(r, hedef ? "ZT_KAYNAKACIKLAMA" : "ZT_HEDEFACIKLAMA") is { } other) sb.AppendLine($"    {(hedef ? "Kaynak" : "Hedef")} açıklaması (karşı taraf): {Cut(other, 200)}");
                if (G(r, "DESCRIPTION") is { } d) sb.AppendLine($"    Açıklama: {Cut(d, 400)}");
                string? hApp = G(r, "ZT_HEDEFUYGULAMA", "ZT_HEDEFUGULAMA"), kApp = G(r, "ZT_KAYNAKUYGULAMA");
                if (hApp != null || kApp != null)
                    sb.AppendLine($"    Talepteki uygulama alanları: {(hApp != null ? $"hedef uygulama = {hApp}{(hedef ? " (BU SUNUCU)" : "")}" : "")}{(hApp != null && kApp != null ? "; " : "")}{(kApp != null ? $"kaynak uygulama = {kApp}{(kaynak && !hedef ? " (BU SUNUCU)" : "")}" : "")}");
            }
            outp[ip] = sb.ToString();
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
        var cands = Missing(env, Dyn).Where(m => !infra.Contains(m.Port))
            .Where(m => wanted == null || wanted.Contains(m.Ip))
            .Where(m => wanted != null || req.Test || !IsTest(env, m.Ip))   // test segmentleri yalnız seçilirse
            .Where(m => !have.TryGetValue((m.Ip, m.Port), out var o) || (o.Durum is not (Onay or Elle) && (req.Yeniden || wanted != null)))
            .GroupBy(m => m.Ip).OrderByDescending(g => g.Count()).ToList();
        int allIps = cands.Count, allPorts = cands.Sum(g => g.Count());
        var todo = cands.Take(req.Max is 0 ? int.MaxValue : Math.Clamp(req.Max ?? cfg.GetValue("Eksik:MaxIp", 200), 1, 100000))
            .ToDictionary(g => g.Key, g => g.Select(m => m.Port).ToList());
        if (todo.Count == 0) throw new InvalidOperationException("Öneri bekleyen satır yok (altyapı portları ve onaylananlar hariç).");
        int total = todo.Values.Sum(v => v.Count), hours = Hours(req.Saat);
        // Etiket alanı özet için: bu çalıştırmada kaç sunucu / bekleyen toplam
        Set(d => d with { Asama = "Kanıtlar toplanıyor", Toplam = total,
            Etiket = todo.Count < allIps ? $"bu çalıştırmada {todo.Count} / {allIps} sunucu ({allPorts - total} port sonraki çalıştırmalara kaldı)" : $"{allIps} sunucu" });
        // Sunucu sunucu (kullanıcı: 20'lik toplu kanıt toplama SM tablosuyla çok uzadı): her sunucunun kanıtı toplanır, hemen AI'a
        // sorulur ve kaydedilir; aynı anda Eksik:Paralel (varsayılan 3) sunucu. İlk öneriler hemen gelir.
        int paralel = Math.Clamp(cfg.GetValue("Eksik:Paralel", 3), 1, 8);
        using var gate = new SemaphoreSlim(paralel);
        Set(d => d with { Asama = $"Kanıt + AI (sunucu sunucu, aynı anda {paralel})" });
        var tasks = todo.Keys.Select(async ip =>
        {
            await gate.WaitAsync(ct);   // iptalde burada çıkar (semafor alınmadan; finally'ye girmez)
            try
            {
                var texts = await BuildAsync([ip], todo, env, hours, ct);
                lock (_gate) _kanit[ip] = texts.GetValueOrDefault(ip) ?? "";
                var got = await AskAsync([ip], todo, texts, env, ct);
                foreach (var port in todo[ip])
                {
                    var o = got.GetValueOrDefault((ip, port)) ?? new EksikOneri(null, false, 0, "AI bu port için cevap vermedi.", Ai, "AI", DateTime.Now);
                    await SaveAsync(ip, port, o, texts.GetValueOrDefault(ip), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fail($"{ip}: {ex.Message}"); }
            finally
            {
                Set(d => d with { Biten = d.Biten + todo[ip].Count });
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks);
    }

    // Kullanıcının genel bilgiler dosyası (Eksik:BilgiDosyasi; .txt, UTF-8 ya da Windows-1254): her AI isteğinin başına eklenir.
    // Model istekler arası hatırlamadığı için "öğretmek" = her istekte göndermek. Dosya değişince (tarih) yeniden okunur.
    (string Path, DateTime At, string Text, bool Cut)? _bilgi;
    public (string? Text, string Durum) Bilgi()
    {
        string? path = cfg["Eksik:BilgiDosyasi"];
        if (string.IsNullOrWhiteSpace(path)) return (null, "genel bilgi dosyası tanımlı değil (Eksik:BilgiDosyasi)");
        if (!System.IO.Path.IsPathRooted(path)) path = System.IO.Path.Combine(AppContext.BaseDirectory, path);
        if (!File.Exists(path)) return (null, $"genel bilgi dosyası bulunamadı: {System.IO.Path.GetFileName(path)}");
        var at = File.GetLastWriteTimeUtc(path);
        if (_bilgi is not { } b || b.Path != path || b.At != at)
        {
            var bytes = File.ReadAllBytes(path);
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1254).GetString(bytes);
            }
            int max = Math.Clamp(cfg.GetValue("Eksik:BilgiMaxKarakter", 15000), 1000, 200000);
            bool cut = text.Length > max;
            _bilgi = b = (path, at, cut ? text[..max] : text, cut);
        }
        return (b.Text, $"genel bilgi dosyası: {System.IO.Path.GetFileName(path)} · {b.Text.Length:N0} karakter{(b.Cut ? " (Eksik:BilgiMaxKarakter sınırında kesildi)" : "")}");
    }

    async Task<Dictionary<(string, string), EksikOneri>> AskAsync(string[] ips, Dictionary<string, List<string>> todo,
        Dictionary<string, string> texts, EnvanterSnapshot env, CancellationToken ct)
    {
        var sb = new StringBuilder();
        if (Bilgi().Text is { Length: > 0 } genel)
            sb.AppendLine("KURUMA ÖZEL GENEL BİLGİLER (kullanıcının hazırladığı; kanıtları yorumlarken bunlara göre düşün):")
              .AppendLine(genel.Trim()).AppendLine("--- genel bilgilerin sonu ---").AppendLine();
        sb.AppendLine("""
            Bir bankanın veri merkezinde çalışan kıdemli bir altyapı mimarısın. Kurumun uygulama envanterinde (ERT) aşağıdaki
            sunucuların bazı portlarında UYGULAMA ADI BOŞ. Verilen kanıtlara bakarak her boş port için o portta hangi uygulamanın
            çalıştığını bul.

            Kanıtların gücü (güçlüden zayıfa): o portu kabul eden exe'nin adı ve klasör yolu (ör. d:\apps\odeme.api\odeme.exe);
            bu sunucunun HEDEF olduğu SM erişim talepleri (özellikle boş portu anan): "BU SUNUCU" diye işaretli açıklama bu
            sunucuyu anlatır, "karşı taraf" açıklaması diğer ucu (onu bu sunucunun uygulaması sanma); talepte bu sunucu için
            yazılmış uygulama alanı varsa çok güçlü; aynı satırdaki WEBSITE (IIS site adı) ve SERVICENAME; aynı sunucunun diğer portlarındaki uygulamalar; VIP ve havuz
            komşuları; BENZER ADLI SUNUCULARIN (aynı ad, farklı numara) uygulamaları; aynı segmentte aynı portu kullanan
            sunucuların uygulamaları; bu porta bağlanan istemci uygulamalar ve exe'nin bağlandığı yerler; sanal sunucu notları;
            iş istekleri; sunucu adındaki kısaltmalar (ör. "atm", "sql", "web"). ntoskrnl.exe görünüyorsa port HTTP.sys üzerindendir; asıl uygulama aynı sunucudaki ve adı WEBSITE ile
            örtüşen exe'dir.

            Kurallar:
            - Uygulama adını mümkünse "ADAY UYGULAMALAR" listesinden HARFİ HARFİNE seç ("yeni": false).
            - Hiçbir aday uymuyor ama kanıt açıkça bir uygulamayı gösteriyorsa kısa bir ad öner ("yeni": true).
            - Port bir altyapı / araç hizmetiyse (izleme ajanı, yedekleme, antivirüs, yönetim) "uygulama": "Altyapı: <ne>" yaz.
            - AJAN / ARAÇ ÜRÜNLERİ (Dataskope, Carbon Black, Splunk, SiteScope, yedekleme, antivirüs, izleme…) sunucunun İŞ UYGULAMASI
              DEĞİLDİR. Bir SM talebinde sunucuya böyle bir ajanın kurulduğu ya da o ürüne erişim açıldığı yazıyorsa bu, sunucunun o
              uygulama olduğunu GÖSTERMEZ ("[AJAN / ARAÇ TALEBİ]" işaretli talepleri uygulama kanıtı sayma). Ajan adını yalnız boş
              portu kabul eden exe o ürüne aitse "Altyapı: <ürün>" olarak yaz; aksi halde diğer kanıtlara (exe yolu, sunucu adı,
              benzer adlı sunucular, WEBSITE…) bak, yoksa null.
            - Port bir veritabanı ya da ara katman portuysa (1433 SQL Server, 1521 Oracle, 5432, 1414 MQ… ya da exe sqlservr.exe,
              oracle.exe, tnslsnr.exe) "SQL Server" gibi teknoloji adı YAZMA: veritabanı hangi uygulamaya aitse onu bul
              (bu porta bağlanan istemci uygulamalar, aynı sunucunun diğer portları, benzer adlı sunucular, SM talepleri).
              Gerekçede teknolojiyi belirt (ör. "1433 SQL Server; istemcileri KREDİ uygulaması"). Sahibi bulunamazsa
              uygulama null, gerekçede "SQL Server veritabanı, sahibi belirsiz" yaz.
            - Kanıt yetersizse "uygulama": null ve güven düşük yaz. Uydurma.
            - "Envanter Kahini" satırları daha önce BAŞKA BİR AI'ın yaptığı tahminlerdir, doğrulanmış bilgi değildir. Girdi olarak
              kullan; ama öneri yalnız Kahin'e dayanıyorsa güven en fazla 60 olsun ve gerekçede "yalnız Kahin tahmini" yaz.
              Kahin diğer kanıtlarla (exe yolu, SM talebi, WEBSITE, benzer adlı sunucular…) çelişirse diğer kanıtları tercih et;
              destekleniyorsa gerekçede destekleyen kanıtı da yaz.

            Yalnızca JSON dizi döndür, başka metin yazma. Her boş port için bir eleman:
            [{"ip":"…","port":"…","uygulama":"… ya da null","yeni":false,"guven":0-100,"gerekce":"tek cümle; hangi kanıta dayandığın"}]
            """);
        foreach (var ip in ips)
            sb.AppendLine().AppendLine($"=== Sunucu {ip} · boş portlar: {string.Join(", ", todo[ip].Select(p => p == "" ? "(port yok)" : p))}")
              .AppendLine(Cut(texts.GetValueOrDefault(ip), cfg.GetValue("Eksik:MaxKarakter", 6000)));
        // Küçük model uzun kanıttan sonra baştaki kuralları unutabiliyor: en önemli kurallar sona tekrar
        sb.AppendLine().AppendLine("""
            HATIRLATMA (kanıtlardan önemli): genel bilgiler dosyasındaki kurallar SM taleplerinden önce gelir. Ajan / araç kurulum ya da
            erişim talepleri (Dataskope vb.) sunucunun uygulamasını göstermez. Yalnızca JSON dizi döndür.
            """);
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
            int guven = Math.Clamp(IslevService.Int(e, "guven"), 0, 100);
            string? gerekce = IslevService.Str(e, "gerekce");
            // Ajan / araç adı önerildiyse: portun exe'si o ürüne aitse "Altyapı: X", değilse öneri yok sayılır (gerekçe saklanır)
            if (app != null && AjanOf(app) is { } ajan)
            {
                if (PortExeHas(texts.GetValueOrDefault(ip), port, ajan)) app = "Altyapı: " + ajan;
                else { gerekce = $"Yok sayıldı: AI '{app}' önerdi ama {ajan} bir ajan / araç ürünü ve bu portu kabul eden exe ona ait değil. AI gerekçesi: {gerekce}"; app = null; guven = 0; }
            }
            if (app != null && !app.StartsWith("Altyapı", StringComparison.OrdinalIgnoreCase)) app = Snap(env, app, out yeni);
            outp[(ip, port)] = new EksikOneri(Cut(app, 200), yeni, guven, gerekce, Ai, "AI", DateTime.Now);
        }
        return outp;
    }

    // ---------------- Excel (CSV) ----------------
    // hepsi = false: yalnız onaylanan / elle girilenler; true: önerisi olan tüm satırlar
    public async Task<string> CsvAsync(bool hepsi, CancellationToken ct)
    {
        var rows = (await ListAsync(null, hepsi ? null : "onay", true, 1, 1000, false, ct, true)) is var first && first.Toplam > 1000
            ? (await Task.WhenAll(Enumerable.Range(1, (first.Toplam + 999) / 1000)
                .Select(p => ListAsync(null, hepsi ? null : "onay", true, p, 1000, false, ct, true)))).SelectMany(l => l.Satirlar).ToList()
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
