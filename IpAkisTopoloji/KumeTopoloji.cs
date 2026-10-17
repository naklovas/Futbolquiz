// ---------------------------------------------------------------------------
// Küme topolojisi: işlev haritasındaki kümeler kutu, içlerinde VIP'ler ve VIP'e bağlı olmayan sunucular eleman olarak çizilir.
// Etiketli sunucular + VIP'leri için trafik (Carbon Black / AppResponse / Firewall) çekilir; iki ucu da etiketli eleman olan
// akışlar eleman → eleman bağına toplanır. Havuz üyesi sunucunun trafiği VIP'inin elemanına yazılır; LB GW → üye (VIP'in kendi
// tesisatı) ve aynı eleman içi akışlar atlanır. Etiket dışına giden / dışarıdan gelen trafik eleman başına özetlenir (popup).
// ---------------------------------------------------------------------------
record KtUye(string Ip, string? Hostname, string? Rol, int? Guven, bool Elle, string? Port, long Gelen, long Giden);
record KtDis(string Ad, long Hits, List<string> Portlar);
record KtEleman(string Id, string Tur, string Ip, string? Port, string Ad, string? Alt, string? Segment, List<KtUye> Uyeler,
    long Gelen, long Giden, List<KtDis> DisGelen, List<KtDis> DisGiden);
record KtKume(string Ad, string? Aciklama, int Sunucu, List<KtEleman> Elemanlar, string? Tenant = null);
record KtBag(string From, string To, List<string> Portlar, long Hits, long Engel, bool Altyapi);
record KtAkis(string Src, string Dst, string Port, long Hits, long Engel);
record KtSonuc(string Etiket, List<KtKume> Kumeler, List<KtBag> Baglar, List<KtAkis> Akislar, int Sorgulanan, int Toplam,
    SourceStatus Splunk, SourceStatus AppResponse, SourceStatus Envanter, long ElapsedMs)
{
    public SourceStatus? Firewall { get; init; }
    public bool Coklu { get; init; }              // birden çok kurum: kümeler kurum başına ayrı kutu, arayüz kurum çerçevesiyle gruplar
    public List<string> Tenantlar { get; init; } = [];
}

static class KumeTopoloji
{
    // Sorgulanacak IP'ler: önce VIP'ler, sonra sunucular
    public static List<string> Queried(IslevHarita map, int max) =>
        map.Kumeler.SelectMany(k => k.Segmentler).SelectMany(s => s.Vipler).Select(v => v.Vip.Split(':')[0])
            .Concat(map.Kumeler.SelectMany(k => k.Segmentler).SelectMany(s => s.Sunucular.Concat(s.Vipler.SelectMany(v => v.Sunucular))).Select(x => x.Ip))
            .Where(x => LookupQuery.TryParseIpv4(x, out _)).Distinct().Take(max).ToList();

    public static KtSonuc Build(IslevHarita map, List<string> queried, SplunkResult? sp, AppResponseResult? ar, SplunkResult? fw,
        EnvanterSnapshot env, HashSet<string> infraPorts, SourceStatus spSt, SourceStatus arSt, SourceStatus envSt, long elapsedMs)
    {
        // --- elemanlar ---
        // Kutu = küme; birden çok kurum gösteriliyorsa küme × kurum (aynı küme her kurumda ayrı kutu).
        var groups = map.Kumeler.SelectMany(k => map.Coklu
                ? k.Segmentler.GroupBy(s => s.Tenant).Select(g => (Id: k.Ad + "\u0001" + g.Key, k.Ad, Tenant: g.Key, k.Aciklama, Segs: g.ToList()))
                : [(Id: k.Ad, k.Ad, Tenant: (string?)null, k.Aciklama, Segs: k.Segmentler)])
            .ToList();
        // VIP birden çok kutuda üyeye sahipse en çok üyesinin olduğu kutuya konur.
        var vipKume = groups.SelectMany(k => k.Segs.SelectMany(s => s.Vipler).Select(v => (v.Vip, k.Id, n: v.Sunucular.Count)))
            .GroupBy(x => x.Vip).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.n).First().Id);
        var vipInfo = groups.SelectMany(k => k.Segs.SelectMany(s => s.Vipler.Select(v => (v, seg: s.Ad))))
            .GroupBy(x => x.v.Vip).ToDictionary(g => g.Key, g => (Vip: g.First().v, Seg: g.First().seg,
                Uyeler: g.SelectMany(x => x.v.Sunucular).GroupBy(s => s.Ip).Select(s => s.First()).ToList()));
        var srvInfo = groups.SelectMany(k => k.Segs.SelectMany(s => s.Sunucular.Concat(s.Vipler.SelectMany(v => v.Sunucular))
                .Select(x => (x, kume: k.Id, seg: s.Ad))))
            .GroupBy(t => t.x.Ip).ToDictionary(g => g.Key, g => g.First());

        // Sunucu → elemanı: üyesi olduğu (haritadaki) ilk VIP, yoksa kendisi
        var srvElem = new Dictionary<string, string>();
        foreach (var (key, info) in vipInfo)
            foreach (var u in info.Uyeler) srvElem.TryAdd(u.Ip, "v:" + key);
        foreach (var ip in srvInfo.Keys) srvElem.TryAdd(ip, "s:" + ip);
        var vipByIp = vipInfo.Keys.GroupBy(k => k.Split(':')[0]).ToDictionary(g => g.Key, g => g.ToList());

        string? ElemOf(string ip, string? port)
        {
            if (vipByIp.TryGetValue(ip, out var keys))
                return "v:" + (keys.FirstOrDefault(k => port != null && k.EndsWith(":" + port)) ?? keys[0]);
            return srvElem.GetValueOrDefault(ip);
        }

        // --- akışlar ---
        var queriedSet = queried.ToHashSet();
        var akis = new Dictionary<(string, string, string), (long hits, long deny)>();
        var gelen = new Dictionary<string, long>();
        var giden = new Dictionary<string, long>();
        var disGelen = new Dictionary<string, Dictionary<string, (long, HashSet<string>)>>();
        var disGiden = new Dictionary<string, Dictionary<string, (long, HashSet<string>)>>();

        void Dis(Dictionary<string, Dictionary<string, (long, HashSet<string>)>> d, string elem, string ad, string port, long hits)
        {
            if (!d.TryGetValue(elem, out var m)) d[elem] = m = new();
            var (h, ps) = m.TryGetValue(ad, out var v) ? v : (0, new HashSet<string>());
            if (port != "") ps.Add(port);
            m[ad] = (h + hits, ps);
        }
        string PeerName(FlowEdge e) =>
            e.PeerVip is { Count: > 0 } pv ? "VIP " + e.PeerIp + " · " + (pv.SelectMany(m => env.AppNamesOnIp(m.Ip)).FirstOrDefault() ?? "")
            : env.AppNamesOnIp(e.PeerIp).FirstOrDefault() is { } a ? a
            : e.PeerSegment?.Label ?? e.PeerIp;

        foreach (var x in queried)
        {
            var (inbound, outbound) = FlowBuilder.Build(x, sp, ar, env, fw);
            inbound = VipResolver.Annotate(inbound, env);
            outbound = VipResolver.Annotate(outbound, env);
            foreach (var e in inbound)
            {
                string? to = ElemOf(x, e.Port);
                if (to == null) continue;
                if (env.IsVipGw(e.PeerIp, out _)) continue;   // LB → havuz üyesi: VIP'in kendi tesisatı
                string? from = vipByIp.ContainsKey(e.PeerIp) ? null : srvElem.GetValueOrDefault(e.PeerIp);
                if (from == null)
                {
                    Dis(disGelen, to, PeerName(e), e.Port, e.Hits);
                    gelen[to] = gelen.GetValueOrDefault(to) + e.Hits;
                    continue;
                }
                var k = (e.PeerIp, x, e.Port);
                var cur = akis.GetValueOrDefault(k);
                akis[k] = (cur.hits + e.Hits, cur.deny + e.FirewallDenied);
            }
            if (!srvElem.TryGetValue(x, out var fromEl)) continue;   // VIP IP'sinin kendi gideni olmaz
            foreach (var e in outbound)
            {
                string? to = ElemOf(e.PeerIp, e.Port);
                if (to != null)
                {
                    // Karşı uç da sorgulandıysa akış onun gelen tarafında zaten sayıldı
                    if (queriedSet.Contains(e.PeerIp)) continue;
                    var k = (x, e.PeerIp, e.Port);
                    var cur = akis.GetValueOrDefault(k);
                    akis[k] = (cur.hits + e.Hits, cur.deny + e.FirewallDenied);
                    continue;
                }
                Dis(disGiden, fromEl, PeerName(e), e.Port, e.Hits);
                giden[fromEl] = giden.GetValueOrDefault(fromEl) + e.Hits;
            }
        }

        // IP düzeyindeki akışlardan eleman bağları ve üye sayaçları
        var uyeGelen = new Dictionary<string, long>();
        var uyeGiden = new Dictionary<string, long>();
        var bag = new Dictionary<(string, string), (HashSet<string> ports, long hits, long deny, bool allInfra)>();
        foreach (var ((src, dst, port), (hits, deny)) in akis)
        {
            string? f = vipByIp.ContainsKey(src) ? null : srvElem.GetValueOrDefault(src), t = ElemOf(dst, port);
            if (f == null || t == null) continue;
            uyeGiden[src] = uyeGiden.GetValueOrDefault(src) + hits;
            uyeGelen[dst] = uyeGelen.GetValueOrDefault(dst) + hits;
            giden[f] = giden.GetValueOrDefault(f) + hits;
            gelen[t] = gelen.GetValueOrDefault(t) + hits;
            if (f == t) continue;   // aynı eleman içi (aynı VIP'in üyeleri arası)
            bool infra = infraPorts.Contains(port);
            var cur = bag.TryGetValue((f, t), out var v) ? v : (new HashSet<string>(), 0, 0, true);
            if (port != "") cur.ports.Add(port);
            bag[(f, t)] = (cur.ports, cur.hits + hits, cur.deny + deny, cur.allInfra && infra);
        }

        List<KtDis> Top(Dictionary<string, Dictionary<string, (long, HashSet<string>)>> d, string elem) =>
            d.TryGetValue(elem, out var m)
                ? m.OrderByDescending(kv => kv.Value.Item1).Take(8).Select(kv => new KtDis(kv.Key, kv.Value.Item1, kv.Value.Item2.Take(6).ToList())).ToList()
                : [];
        KtUye Uye(IslevSunucu s, string? port) => new(s.Ip, s.Hostname, s.Rol, s.Guven, s.Elle, port,
            uyeGelen.GetValueOrDefault(s.Ip), uyeGiden.GetValueOrDefault(s.Ip));

        var kumeler = groups.Select(k =>
        {
            var els = new List<KtEleman>();
            foreach (var (key, info) in vipInfo.Where(v => vipKume[v.Key] == k.Id))
            {
                string id = "v:" + key;
                var parts = key.Split(':');
                var members = env.VipMembers(parts[0], parts.Length > 1 ? parts[1] : null);
                els.Add(new KtEleman(id, "vip", parts[0], parts.Length > 1 ? parts[1] : null,
                    info.Vip.App ?? info.Vip.Pool ?? key, info.Vip.Pool, info.Seg,
                    info.Uyeler.Select(u => Uye(u, members.FirstOrDefault(m => m.Ip == u.Ip)?.Port)).ToList(),
                    gelen.GetValueOrDefault(id), giden.GetValueOrDefault(id), Top(disGelen, id), Top(disGiden, id)));
            }
            foreach (var (ip, info) in srvInfo.Where(s => s.Value.kume == k.Id && srvElem[s.Key] == "s:" + s.Key))
            {
                string id = "s:" + ip;
                els.Add(new KtEleman(id, "sunucu", ip, null, info.x.Hostname ?? ip, info.x.Rol, info.seg, [Uye(info.x, null)],
                    gelen.GetValueOrDefault(id), giden.GetValueOrDefault(id), Top(disGelen, id), Top(disGiden, id)));
            }
            int n = k.Segs.SelectMany(s => s.Sunucular.Concat(s.Vipler.SelectMany(v => v.Sunucular))).Select(x => x.Ip).Distinct().Count();
            return new KtKume(k.Ad, k.Aciklama, n,
                els.OrderBy(e => e.Tur == "vip" ? 0 : 1).ThenBy(e => e.Segment).ThenByDescending(e => e.Gelen + e.Giden).ToList(), k.Tenant);
        }).Where(k => k.Elemanlar.Count > 0).ToList();

        var baglar = bag.Select(kv => new KtBag(kv.Key.Item1, kv.Key.Item2,
                kv.Value.ports.OrderBy(p => int.TryParse(p, out var n) ? n : int.MaxValue).ToList(), kv.Value.hits, kv.Value.deny, kv.Value.allInfra))
            .OrderByDescending(b => b.Hits).ToList();
        var akislar = akis.Where(kv => kv.Value.hits > 0).OrderByDescending(kv => kv.Value.hits).Take(5000)
            .Select(kv => new KtAkis(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value.hits, kv.Value.deny)).ToList();
        return new KtSonuc(map.Etiket, kumeler, baglar, akislar, queried.Count, map.Toplam, spSt, arSt, envSt, elapsedMs)
            { Coklu = map.Coklu, Tenantlar = map.Tenantlar ?? [] };
    }
}
