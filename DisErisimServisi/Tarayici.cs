using System.Data;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace DisErisimServisi;

// Bir günün (00:00–24:00, yerel saat) internetten gelen erişimleri:
//   Firewall (Palo Alto, Splunk): kaynağı DIŞ (public ve kurumun kendi genel bloklarında değil), hedefi (NAT sonrası, dest_translated_ip) İÇ olan oturumlar;
//     izinli ve engellenen ayrı sayılır (Firewall:IzinDegerleri). DisErisim:DisZonlar verilirse yalnız o kaynak zonları (ör. untrust).
//   Carbon Black (Splunk netconn): direction=inbound, remote_ip DIŞ → local_ip:local_port (yalnız ajanlı sunucular; F5 SNAT arkasında görünmez).
// "İç" = RFC1918 / özel aralıklar + segment tablosundaki (Envanter:SegmentTablosu) public CIDR'lar + DisErisim:IcAglar.
// Yazılanlar: DF_DIS_ERISIM (servis başına kümülatif ilk / son görülme), _GUN (gün × servis özet, izinli + engel), _DETAY (gün × servis × dış IP,
// yalnız izinli; servis başına en çok DisErisim:MaxKaynakIp), _KAYNAK (servis × dış IP kümülatif ilk / son), _CALISMA (çalışma kaydı).
sealed class Tarayici(IConfiguration cfg, ILogger<Tarayici> log)
{
    static readonly Regex Guvenli = new("^[A-Za-z0-9_.:-]+$");
    static readonly string[] Ozel = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "169.254.0.0/16", "100.64.0.0/10", "0.0.0.0/8", "224.0.0.0/3"];

    IConfigurationSection D => cfg.GetSection("DisErisim");
    string Tablo(string ad, string varsayilan)
    {
        string t = D[$"Tablolar:{ad}"] is { Length: > 0 } v ? v : varsayilan;
        return Regex.IsMatch(t, @"^[A-Za-z0-9_.\[\]]+$") ? t : throw new InvalidOperationException($"Geçersiz tablo adı: {t}");
    }
    string TAna => Tablo("Ana", "dbo.DF_DIS_ERISIM");
    string TGun => Tablo("Gun", "dbo.DF_DIS_ERISIM_GUN");
    string TDetay => Tablo("Detay", "dbo.DF_DIS_ERISIM_DETAY");
    string TKaynak => Tablo("Kaynak", "dbo.DF_DIS_ERISIM_KAYNAK");
    string TCalisma => Tablo("Calisma", "dbo.DF_DIS_ERISIM_CALISMA");
    string Baglanti => D["Baglanti"] is { Length: > 0 } b ? b
        : cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter (ya da DisErisim:Baglanti) tanımlı değil.");

    // ---- gün seçimi ----
    public async Task EksikGunleriTaraAsync(CancellationToken ct)
    {
        DateTime dun = DateTime.Today.AddDays(-1);
        DateTime? son;
        await using (var c = new SqlConnection(Baglanti))
        {
            await c.OpenAsync(ct);
            await using var cmd = new SqlCommand($"SELECT MAX(ARALIK_BIT) FROM {TCalisma} WHERE DURUM = 'tamam'", c);
            try { son = await cmd.ExecuteScalarAsync(ct) is DateTime d ? d : null; }
            catch (SqlException ex) when (ex.Number == 208) { throw new InvalidOperationException($"{TCalisma} tablosu yok — sql/dis_erisim.sql betiğini doku DB'de çalıştırın."); }
        }
        DateTime ilk = son?.Date ?? dun;
        DateTime enEski = DateTime.Today.AddDays(-Math.Max(1, D.GetValue("MaxGeriGun", 3)));
        if (ilk < enEski) { log.LogWarning("Son başarılı tarama {Son}; en çok {N} gün geriye gidilir.", son, D.GetValue("MaxGeriGun", 3)); ilk = enEski; }
        if (ilk > dun) { log.LogInformation("Taranacak gün yok (son tarama {Son}).", son); return; }
        for (var g = ilk; g <= dun; g = g.AddDays(1)) await GunuTaraAsync(g, false, ct);
    }

    public async Task GunuTaraAsync(DateTime gun, bool deneme, CancellationToken ct)
    {
        DateTime bas = gun.Date, bit = gun.Date.AddDays(1);
        log.LogInformation("Dış erişim taraması: {Bas:yyyy-MM-dd HH:mm} – {Bit:yyyy-MM-dd HH:mm}{D}", bas, bit, deneme ? " (deneme, yazılmaz)" : "");
        int? id = deneme ? null : await CalismaBaslatAsync(bas, bit, ct);
        var mesaj = new List<string>();
        try
        {
            var sonuc = await TaraAsync(bas, bit, mesaj, ct);
            if (deneme) { Yazdir(sonuc, mesaj); return; }
            var (servis, yeni, detay, kesik) = await YazAsync(sonuc, bas, bit, ct);
            mesaj.Insert(0, $"{servis} servis (izinli), {yeni} yeni, {sonuc.Servisler.Count(s => s.Value.IzinToplam == 0)} yalnız engellenen; {detay} kaynak satırı");
            await CalismaBitirAsync(id!.Value, "tamam", servis, yeni, detay, kesik, mesaj, ct);
            log.LogInformation("Bitti: {Mesaj}", string.Join(" | ", mesaj));
        }
        catch (Exception ex)
        {
            mesaj.Add("HATA: " + ex.Message);
            if (id != null) try { await CalismaBitirAsync(id.Value, "hata", null, null, null, null, mesaj, CancellationToken.None); } catch { }
            throw;
        }
    }

    // ---- tarama ----
    sealed class Servis
    {
        public string Ip = ""; public int Port;
        public long FwIzin, FwEngel, CbIzin; public int FwIzinIp, FwEngelIp, CbIp;
        public DateTime? FwIlk, FwSon, CbIlk, CbSon;
        public HashSet<string> DisHedef = [], Kural = [], App = [], Exe = [], Sunucu = [];
        public long IzinToplam => Math.Max(FwIzin, CbIzin);
        public DateTime? Ilk => Min(FwIlk, CbIlk);
        public DateTime? Son => Max(FwSon, CbSon);
    }
    sealed class Kaynak { public long Fw, Cb; public DateTime? Ilk, Son; }
    sealed record Sonuc(Dictionary<(string, int), Servis> Servisler, Dictionary<(string Ip, int Port, string Dis), Kaynak> Detay, HashSet<(string, int)> Kesik);

    async Task<Sonuc> TaraAsync(DateTime bas, DateTime bit, List<string> mesaj, CancellationToken ct)
    {
        var icAg = await IcAglarAsync(mesaj, ct);
        var kaynaklar = (D.GetSection("Kaynaklar").Get<string[]>() is { Length: > 0 } ka ? ka : ["fw", "cb"]).Select(x => x.Trim().ToLowerInvariant()).ToHashSet();
        int maxKaynak = Math.Max(0, D.GetValue("MaxKaynakIp", 20000));
        var servis = new Dictionary<(string, int), Servis>();
        var detay = new Dictionary<(string, int, string), Kaynak>();
        var kesik = new HashSet<(string, int)>();
        Servis S(string ip, int port) { if (!servis.TryGetValue((ip, port), out var x)) servis[(ip, port)] = x = new Servis { Ip = ip, Port = port }; return x; }
        Kaynak K(string ip, int port, string dis) { if (!detay.TryGetValue((ip, port, dis), out var x)) detay[(ip, port, dis)] = x = new Kaynak(); return x; }
        using var sp = new Splunk(cfg);

        if (kaynaklar.Contains("fw"))
        {
            var (onek, izin) = FwOnek(icAg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (l, m) = await sp.AraAsync($"""
                {onek}
                | eval ok=if(in(a, {izin}), 1, 0)
                | stats sum(ok) as izin, count as toplam, dc(eval(if(ok=1, src, null()))) as izin_ip, dc(eval(if(ok=0, src, null()))) as engel_ip,
                        min(eval(if(ok=1, _time, null()))) as ilk, max(eval(if(ok=1, _time, null()))) as son,
                        values(eval(if(ok=1 AND od!=d, od, null()))) as dis_hedef, values(eval(if(ok=1, kural, null()))) as kural,
                        values(eval(if(ok=1, app, null()))) as app by d p
                """, bas, bit, ct);
            mesaj.AddRange(m.Select(x => "Firewall: " + x));
            foreach (var r in l)
            {
                if (!Anahtar(r, out var ip, out int port)) continue;
                var x = S(ip, port);
                x.FwIzin = Sayi(r, "izin"); x.FwEngel = Sayi(r, "toplam") - x.FwIzin; x.FwIzinIp = (int)Sayi(r, "izin_ip"); x.FwEngelIp = (int)Sayi(r, "engel_ip");
                x.FwIlk = Zaman(r, "ilk"); x.FwSon = Zaman(r, "son");
                Ekle(x.DisHedef, r, "dis_hedef"); Ekle(x.Kural, r, "kural"); Ekle(x.App, r, "app");
            }
            log.LogInformation("Firewall servis: {N} satır, {Sn:0} sn", l.Count, sw.Elapsed.TotalSeconds);
            if (servis.Values.Any(s => s.FwIzin > 0))
            {
                sw.Restart();
                (l, m) = await sp.AraAsync($"""
                    {onek}
                    | where in(a, {izin})
                    | stats count as n min(_time) as ilk max(_time) as son by d p src
                    {Sinir(maxKaynak)}
                    """, bas, bit, ct);
                mesaj.AddRange(m.Select(x => "Firewall kaynak: " + x));
                foreach (var r in l)
                {
                    if (!Anahtar(r, out var ip, out int port) || r.GetValueOrDefault("src") is not { Length: > 0 } src) continue;
                    var k = K(ip, port, src); k.Fw += Sayi(r, "n"); k.Ilk = Min(k.Ilk, Zaman(r, "ilk")); k.Son = Max(k.Son, Zaman(r, "son"));
                }
                foreach (var s in servis.Values.Where(s => maxKaynak > 0 && s.FwIzinIp > maxKaynak)) kesik.Add((s.Ip, s.Port));
                log.LogInformation("Firewall kaynak: {N} satır, {Sn:0} sn", l.Count, sw.Elapsed.TotalSeconds);
            }
        }

        if (kaynaklar.Contains("cb"))
        {
            string onek = CbOnek(icAg);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (l, m) = await sp.AraAsync($"""
                {onek}
                | stats count as n dc(src) as ip min(_time) as ilk max(_time) as son values(process_path) as exe values(computer_name) as sunucu by d p
                """, bas, bit, ct);
            mesaj.AddRange(m.Select(x => "Carbon Black: " + x));
            foreach (var r in l)
            {
                if (!Anahtar(r, out var ip, out int port)) continue;
                var x = S(ip, port);
                x.CbIzin = Sayi(r, "n"); x.CbIp = (int)Sayi(r, "ip"); x.CbIlk = Zaman(r, "ilk"); x.CbSon = Zaman(r, "son");
                Ekle(x.Exe, r, "exe"); Ekle(x.Sunucu, r, "sunucu");
            }
            log.LogInformation("Carbon Black servis: {N} satır, {Sn:0} sn", l.Count, sw.Elapsed.TotalSeconds);
            if (l.Count > 0)
            {
                sw.Restart();
                (l, m) = await sp.AraAsync($"""
                    {onek}
                    | stats count as n min(_time) as ilk max(_time) as son by d p src
                    {Sinir(maxKaynak)}
                    """, bas, bit, ct);
                mesaj.AddRange(m.Select(x => "Carbon Black kaynak: " + x));
                foreach (var r in l)
                {
                    if (!Anahtar(r, out var ip, out int port) || r.GetValueOrDefault("src") is not { Length: > 0 } src) continue;
                    var k = K(ip, port, src); k.Cb += Sayi(r, "n"); k.Ilk = Min(k.Ilk, Zaman(r, "ilk")); k.Son = Max(k.Son, Zaman(r, "son"));
                }
                foreach (var s in servis.Values.Where(s => maxKaynak > 0 && s.CbIp > maxKaynak)) kesik.Add((s.Ip, s.Port));
                log.LogInformation("Carbon Black kaynak: {N} satır, {Sn:0} sn", l.Count, sw.Elapsed.TotalSeconds);
            }
        }
        if (kesik.Count > 0) mesaj.Add($"{kesik.Count} serviste dış kaynak IP'si DisErisim:MaxKaynakIp={maxKaynak} sınırını aştı; en çok oturum açan {maxKaynak} IP kaydedildi.");
        return new Sonuc(servis, detay, kesik);
    }

    // servis (d:p) başına en çok oturumlu N kaynak (0 = sınırsız)
    static string Sinir(int n) => n <= 0 ? "" : $"""
        | sort 0 d, p, -n
        | streamstats count as sira by d p
        | where sira <= {n}
        """;

    string Ad(string? v, string varsayilan)
    {
        string a = string.IsNullOrWhiteSpace(v) ? varsayilan : v.Trim();
        return Guvenli.IsMatch(a) ? a : throw new InvalidOperationException($"Geçersiz alan / indeks adı: {a}");
    }

    // dış: noktalı IPv4 ve özel / kurum içi CIDR'ların hiçbirinde değil
    static string Dis(string f, List<string> icAg) =>
        $"like({f}, \"%.%.%.%\") AND NOT ({string.Join(" OR ", Ozel.Concat(icAg).Select(c => $"cidrmatch(\"{c}\", {f})"))})";
    // arama düzeyinde kaba ön süzgeç (özel aralıklar dizinden okunmasın)
    static string OzelDegil(string f) =>
        string.Join(" ", new[] { "10.*", "192.168.*", "127.*", "169.254.*" }.Concat(Enumerable.Range(16, 16).Select(i => $"172.{i}.*")).Select(p => $"NOT {f}={p}"));

    (string Onek, string Izin) FwOnek(List<string> icAg)
    {
        var s = cfg.GetSection("Firewall"); var f = s.GetSection("Alanlar");
        string index = Ad(s["Index"], "fw_paloalto");
        string src = Ad(f["SrcIp"], "src_ip"), dest = Ad(f["DestIp"], "dest_ip"), dport = Ad(f["DestPort"], "dest_port");
        string natDest = Ad(f["NatDestIp"], "dest_translated_ip"), natPort = Ad(f["NatDestPort"], "dest_translated_port");
        string action = Ad(f["Action"], "action"), rule = Ad(f["Rule"], "rule"), app = Ad(f["App"], "app"), zone = Ad(f["SrcZone"], "src_zone");
        var izin = s.GetSection("IzinDegerleri").Get<string[]>()?.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0 && Guvenli.IsMatch(x)).ToList() is { Count: > 0 } iz
            ? iz : ["allowed", "allow", "accept"];
        var zonlar = (D.GetSection("DisZonlar").Get<string[]>() ?? []).Select(x => x.Trim()).Where(x => x.Length > 0 && Guvenli.IsMatch(x)).ToList();
        string zonSuz = zonlar.Count > 0 ? $"{zone} IN ({string.Join(", ", zonlar.Select(z => $"\"{z}\""))})" : "";
        string onek = $"""
            search index={index} {OzelDegil(src)} {zonSuz}
            | eval nat=if(isnull('{natDest}') OR '{natDest}'="" OR '{natDest}'="0.0.0.0", null(), '{natDest}')
            | eval src='{src}', od='{dest}', d=coalesce(nat, '{dest}'),
                   p=if(isnull(nat), '{dport}', coalesce(nullif('{natPort}', "0"), '{dport}')), a=lower('{action}'), kural='{rule}', app='{app}'
            | where isnotnull(p) AND p!="" AND p!="0"
            | where ({Dis("src", icAg)}) AND NOT ({Dis("d", icAg)})
            """;
        return (onek, string.Join(", ", izin.Select(x => $"\"{x}\"")));
    }

    string CbOnek(List<string> icAg)
    {
        var s = cfg.GetSection("Splunk");
        string index = Ad(s["Index"], "carbonblack");
        string st = s["Sourcetype"] is { Length: > 0 } x && Regex.IsMatch(x, @"^[A-Za-z0-9_.:\-]+$") ? x : "bit9:carbonblack:json";
        return $"""
            search index={index} sourcetype="{st}" direction=inbound {OzelDegil("remote_ip")}
            | where lower(direction)="inbound"
            | eval src=remote_ip, d=local_ip, p=local_port
            | where isnotnull(p) AND p!="" AND p!="0"
            | where ({Dis("src", icAg)}) AND NOT ({Dis("d", icAg)})
            """;
    }

    // kurumun kendi genel (public) blokları: segment tablosundaki özel olmayan CIDR'lar + DisErisim:IcAglar; iç içe olanlar teke indirilir
    async Task<List<string>> IcAglarAsync(List<string> mesaj, CancellationToken ct)
    {
        var l = new List<(uint Ag, int N)>();
        void Ekle(string? c) { if (Cidr(c) is { } x && !Ozel.Any(o => Icinde(Cidr(o)!.Value, x))) l.Add(x); }
        foreach (var c in D.GetSection("IcAglar").Get<string[]>() ?? []) Ekle(c);
        if (D.GetValue("SegmentlerdenIcAg", true))
        {
            string seg = cfg["Envanter:SegmentTablosu"] is { Length: > 0 } t && Regex.IsMatch(t, @"^[A-Za-z0-9_.\[\]]+$") ? t : "dbo.KonsolideSegmentler4";
            try
            {
                await using var c = new SqlConnection(Baglanti);
                await c.OpenAsync(ct);
                await using var cmd = new SqlCommand($"SELECT DISTINCT SEGMENT FROM {seg}", c);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) if (!r.IsDBNull(0)) Ekle(Convert.ToString(r.GetValue(0)));
            }
            catch (SqlException ex) { mesaj.Add($"Segment tablosu ({seg}) okunamadı, yalnız DisErisim:IcAglar kullanıldı: {ex.Message}"); }
        }
        var tek = new List<(uint Ag, int N)>();
        foreach (var x in l.Distinct().OrderBy(x => x.N)) if (!tek.Any(t => Icinde(t, x))) tek.Add(x);
        int max = D.GetValue("MaxIcAg", 500);
        if (tek.Count > max)
        {
            mesaj.Add($"Kurum içi genel blok sayısı {tek.Count} > DisErisim:MaxIcAg={max}; en geniş {max} blok kullanıldı (dar bloklardaki iç kaynaklar dış sayılabilir).");
            tek = tek.Take(max).ToList();
        }
        log.LogInformation("Kurum içi genel blok: {N}", tek.Count);
        return tek.Select(x => $"{new IPAddress(BitConverter.GetBytes(x.Ag).Reverse().ToArray())}/{x.N}").ToList();
    }

    static (uint Ag, int N)? Cidr(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var p = s.Trim().Split('/');
        if (!IPAddress.TryParse(p[0], out var a) || a.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return null;
        int n = p.Length > 1 && int.TryParse(p[1], out int k) && k is >= 0 and <= 32 ? k : 32;
        var b = a.GetAddressBytes();
        uint u = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]), m = n == 0 ? 0 : uint.MaxValue << (32 - n);
        return (u & m, n);
    }
    static bool Icinde((uint Ag, int N) dis, (uint Ag, int N) ic) => dis.N <= ic.N && (ic.Ag & (dis.N == 0 ? 0 : uint.MaxValue << (32 - dis.N))) == dis.Ag;

    // ---- satır yardımcıları ----
    static bool Anahtar(Dictionary<string, string> r, out string ip, out int port)
    {
        ip = (r.GetValueOrDefault("d") ?? "").Trim();
        return int.TryParse(r.GetValueOrDefault("p"), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && IPAddress.TryParse(ip, out _);
    }
    static long Sayi(Dictionary<string, string> r, string k) =>
        double.TryParse(r.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (long)d : 0;
    static DateTime? Zaman(Dictionary<string, string> r, string k) =>
        double.TryParse(r.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(d * 1000)).LocalDateTime : null;
    static void Ekle(HashSet<string> h, Dictionary<string, string> r, string k)
    {
        foreach (var v in (r.GetValueOrDefault(k) ?? "").Split("|||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (h.Count < 20) h.Add(v);
    }
    static DateTime? Min(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a < b ? a : b;
    static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a > b ? a : b;
    static string? Birlestir(IEnumerable<string> l, int max) { var s = string.Join(" | ", l.Order()); return s.Length == 0 ? null : s.Length > max ? s[..(max - 1)] + "…" : s; }

    // ---- yazma ----
    async Task<(int Servis, int Yeni, int Detay, int Kesik)> YazAsync(Sonuc s, DateTime bas, DateTime bit, CancellationToken ct)
    {
        var g = new DataTable();
        foreach (var (ad, tip) in new (string, Type)[] { ("IP", typeof(string)), ("PORT", typeof(int)), ("IZIN_OTURUM", typeof(long)), ("IZIN_DIS_IP", typeof(int)),
            ("ENGEL_OTURUM", typeof(long)), ("ENGEL_DIS_IP", typeof(int)), ("FW", typeof(bool)), ("CB", typeof(bool)), ("ILK", typeof(DateTime)), ("SON", typeof(DateTime)),
            ("FW_ILK", typeof(DateTime)), ("FW_SON", typeof(DateTime)), ("CB_ILK", typeof(DateTime)), ("CB_SON", typeof(DateTime)), ("DIS_HEDEF", typeof(string)),
            ("KURAL", typeof(string)), ("FW_UYGULAMA", typeof(string)), ("EXE", typeof(string)), ("SUNUCU", typeof(string)), ("KAYNAK", typeof(string)) })
            g.Columns.Add(ad, tip);
        var dIp = s.Detay.GroupBy(x => (x.Key.Ip, x.Key.Port)).ToDictionary(x => x.Key, x => x.Count());
        object N(object? o) => o ?? DBNull.Value;
        foreach (var x in s.Servisler.Values)
        {
            string? kaynak = (x.FwIzin > 0, x.CbIzin > 0) switch { (true, true) => "FW+CB", (true, false) => "FW", (false, true) => "CB", _ => null };
            int disIp = Math.Max(Math.Max(x.FwIzinIp, x.CbIp), dIp.GetValueOrDefault((x.Ip, x.Port)));
            g.Rows.Add(x.Ip, x.Port, x.IzinToplam, disIp, x.FwEngel, x.FwEngelIp, x.FwIzin + x.FwEngel > 0, x.CbIzin > 0, N(x.Ilk), N(x.Son), N(x.FwIlk), N(x.FwSon), N(x.CbIlk), N(x.CbSon),
                N(Birlestir(x.DisHedef, 400)), N(Birlestir(x.Kural, 400)), N(Birlestir(x.App, 200)), N(Birlestir(x.Exe, 600)), N(Birlestir(x.Sunucu, 200)), N(kaynak));
        }
        var dt = new DataTable();
        foreach (var (ad, tip) in new (string, Type)[] { ("IP", typeof(string)), ("PORT", typeof(int)), ("DIS_IP", typeof(string)), ("KAYNAK", typeof(string)),
            ("OTURUM", typeof(long)), ("ILK", typeof(DateTime)), ("SON", typeof(DateTime)) })
            dt.Columns.Add(ad, tip);
        foreach (var (k, v) in s.Detay)
            dt.Rows.Add(k.Ip, k.Port, k.Dis, v.Fw > 0 && v.Cb > 0 ? "FW+CB" : v.Fw > 0 ? "FW" : "CB", Math.Max(v.Fw, v.Cb), N(v.Ilk), N(v.Son));

        DateTime tarih = bas.Date;
        await using var c = new SqlConnection(Baglanti);
        await c.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);
        async Task Calis(string sql, params (string, object)[] p)
        {
            await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 1800 };
            foreach (var (a, v) in p) cmd.Parameters.AddWithValue(a, v);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await Calis("""
            CREATE TABLE #G (IP VARCHAR(45), PORT INT, IZIN_OTURUM BIGINT, IZIN_DIS_IP INT, ENGEL_OTURUM BIGINT, ENGEL_DIS_IP INT, FW BIT, CB BIT,
              ILK DATETIME2(0), SON DATETIME2(0), FW_ILK DATETIME2(0), FW_SON DATETIME2(0), CB_ILK DATETIME2(0), CB_SON DATETIME2(0),
              DIS_HEDEF NVARCHAR(400), KURAL NVARCHAR(400), FW_UYGULAMA NVARCHAR(200), EXE NVARCHAR(600), SUNUCU NVARCHAR(200), KAYNAK VARCHAR(10));
            CREATE TABLE #D (IP VARCHAR(45), PORT INT, DIS_IP VARCHAR(45), KAYNAK VARCHAR(10), OTURUM BIGINT, ILK DATETIME2(0), SON DATETIME2(0));
            """);
        foreach (var (tablo, veri) in new[] { ("#G", g), ("#D", dt) })
        {
            using var bc = new SqlBulkCopy(c, SqlBulkCopyOptions.Default, tx) { DestinationTableName = tablo, BulkCopyTimeout = 1800, BatchSize = 20000 };
            foreach (DataColumn col in veri.Columns) bc.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bc.WriteToServerAsync(veri, ct);
        }
        await Calis($"""
            DELETE FROM {TGun} WHERE TARIH = @t;
            INSERT INTO {TGun} (TARIH, IP, PORT, IZIN_OTURUM, IZIN_DIS_IP, ENGEL_OTURUM, ENGEL_DIS_IP, FW, CB, ILK, SON)
              SELECT @t, IP, PORT, IZIN_OTURUM, IZIN_DIS_IP, ENGEL_OTURUM, ENGEL_DIS_IP, FW, CB, ILK, SON FROM #G;
            DELETE FROM {TDetay} WHERE TARIH = @t;
            INSERT INTO {TDetay} (TARIH, IP, PORT, DIS_IP, KAYNAK, OTURUM, ILK, SON) SELECT @t, IP, PORT, DIS_IP, KAYNAK, OTURUM, ILK, SON FROM #D;
            """, ("@t", tarih));
        // ana tablo: SON_* yalnız bu gün daha yeniyse; toplamlar bu gün daha önce işlenmediyse (aynı gün yeniden taranırsa iki kez sayılmaz)
        int yeni;
        await using (var cmd = new SqlCommand($"""
            DECLARE @a TABLE (act NVARCHAR(10));
            MERGE {TAna} WITH (HOLDLOCK) AS t USING #G AS s ON t.IP = s.IP AND t.PORT = s.PORT
            WHEN MATCHED THEN UPDATE SET
              ILK_GORULME = CASE WHEN s.IZIN_OTURUM > 0 AND (t.ILK_GORULME IS NULL OR s.ILK < t.ILK_GORULME) THEN s.ILK ELSE t.ILK_GORULME END,
              SON_GORULME = CASE WHEN s.IZIN_OTURUM > 0 AND (t.SON_GORULME IS NULL OR s.SON > t.SON_GORULME) THEN s.SON ELSE t.SON_GORULME END,
              FW_ILK = CASE WHEN s.FW_ILK IS NOT NULL AND (t.FW_ILK IS NULL OR s.FW_ILK < t.FW_ILK) THEN s.FW_ILK ELSE t.FW_ILK END,
              FW_SON = CASE WHEN s.FW_SON IS NOT NULL AND (t.FW_SON IS NULL OR s.FW_SON > t.FW_SON) THEN s.FW_SON ELSE t.FW_SON END,
              CB_ILK = CASE WHEN s.CB_ILK IS NOT NULL AND (t.CB_ILK IS NULL OR s.CB_ILK < t.CB_ILK) THEN s.CB_ILK ELSE t.CB_ILK END,
              CB_SON = CASE WHEN s.CB_SON IS NOT NULL AND (t.CB_SON IS NULL OR s.CB_SON > t.CB_SON) THEN s.CB_SON ELSE t.CB_SON END,
              TOPLAM_OTURUM = t.TOPLAM_OTURUM + CASE WHEN t.SON_TARAMA IS NULL OR t.SON_TARAMA < @bit THEN s.IZIN_OTURUM ELSE 0 END,
              GUN_SAYISI = t.GUN_SAYISI + CASE WHEN s.IZIN_OTURUM > 0 AND (t.SON_TARAMA IS NULL OR t.SON_TARAMA < @bit) THEN 1 ELSE 0 END,
              SON_OTURUM = CASE WHEN t.SON_TARAMA IS NULL OR @bit >= t.SON_TARAMA THEN s.IZIN_OTURUM ELSE t.SON_OTURUM END,
              SON_DIS_IP = CASE WHEN t.SON_TARAMA IS NULL OR @bit >= t.SON_TARAMA THEN s.IZIN_DIS_IP ELSE t.SON_DIS_IP END,
              SON_ENGEL = CASE WHEN t.SON_TARAMA IS NULL OR @bit >= t.SON_TARAMA THEN s.ENGEL_OTURUM ELSE t.SON_ENGEL END,
              SON_ENGEL_DIS_IP = CASE WHEN t.SON_TARAMA IS NULL OR @bit >= t.SON_TARAMA THEN s.ENGEL_DIS_IP ELSE t.SON_ENGEL_DIS_IP END,
              DIS_HEDEF = COALESCE(s.DIS_HEDEF, t.DIS_HEDEF), KURAL = COALESCE(s.KURAL, t.KURAL), FW_UYGULAMA = COALESCE(s.FW_UYGULAMA, t.FW_UYGULAMA),
              EXE = COALESCE(s.EXE, t.EXE), SUNUCU = COALESCE(s.SUNUCU, t.SUNUCU),
              KAYNAK = CASE WHEN t.KAYNAK IS NULL THEN s.KAYNAK WHEN s.KAYNAK IS NULL OR t.KAYNAK = s.KAYNAK THEN t.KAYNAK ELSE 'FW+CB' END,
              SON_TARAMA = CASE WHEN t.SON_TARAMA IS NULL OR @bit > t.SON_TARAMA THEN @bit ELSE t.SON_TARAMA END
            WHEN NOT MATCHED BY TARGET AND s.IZIN_OTURUM > 0 THEN INSERT
              (IP, PORT, ILK_GORULME, SON_GORULME, FW_ILK, FW_SON, CB_ILK, CB_SON, TOPLAM_OTURUM, GUN_SAYISI, SON_OTURUM, SON_DIS_IP, SON_ENGEL, SON_ENGEL_DIS_IP,
               DIS_HEDEF, KURAL, FW_UYGULAMA, EXE, SUNUCU, KAYNAK, SON_TARAMA)
              VALUES (s.IP, s.PORT, s.ILK, s.SON, s.FW_ILK, s.FW_SON, s.CB_ILK, s.CB_SON, s.IZIN_OTURUM, 1, s.IZIN_OTURUM, s.IZIN_DIS_IP, s.ENGEL_OTURUM, s.ENGEL_DIS_IP,
               s.DIS_HEDEF, s.KURAL, s.FW_UYGULAMA, s.EXE, s.SUNUCU, s.KAYNAK, @bit)
            OUTPUT $action INTO @a;
            SELECT COUNT(*) FROM @a WHERE act = 'INSERT';
            """, c, tx) { CommandTimeout = 1800 })
        {
            cmd.Parameters.AddWithValue("@bit", bit);
            yeni = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        await Calis($"""
            MERGE {TKaynak} WITH (HOLDLOCK) AS t USING #D AS s ON t.IP = s.IP AND t.PORT = s.PORT AND t.DIS_IP = s.DIS_IP
            WHEN MATCHED THEN UPDATE SET
              ILK_GORULME = CASE WHEN t.ILK_GORULME IS NULL OR s.ILK < t.ILK_GORULME THEN s.ILK ELSE t.ILK_GORULME END,
              SON_GORULME = CASE WHEN t.SON_GORULME IS NULL OR s.SON > t.SON_GORULME THEN s.SON ELSE t.SON_GORULME END,
              TOPLAM_OTURUM = t.TOPLAM_OTURUM + CASE WHEN t.SON_TARIH IS NULL OR t.SON_TARIH < @t THEN s.OTURUM ELSE 0 END,
              GUN_SAYISI = t.GUN_SAYISI + CASE WHEN t.SON_TARIH IS NULL OR t.SON_TARIH < @t THEN 1 ELSE 0 END,
              SON_TARIH = CASE WHEN t.SON_TARIH IS NULL OR @t > t.SON_TARIH THEN @t ELSE t.SON_TARIH END
            WHEN NOT MATCHED BY TARGET THEN INSERT (IP, PORT, DIS_IP, ILK_GORULME, SON_GORULME, TOPLAM_OTURUM, GUN_SAYISI, SON_TARIH)
              VALUES (s.IP, s.PORT, s.DIS_IP, s.ILK, s.SON, s.OTURUM, 1, @t);
            """, ("@t", tarih));
        // saklama süresi
        await Calis($"""
            DELETE FROM {TDetay} WHERE TARIH < @d;
            DELETE FROM {TGun} WHERE TARIH < @g;
            """, ("@d", tarih.AddDays(-Math.Max(7, D.GetValue("DetaySaklamaGun", 180)))), ("@g", tarih.AddDays(-Math.Max(30, D.GetValue("GunSaklamaGun", 730)))));
        await tx.CommitAsync(ct);
        return (s.Servisler.Values.Count(x => x.IzinToplam > 0), yeni, dt.Rows.Count, s.Kesik.Count);
    }

    async Task<int> CalismaBaslatAsync(DateTime bas, DateTime bit, CancellationToken ct)
    {
        await using var c = new SqlConnection(Baglanti);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand($"""
            INSERT INTO {TCalisma} (BASLANGIC, ARALIK_BAS, ARALIK_BIT, DURUM, MAKINE) OUTPUT INSERTED.ID VALUES (@b, @ab, @ai, 'suruyor', @m)
            """, c);
        cmd.Parameters.AddWithValue("@b", DateTime.Now); cmd.Parameters.AddWithValue("@ab", bas); cmd.Parameters.AddWithValue("@ai", bit);
        cmd.Parameters.AddWithValue("@m", Environment.MachineName);
        try { return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)); }
        catch (SqlException ex) when (ex.Number == 208) { throw new InvalidOperationException($"{TCalisma} tablosu yok — sql/dis_erisim.sql betiğini doku DB'de çalıştırın."); }
    }

    async Task CalismaBitirAsync(int id, string durum, int? servis, int? yeni, int? detay, int? kesik, List<string> mesaj, CancellationToken ct)
    {
        await using var c = new SqlConnection(Baglanti);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand($"""
            UPDATE {TCalisma} SET BITIS = @b, DURUM = @d, SERVIS = @s, YENI_SERVIS = @y, DETAY_SATIR = @dt, KAYNAK_KESIK = @k, MESAJ = @m WHERE ID = @id
            """, c);
        cmd.Parameters.AddWithValue("@b", DateTime.Now); cmd.Parameters.AddWithValue("@d", durum);
        cmd.Parameters.AddWithValue("@s", (object?)servis ?? DBNull.Value); cmd.Parameters.AddWithValue("@y", (object?)yeni ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@dt", (object?)detay ?? DBNull.Value); cmd.Parameters.AddWithValue("@k", (object?)kesik ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", string.Join("\n", mesaj)); cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    void Yazdir(Sonuc s, List<string> mesaj)
    {
        foreach (var m in mesaj) Console.WriteLine("  " + m);
        Console.WriteLine($"Servis: {s.Servisler.Count} ({s.Servisler.Values.Count(x => x.IzinToplam > 0)} izinli), kaynak satırı: {s.Detay.Count}, sınıra takılan: {s.Kesik.Count}");
        foreach (var x in s.Servisler.Values.OrderByDescending(x => x.IzinToplam).ThenByDescending(x => x.FwEngel).Take(40))
            Console.WriteLine($"  {x.Ip}:{x.Port,-6} izinli FW {x.FwIzin,8} ({x.FwIzinIp} IP)  CB {x.CbIzin,8} ({x.CbIp} IP)  engel {x.FwEngel,8} ({x.FwEngelIp} IP)  " +
                $"{x.Ilk:dd.MM HH:mm}–{x.Son:HH:mm}  {Birlestir(x.Exe, 80)} {Birlestir(x.Kural, 60)}");
    }
}
