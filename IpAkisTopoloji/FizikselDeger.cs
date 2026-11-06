// Fiziksel cihaz değerleri (Varlık, İş, Erişilebilirlik, Gizlilik, Bütünlük) — kural motoru.
// Kullanıcı: "kablo bağlantısı çok az, takip edemiyoruz; FW, veri merkezi switch'leri, LB full 5 olmalı gibi genel kurallar bulalım,
// Exadata (model Server X9-2 / X9-2L) düğümleri de 5". Öncelik: hesaplanan (VM'lerin uygulamaları; VM'siz fiziksel sunucuda ERT'deki
// UNIXNAME / IP eşleşmesi; chassis → blade'leri) ile tür kuralının boyut başına en yükseği; PDU / panel / KVM / UPS / klima kabinin ya da
// odanın değerini yalnız ilgili boyutlarda miras alır. ERT analizi (canlı, 1.208 uygulama): Varlık hiçbir uygulamada en yüksek(G, B, E)'yi
// geçmiyor (%71 eşit) → türetilen Varlık = en yüksek(G, B, E); İş ya E'ye eşit ya 0 (girilmemiş) → İş 0 / boşsa E.
using System.Text;
using System.Text.RegularExpressions;

record FizKural(string Ad, string[]? Turler = null, string? Model = null, string? Isim = null, string? Kabin = null,
    int?[]? Degerler = null, string? Miras = null, string? Boyutlar = null, string? Aciklama = null);
// Değer + boyut başına kaynağı + nasıl bulunduğu
record FizDv(int?[] D, string?[] Kaynak, string Yontem);

sealed class FizDegerSonuc
{
    public Dictionary<FizCihaz, FizDv> Cihaz { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<FizCihaz, FizKural> Kural { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<string, FizDv> Kabin { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int ErtEslesen { get; set; }
}

sealed partial class FizikselService
{
    static readonly int?[] Bes = [5, 5, 5, 5, 5];
    public static readonly FizKural[] VarsayilanKurallar =
    [
        new("Exadata / Oracle mühendislik sistemi", Model: @"exadata|zdlra|exalogic|\bx\d+m?-\d+[a-z]*\b", Degerler: Bes,
            Aciklama: "Oracle Exadata veritabanı düğümü / depolama hücresi (ör. Server X9-2, X9-2L): kritik veritabanları"),
        new("Exadata kabini", Kabin: @"exa", Degerler: Bes, Aciklama: "Kabin adında EXA geçiyor: kabindeki tüm cihazlar"),
        new("SAN switch", Model: @"\bmds\b|brocade|\bds-c\d", Degerler: Bes, Aciklama: "Depolama ağı (fabric): veri yolu"),
        new("Firewall", Turler: ["Firewall"], Degerler: Bes, Aciklama: "Tüm trafik geçer; güvenlik sınırı"),
        new("Load balancer", Turler: ["LoadBalancer"], Degerler: Bes, Aciklama: "Uygulama girişi; SSL'i açar (trafiği şifresiz görür)"),
        new("Ağ cihazı (switch / router)", Turler: ["Network"], Degerler: Bes, Aciklama: "Veri merkezi ağı: trafik geçer, kapanırsa bağlı sunucular erişilemez"),
        new("Storage", Turler: ["Storage"], Degerler: Bes, Aciklama: "Veri burada durur"),
        new("KVM / konsol", Turler: ["KVM"], Miras: "kabin", Boyutlar: "GB", Aciklama: "Kabindeki sunuculara konsol erişimi verir: kabinin gizlilik / bütünlük değeri"),
        new("PDU / patch panel", Turler: ["PDU", "PatchPanel"], Miras: "kabin", Boyutlar: "E", Aciklama: "Yalnız erişilebilirliği etkiler: kabinin değeri"),
        new("UPS / statik switch / klima", Turler: ["UPS", "StaticSwitch", "Cooling"], Miras: "oda", Boyutlar: "E", Aciklama: "Odayı / birden çok kabini besler: odanın değeri"),
    ];

    // appsettings Fiziksel:DegerKurallari doluysa onlar (sırayla; ilk uyan kural), yoksa varsayılanlar
    public FizKural[] Kurallar()
    {
        try
        {
            var l = cfg.GetSection("Fiziksel:DegerKurallari").Get<List<FizKural>>();
            if (l is { Count: > 0 }) return [.. l];
        }
        catch (Exception ex) { log.LogWarning("Fiziksel:DegerKurallari okunamadı: {M}", ex.Message); }
        return VarsayilanKurallar;
    }

    static bool Uyar(FizKural r, FizCihaz c, string kabin)
    {
        static bool Re(string? desen, string? metin) => desen == null || (metin != null && Regex.IsMatch(metin, desen, RegexOptions.IgnoreCase));
        if (r.Turler is { Length: > 0 } && !r.Turler.Contains(c.Tur, StringComparer.OrdinalIgnoreCase)) return false;
        if (r.Model != null && !Re(r.Model, $"{c.Model} {c.MalzemeTip}")) return false;
        if (r.Isim != null && !c.Adlar.Append(c.Ad).Any(a => Re(r.Isim, a))) return false;
        if (r.Kabin != null && !Re(r.Kabin, kabin)) return false;
        return r.Turler != null || r.Model != null || r.Isim != null || r.Kabin != null;
    }

    // Uygulama kümesinin ham değerleri (boyut başına en yüksek + o değeri veren uygulama)
    static FizDv? Dv(FizikselSnapshot s, IEnumerable<string> uygulamalar, string yontem)
    {
        var d = new int?[5]; var k = new string?[5];
        foreach (var a in uygulamalar.Distinct(StringComparer.OrdinalIgnoreCase))
            if (s.AppDeger.TryGetValue(a.Trim(), out var x))
                for (int i = 0; i < 5; i++)
                    if (x[i] is int v && (d[i] == null || v > d[i])) { d[i] = v; k[i] = a; }
        return d.All(x => x == null) ? null : new FizDv(d, k, yontem);
    }

    static FizDv? EnYuksek(FizDv? a, FizDv? b)
    {
        if (a == null) return b;
        if (b == null) return a;
        var d = new int?[5]; var k = new string?[5];
        for (int i = 0; i < 5; i++)
        {
            bool bb = b.D[i] != null && (a.D[i] == null || b.D[i] > a.D[i]);
            d[i] = bb ? b.D[i] : a.D[i]; k[i] = bb ? b.Kaynak[i] : a.Kaynak[i];
        }
        return new FizDv(d, k, a.Yontem == b.Yontem ? a.Yontem : $"{a.Yontem} + {b.Yontem}");
    }

    // VM'siz fiziksel sunucu (Exadata, bare-metal DB…): cihaz adı ↔ ERT UNIXNAME; MSY fiziksel envanterinde seri → DNS adı / IP ↔ ERT
    static List<string> ErtUygulamalari(FizikselSnapshot s, FizCihaz c)
    {
        var l = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in c.Adlar.Append(c.Ad)) if (s.ErtAd.TryGetValue(Norm(a), out var x)) l.UnionWith(x);
        if (c.Seri != null && s.MsySeri.TryGetValue(c.Seri.Trim(), out var m))
        {
            if (m.DnsName != null && s.ErtAd.TryGetValue(Norm(m.DnsName), out var x)) l.UnionWith(x);
            if (m.DnsIp != null && s.ErtIp.TryGetValue(m.DnsIp.Trim(), out var y)) l.UnionWith(y);
        }
        return [.. l];
    }

    public FizDegerSonuc Degerler(FizikselSnapshot s)
    {
        if (s.DegerOnbellek != null) return s.DegerOnbellek;
        var r = new FizDegerSonuc();
        var kurallar = Kurallar();
        var avm = AssetVm(s);
        var miras = new List<(FizCihaz C, string Kabin, FizKural K)>();
        foreach (var kabin in s.TumKabinler)
        {
            var cs = (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Where(c => c.Aktif).ToList();
            foreach (var c in cs)
            {
                FizDv? hes = null;
                if (c.AssetId != null && avm.TryGetValue(c.AssetId, out var vs)) hes = Dv(s, vs.SelectMany(v => v.Uygulamalar), "VM'lerin uygulamalarından");
                else if (ErtUygulamalari(s, c) is { Count: > 0 } ua && Dv(s, ua, "ERT'deki uygulamalarından (sunucu adı / IP)") is { } e) { hes = e; r.ErtEslesen++; }
                // VM'i olan cihaz sunucudur (nlyte'ta türü yanlış girilmiş olabilir: esxzbdwh04 "Network"): türe bağlı kurallar uygulanmaz, model / ad / kabin kuralları uygulanır
                bool vmli = c.AssetId != null && avm.ContainsKey(c.AssetId);
                var k = kurallar.FirstOrDefault(x => (!vmli || x.Turler is not { Length: > 0 }) && Uyar(x, c, kabin));
                if (k != null) r.Kural[c] = k;
                if (k?.Miras != null) { miras.Add((c, kabin, k)); if (hes != null) r.Cihaz[c] = hes; continue; }
                var kd = k?.Degerler is { Length: 5 } ? new FizDv(k.Degerler, Enumerable.Repeat<string?>($"kural: {k.Ad}", 5).ToArray(), $"kural: {k.Ad}") : null;
                if (EnYuksek(hes, kd) is { } son) r.Cihaz[c] = son;
            }
            // chassis: kendi değeri yoksa blade'lerinin en yükseği
            foreach (var ch in cs.Where(c => c.Tur.Equals("Chassis", StringComparison.OrdinalIgnoreCase) && c.AssetId != null))
            {
                var bl = cs.Where(b => string.Equals(b.ChassisId, ch.AssetId, StringComparison.OrdinalIgnoreCase) && r.Cihaz.ContainsKey(b)).Select(b => r.Cihaz[b] with { Yontem = "blade'lerinden" });
                if (bl.Aggregate((FizDv?)null, EnYuksek) is { } bd) r.Cihaz[ch] = EnYuksek(r.Cihaz.GetValueOrDefault(ch), bd)! with { Yontem = "blade'lerinden" };
            }
            // kabinin değeri: cihazları + kabindeki tüm VM'ler (nlyte cihazıyla eşleşmeyen host'lar dahil)
            FizDv? kv = Dv(s, (s.KabinVm.GetValueOrDefault(kabin) ?? []).SelectMany(v => v.Uygulamalar), "VM'lerin uygulamalarından");
            foreach (var c in cs) if (r.Cihaz.TryGetValue(c, out var cd)) kv = EnYuksek(kv, cd with { Kaynak = cd.Kaynak.Select(x => x == null ? null : $"{c.Ad} ({x})").ToArray() });
            if (kv != null) r.Kabin[kabin] = kv with { Yontem = "kabindeki uygulamalar ve cihazlar" };
        }
        // oda değeri (miras için): odadaki kabinlerin en yükseği; "Diğer" oda değil → kabinin kendisi
        var oda = r.Kabin.GroupBy(kv => Parcala(kv.Key).Oda ?? "Diğer", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Value with { Kaynak = x.Value.Kaynak.Select(y => y == null ? null : $"{x.Key}: {y}").ToArray() }).Aggregate((FizDv?)null, EnYuksek), StringComparer.OrdinalIgnoreCase);
        foreach (var (c, kabin, k) in miras)
        {
            string odaAd = Parcala(kabin).Oda ?? "Diğer";
            bool odadan = k.Miras!.Equals("oda", StringComparison.OrdinalIgnoreCase) && odaAd != "Diğer";
            var src = odadan ? oda.GetValueOrDefault(odaAd) : r.Kabin.GetValueOrDefault(kabin);
            if (src == null) continue;
            string bas = odadan ? $"{odaAd} odasından" : $"{kabin} kabininden";
            var d = new int?[5]; var kay = new string?[5];
            var boy = (k.Boyutlar ?? "E").ToUpperInvariant();
            foreach (var (harf, i) in new[] { ('E', 2), ('G', 3), ('B', 4) }) if (boy.Contains(harf)) { d[i] = src.D[i]; kay[i] = $"{bas}: {src.Kaynak[i]}"; }
            d[1] = src.D[1]; kay[1] = $"{bas}: {src.Kaynak[1]}";                          // iş durur → İş değeri de gelir
            d[0] = new[] { d[2], d[3], d[4] }.Max(); kay[0] = d[0] != null ? "türetildi: en yüksek(G, B, E)" : null;
            var md = new FizDv(d, kay, $"kural: {k.Ad} ({bas} miras: {string.Join(", ", boy.Select(h => h switch { 'E' => "erişilebilirlik", 'G' => "gizlilik", 'B' => "bütünlük", _ => h.ToString() }))})");
            if (EnYuksek(r.Cihaz.GetValueOrDefault(c), md) is { } son) r.Cihaz[c] = son;
        }
        return s.DegerOnbellek = r;
    }

    // arayüz nesnesi: {v,i,e,g,b,kaynak,yontem}
    static object? DvObj(FizDv? x) => x == null ? null : new
    {
        v = x.D[0], i = x.D[1], e = x.D[2], g = x.D[3], b = x.D[4],
        kaynak = new { v = x.Kaynak[0], i = x.Kaynak[1], e = x.Kaynak[2], g = x.Kaynak[3], b = x.Kaynak[4] }, yontem = x.Yontem,
    };
    object? CihazDeger(FizikselSnapshot s, FizCihaz? c) => c != null && Degerler(s).Cihaz.TryGetValue(c, out var d) ? DvObj(d) : null;
    object? KabinDeger(FizikselSnapshot s, string? kabin) => kabin != null && Degerler(s).Kabin.TryGetValue(kabin, out var d) ? DvObj(d) : null;

    // "Değer kuralları" paneli: kurallar ve uyan cihaz sayısı, değeri bulunamayan cihazlar, türetilen uygulama değerleri
    public object KuralOzet(FizikselSnapshot s)
    {
        var r = Degerler(s);
        var aktif = s.TumKabinler.SelectMany(k => (s.KabinCihaz.GetValueOrDefault(k) ?? []).Where(c => c.Aktif)).ToList();
        return new
        {
            kurallar = Kurallar().Select(k => new
            {
                ad = k.Ad, turler = k.Turler, model = k.Model, isim = k.Isim, kabin = k.Kabin, degerler = k.Degerler, miras = k.Miras, boyutlar = k.Boyutlar, aciklama = k.Aciklama,
                cihaz = r.Kural.Count(x => x.Value == k),
            }).ToList(),
            ayarli = cfg.GetSection("Fiziksel:DegerKurallari").GetChildren().Any(),
            cihaz = aktif.Count, degerli = aktif.Count(r.Cihaz.ContainsKey), ertEslesen = r.ErtEslesen,
            vmden = aktif.Count(c => r.Cihaz.TryGetValue(c, out var d) && d.Yontem.Contains("VM'ler")),
            degersiz = aktif.Where(c => !r.Cihaz.ContainsKey(c)).GroupBy(c => c.Tur, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { tur = g.Key, adet = g.Count(), ornek = g.Select(c => c.Ad).Take(5).ToList() }).OrderByDescending(x => x.adet).ToList(),
            kabin = s.TumKabinler.Count(), kabinDegerli = r.Kabin.Count,
            turetilenIs = s.AppTuretilen.Count, uygulama = s.AppDeger.Count, kabinDisi = s.KabinDisi.Count(c => c.Aktif),
        };
    }

    public string DegerCsv(FizikselSnapshot s)
    {
        var r = Degerler(s);
        var sb = new StringBuilder("﻿KABİN;ODA;CİHAZ;TÜR;MODEL;NLYTE ASSETID;VARLIK;İŞ;ERİŞİLEBİLİRLİK;GİZLİLİK;BÜTÜNLÜK;YÖNTEM;EN YÜKSEK DEĞERİ VEREN\r\n");
        foreach (var kabin in s.TumKabinler.OrderBy(k => k))
            foreach (var c in (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Where(c => c.Aktif).OrderBy(c => c.UAlt ?? 0))
            {
                var d = r.Cihaz.GetValueOrDefault(c);
                string N(int i) => d?.D[i]?.ToString() ?? "";
                sb.Append(string.Join(";", CsvAlan(kabin), CsvAlan(Parcala(kabin).Oda ?? "Diğer"), CsvAlan(c.Ad), CsvAlan(c.Tur), CsvAlan(c.Model), CsvAlan(c.AssetId),
                    N(0), N(1), N(2), N(3), N(4), CsvAlan(d?.Yontem ?? "değer bulunamadı"),
                    CsvAlan(d == null ? null : string.Join(" | ", d.Kaynak.Where(x => x != null).Distinct().Take(3)))))
                  .Append("\r\n");
            }
        return sb.ToString();
    }
}
