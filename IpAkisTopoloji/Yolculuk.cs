// ---------------------------------------------------------------------------
// Uçtan uca yolculuk: sorgulanan sunucudan geriye (dış IP'lere) ve ileriye (DB'ye) durak durak oturum eşleştirmesi.
// Her durakta Oturum.Match kullanılır (gelen kısa oturumun penceresinde başlayan giden bağlantılar):
//  - İleri: X'e ÖNCEKİ duraktan gelen oturumlar içinde X'in gittiği yerler → sonraki durak.
//    Hedef VIP ise: VIP düğümü, sonra havuz üyeleri; üyelerde yalnızca LB GW'den gelen oturumlar sayılır.
//  - Geri: X'in SONRAKİ durağa giden oturumları hangi kaynaklardan geldi → önceki durak.
//    Kaynak LB GW ise: sunucunun VIP'i, sonra VIP'e gelenler (Firewall NAT'ı çözülmüş hedefle).
//  - Özel (10/8, 172.16/12, 192.168/16) olmayan IP "dış" sayılır, orada durulur.
//  - Oran, hedefe giden oturumların "bir yere giden" oturumlara oranıdır: health check / izleme gibi hiçbir yere
//    gitmeyen kısa oturumlar paydayı şişirmez. Hedefler önce bu orana, sonra ağırlığa göre seçilir.
//  - VIP durak sayısını (Ileri) tüketmez: kök → VIP → üye tek durak sayılır.
//  - Bağlantı havuzu: DB'ye uygulamalar çoğunlukla önceden açık (havuzdaki) bağlantılarla gider; bu bağlantılar
//    istek anında açılmadığı için zaman eşleştirmesine girmez. Bu yüzden ileri yönde her sunucunun DB portlarına
//    (Yolculuk:HavuzPortlari) giden bağlantıları ayrıca "havuz" bağı (kesik çizgi) olarak eklenir.
// Veri her seviye için tek seferde çekilir (o seviyedeki tüm sunucular için tek AppResponse raporu + tek Firewall sorgusu).
// ---------------------------------------------------------------------------
record JNode(string Id, int Level, string Ip, string? Port, string Kind, SegmentInfo? Segment, List<string> Apps);
record JEdge(string From, string To, double W, int Exact, double? Oran, bool Havuz = false);
record YolculukSonuc(string Ip, string Pencere, string Kaynaklar, List<JNode> Nodes, List<JEdge> Edges, List<string> Mesajlar, long SureMs);

static class Yolculuk
{
    sealed record Item(string Id, string Ip, string Kind, int Level, HashSet<string>? Allowed, HashSet<string>? Next, string? ParentVip, string? Port, int Hops = 0);

    public static bool IsPrivate(string ip)
    {
        var p = ip.Split('.');
        if (p.Length != 4 || !int.TryParse(p[0], out int a) || !int.TryParse(p[1], out int b)) return true;
        return a == 10 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) || a == 127;
    }

    public static async Task<YolculukSonuc> RunAsync(LookupQuery q, int appliance, bool useAr, bool useFw, IConfiguration cfg,
        HttpClient arHttp, HttpClient spHttp, EnvanterSnapshot? env, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int up = cfg.GetValue("Yolculuk:Geri", 3), down = cfg.GetValue("Yolculuk:Ileri", 3), K = cfg.GetValue("Yolculuk:Dallanma", 6);
        double maxSec = cfg.GetValue("Oturum:MaxOturumSaniye", 30.0);
        var poolPorts = cfg.GetSection("Yolculuk:HavuzPortlari").GetChildren().Select(c => c.Value ?? "").Where(v => v != "").ToHashSet();
        if (poolPorts.Count == 0) poolPorts = ["1521", "1522", "1526", "2484", "1433", "1434", "5432", "3306", "50000", "27017", "9042"];
        var msgs = new List<string>();
        var sources = new List<string>();
        useFw = useFw && FirewallService.Enabled(cfg);

        // ---- veri: seviye başına yeni IP'ler için tek çekim, bağlantılar tekilleştirilir
        var fetched = new HashSet<string>();
        var flows = new List<FlowRec>();
        var seen = new HashSet<(string, string, string, string)>();
        int arCount = 0, fwCount = 0;
        async Task Ensure(IEnumerable<string> ips)
        {
            var need = ips.Where(fetched.Add).ToList();
            if (need.Count == 0) return;
            var arT = useAr ? OturumAkisi.FetchArAsync(q, need, appliance, true, cfg, arHttp, msgs, ct) : Task.FromResult<(string, List<FlowRec>)?>(null);
            var fwT = useFw ? OturumAkisi.FetchFwAsync(q, need, cfg, spHttp, msgs, ct) : Task.FromResult<List<FlowRec>?>(null);
            await Task.WhenAll(arT, fwT);
            foreach (var f in arT.Result?.Item2 ?? []) if (seen.Add((f.Cip, f.Cport, f.Sip, f.Sport))) { flows.Add(f); arCount++; }
            foreach (var f in fwT.Result ?? []) if (f.Cport != "" && seen.Add((f.Cip, f.Cport, f.Sip, f.Sport))) { flows.Add(f); fwCount++; }
        }

        var nodes = new Dictionary<string, JNode>();
        var edges = new Dictionary<(string, string), JEdge>();
        static string NodeId(int level, string ip, string kind, string? port) => $"{level}|{ip}|{(kind == "vip" ? port : "")}";
        string Node(int level, string ip, string kind, string? port)
        {
            string id = NodeId(level, ip, kind, port);
            if (!nodes.ContainsKey(id))
            {
                var apps = env == null ? []
                    : kind == "vip" ? env.VipMembers(ip, port).SelectMany(m => env.AppNamesOnIp(m.Ip)).Distinct().Order().ToList()
                    : env.AppNamesOnIp(ip);
                nodes[id] = new JNode(id, level, ip, port, kind, env?.FindSegment(ip), apps);
            }
            return id;
        }
        void Edge(string from, string to, double w, int exact, double? oran, bool havuz = false)
        {
            var e = edges.GetValueOrDefault((from, to));
            edges[(from, to)] = e == null ? new JEdge(from, to, w, exact, oran, havuz)
                : e with { W = e.W + w, Exact = e.Exact + exact, Oran = e.Oran == null || oran == null ? e.Oran ?? oran : Math.Min(1, e.Oran.Value + oran.Value) };
        }
        string Kind(string ip) => !IsPrivate(ip) ? "dis" : "host";

        string target = q.Ip;
        await Ensure([target]);
        string root = Node(0, target, "host", null);

        // ---- ileri (DB yönü)
        // VIP bir sunucu değil, ara durak: VIP'ten sonraki üyeler derinlik sınırına takılsa da işlenir (VIP → üye bağı için).
        var level = new List<Item> { new(root, target, "host", 0, null, null, null, null) };
        for (int guard = 0; guard < 12 && level.Count > 0; guard++)
        {
            await Ensure(level.Where(i => i.Kind == "host").Select(i => i.Ip));
            var next = new List<Item>();
            foreach (var it in level)
            {
                if (it.Kind == "vip")
                {
                    // VIP → havuz üyeleri: üyenin kendi işlemesinde LB GW'den gelen oturumlarla bağlanır
                    foreach (var m in env?.VipMembers(it.Ip, it.Port).Take(K) ?? [])
                        next.Add(new(NodeId(it.Level + 1, m.Ip, "host", null), m.Ip, "host", it.Level + 1, m.GwIps.ToHashSet(), null, it.Id, m.Port, it.Hops + 1));
                    continue;
                }
                var mr = OturumAkisi.Match(it.Ip, flows, maxSec);
                var considered = Enumerable.Range(0, mr.ShortIn.Count)
                    .Where(i => (it.Allowed == null || it.Allowed.Contains(mr.ShortIn[i].Cip)) && (it.Port == null || mr.ShortIn[i].Sport == it.Port))
                    .ToList();
                if (it.ParentVip != null)
                {
                    if (considered.Count == 0) continue;
                    Node(it.Level, it.Ip, "host", null);
                    Edge(it.ParentVip, it.Id, considered.Count, 0, null);
                }
                if (considered.Count == 0 || it.Hops >= down) continue;
                // Pay: bu oturumlardan en az bir yere gidenler (health check / izleme oturumları oranı düşürmesin)
                int active = considered.Count(mr.PerSession.ContainsKey);
                var targets = considered.Where(mr.PerSession.ContainsKey)
                    .SelectMany(i => mr.PerSession[i].Select(kv => (kv.Key, kv.Value, i)))
                    .GroupBy(x => x.Key)
                    .Select(g => (key: g.Key, w: g.Sum(x => x.Value.w), exact: g.Sum(x => x.Value.exact), cov: (double)g.Select(x => x.i).Distinct().Count() / Math.Max(1, active)))
                    .Where(t => t.cov >= 0.05)
                    .OrderByDescending(t => t.cov).ThenByDescending(t => t.w).Take(K).ToList();
                foreach (var t in targets)
                {
                    bool isVip = env != null && env.VipMembers(t.key.ip, null).Count > 0;
                    string kind = isVip ? "vip" : Kind(t.key.ip);
                    string id = Node(it.Level + 1, t.key.ip, kind, kind == "vip" ? t.key.port : null);
                    Edge(it.Id, id, Math.Round(t.w, 1), t.exact, Math.Round(t.cov, 3));
                    if (kind == "vip" || (kind == "host" && it.Hops + 1 < down))
                        next.Add(new(id, t.key.ip, kind, it.Level + 1, kind == "vip" ? null : [it.Ip], null, null, kind == "vip" ? t.key.port : null,
                            kind == "vip" ? it.Hops : it.Hops + 1));
                }
                // Havuz bağlantıları: DB portlarına giden, oturum eşleşmesine girmemiş hedefler (uç durak, devam edilmez)
                var matched = targets.Select(t => t.key).ToHashSet();
                foreach (var g in mr.Outbound.Where(o => poolPorts.Contains(o.Sport) && !matched.Contains((o.Sip, o.Sport)))
                    .GroupBy(o => (o.Sip, o.Sport)).OrderByDescending(g => g.Count()).Take(K))
                {
                    bool isVip = env != null && env.VipMembers(g.Key.Sip, null).Count > 0;
                    string kind = isVip ? "vip" : Kind(g.Key.Sip);
                    Edge(it.Id, Node(it.Level + 1, g.Key.Sip, kind, kind == "vip" ? g.Key.Sport : null), g.Count(), 0, null, true);
                }
            }
            level = next;
        }

        // ---- geri (dış IP yönü)
        level = [new(root, target, "host", 0, null, null, null, null)];
        for (int guard = 0; guard < 12 && level.Count > 0; guard++)
        {
            await Ensure(level.Select(i => i.Ip));
            var next = new List<Item>();
            foreach (var it in level)
            {
                // Kaynak → sayı: X'e gelen (ve gerekiyorsa sonraki durağa giden) oturumların kaynakları
                Dictionary<string, (int n, string port)> src = [];
                int total;
                if (it.Kind == "vip")
                {
                    var inVip = flows.Where(f => f.Sip == it.Ip && (it.Port == null || f.Sport == it.Port) && f.Cip != it.Ip).ToList();
                    total = inVip.Count;
                    foreach (var g in inVip.GroupBy(f => f.Cip)) src[g.Key] = (g.Count(), it.Port ?? "");
                }
                else
                {
                    var mr = OturumAkisi.Match(it.Ip, flows, maxSec);
                    var idx = Enumerable.Range(0, mr.ShortIn.Count)
                        .Where(i => it.Next == null || (mr.PerSession.TryGetValue(i, out var ps) && ps.Keys.Any(k => it.Next.Contains(k.ip))))
                        .ToList();
                    // Sorgulanan sunucunun kendisinde uzun (keep-alive) gelenler de kaynak sayılır
                    var srcFlows = it.Next == null
                        ? flows.Where(f => f.Sip == it.Ip && f.Cip != it.Ip && f.E != null).ToList()
                        : idx.Select(i => mr.ShortIn[i]).ToList();
                    total = srcFlows.Count;
                    foreach (var g in srcFlows.GroupBy(f => f.Cip)) src[g.Key] = (g.Count(), g.GroupBy(f => f.Sport).OrderByDescending(x => x.Count()).First().Key);
                }
                if (total == 0) continue;
                foreach (var (sip, (n, port)) in src.OrderByDescending(x => x.Value.n).Take(K))
                {
                    double oran = Math.Round((double)n / total, 3);
                    if (env != null && it.Kind == "host" && env.IsVipGw(sip, out _))
                    {
                        // LB GW'den geliyorsa: bu sunucunun bu porttaki VIP'i
                        var vips = env.MemberOf(it.Ip).Where(m => m.Port == null || m.Port == port).ToList();
                        if (vips.Count == 0) { Edge(Node(it.Level - 1, sip, "host", null), it.Id, n, 0, oran); continue; }
                        foreach (var v in vips.DistinctBy(v => (v.LbIp, v.LbPort)))
                        {
                            string vid = Node(it.Level - 1, v.LbIp, "vip", v.LbPort);
                            Edge(vid, it.Id, n, 0, oran);
                            next.Add(new(vid, v.LbIp, "vip", it.Level - 1, null, null, null, v.LbPort));
                        }
                        continue;
                    }
                    string kind = Kind(sip);
                    string id = Node(it.Level - 1, sip, kind, null);
                    Edge(id, it.Id, n, 0, oran);
                    if (kind == "host" && it.Level - 1 > -up) next.Add(new(id, sip, "host", it.Level - 1, null, [it.Ip], null, null));
                }
            }
            level = next.DistinctBy(i => i.Id).ToList();
        }

        if (useAr) sources.Add($"AppResponse {arCount:N0}");
        if (useFw) sources.Add($"Firewall {fwCount:N0}");
        return new YolculukSonuc(target, $"{q.Start:HH:mm}-{q.End:HH:mm}", string.Join(" + ", sources) + " bağlantı",
            [.. nodes.Values], [.. edges.Values.Where(e => nodes.ContainsKey(e.From) && nodes.ContainsKey(e.To))], msgs, sw.ElapsedMilliseconds);
    }
}
