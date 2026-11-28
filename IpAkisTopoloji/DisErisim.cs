using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// İnternetten erişilen servisler raporu (wwwroot/diserisim.html). Veriyi DeltaFlow TOPLAMAZ: repo kökündeki DisErisimServisi (Windows servisi)
// her gece bir önceki günün firewall (Palo Alto) + Carbon Black verisinden doku DB'deki DF_DIS_ERISIM* tablolarına yazar (sql/dis_erisim.sql);
// burası yalnız okur ve envanterle (ERT uygulaması, segment / kurum, VIP havuzu, etiket) zenginleştirir.
// Tablo adları DisErisim:Tablolar:* (servisle aynı), bağlantı DisErisim:Baglanti ya da ConnectionStrings:Envanter.
// ---------------------------------------------------------------------------
record DeServis(string Ip, int Port, DateTime? Ilk, DateTime? Son, DateTime? FwIlk, DateTime? FwSon, DateTime? CbIlk, DateTime? CbSon, long Toplam, int Gun,
    long? SonOturum, int? SonDisIp, long? SonEngel, int? SonEngelDisIp, string? DisHedef, string? Kural, string? FwUygulama, string? Exe, string? Sunucu,
    string? Kaynak, DateTime? SonTarama)
{
    public string? Uygulama { get; set; }
    public string? ErtSunucu { get; set; }
    public string? Segment { get; set; }
    public string? Kurum { get; set; }
    public string? Vlan { get; set; }
    public string? Vip { get; set; }          // bu IP:port bir VIP ise havuz; üyeyse üyesi olduğu VIP(ler)
    public List<string>? VipUyeler { get; set; }
    public List<string>? Etiketler { get; set; }
    public long[]? Seri { get; set; }        // son N günün izinli oturum sayısı (eski → yeni)
    public long[]? EngelSeri { get; set; }
}
record DeGun(DateTime Tarih, string Ip, int Port, long Izin, int IzinDisIp, long Engel, int EngelDisIp, bool Fw, bool Cb, DateTime? Ilk, DateTime? Son)
{
    public string? Uygulama { get; set; }
    public string? ErtSunucu { get; set; }
    public string? Segment { get; set; }
    public string? Kurum { get; set; }
    public string? Vip { get; set; }
}
record DeKaynak(string Ip, int Port, string DisIp, DateTime? Ilk, DateTime? Son, long Toplam, int Gun, DateTime? SonTarih);
record DeDetay(DateTime Tarih, string DisIp, string Kaynak, long Oturum, DateTime? Ilk, DateTime? Son);
record DeListe(DateTime? SonGun, DateTime? IlkGun, List<DateTime> Gunler, int YeniGun, string? EnvHata, List<DeCalisma> Calismalar, List<DeServis> Servisler, List<DeGun> Engel);
record DeCalisma(int Id, DateTime Baslangic, DateTime? Bitis, DateTime AralikBas, DateTime AralikBit, string Durum, int? Servis, int? Yeni, int? Detay, int? Kesik,
    string? Mesaj, string? Makine);

sealed class DisErisimService(IConfiguration cfg, EnvanterService envanter)
{
    IConfigurationSection D => cfg.GetSection("DisErisim");
    string Tablo(string ad, string v)
    {
        string t = D[$"Tablolar:{ad}"] is { Length: > 0 } x ? x : v;
        return Regex.IsMatch(t, @"^[A-Za-z0-9_.\[\]]+$") ? t : throw new InvalidOperationException($"Geçersiz tablo adı: {t}");
    }
    string TAna => Tablo("Ana", "dbo.DF_DIS_ERISIM");
    string TGun => Tablo("Gun", "dbo.DF_DIS_ERISIM_GUN");
    string TDetay => Tablo("Detay", "dbo.DF_DIS_ERISIM_DETAY");
    string TKaynak => Tablo("Kaynak", "dbo.DF_DIS_ERISIM_KAYNAK");
    string TCalisma => Tablo("Calisma", "dbo.DF_DIS_ERISIM_CALISMA");
    string Baglanti => D["Baglanti"] is { Length: > 0 } b ? b
        : cfg.GetConnectionString("Envanter") ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");

    async Task<List<T>> OkuAsync<T>(string sql, Func<SqlDataReader, T> f, CancellationToken ct, params (string, object)[] p)
    {
        await using var c = new SqlConnection(Baglanti);
        await c.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        foreach (var (a, v) in p) cmd.Parameters.AddWithValue(a, v);
        var l = new List<T>();
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) l.Add(f(r));
        }
        catch (SqlException ex) when (ex.Number == 208)
        { throw new InvalidOperationException("DF_DIS_ERISIM tabloları yok — sql/dis_erisim.sql betiğini doku DB'de çalıştırın ve DisErisimServisi'ni kurun."); }
        return l;
    }
    static DateTime? Dt(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDateTime(i);
    static string? St(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToString(r.GetValue(i));
    static long? Ln(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt64(r.GetValue(i));
    static int? In(SqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    public Task<List<DeCalisma>> CalismalarAsync(int n, CancellationToken ct) => OkuAsync($"""
        SELECT TOP (@n) ID, BASLANGIC, BITIS, ARALIK_BAS, ARALIK_BIT, DURUM, SERVIS, YENI_SERVIS, DETAY_SATIR, KAYNAK_KESIK, MESAJ, MAKINE
        FROM {TCalisma} ORDER BY ID DESC
        """, r => new DeCalisma(r.GetInt32(0), r.GetDateTime(1), Dt(r, 2), r.GetDateTime(3), r.GetDateTime(4), r.GetString(5), In(r, 6), In(r, 7), In(r, 8), In(r, 9),
        St(r, 10), St(r, 11)), ct, ("@n", n));

    // Özet + servis listesi (envanterle zenginleştirilmiş) + son seriGun günün günlük sayıları
    public async Task<DeListe> ListeAsync(int seriGun, CancellationToken ct)
    {
        var calismalar = await CalismalarAsync(30, ct);
        var servis = await OkuAsync($"""
            SELECT IP, PORT, ILK_GORULME, SON_GORULME, FW_ILK, FW_SON, CB_ILK, CB_SON, TOPLAM_OTURUM, GUN_SAYISI, SON_OTURUM, SON_DIS_IP, SON_ENGEL, SON_ENGEL_DIS_IP,
                   DIS_HEDEF, KURAL, FW_UYGULAMA, EXE, SUNUCU, KAYNAK, SON_TARAMA FROM {TAna}
            """, r => new DeServis(r.GetString(0), r.GetInt32(1), Dt(r, 2), Dt(r, 3), Dt(r, 4), Dt(r, 5), Dt(r, 6), Dt(r, 7), r.GetInt64(8), r.GetInt32(9),
            Ln(r, 10), In(r, 11), Ln(r, 12), In(r, 13), St(r, 14), St(r, 15), St(r, 16), St(r, 17), St(r, 18), St(r, 19), Dt(r, 20)), ct);
        var sonGun = calismalar.Where(c => c.Durum == "tamam").Select(c => (DateTime?)c.AralikBas).Max();
        var ilkGun = await OkuAsync($"SELECT MIN(ARALIK_BAS) FROM {TCalisma} WHERE DURUM = 'tamam'", r => Dt(r, 0), ct);
        var gunler = new List<DateTime>();
        if (sonGun is { } sg) for (int i = seriGun - 1; i >= 0; i--) gunler.Add(sg.Date.AddDays(-i));
        var seri = new Dictionary<(string, int), (long[] Izin, long[] Engel)>();
        if (gunler.Count > 0)
        {
            var g = await OkuAsync($"SELECT TARIH, IP, PORT, IZIN_OTURUM, ENGEL_OTURUM FROM {TGun} WHERE TARIH >= @t",
                r => (r.GetDateTime(0), r.GetString(1), r.GetInt32(2), r.GetInt64(3), r.GetInt64(4)), ct, ("@t", gunler[0]));
            foreach (var (t, ip, port, iz, en) in g)
            {
                int i = gunler.IndexOf(t.Date);
                if (i < 0) continue;
                if (!seri.TryGetValue((ip, port), out var s)) seri[(ip, port)] = s = (new long[gunler.Count], new long[gunler.Count]);
                s.Izin[i] += iz; s.Engel[i] += en;
            }
        }
        // yalnız engellenen (son günde izinli erişimi olmayan, engellenen denemesi olan) servisler
        List<DeGun> engel = sonGun == null ? [] : await OkuAsync($"""
            SELECT TOP (@n) TARIH, IP, PORT, IZIN_OTURUM, IZIN_DIS_IP, ENGEL_OTURUM, ENGEL_DIS_IP, FW, CB, ILK, SON FROM {TGun}
            WHERE TARIH = @t AND IZIN_OTURUM = 0 AND ENGEL_OTURUM > 0 ORDER BY ENGEL_OTURUM DESC
            """, Gun, ct, ("@t", sonGun.Value.Date), ("@n", D.GetValue("RaporMaxEngel", 2000)));
        EnvanterSnapshot? env = null;
        string? envHata = null;
        try { env = await envanter.GetAsync(false, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { envHata = ex.Message; }
        foreach (var x in servis)
        {
            if (env != null) Zengin(env, x.Ip, x.Port, a => x.Uygulama = a, s => x.ErtSunucu = s, (sg2, k, v) => { x.Segment = sg2; x.Kurum = k; x.Vlan = v; },
                (v, u) => { x.Vip = v; x.VipUyeler = u; }, e => x.Etiketler = e);
            if (seri.TryGetValue((x.Ip, x.Port), out var s)) { x.Seri = s.Izin; x.EngelSeri = s.Engel; }
        }
        foreach (var x in engel)
            if (env != null) Zengin(env, x.Ip, x.Port, a => x.Uygulama = a, s => x.ErtSunucu = s, (sg2, k, _) => { x.Segment = sg2; x.Kurum = k; }, (v, _) => x.Vip = v, _ => { });
        int yeniGun = D.GetValue("YeniGun", 7);
        return new DeListe(sonGun, ilkGun.FirstOrDefault(), gunler, yeniGun, envHata, calismalar,
            servis.OrderByDescending(x => x.Son).ThenByDescending(x => x.SonOturum).ToList(), engel);
    }

    DeGun Gun(SqlDataReader r) => new(r.GetDateTime(0), r.GetString(1), r.GetInt32(2), r.GetInt64(3), r.GetInt32(4), r.GetInt64(5), r.GetInt32(6), r.GetBoolean(7),
        r.GetBoolean(8), Dt(r, 9), Dt(r, 10));

    // ERT uygulaması (IP + port, yoksa IP'nin uygulamaları "(IP)"), sunucu adı, segment / kurum / VLAN, VIP (havuz ya da üyelik), etiketler
    static void Zengin(EnvanterSnapshot env, string ip, int port, Action<string?> uyg, Action<string?> sunucu, Action<string?, string?, string?> seg,
        Action<string?, List<string>?> vip, Action<List<string>?> etiket)
    {
        string p = port.ToString();
        var apps = env.AppsOnIp(ip);
        var portAd = apps.Where(h => h.Port == p).Select(h => h.AppName).OfType<string>().Distinct().ToList();
        var vipAd = apps.Where(h => h.VipIp == ip && h.VipPort == p).Select(h => h.AppName).OfType<string>().Distinct().ToList();
        var ipAd = env.AppNamesOnIp(ip);
        uyg(portAd.Count > 0 ? string.Join(" | ", portAd) : vipAd.Count > 0 ? string.Join(" | ", vipAd) : ipAd.Count > 0 ? string.Join(" | ", ipAd.Take(4)) + " (IP)" : null);
        sunucu(apps.Select(h => h.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
        var s = env.FindSegment(ip);
        seg(s?.Label, s?.Domain, s?.Vlan);
        var uyeler = env.VipMembers(ip, p);
        if (uyeler.Count > 0)
        {
            var havuz = uyeler.Select(u => u.Pool).FirstOrDefault(x => !string.IsNullOrEmpty(x))?.Replace("/Common/", "");
            vip($"VIP{(havuz != null ? " · havuz " + havuz : "")}", uyeler.Select(u =>
            {
                var a = env.AppsOnIp(u.Ip).Where(h => h.Port == u.Port).Select(h => h.AppName).OfType<string>().FirstOrDefault();
                return $"{u.Ip}:{u.Port}{(u.Hostname is { Length: > 0 } hn ? " " + hn : "")}{(a != null ? " · " + a : "")}";
            }).Distinct().Take(30).ToList());
        }
        else
        {
            var uye = env.MemberOf(ip).Where(m => m.Port == p).ToList();
            if (uye.Count > 0) vip("havuz üyesi · " + string.Join(", ", uye.Select(m => $"{m.LbIp}:{m.LbPort}{(m.Pool is { Length: > 0 } pl ? " (" + pl.Replace("/Common/", "") + ")" : "")}").Distinct().Take(4)), null);
        }
        var e = env.TagsOf(ip);
        etiket(e.Count > 0 ? e : null);
    }

    // Bir servisin dış kaynakları (kümülatif) + günlük eğilim + istenirse bir günün ayrıntısı
    public async Task<object> ServisAsync(string ip, int port, DateTime? gun, CancellationToken ct)
    {
        int max = D.GetValue("RaporMaxKaynak", 5000);
        var kaynak = await OkuAsync($"""
            SELECT TOP (@n) IP, PORT, DIS_IP, ILK_GORULME, SON_GORULME, TOPLAM_OTURUM, GUN_SAYISI, SON_TARIH FROM {TKaynak}
            WHERE IP = @ip AND PORT = @p ORDER BY SON_GORULME DESC, TOPLAM_OTURUM DESC
            """, r => new DeKaynak(r.GetString(0), r.GetInt32(1), r.GetString(2), Dt(r, 3), Dt(r, 4), r.GetInt64(5), r.GetInt32(6), Dt(r, 7)), ct,
            ("@n", max), ("@ip", ip), ("@p", port));
        var toplamKaynak = (await OkuAsync($"SELECT COUNT(*) FROM {TKaynak} WHERE IP = @ip AND PORT = @p", r => r.GetInt32(0), ct, ("@ip", ip), ("@p", port)))[0];
        var gunler = await OkuAsync($"""
            SELECT TARIH, IP, PORT, IZIN_OTURUM, IZIN_DIS_IP, ENGEL_OTURUM, ENGEL_DIS_IP, FW, CB, ILK, SON FROM {TGun}
            WHERE IP = @ip AND PORT = @p AND TARIH >= @t ORDER BY TARIH
            """, Gun, ct, ("@ip", ip), ("@p", port), ("@t", DateTime.Today.AddDays(-D.GetValue("RaporEgilimGun", 90))));
        List<DeDetay>? detay = null;
        if (gun is { } g)
            detay = await OkuAsync($"""
                SELECT TOP (@n) TARIH, DIS_IP, KAYNAK, OTURUM, ILK, SON FROM {TDetay} WHERE IP = @ip AND PORT = @p AND TARIH = @t ORDER BY OTURUM DESC
                """, r => new DeDetay(r.GetDateTime(0), r.GetString(1), r.GetString(2), r.GetInt64(3), Dt(r, 4), Dt(r, 5)), ct,
                ("@n", max), ("@ip", ip), ("@p", port), ("@t", g.Date));
        return new { ip, port, kaynak, toplamKaynak, kesik = toplamKaynak > kaynak.Count, gunler, detay, gun };
    }

    // Dış IP (ya da "a.b.c." öneki) → hangi iç servislere erişmiş
    public async Task<object> DisIpAsync(string q, CancellationToken ct)
    {
        q = q.Trim();
        bool onek = !System.Net.IPAddress.TryParse(q, out _);
        if (!Regex.IsMatch(q, @"^[0-9.]{3,15}$")) throw new InvalidOperationException("IP ya da IP öneki girin (ör. 31.10.0.1 ya da 31.10.).");
        var l = await OkuAsync($"""
            SELECT TOP 5000 IP, PORT, DIS_IP, ILK_GORULME, SON_GORULME, TOPLAM_OTURUM, GUN_SAYISI, SON_TARIH FROM {TKaynak}
            WHERE {(onek ? "DIS_IP LIKE @q" : "DIS_IP = @q")} ORDER BY SON_GORULME DESC
            """, r => new DeKaynak(r.GetString(0), r.GetInt32(1), r.GetString(2), Dt(r, 3), Dt(r, 4), r.GetInt64(5), r.GetInt32(6), Dt(r, 7)), ct,
            ("@q", onek ? q.TrimEnd('%') + "%" : q));
        EnvanterSnapshot? env = null;
        try { env = await envanter.GetAsync(false, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { }
        return new
        {
            q, satirlar = l.Select(k =>
            {
                string? uyg = null, sun = null, seg = null;
                if (env != null) Zengin(env, k.Ip, k.Port, a => uyg = a, s => sun = s, (s, _, _) => seg = s, (_, _) => { }, _ => { });
                return new { k.Ip, k.Port, k.DisIp, k.Ilk, k.Son, k.Toplam, k.Gun, uygulama = uyg, ertSunucu = sun, segment = seg };
            })
        };
    }

    // Excel: servisler, yalnız engellenen (son gün), son günün dış kaynakları, çalışmalar
    public async Task<byte[]> ExcelAsync(CancellationToken ct)
    {
        var l = await ListeAsync(14, ct);
        var (servis, engel, cal, sonGun, yeniGun) = (l.Servisler, l.Engel, l.Calismalar, l.SonGun, l.YeniGun);
        static object? T(DateTime? d) => d?.ToString("dd.MM.yyyy HH:mm:ss");
        var s1 = new List<object?[]> { new object?[] { "IP", "PORT", "ERT UYGULAMA", "ERT SUNUCU", "SEGMENT", "KURUM", "VLAN", "VIP", "VIP ÜYELERİ", "ETİKETLER",
            "İLK GÖRÜLME", "SON GÖRÜLME", "YENİ", "SON GÜN OTURUM", "SON GÜN DIŞ IP", "SON GÜN ENGELLENEN", "TOPLAM OTURUM", "GÖRÜLDÜĞÜ GÜN", "KAYNAK",
            "FIREWALL İLK", "FIREWALL SON", "CARBON BLACK İLK", "CARBON BLACK SON", "PUBLIC HEDEF (NAT ÖNCESİ)", "FIREWALL KURALI", "APP-ID", "EXE (CARBON BLACK)", "SUNUCU (CARBON BLACK)" } };
        s1.AddRange(servis.Select(x => new object?[] { x.Ip, x.Port, x.Uygulama, x.ErtSunucu, x.Segment, x.Kurum, x.Vlan, x.Vip, x.VipUyeler == null ? null : string.Join(" | ", x.VipUyeler),
            x.Etiketler == null ? null : string.Join(", ", x.Etiketler), T(x.Ilk), T(x.Son),
            x.Ilk != null && sonGun != null && x.Ilk.Value.Date > sonGun.Value.Date.AddDays(-yeniGun) ? "evet" : "", x.SonOturum, x.SonDisIp, x.SonEngel, x.Toplam, x.Gun, x.Kaynak,
            T(x.FwIlk), T(x.FwSon), T(x.CbIlk), T(x.CbSon), x.DisHedef, x.Kural, x.FwUygulama, x.Exe, x.Sunucu }));
        var s2 = new List<object?[]> { new object?[] { "TARİH", "IP", "PORT", "ERT UYGULAMA", "ERT SUNUCU", "SEGMENT", "KURUM", "VIP", "ENGELLENEN OTURUM", "ENGELLENEN DIŞ IP" } };
        s2.AddRange(engel.Select(x => new object?[] { x.Tarih.ToString("dd.MM.yyyy"), x.Ip, x.Port, x.Uygulama, x.ErtSunucu, x.Segment, x.Kurum, x.Vip, x.Engel, x.EngelDisIp }));
        var s3 = new List<object?[]> { new object?[] { "TARİH", "IP", "PORT", "DIŞ IP", "KAYNAK", "OTURUM", "İLK", "SON" } };
        if (sonGun != null)
            s3.AddRange((await OkuAsync($"""
                SELECT TOP (@n) TARIH, IP, PORT, DIS_IP, KAYNAK, OTURUM, ILK, SON FROM {TDetay} WHERE TARIH = @t ORDER BY IP, PORT, OTURUM DESC
                """, r => new object?[] { r.GetDateTime(0).ToString("dd.MM.yyyy"), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetInt64(5), T(Dt(r, 6)), T(Dt(r, 7)) },
                ct, ("@n", D.GetValue("ExcelMaxDetay", 500000)), ("@t", sonGun.Value.Date))));
        var s4 = new List<object?[]> { new object?[] { "ID", "BAŞLANGIÇ", "BİTİŞ", "TARANAN GÜN", "DURUM", "SERVİS", "YENİ", "KAYNAK SATIRI", "KAYNAK SINIRINA TAKILAN", "MAKİNE", "MESAJ" } };
        s4.AddRange(cal.Select(c => new object?[] { c.Id, T(c.Baslangic), T(c.Bitis), c.AralikBas.ToString("dd.MM.yyyy"), c.Durum, c.Servis, c.Yeni, c.Detay, c.Kesik, c.Makine, c.Mesaj }));
        return Xlsx.Yaz([("İnternetten erişilen", s1), ("Yalnız engellenen", s2), ($"Dış kaynaklar {sonGun:dd.MM}", s3), ("Çalışmalar", s4)]);
    }
}
