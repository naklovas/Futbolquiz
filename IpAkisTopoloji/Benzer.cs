using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Benzer sunucular (segment / etiket tarama, wwwroot/segment.html "Benzer sunucular" görünümü; kullanıcı: "sunucuların exe ve trafik parmak
// izini çıkarsak, aynı amaca hizmet edenleri bulabilir miyiz — aynı amaçlı, yakın amaçlı, alakasızları anlayalım"). AI yok, kodla:
//  Parmak izi (sunucu başına, tarama sonucundan): iş exe'leri (genel süreçler hariç, sürüm / sondaki sayı atılmış ad + klasör kökü),
//  dinlenen portlar (altyapı / dinamik hariç), gelen trafik (bağlanan uygulama ya da segment), giden trafik (gidilen uygulama / segment),
//  Nessus (servis adları, sertifika adı kökü, ürünler). ERT benzerlikte KULLANILMAZ (sonradan doğrulama / öneri için).
//  Benzerlik: bileşen başına IDF ağırlıklı Jaccard (herkeste olan w3wp / sqlservr az, nadir odeme.api çok sayar), iki tarafta da dolu
//  bileşenlerin ağırlıklı ortalaması; exe bileşeni yoksa karşılaştırılmaz (zayıf parmak izi).
//  Sınıf: puan ≥ Ayni (0,6) → aynı amaçlı (birleşik gruplar); Yakin (0,35) ≤ puan < Ayni → yakın amaçlı; en iyi eşi < Yakin → alakasız.
// ---------------------------------------------------------------------------
record BenzerSunucu(string Ip, string? Sunucu, string? Segment, List<string> ErtUygulamalar, Dictionary<string, List<string>> Iz,
    string Sinif, int? Grup, string? EnBenzer, double EnPuan, string? Oneri, string? OneriDuzey = null);
record BenzerGrup(int No, string Ad, List<string> Uyeler, double Puan, Dictionary<string, List<string>> Ortak,
    Dictionary<string, int> ErtDagilim, bool Tutarsiz, List<BenzerOneri> Oneriler);
record BenzerOneri(string Ip, string Uygulama, int Destek, int Toplam);
record BenzerYakin(string A, string B, int? GrupA, int? GrupB, double Puan, List<string> Ortak);
record BenzerSonuc(double Ayni, double Yakin, Dictionary<string, BenzerSunucu> Sunucular, List<BenzerGrup> Gruplar,
    List<BenzerYakin> Yakinlar, List<string> Alakasiz, List<string> Zayif, Dictionary<string, double> Agirlik);
record BenzerKarsilastirma(string A, string B, double Puan, List<BenzerBilesen> Bilesenler);
record BenzerBilesen(string Ad, double? Puan, double Agirlik, List<string> Ortak, List<string> SadeceA, List<string> SadeceB);

static class SunucuBenzerlik
{
    public static readonly string[] Bilesenler = ["exe", "port", "gelen", "giden", "nessus"];
    static readonly Dictionary<string, string> Adlar = new() { ["exe"] = "İş programları (exe)", ["port"] = "Dinlenen portlar",
        ["gelen"] = "Bağlanan uygulamalar / segmentler", ["giden"] = "Bağlandığı uygulamalar / segmentler", ["nessus"] = "Nessus (servis, sertifika, ürün)" };
    public static string BilesenAdi(string k) => Adlar.GetValueOrDefault(k, k);

    public static Dictionary<string, double> Agirliklar(IConfiguration cfg) => new()
    {
        ["exe"] = cfg.GetValue("Segment:Benzer:ExeAgirlik", 0.45), ["port"] = cfg.GetValue("Segment:Benzer:PortAgirlik", 0.15),
        ["gelen"] = cfg.GetValue("Segment:Benzer:GelenAgirlik", 0.15), ["giden"] = cfg.GetValue("Segment:Benzer:GidenAgirlik", 0.15),
        ["nessus"] = cfg.GetValue("Segment:Benzer:NessusAgirlik", 0.10)
    };

    // "d:\apps\odeme\v3.2\odeme.api.exe" → exe "odeme.api", klasör kökü "apps\odeme"; sürüm / tarih / sondaki sayı atılır (node01 ↔ node02)
    static string Sade(string s) => Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[\d]+([._-]\d+)*", "#"), @"[#._-]+$", "").Trim('#', '.', '_', '-', ' ');
    static readonly HashSet<string> GenelKlasor = ["c:", "d:", "e:", "f:", "program files", "program files (x86)", "programdata", "windows", "system32",
        "bin", "lib", "opt", "usr", "local", "apps", "app", "srv", "home", "x64", "x86", "release", "current", "core", "server", "service", "services", "#"];

    static List<string> ExeIz(string yol, string exe)
    {
        var l = new List<string> { "exe:" + Sade(Regex.Replace(exe, @"\.exe$", "", RegexOptions.IgnoreCase)) };
        var parca = yol.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).Select(Sade)
            .Where(p => p.Length >= 3 && !GenelKlasor.Contains(p)).Take(3).ToList();
        if (parca.Count > 0) l.Add("klasör:" + string.Join("\\", parca));
        return l;
    }

    // "10.1.2.5 (ODEME)" → karşı tarafın uygulaması (sistem portu / LB GW hariç) ya da segmenti
    static string? Karsi(EnvanterSnapshot env, string s)
    {
        string ip = s.Split(' ')[0];
        if (!LookupQuery.TryParseIpv4(ip, out _)) return null;
        if (env.IsVipGw(ip, out _)) return null;
        if (env.AppNamesOnIp(ip).FirstOrDefault(a => !env.IsSystemApp(a)) is { } app) return "uyg:" + app;
        return env.FindSegment(ip)?.Label is { } seg ? "seg:" + seg : null;
    }

    static Dictionary<string, Dictionary<string, HashSet<string>>> Izler(SegmentSonuc r, EnvanterSnapshot env)
    {
        var iz = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        HashSet<string> B(string ip, string k)
        {
            if (!iz.TryGetValue(ip, out var d)) iz[ip] = d = Bilesenler.ToDictionary(b => b, _ => new HashSet<string>());
            return d[k];
        }
        foreach (var x in r.Satirlar.Where(x => !x.Genel))
        {
            foreach (var f in ExeIz(x.Yol, x.Exe)) B(x.Ip, "exe").Add(f);
            if (!x.Altyapi && !x.Dinamik && !string.IsNullOrEmpty(x.Port) && x.Port != "0") B(x.Ip, "port").Add(x.Port);
            foreach (var c in x.Istemciler) if (Karsi(env, c) is { } k) B(x.Ip, "gelen").Add(k);
        }
        foreach (var g in r.Giden.Where(g => !g.Genel))
        {
            foreach (var f in ExeIz(g.Yol, g.Exe)) B(g.Ip, "exe").Add(f);
            foreach (var h in g.Hedefler) if (Karsi(env, h) is { } k) B(g.Ip, "giden").Add(k);
        }
        if (r.Nessus != null)
            foreach (var (ip, n) in r.Nessus)
            {
                if (!iz.ContainsKey(ip)) continue;   // yalnız Carbon Black'te iş exe'si olanlar (Nessus tek başına parmak izi sayılmaz)
                var b = B(ip, "nessus");
                foreach (var s in n.Servisler) b.Add("servis:" + Sade(s.Ad));
                foreach (var u in n.Urunler) b.Add("ürün:" + u.ToLowerInvariant());
                foreach (var c in n.Portlar.SelectMany(p => p.Sertifika)) b.Add("sertifika:" + Sade(c));   // odeme01.banka.local ↔ odeme02.banka.local
                foreach (var d in n.Veritabani) b.Add("db:" + Sade(d));
            }
        return iz;
    }

    public static BenzerSonuc Hesapla(SegmentSonuc r, EnvanterSnapshot env, IConfiguration cfg, double? ayniP = null, double? yakinP = null)
    {
        double ayni = Math.Clamp(ayniP ?? cfg.GetValue("Segment:Benzer:Ayni", 0.6), 0.1, 1), yakin = Math.Clamp(yakinP ?? cfg.GetValue("Segment:Benzer:Yakin", 0.35), 0.05, ayni);
        var ag = Agirliklar(cfg);
        var iz = Izler(r, env);
        var ips = iz.Keys.OrderBy(ip => EnvanterSnapshot.TryToUInt(ip, out uint u) ? u : 0).ToList();
        // zayıf parmak izi: Carbon Black'te görülen ama iş exe'si olmayan (yalnız genel / ajan süreçleri)
        var zayif = ips.Where(ip => iz[ip]["exe"].Count == 0)
            .Concat(r.Satirlar.Select(x => x.Ip).Concat(r.Giden.Select(g => g.Ip)).Distinct().Where(ip => !iz.ContainsKey(ip))).Distinct().ToList();
        var tam = ips.Where(ip => iz[ip]["exe"].Count > 0).ToList();
        var w = Idf(iz, tam);

        // aday çiftler: en az bir ortak, herkeste olmayan özellik (ters dizin)
        var dizin = new Dictionary<string, List<int>>();
        for (int i = 0; i < tam.Count; i++)
            foreach (var f in iz[tam[i]].SelectMany(kv => kv.Value.Select(v => kv.Key + "|" + v)))
                (dizin.TryGetValue(f, out var l) ? l : dizin[f] = []).Add(i);
        var puan = new Dictionary<(int, int), double>();
        foreach (var l in dizin.Values.Where(l => l.Count > 1 && l.Count <= Math.Max(2, tam.Count * 0.6)))
            for (int a = 0; a < l.Count; a++)
                for (int b = a + 1; b < l.Count; b++)
                {
                    var k = (l[a], l[b]);
                    if (!puan.ContainsKey(k)) puan[k] = Puan(iz[tam[l[a]]], iz[tam[l[b]]], w, ag).Puan;
                }

        // aynı amaçlı gruplar: puan ≥ ayni olan çiftlerin birleşik bileşenleri (union-find)
        var ust = Enumerable.Range(0, tam.Count).ToArray();
        int Kok(int x) { while (ust[x] != x) x = ust[x] = ust[ust[x]]; return x; }
        foreach (var ((a, b), p) in puan) if (p >= ayni) ust[Kok(a)] = Kok(b);
        var gruplar = Enumerable.Range(0, tam.Count).GroupBy(Kok).Where(g => g.Count() > 1).OrderByDescending(g => g.Count()).ToList();
        var grupNo = new Dictionary<int, int>();
        var sonGrup = new List<BenzerGrup>();
        for (int gi = 0; gi < gruplar.Count; gi++)
        {
            var uye = gruplar[gi].ToList();
            foreach (var u in uye) grupNo[u] = gi + 1;
            var ici = puan.Where(kv => uye.Contains(kv.Key.Item1) && uye.Contains(kv.Key.Item2)).Select(kv => kv.Value).ToList();
            // ortak özellikler: üyelerin en az %60'ında olanlar, nadir olan önce
            var ortak = Bilesenler.ToDictionary(k => k, k => uye.SelectMany(u => iz[tam[u]][k]).GroupBy(f => f)
                .Where(g => g.Count() * 10 >= uye.Count * 6).OrderByDescending(g => w.GetValueOrDefault(k + "|" + g.Key)).Select(g => Goster(g.Key)).Take(8).ToList());
            var ertler = uye.ToDictionary(u => tam[u], u => Ert(env, tam[u]));
            var dag = ertler.Values.SelectMany(x => x).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
            var kayitli = ertler.Where(e => e.Value.Count > 0).ToList();
            // tutarsız: ERT'de kayıtlı üyelerin hiçbir ortak uygulaması yok
            bool tutarsiz = kayitli.Count >= 2 && !kayitli.Skip(1).Aggregate(kayitli[0].Value.ToHashSet(StringComparer.OrdinalIgnoreCase),
                (acc, e) => { acc.IntersectWith(e.Value); return acc; }).Any();
            // öneri: ERT'de uygulaması olmayan üyeye, kayıtlı üyelerin yarısından fazlasında olan uygulama
            var oneri = new List<BenzerOneri>();
            if (dag.FirstOrDefault() is { Key: { } enCok, Value: int adet } && kayitli.Count > 0 && adet * 2 > kayitli.Count)
                foreach (var e in ertler.Where(e => e.Value.Count == 0)) oneri.Add(new(e.Key, enCok, adet, kayitli.Count));
            string ad = dag.FirstOrDefault().Key is { } da && dag.First().Value * 2 >= uye.Count ? da
                : ortak["exe"].FirstOrDefault() is { } oe ? oe.Replace("exe: ", "") : $"Grup {gi + 1}";
            sonGrup.Add(new BenzerGrup(gi + 1, ad, uye.Select(u => tam[u]).ToList(), ici.Count > 0 ? Math.Round(ici.Average(), 2) : 0, ortak, dag, tutarsiz, oneri));
        }

        // yakın amaçlı: farklı gruptaki (ya da grupsuz) çiftler; grup çifti başına en yüksek
        var yakinlar = puan.Where(kv => kv.Value >= yakin && kv.Value < ayni)
            .Where(kv => !grupNo.TryGetValue(kv.Key.Item1, out int ga) || !grupNo.TryGetValue(kv.Key.Item2, out int gb) || ga != gb)
            .GroupBy(kv => (grupNo.TryGetValue(kv.Key.Item1, out int x) ? "g" + x : tam[kv.Key.Item1], grupNo.TryGetValue(kv.Key.Item2, out int y) ? "g" + y : tam[kv.Key.Item2]))
            .Select(g => g.MaxBy(kv => kv.Value)).OrderByDescending(kv => kv.Value).Take(300)
            .Select(kv => new BenzerYakin(tam[kv.Key.Item1], tam[kv.Key.Item2], grupNo.TryGetValue(kv.Key.Item1, out int a) ? a : null,
                grupNo.TryGetValue(kv.Key.Item2, out int b) ? b : null, Math.Round(kv.Value, 2), OrtakListe(iz[tam[kv.Key.Item1]], iz[tam[kv.Key.Item2]], w)))
            .ToList();

        // sunucu başına en benzer eş ve sınıf
        var enIyi = new Dictionary<int, (int Es, double P)>();
        foreach (var ((a, b), p) in puan)
        {
            if (!enIyi.TryGetValue(a, out var ea) || p > ea.P) enIyi[a] = (b, p);
            if (!enIyi.TryGetValue(b, out var eb) || p > eb.P) enIyi[b] = (a, p);
        }
        var sunucular = new Dictionary<string, BenzerSunucu>();
        string? Host(string ip) => env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault();
        for (int i = 0; i < tam.Count; i++)
        {
            var e = enIyi.TryGetValue(i, out var x) ? x : (-1, 0);
            string sinif = grupNo.ContainsKey(i) ? "ayni" : e.Item2 >= yakin ? "yakin" : "alakasiz";
            var grp = grupNo.TryGetValue(i, out int gn) ? sonGrup[gn - 1] : null;
            // öneri: ERT'de uygulaması yoksa grubun çoğunluğu; grubu yoksa en benzer yakın eşin ERT uygulaması (zayıf öneri)
            string? oneri = grp?.Oneriler.FirstOrDefault(o => o.Ip == tam[i])?.Uygulama, duzey = oneri != null ? "grup" : null;
            if (oneri == null && sinif == "yakin" && Ert(env, tam[i]).Count == 0 && Ert(env, tam[e.Item1]) is { Count: 1 } es) { oneri = es[0]; duzey = "yakın"; }
            sunucular[tam[i]] = new BenzerSunucu(tam[i], Host(tam[i]), env.FindSegment(tam[i])?.Label, Ert(env, tam[i]), Goster(iz[tam[i]]), sinif,
                grp?.No, e.Item1 >= 0 ? tam[e.Item1] : null, Math.Round(e.Item2, 2), oneri, duzey);
        }
        foreach (var ip in zayif)
            sunucular[ip] = new BenzerSunucu(ip, Host(ip), env.FindSegment(ip)?.Label, Ert(env, ip),
                iz.TryGetValue(ip, out var zi) ? Goster(zi) : Bilesenler.ToDictionary(k => k, _ => new List<string>()), "zayif", null, null, 0, null);
        var alakasiz = sunucular.Values.Where(s => s.Sinif == "alakasiz").Select(s => s.Ip).ToList();
        return new BenzerSonuc(ayni, yakin, sunucular, sonGrup, yakinlar, alakasiz, zayif, ag);
    }

    public static BenzerKarsilastirma Karsilastir(SegmentSonuc r, EnvanterSnapshot env, IConfiguration cfg, string a, string b)
    {
        var iz = Izler(r, env);
        if (!iz.ContainsKey(a) || !iz.ContainsKey(b)) throw new InvalidOperationException("İki sunucunun da tarama sonucunda iş programı olmalı.");
        var w = Idf(iz, iz.Keys.ToList());
        var (p, bil) = Puan(iz[a], iz[b], w, Agirliklar(cfg));
        return new BenzerKarsilastirma(a, b, Math.Round(p, 2), Bilesenler.Select(k => new BenzerBilesen(BilesenAdi(k), bil.TryGetValue(k, out var x) ? Math.Round(x, 2) : null,
            Agirliklar(cfg)[k], iz[a][k].Intersect(iz[b][k]).Select(Goster).ToList(), iz[a][k].Except(iz[b][k]).Select(Goster).ToList(),
            iz[b][k].Except(iz[a][k]).Select(Goster).ToList())).ToList());
    }

    // IDF: log(1 + N / df) — çok sunucuda olan özellik az sayar
    static Dictionary<string, double> Idf(Dictionary<string, Dictionary<string, HashSet<string>>> iz, List<string> ips)
    {
        var df = new Dictionary<string, int>();
        foreach (var ip in ips) foreach (var (k, s) in iz[ip]) foreach (var f in s) df[k + "|" + f] = df.GetValueOrDefault(k + "|" + f) + 1;
        return df.ToDictionary(kv => kv.Key, kv => Math.Log(1 + (double)Math.Max(ips.Count, 1) / kv.Value));
    }

    static (double Puan, Dictionary<string, double> Bilesen) Puan(Dictionary<string, HashSet<string>> a, Dictionary<string, HashSet<string>> b,
        Dictionary<string, double> w, Dictionary<string, double> ag)
    {
        double top = 0, agTop = 0; var bil = new Dictionary<string, double>();
        foreach (var k in Bilesenler)
        {
            if (a[k].Count == 0 || b[k].Count == 0) continue;   // iki tarafta da dolu olmayan bileşen sayılmaz
            double W(string f) => w.GetValueOrDefault(k + "|" + f, 1);
            double kes = a[k].Intersect(b[k]).Sum(W), bir = a[k].Union(b[k]).Sum(W);
            double j = bir > 0 ? kes / bir : 0;
            bil[k] = j; top += ag[k] * j; agTop += ag[k];
        }
        return (agTop > 0 && bil.ContainsKey("exe") ? top / agTop : 0, bil);
    }

    static List<string> OrtakListe(Dictionary<string, HashSet<string>> a, Dictionary<string, HashSet<string>> b, Dictionary<string, double> w) =>
        Bilesenler.SelectMany(k => a[k].Intersect(b[k]).Select(f => (k, f))).OrderByDescending(x => w.GetValueOrDefault(x.k + "|" + x.f))
            .Select(x => Goster(x.f)).Take(6).ToList();

    static List<string> Ert(EnvanterSnapshot env, string ip) => env.AppNamesOnIp(ip).Where(a => !env.IsSystemApp(a)).Distinct().ToList();
    static string Goster(string f) => f.Replace("uyg:", "").Replace("seg:", "segment ").Replace("exe:", "exe: ").Replace("klasör:", "klasör: ");
    static Dictionary<string, List<string>> Goster(Dictionary<string, HashSet<string>> d) => d.ToDictionary(kv => kv.Key, kv => kv.Value.Select(Goster).OrderBy(x => x).ToList());
}
