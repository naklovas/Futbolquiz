// ---------------------------------------------------------------------------
// Uygulama topolojisi: ERT'deki bir uygulamanın sunucuları ve VIP'leri üzerinden
// "bu uygulamayı kullananlar", "bu uygulamanın bağımlı oldukları" ve uygulama içi trafik.
// Akışlar sunucu değil uygulama seviyesinde gruplanır (envanterde olmayan IP'ler segmentle).
// ---------------------------------------------------------------------------

record AppServer(string Ip, List<string> Ports, string? Hostname, SegmentInfo? Segment, List<string> Vips)
{
    public long InHits { get; set; }
    public long OutHits { get; set; }
}

record AppVip(string Ip, string? Port, string? Pool, List<VipMember> Members)
{
    public long Hits { get; set; }
}

record AppPeer(string Ip, SegmentInfo? Segment, long Hits, List<string> Ports);

// Dış ilişki grubu: bir uygulama (ya da envanterde olmayan IP'ler için bir segment).
// Targets: kullananlarda uygulamanın hangi uç noktasına (VIP/sunucu IP) geldiği,
// bağımlılıklarda uygulamanın hangi sunucusundan gidildiği -> hit.
record AppLink(string Key, string Name, bool Unknown, bool Infra, SegmentInfo? Segment, long Hits,
    List<string> Ports, List<AppPeer> Peers, Dictionary<string, long> Targets, List<string> ViaVips);

// Uygulama içi: kaynak -> hedef:port. Kind: "direct" ya da "lb" (VIP GW üzerinden havuz üyesine).
record AppInternal(string From, string To, string Port, long Hits, string Kind);

record AppTopologyResponse(string Name, List<string> Owners, List<AppServer> Servers, List<AppVip> Vips,
    List<AppLink> Callers, List<AppLink> Deps, List<AppInternal> Internal, int QueriedIps, int TotalIps,
    SourceStatus Splunk, SourceStatus AppResponse, SourceStatus Envanter, long ElapsedMs);

static class AppTopology
{
    // Varsayılan altyapı portları (DNS, AD/Kerberos/LDAP, NTP, RPC, SMB, SNMP, syslog, izleme, WinRM, RDP, SSH)
    static readonly HashSet<string> DefaultInfraPorts =
        ["22", "53", "88", "123", "135", "137", "138", "139", "161", "162", "389", "445", "464", "514",
         "636", "3268", "3269", "3389", "5985", "5986", "10050", "10051"];

    public static HashSet<string> InfraPorts(IConfiguration cfg)
    {
        var list = cfg.GetSection("Uygulama:AltyapiPortlari").GetChildren().Select(c => c.Value?.Trim()).OfType<string>().ToList();
        return list.Count > 0 ? list.ToHashSet() : DefaultInfraPorts;
    }

    // Uygulamanın uç noktaları: sunucular (ERT IP'leri) ve VIP'ler (ERT VIP_IP + üyesi olduğu LB'ler)
    public static (List<AppServer> servers, List<AppVip> vips) Endpoints(string name, EnvanterSnapshot env)
    {
        var rows = env.AppRows(name);
        var servers = rows.Where(h => h.Ip != null).GroupBy(h => h.Ip!)
            .Select(g => new AppServer(g.Key,
                g.Select(h => h.Port).OfType<string>().Distinct().OrderBy(p => int.TryParse(p, out var n) ? n : int.MaxValue).ToList(),
                g.Select(h => h.UnixName).OfType<string>().FirstOrDefault(),
                env.FindSegment(g.Key),
                env.MemberOf(g.Key).Select(m => m.LbIp).Distinct().ToList()))
            .OrderBy(s => s.Segment?.Label).ThenBy(s => s.Ip, StringComparer.Ordinal)
            .ToList();

        var vipKeys = rows.Where(h => h.VipIp != null).Select(h => (Ip: h.VipIp!, Port: h.VipPort))
            .Concat(servers.SelectMany(s => env.MemberOf(s.Ip)).Select(m => (Ip: m.LbIp, Port: m.LbPort)))
            .Distinct().ToList();
        var vips = vipKeys
            .GroupBy(k => k.Ip)
            // Aynı VIP'in port kaydı varsa portsuz tekrarı at.
            .SelectMany(g => g.Any(k => k.Port != null) ? g.Where(k => k.Port != null) : g)
            .Select(k =>
            {
                var members = env.VipMembers(k.Ip, k.Port);
                return new AppVip(k.Ip, k.Port, members.Select(m => m.Pool).OfType<string>().FirstOrDefault(), members);
            })
            .ToList();
        return (servers, vips);
    }

    public static AppTopologyResponse Build(string name, List<AppServer> servers, List<AppVip> vips, List<string> queried,
        SplunkResult? sp, AppResponseResult? ar, EnvanterSnapshot env, HashSet<string> infraPorts,
        SourceStatus spSt, SourceStatus arSt, SourceStatus envSt, long elapsedMs)
    {
        var appIps = servers.Select(s => s.Ip).Concat(vips.Select(v => v.Ip)).ToHashSet();
        var serverByIp = servers.ToDictionary(s => s.Ip);
        var vipByIp = vips.GroupBy(v => v.Ip).ToDictionary(g => g.Key, g => g.ToList());
        var queriedSet = queried.ToHashSet();

        var callers = new Dictionary<string, LinkAgg>();
        var deps = new Dictionary<string, LinkAgg>();
        var internals = new Dictionary<(string, string, string, string), long>();

        foreach (var x in queried)
        {
            var (inbound, outbound) = FlowBuilder.Build(x, sp, ar, env);
            bool isServer = serverByIp.TryGetValue(x, out var srv);

            foreach (var e in inbound)
            {
                if (isServer) srv!.InHits += e.Hits;
                else if (vipByIp.TryGetValue(x, out var vl)) vl.ForEach(v => { if (v.Port == null || v.Port == e.Port) v.Hits += e.Hits; });

                if (appIps.Contains(e.PeerIp))
                {
                    AddInternal(internals, e.PeerIp, x, e.Port, "direct", e.Hits);
                }
                else if (isServer && env.IsVipGw(e.PeerIp, out _))
                {
                    // LB, havuz üyesine GW IP'sinden gelir: VIP -> üye (uygulama içi)
                    var viaVip = env.MemberOf(x).Where(m => m.Port == null || m.Port == e.Port).Select(m => m.LbIp).FirstOrDefault();
                    AddInternal(internals, viaVip ?? $"LB GW {e.PeerIp}", x, e.Port, "lb", e.Hits);
                }
                else
                {
                    var (key, label, unknown) = PeerKey(e.PeerIp, env.AppNamesOnIp(e.PeerIp), e.PeerSegment);
                    bool infra = infraPorts.Contains(e.Port) || (e.LocalApp?.Apps.All(a => env.IsSystemApp(a.AppName)) == true && e.LocalApp.Apps.Count > 0);
                    Get(callers, key, label, unknown, e.PeerSegment).Add(e, x, infra, null);
                }
            }

            if (!isServer) continue;
            foreach (var e in outbound)
            {
                srv!.OutHits += e.Hits;
                if (appIps.Contains(e.PeerIp) || queriedSet.Contains(e.PeerIp)) continue; // iç trafik hedefin gelen tarafında sayılır

                List<string> names;
                string? viaVip = null;
                if (e.PeerVip is { Count: > 0 })
                {
                    viaVip = e.PeerIp;
                    names = e.PeerVip.SelectMany(m => env.AppNamesOnIp(m.Ip)).Distinct().Order().ToList();
                }
                else
                {
                    names = (e.PeerApps?.Apps ?? []).Select(a => a.AppName).OfType<string>()
                        .Where(n => !env.IsSystemApp(n)).Distinct().Order().ToList();
                }
                var (key, label, unknown) = PeerKey(e.PeerIp, names, e.PeerSegment);
                bool infra = infraPorts.Contains(e.Port) ||
                             (names.Count == 0 && e.PeerApps?.Apps.Any(a => env.IsSystemApp(a.AppName)) == true);
                Get(deps, key, label, unknown, e.PeerSegment).Add(e, x, infra, viaVip);
            }
        }

        var owners = env.AppRows(name).SelectMany(h => new[] { h.VarlikMuhafizi, h.UygulamaVarlikSahibi, h.SunucuIsletenBirim })
            .OfType<string>().Distinct().ToList();

        return new AppTopologyResponse(
            env.AppRows(name).FirstOrDefault()?.AppName ?? name, owners, servers, vips,
            callers.Values.Select(a => a.ToLink()).OrderByDescending(l => l.Hits).ToList(),
            deps.Values.Select(a => a.ToLink()).OrderByDescending(l => l.Hits).ToList(),
            internals.Select(kv => new AppInternal(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value, kv.Key.Item4))
                .OrderByDescending(i => i.Hits).ToList(),
            queried.Count, appIps.Count, spSt, arSt, envSt, elapsedMs);
    }

    // Karşı IP'nin grubu: envanterdeki uygulama adları; yoksa segmenti.
    static (string key, string label, bool unknown) PeerKey(string ip, List<string> names, SegmentInfo? seg)
    {
        if (names.Count > 0)
        {
            string label = string.Join(" + ", names.Take(3)) + (names.Count > 3 ? $" +{names.Count - 3}" : "");
            return ("app:" + label.ToLowerInvariant(), label, false);
        }
        return ("seg:" + (seg?.Label ?? "?"), seg?.Label ?? "Segment envanterinde yok", true);
    }

    static void AddInternal(Dictionary<(string, string, string, string), long> d, string from, string to, string port, string kind, long hits)
    {
        var k = (from, to, port, kind);
        d[k] = d.GetValueOrDefault(k) + hits;
    }

    static LinkAgg Get(Dictionary<string, LinkAgg> d, string key, string label, bool unknown, SegmentInfo? seg)
    {
        if (!d.TryGetValue(key, out var a)) d[key] = a = new LinkAgg(key, label, unknown, seg);
        return a;
    }

    sealed class LinkAgg(string key, string name, bool unknown, SegmentInfo? seg)
    {
        long _hits, _infraHits;
        readonly HashSet<string> _ports = new();
        readonly HashSet<string> _vias = new();
        readonly Dictionary<string, (SegmentInfo? seg, long hits, HashSet<string> ports)> _peers = new();
        readonly Dictionary<string, long> _targets = new();

        public void Add(FlowEdge e, string ownEndpoint, bool infra, string? viaVip)
        {
            _hits += e.Hits;
            if (infra) _infraHits += e.Hits;
            if (e.Port != "") _ports.Add(e.Port);
            if (viaVip != null) _vias.Add(viaVip);
            if (!_peers.TryGetValue(e.PeerIp, out var p)) p = (e.PeerSegment, 0L, new HashSet<string>());
            if (e.Port != "") p.ports.Add(e.Port);
            _peers[e.PeerIp] = (p.seg, p.hits + e.Hits, p.ports);
            _targets[ownEndpoint] = _targets.GetValueOrDefault(ownEndpoint) + e.Hits;
        }

        // Grup, trafiğinin tamamı altyapı portlarındaysa "altyapı" sayılır.
        public AppLink ToLink() => new(key, name, unknown, _hits > 0 && _infraHits == _hits, seg, _hits,
            _ports.OrderBy(p => int.TryParse(p, out var n) ? n : int.MaxValue).ToList(),
            _peers.Select(kv => new AppPeer(kv.Key, kv.Value.seg, kv.Value.hits,
                kv.Value.ports.OrderBy(p => int.TryParse(p, out var n) ? n : int.MaxValue).ToList()))
                .OrderByDescending(p => p.Hits).ToList(),
            _targets, _vias.ToList());
    }
}
