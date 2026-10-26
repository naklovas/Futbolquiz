// ---------------------------------------------------------------------------
// Exe ilişki ağı (kullanıcı: "exelerin ilişki ağını çıkaralım; topolojide etiketlileri çizdiğimiz gibi, aynı yapı"): segment / etiket
// taramasının ham bağlarından (SegmentSonuc.Baglar) küme topolojisi verisi (KtSonuc) üretir; çizim kumetopo.js'in aynısı.
//  Kutu (küme) = exe (aynı exe'yi çalıştıran sunucular tek kutuda), karo (eleman) = o exe'nin bir sunucudaki örneği, ok = exe → exe trafik.
//  Bağ: giden olay A sunucusunda X.exe → B:P; B taranmışsa ve P'yi kabul eden exe Y biliniyorsa X@A → Y@B. B taranmamışsa ya da P'de exe
//  görülmediyse hedef "Dış · <ERT uygulaması / segment>" kutusunda B:P karosu. Taranmayan istemciden gelen olay → "Dış istemci · …" kutusu.
//  Genel / ajan süreçleri (SurecAi.GenelExe) çizilmez (ntoskrnl = HTTP.sys tutulur). ERT uygulaması karonun "Uygulama"sı (kutu içi grup).
// ---------------------------------------------------------------------------
static class ExeAgi
{
    static string ExeAd(string yol) => yol.Replace('/', '\\').Split('\\').Last();

    public static KtSonuc Build(SegmentSonuc r, EnvanterSnapshot env, HashSet<string> infra, bool genel)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bags = r.Baglar ?? [];
        var tarali = r.Sunucular.Select(x => x.Ip).ToHashSet();
        bool Genel(string yol) => !genel && SurecAi.GenelExe(ExeAd(yol), yol) && !ExeAd(yol).StartsWith("ntoskrnl", StringComparison.OrdinalIgnoreCase);
        string? Host(string ip) => env.AppsOnIp(ip).Select(h => h.UnixName).OfType<string>().FirstOrDefault();
        string? Ert(string ip, string? port) =>
            env.AppsOnIp(ip).Where(h => port == null || h.Port == port).Select(h => h.AppName).OfType<string>().FirstOrDefault(a => !env.IsSystemApp(a))
            ?? env.AppNamesOnIp(ip).FirstOrDefault(a => !env.IsSystemApp(a));
        string DisAd(string ip, string? port) => Ert(ip, port) ?? (env.IsVipGw(ip, out _) ? "LB GW" : env.FindSegment(ip)?.Label) ?? "bilinmiyor";

        // (sunucu, port) → portu kabul eden exe yolu (en çok bağlantılı; genel olmayan önce)
        var kabul = r.Satirlar.Where(x => !string.IsNullOrEmpty(x.Port) && x.Port != "0")
            .GroupBy(x => (x.Ip, x.Port)).ToDictionary(g => g.Key, g => g.OrderBy(x => Genel(x.Yol)).ThenByDescending(x => x.Baglanti).First().Yol);

        // elemanlar: kutu adı → (eleman id → bilgi)
        var kutu = new Dictionary<string, (string Aciklama, Dictionary<string, (string Ip, string? Port, string Ad, string? Alt, string Tur, HashSet<string> Portlar)> El)>();
        string El(string kume, string aciklama, string id, string ip, string? port, string ad, string? alt, string tur)
        {
            if (!kutu.TryGetValue(kume, out var k)) kutu[kume] = k = (aciklama, new());
            if (!k.El.TryGetValue(id, out var e)) k.El[id] = e = (ip, port, ad, alt, tur, new());
            if (port != null) e.Portlar.Add(port);
            return id;
        }
        var kumeAd = new Dictionary<string, string>();   // küçük harf anahtar → gösterilen ad
        void KumeAd(string key, string ad) => kumeAd.TryAdd(key, ad);
        string ExeEl(string ip, string yol, string? port)
        {
            string exe = ExeAd(yol), kume = exe.ToLowerInvariant().StartsWith("ntoskrnl") ? "HTTP.sys (ntoskrnl.exe)" : exe;
            string? urun = EksikService.BilinenUrun(yol);
            KumeAd(kume.ToLowerInvariant(), kume);
            return El(kume.ToLowerInvariant(), (urun != null ? urun + " · " : "") + yol, $"x:{ip}|{exe.ToLowerInvariant()}", ip, port, Host(ip) ?? ip, yol, "sunucu");
        }
        var bagAgg = new Dictionary<(string, string), (HashSet<string> Portlar, long Hits)>();
        var akis = new Dictionary<(string, string, string), long>();
        var gelen = new Dictionary<string, long>(); var giden = new Dictionary<string, long>();
        void Bag(string from, string to, string port, long n, string srcIp, string dstIp)
        {
            if (from == to) return;
            var k = (from, to);
            var v = bagAgg.TryGetValue(k, out var x) ? x : ([], 0);
            v.Portlar.Add(port); bagAgg[k] = (v.Portlar, v.Hits + n);
            akis[(srcIp, dstIp, port)] = akis.GetValueOrDefault((srcIp, dstIp, port)) + n;
            giden[from] = giden.GetValueOrDefault(from) + n; gelen[to] = gelen.GetValueOrDefault(to) + n;
        }

        // taranan sunucudan gidenler (istemci tarafı exe'si biliniyor)
        var gidenAnahtar = new HashSet<(string, string, string)>();
        var tumGiden = bags.Where(b => b.KaynakYol != null).Select(b => (b.Kaynak, b.Hedef, b.Port)).ToHashSet();   // genel süreçler dahil
        foreach (var b in bags.Where(b => b.KaynakYol != null && !Genel(b.KaynakYol!)))
        {
            string from = ExeEl(b.Kaynak, b.KaynakYol!, null);
            string to;
            if (tarali.Contains(b.Hedef) && kabul.TryGetValue((b.Hedef, b.Port), out var hy))
            {
                if (Genel(hy)) continue;   // hedefte genel süreç (ör. svchost RDP) — çizilmez
                to = ExeEl(b.Hedef, hy, b.Port);
            }
            else
            {
                string ad = DisAd(b.Hedef, b.Port), on = tarali.Contains(b.Hedef) ? "Exe görülmedi · " : "Dış · ";
                to = El("dış · " + on + ad.ToLowerInvariant(), tarali.Contains(b.Hedef) ? "taranan sunucu, bu portu kabul eden exe Carbon Black'te görülmedi" : "taranan segment / etiket dışı", $"d:{b.Hedef}:{b.Port}", b.Hedef, b.Port, $"{b.Hedef}:{b.Port}", ad, "dis");
                KumeAd("dış · " + on + ad.ToLowerInvariant(), on + ad);
            }
            gidenAnahtar.Add((b.Kaynak, b.Hedef, b.Port));
            Bag(from, to, b.Port, b.Baglanti, b.Kaynak, b.Hedef);
        }
        // taranmayan istemcilerden gelenler (ya da taranan ama giden olayı görülmeyen istemciler)
        foreach (var b in bags.Where(b => b.HedefYol != null && !Genel(b.HedefYol!)))
        {
            if (gidenAnahtar.Contains((b.Kaynak, b.Hedef, b.Port))) continue;   // istemci tarafından zaten çizildi
            if (tumGiden.Contains((b.Kaynak, b.Hedef, b.Port))) continue;   // istemci taranmış, giden olayı (genel süreç olsa da) var
            string to = ExeEl(b.Hedef, b.HedefYol!, b.Port);
            string ad = DisAd(b.Kaynak, null), on = tarali.Contains(b.Kaynak) ? "İstemci, exe görülmedi · " : "Dış istemci · ";
            string from = El("dış · " + on + ad.ToLowerInvariant(), tarali.Contains(b.Kaynak) ? "taranan sunucu, bu bağlantının istemci exe'si Carbon Black'te görülmedi" : "bu exe'ye bağlanan, taramada olmayan istemciler",
                $"c:{b.Kaynak}", b.Kaynak, null, Host(b.Kaynak) ?? b.Kaynak, ad, "dis");
            KumeAd("dış · " + on + ad.ToLowerInvariant(), on + ad);
            Bag(from, to, b.Port, b.Baglanti, b.Kaynak, b.Hedef);
        }
        // hiç bağı olmayan iş exe'leri de kutusunda görünsün (trafiksiz karo)
        foreach (var x in r.Satirlar.Where(x => !Genel(x.Yol)))
        {
            ExeEl(x.Ip, x.Yol, string.IsNullOrEmpty(x.Port) || x.Port == "0" ? null : x.Port);
        }

        var kumeler = kutu.Select(kv =>
            {
                bool dis = kv.Key.StartsWith("dış");
                var els = kv.Value.El.Select(e => new KtEleman(e.Key, "sunucu", e.Value.Ip, e.Value.Portlar.Count > 0 ? string.Join(",", e.Value.Portlar.OrderBy(p => int.TryParse(p, out int n) ? n : 99999)) : null,
                        e.Value.Ad, e.Value.Alt, env.FindSegment(e.Value.Ip)?.Label,
                        [new KtUye(e.Value.Ip, Host(e.Value.Ip), e.Value.Tur == "dis" ? e.Value.Alt : e.Value.Alt, null, false, e.Value.Portlar.Count > 0 ? string.Join(",", e.Value.Portlar) : null,
                            gelen.GetValueOrDefault(e.Key), giden.GetValueOrDefault(e.Key))],
                        gelen.GetValueOrDefault(e.Key), giden.GetValueOrDefault(e.Key), [], [],
                        dis ? e.Value.Alt : Ert(e.Value.Ip, e.Value.Portlar.FirstOrDefault())))
                    .OrderBy(e => EnvanterSnapshot.TryToUInt(e.Ip, out uint u) ? u : 0).ToList();
                string ad = kumeAd.GetValueOrDefault(kv.Key, kv.Key);
                return (Dis: dis, Kume: new KtKume(ad, kv.Value.Aciklama, els.Select(e => e.Ip).Distinct().Count(), els));
            })
            .OrderBy(k => k.Dis).ThenByDescending(k => k.Kume.Elemanlar.Sum(e => e.Gelen + e.Giden)).ThenByDescending(k => k.Kume.Elemanlar.Count)
            .Select(k => k.Kume).ToList();
        var baglar = bagAgg.Select(kv => new KtBag(kv.Key.Item1, kv.Key.Item2, kv.Value.Portlar.OrderBy(p => int.TryParse(p, out int n) ? n : 99999).ToList(),
                kv.Value.Hits, 0, kv.Value.Portlar.All(infra.Contains)))
            .OrderByDescending(b => b.Hits).ToList();
        var akislar = akis.OrderByDescending(kv => kv.Value).Take(5000).Select(kv => new KtAkis(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value, 0)).ToList();
        var ok = new SourceStatus(true, null, [$"{bags.Count:N0} ham bağ (Carbon Black, kayıtlı tarama)"], r.Ms, bags.Count);
        return new KtSonuc((r.Tur == "etiket" ? "Etiket " : "") + r.Segment, kumeler, baglar, akislar, tarali.Count, tarali.Count,
            ok, new SourceStatus(false, "Sorgulanmadı", [], null, 0), new SourceStatus(true, null, [], null, 0), sw.ElapsedMilliseconds);
    }
}
