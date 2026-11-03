// ---------------------------------------------------------------------------
// Fiziksel envanter (kullanıcı: oda planı → kabin → host → VM → IP → uygulama; "Bu kabin kapanırsa hangi uygulamalar etkilenir?").
// Veri doku DB'de HAZIR: NlyteCollector (nlyte OData → dbo.NlyteRaw) + EXEC dbo.sp_KabinZincirYenile doldurur. DeltaFlow YALNIZ OKUR
// (tabloya yazmaz, prosedürü çağırmaz). Tablolar `SELECT *` ile okunur, kolonlar adla eşlenir (tip / sıra farkına dayanıklı).
//  - dbo.KabinHostVm: VM başına satır (kabin, U konumu, host, cluster, VM, IP, uygulamalar, kapsam, eşleşme türü)
//  - dbo.NlyteHostEslesme: ESX host başına seçilen nlyte kaydı ("nlyte'ta eksik host'lar" listesi; yoksa KabinHostVm'den türetilir)
// Kabin adı kalıbı `DR{oda}-{sıra harfi}{numara}` (ör. DR4-D3) → oda planı ızgarası; uymayanlar "Diğer".
// Ayarlar `Fiziksel:*` (tablo adları, CacheMinutes=30, AnaKapsam, UstSirasiIfadeleri).
// ---------------------------------------------------------------------------
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

record FizVm(string? Kabin, int? KabinU, string? KabinUSirasi, int? KabinBosU, int? HostUAlt, int? HostUYukseklik, string? HostYuz,
    string? HostModel, string? ChassisAdi, string? BladeSlot, string? Konum, string? Host, string? HostSeri, string? CabinetAssetId,
    string? Cluster, string? HostFqdn, string? Vm, string? Ip, string? DnsName, string? Os, List<string> Uygulamalar, List<string> Muhafizlar,
    string? Kaynak, string? Kapsam, string? Eslesme, DateTime? Yenileme);
record FizEksik(string Host, string? Fqdn, int VmSayisi, string? Kapsam, string? Cluster, string? EslesmeTuru, int? AdaySayisi, string? Not);

sealed class FizikselSnapshot
{
    public required List<FizVm> Vmler { get; init; }
    public required List<FizEksik> Eksik { get; init; }
    public required bool EksikTablodan { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }
    public DateTime? Yenileme => Vmler.Select(v => v.Yenileme).Where(x => x != null).Max();
    public Dictionary<string, List<FizVm>> KabinVm { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<FizVm>> IpVm { get; } = new(StringComparer.OrdinalIgnoreCase);
}

sealed class FizikselService(IConfiguration cfg, ILogger<FizikselService> log)
{
    readonly SemaphoreSlim _lock = new(1, 1);
    FizikselSnapshot? _snap;
    public static readonly Regex KabinKalip = new(@"^(DR\d+)-([A-Z]+)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string AnaKapsam => cfg["Fiziksel:AnaKapsam"] ?? "Ana veri merkezi";

    public async Task<FizikselSnapshot> GetAsync(bool force, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(cfg.GetValue("Fiziksel:CacheMinutes", 30));
        var cur = _snap;
        if (!force && cur != null && DateTimeOffset.Now - cur.LoadedAt < ttl) return cur;
        await _lock.WaitAsync(ct);
        try
        {
            cur = _snap;
            if (!force && cur != null && DateTimeOffset.Now - cur.LoadedAt < ttl) return cur;
            try
            {
                _snap = await LoadAsync(ct);
                log.LogInformation("Fiziksel envanter yüklendi: {Vm} satır, {Kabin} kabin, {Eksik} eksik host", _snap.Vmler.Count, _snap.KabinVm.Count, _snap.Eksik.Count);
                return _snap;
            }
            catch (Exception ex) when (cur != null && !force && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Fiziksel envanter yenilenemedi, önceki kopya kullanılıyor.");
                return cur;
            }
        }
        finally { _lock.Release(); }
    }

    async Task<FizikselSnapshot> LoadAsync(CancellationToken ct)
    {
        string connStr = cfg.GetConnectionString(cfg["Fiziksel:Baglanti"] ?? "Envanter")
            ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
        // Tablo adları yalnız config'den (kullanıcı girdisi değil)
        string vmTablo = cfg["Fiziksel:Tablolar:KabinHostVm"] ?? "dbo.KabinHostVm";
        string eslTablo = cfg["Fiziksel:Tablolar:NlyteHostEslesme"] ?? "dbo.NlyteHostEslesme";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        var vmler = new List<FizVm>();
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {vmTablo}", conn) { CommandTimeout = 120 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
                vmler.Add(new FizVm(S(r, k, "Kabin"), I(r, k, "KabinUYukseklik"), S(r, k, "KabinUSirasi"), I(r, k, "KabinBosU"),
                    I(r, k, "HostUAlt"), I(r, k, "HostUYukseklik"), S(r, k, "HostYuz"), S(r, k, "HostModel"), S(r, k, "ChassisAdi"), S(r, k, "BladeSlot"),
                    S(r, k, "Konum"), S(r, k, "FizikselHost"), S(r, k, "HostSeri"), S(r, k, "CabinetAssetID"), S(r, k, "Cluster"), S(r, k, "HostFqdn"),
                    S(r, k, "Vm"), S(r, k, "Ip"), S(r, k, "DnsName"), S(r, k, "Os"), Liste(S(r, k, "Uygulamalar")), Liste(S(r, k, "Muhafizlar")),
                    S(r, k, "Kaynak"), S(r, k, "Kapsam"), S(r, k, "EslesmeTuru"), D(r, k, "YenilemeZamani")));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException($"{vmTablo} tablosu yok: NlyteCollector / sp_KabinZincirYenile henüz çalıştırılmamış (sql/05_kabin_zincir_tablolar.sql).");
        }

        // nlyte'ta eksik host'lar: NlyteHostEslesme (EslesmeTuru = 'Yok'); tablo yoksa KabinHostVm'den (kabini olmayan host'lar)
        var eksik = new List<FizEksik>();
        bool tablodan = true;
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {eslTablo}", conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
            {
                if (!string.Equals(S(r, k, "EslesmeTuru"), "Yok", StringComparison.OrdinalIgnoreCase)) continue;
                string host = S(r, k, "HostKey") ?? S(r, k, "HostFqdn") ?? "?";
                eksik.Add(new FizEksik(host, S(r, k, "HostFqdn"), I(r, k, "VmSayisi") ?? 0, S(r, k, "Kapsam"), null, "Yok", I(r, k, "AdaySayisi"), null));
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            tablodan = false;
            log.LogWarning("{Tablo} yok; nlyte'ta eksik host'lar KabinHostVm'den türetiliyor.", eslTablo);
        }
        if (!tablodan)
            eksik = vmler.Where(v => v.Kabin == null && v.Eslesme is "Yok" or null && (v.HostFqdn ?? v.Host) != null)
                .GroupBy(v => v.HostFqdn ?? v.Host!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FizEksik(g.Key, g.First().HostFqdn, g.Count(), g.First().Kapsam, g.First().Cluster, "Yok", null, "KabinHostVm'den türetildi"))
                .ToList();

        return Olustur(vmler, eksik, tablodan);
    }

    // Anlık görüntü + dizinler (kabin → VM'ler, IP → VM'ler); sahte veriyle test için de kullanılır
    public static FizikselSnapshot Olustur(List<FizVm> vmler, List<FizEksik> eksik, bool tablodan)
    {
        // Cluster bilgisi eksik listesine KabinHostVm'den eklenir (FQDN ya da kısa ad)
        var clusterOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in vmler.Where(v => v.Cluster != null))
            foreach (var k in new[] { v.HostFqdn, v.HostFqdn?.Split('.')[0], v.Host }.OfType<string>()) clusterOf.TryAdd(k, v.Cluster!);
        eksik = eksik.Select(e => e.Cluster == null && (clusterOf.GetValueOrDefault(e.Fqdn ?? "") ?? clusterOf.GetValueOrDefault(e.Host)) is string c ? e with { Cluster = c } : e)
            .OrderByDescending(e => e.VmSayisi).ThenBy(e => e.Host).ToList();
        var s = new FizikselSnapshot { Vmler = vmler, Eksik = eksik, EksikTablodan = tablodan, LoadedAt = DateTimeOffset.Now };
        foreach (var v in vmler)
        {
            if (v.Kabin != null) (s.KabinVm.TryGetValue(v.Kabin, out var l) ? l : s.KabinVm[v.Kabin] = []).Add(v);
            if (v.Ip != null) foreach (var ip in v.Ip.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    (s.IpVm.TryGetValue(ip, out var l2) ? l2 : s.IpVm[ip] = []).Add(v);
        }
        return s;
    }

    // ---- okuma yardımcıları (kolon yoksa null) ----
    static Dictionary<string, int> Kolonlar(SqlDataReader r)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++) d.TryAdd(r.GetName(i), i);
        return d;
    }
    static string? S(SqlDataReader r, Dictionary<string, int> k, string ad)
    {
        if (!k.TryGetValue(ad, out int i) || r.IsDBNull(i)) return null;
        string v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim() ?? "";
        return v == "" || v is "#N/A" or "NULL" ? null : v;
    }
    static int? I(SqlDataReader r, Dictionary<string, int> k, string ad) =>
        S(r, k, ad) is string v && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? (int)Math.Round(d) : null;
    static DateTime? D(SqlDataReader r, Dictionary<string, int> k, string ad) =>
        k.TryGetValue(ad, out int i) && !r.IsDBNull(i) && r.GetValue(i) is DateTime dt ? dt
        : S(r, k, ad) is string v && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static List<string> Liste(string? v) => v == null ? [] :
        v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ---- görünümler ----
    public static (string? Oda, string? Sira, int? No) Parcala(string kabin)
    {
        var m = KabinKalip.Match(kabin);
        return m.Success ? (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant(), int.Parse(m.Groups[3].Value)) : (null, null, null);
    }
    static string OdaAdi(string? konum) => konum == null ? "" : konum.Split(" - ")[0].Trim();

    // U1 üstte mi? (nlyte CabinetUOrder). Değerler kesin bilinmiyor: "top"/"yukarı" ile başlayan ya da Fiziksel:UstSirasiIfadeleri'nde geçen → U1 üstte
    public bool U1Ustte(string? sira)
    {
        if (string.IsNullOrWhiteSpace(sira)) return false;
        var ifadeler = cfg.GetSection("Fiziksel:UstSirasiIfadeleri").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (ifadeler.Count > 0) return ifadeler.Any(x => string.Equals(x.Trim(), sira.Trim(), StringComparison.OrdinalIgnoreCase));
        string s = sira.Trim().ToLowerInvariant();
        return s.StartsWith("top") || s.StartsWith("yukar") || s.Contains("toptobottom") || s.Contains("descending");
    }

    public object Ozet(FizikselSnapshot s)
    {
        var kapsamlar = s.Vmler.GroupBy(v => v.Kapsam ?? "(boş)").Select(g => new
        {
            kapsam = g.Key, vm = g.Count(), kabinli = g.Count(v => v.Kabin != null),
            host = g.Select(v => v.HostFqdn ?? v.Host).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        }).OrderByDescending(x => x.kapsam == AnaKapsam).ThenByDescending(x => x.vm).ToList();
        var odalar = s.KabinVm.Keys.Select(k => (k, p: Parcala(k))).GroupBy(x => x.p.Oda ?? "Diğer")
            .Select(g => new
            {
                ad = g.Key, kabin = g.Count(), vm = g.Sum(x => s.KabinVm[x.k].Count),
                konum = g.SelectMany(x => s.KabinVm[x.k]).Select(v => OdaAdi(v.Konum)).Where(x => x != "").GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault(),
            })
            .OrderBy(x => x.ad == "Diğer").ThenBy(x => int.TryParse(x.ad.AsSpan(2), out int n) ? n : 999).ToList();
        return new { yenileme = s.Yenileme, yuklendi = s.LoadedAt, anaKapsam = AnaKapsam, kapsamlar, odalar, eksikSayisi = s.Eksik.Count(e => e.Kapsam == null || e.Kapsam == AnaKapsam) };
    }

    public object Oda(FizikselSnapshot s, string ad)
    {
        var kabinler = s.KabinVm.Where(kv => (Parcala(kv.Key).Oda ?? "Diğer").Equals(ad, StringComparison.OrdinalIgnoreCase)).Select(kv =>
        {
            var p = Parcala(kv.Key);
            var vs = kv.Value;
            return new
            {
                ad = kv.Key, sira = p.Sira, no = p.No, vm = vs.Count,
                host = vs.Select(v => v.Host ?? v.HostFqdn).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                uygulama = vs.SelectMany(v => v.Uygulamalar).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                bosU = vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), konum = vs.Select(v => v.Konum).FirstOrDefault(x => x != null),
            };
        }).OrderBy(k => k.sira).ThenBy(k => k.no).ThenBy(k => k.ad).ToList();
        return new { ad, kabinler };
    }

    public object? Kabin(FizikselSnapshot s, string ad)
    {
        if (!s.KabinVm.TryGetValue(ad, out var vs)) return null;
        var ilk = vs[0];
        var hostlar = vs.GroupBy(v => v.Host ?? v.HostFqdn ?? "?", StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var h = g.First();
            return new
            {
                ad = g.Key, fqdn = h.HostFqdn, seri = h.HostSeri, cluster = h.Cluster, model = h.HostModel, uAlt = h.HostUAlt, uYukseklik = h.HostUYukseklik,
                yuz = h.HostYuz, chassis = h.ChassisAdi, slot = h.BladeSlot, eslesme = h.Eslesme, kaynak = h.Kaynak, vm = g.Count(v => v.Vm != null),
                uygulama = g.SelectMany(v => v.Uygulamalar).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            };
        }).OrderByDescending(h => h.uAlt ?? -1).ThenBy(h => h.chassis).ThenBy(h => h.slot).ToList();
        int enUst = hostlar.Where(h => h.uAlt != null).Select(h => h.uAlt!.Value + Math.Max(1, h.uYukseklik ?? 1) - 1).DefaultIfEmpty(0).Max();
        int uKap = Math.Max(ilk.KabinU ?? vs.Select(v => v.KabinU).FirstOrDefault(x => x != null) ?? 42, enUst);
        var vmler = vs.Where(v => v.Vm != null || v.Ip != null).Select(v => new { host = v.Host ?? v.HostFqdn, v.Vm, v.Ip, dns = v.DnsName, v.Os, uygulamalar = v.Uygulamalar, muhafizlar = v.Muhafizlar, v.Kaynak })
            .OrderBy(v => v.host).ThenBy(v => v.Vm).ToList();
        return new
        {
            ad, konum = ilk.Konum, cabinetAssetId = ilk.CabinetAssetId, uKapasite = uKap, uSirasi = ilk.KabinUSirasi, u1Ustte = U1Ustte(ilk.KabinUSirasi),
            bosU = vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), hostlar, vmler, uygulamalar = Uygulamalar(s, vs, ad),
        };
    }

    // Kabinin uygulamaları + "kabin kapanırsa" etkisi: uygulamanın bu kabindeki VM'leri / tüm (ana DC) VM'leri → tamamı bu kabindeyse kritik
    object Uygulamalar(FizikselSnapshot s, List<FizVm> vs, string kabin)
    {
        var tumu = s.Vmler.Where(v => v.Kapsam == null || v.Kapsam == AnaKapsam).SelectMany(v => v.Uygulamalar.Select(a => (a, v)))
            .GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.v).ToList(), StringComparer.OrdinalIgnoreCase);
        return vs.SelectMany(v => v.Uygulamalar.Select(a => (a, v))).GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var hepsi = tumu.GetValueOrDefault(g.Key) ?? [];
            int buKabin = g.Select(x => x.v.Vm ?? x.v.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int toplam = hepsi.Select(v => v.Vm ?? v.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var digerKabin = hepsi.Where(v => v.Kabin != null && !v.Kabin.Equals(kabin, StringComparison.OrdinalIgnoreCase)).Select(v => v.Kabin!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int kabinsiz = hepsi.Count(v => v.Kabin == null);
            string etki = digerKabin.Count == 0 && kabinsiz == 0 ? "tamamı bu kabinde" : digerKabin.Count == 0 ? "başka kabin yok (kabini bilinmeyen VM var)" : "başka kabinlerde de var";
            return new
            {
                ad = g.Key, vm = buKabin, toplamVm = Math.Max(toplam, buKabin), etki, digerKabinler = digerKabin.Take(10).ToList(), digerKabinSayisi = digerKabin.Count, kabinsizVm = kabinsiz,
                hostlar = g.Select(x => x.v.Host ?? x.v.HostFqdn).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                muhafizlar = g.SelectMany(x => x.v.Muhafizlar).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }).OrderBy(x => x.etki == "tamamı bu kabinde" ? 0 : x.etki.StartsWith("başka kabin yok") ? 1 : 2).ThenByDescending(x => x.vm).ThenBy(x => x.ad).ToList();
    }

    public object Ara(FizikselSnapshot s, string q, int max = 50)
    {
        q = q.Trim();
        if (q.Length < 2) return new { q, sonuclar = Array.Empty<object>() };
        bool Has(string? x) => x != null && x.Contains(q, StringComparison.OrdinalIgnoreCase);
        var list = new List<(int Sira, string Tur, string Deger, FizVm V)>();
        foreach (var v in s.Vmler)
        {
            if (v.Ip != null && (v.Ip == q || Has(v.Ip))) list.Add((v.Ip == q ? 0 : 1, "IP", v.Ip, v));
            else if (Has(v.Vm) || Has(v.DnsName)) list.Add((2, "VM", v.Vm ?? v.DnsName!, v));
            else if (Has(v.Host) || Has(v.HostFqdn)) list.Add((3, "Host", v.Host ?? v.HostFqdn!, v));
            else if (Has(v.Kabin)) list.Add((4, "Kabin", v.Kabin!, v));
            else if (v.Uygulamalar.FirstOrDefault(Has) is string a) list.Add((5, "Uygulama", a, v));
            else if (Has(v.Cluster)) list.Add((6, "Cluster", v.Cluster!, v));
        }
        // Host / kabin / uygulama / cluster eşleşmeleri tekilleşir (VM'leri tek tek listelenmez)
        var sonuc = list.GroupBy(x => x.Tur is "IP" or "VM" ? $"{x.Tur}|{x.Deger}|{x.V.Vm}" : $"{x.Tur}|{x.Deger}|{x.V.Kabin}", StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First(), N: g.Count())).OrderBy(x => x.Item1.Sira).ThenBy(x => x.Item1.Deger)
            .Take(max).Select(x => new
            {
                tur = x.Item1.Tur, deger = x.Item1.Deger, kabin = x.Item1.V.Kabin, oda = x.Item1.V.Kabin != null ? Parcala(x.Item1.V.Kabin).Oda ?? "Diğer" : null,
                host = x.Item1.V.Host ?? x.Item1.V.HostFqdn, vm = x.Item1.V.Vm, ip = x.Item1.V.Ip, kapsam = x.Item1.V.Kapsam, adet = x.N,
            }).ToList();
        var kabinler = sonuc.Select(x => x.kabin).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new { q, sonuclar = sonuc, kabinler, kesik = list.Count > max };
    }

    public object? Ip(FizikselSnapshot s, string ip)
    {
        if (!s.IpVm.TryGetValue(ip, out var vs)) return null;
        var v = vs.OrderByDescending(x => x.Kabin != null).First();
        return new
        {
            ip, kabin = v.Kabin, oda = v.Kabin != null ? Parcala(v.Kabin).Oda : null, konum = v.Konum, odaAdi = OdaAdi(v.Konum), host = v.Host ?? v.HostFqdn,
            cluster = v.Cluster, vm = v.Vm, kapsam = v.Kapsam, anaDc = v.Kapsam == null || v.Kapsam == AnaKapsam, uAlt = v.HostUAlt, uYukseklik = v.HostUYukseklik,
            yuz = v.HostYuz, chassis = v.ChassisAdi, eslesme = v.Eslesme,
        };
    }

    public IEnumerable<FizEksik> Eksik(FizikselSnapshot s) => s.Eksik.Where(e => e.Kapsam == null || e.Kapsam == AnaKapsam);

    public static string EksikCsv(IEnumerable<FizEksik> l)
    {
        static string Q(string? v) { v ??= ""; return v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }
        var sb = new StringBuilder("﻿HOST;FQDN;VM SAYISI;CLUSTER;KAPSAM;ADAY SAYISI;NOT\r\n");
        foreach (var e in l) sb.Append(string.Join(";", Q(e.Host), Q(e.Fqdn), e.VmSayisi, Q(e.Cluster), Q(e.Kapsam), e.AdaySayisi?.ToString() ?? "", Q(e.Not))).Append("\r\n");
        return sb.ToString();
    }
}
