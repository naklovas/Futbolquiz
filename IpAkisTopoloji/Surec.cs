// ---------------------------------------------------------------------------
// Sunucu içi akış (gelen → süreç → giden): Carbon Black her bağlantıyı onu açan / kabul eden süreçle
// kaydeder. Sorgulanan sunucunun kendi ajanının olayları (local_ip = IP) süreç bazında gruplanınca
// "şu porttan gelen istekleri hangi süreç karşılıyor, o süreç nereye gidiyor" görülür.
// Bu istek bazında değil süreç bazında bir bağdır: aynı süreçten geçen gelen ve giden trafik birlikte gösterilir.
// ---------------------------------------------------------------------------
record ProcPeer(string Ip, string Port, long Hits, SegmentInfo? Segment, List<string> Apps, bool Vip, bool LbGw);

record ProcNode(string Key, string Name, string? Pid, long InHits, long OutHits, List<string> ListenPorts, List<string> LocalApps,
    List<ProcPeer> Inbound, List<ProcPeer> Outbound);

record ProcessFlowResponse(string Ip, List<ProcNode> Processes, SourceStatus Splunk, SourceStatus Envanter, long ElapsedMs);

static class ProcessFlowBuilder
{
    const int MaxPeersPerSide = 40;

    // Satırlar: process, pid, yon (in/out), peer, port, count
    public static List<ProcNode> Build(string target, SplunkResult sp, EnvanterSnapshot? env)
    {
        static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? EnvanterSnapshot.Clean(v) : null;

        var rows = sp.Rows
            .Select(r => (proc: V(r, "process") ?? "bilinmeyen süreç", pid: V(r, "pid"), dir: V(r, "yon"),
                peer: V(r, "peer"), port: V(r, "port") ?? "", hits: long.TryParse(V(r, "count"), out long c) ? c : 1))
            .Where(r => r.dir is "in" or "out" && r.peer != null && r.peer != target && r.peer != "127.0.0.1")
            .ToList();

        // Süreç kopyası (PID) ayrı düğüm: IIS'te her uygulama havuzu ayrı w3wp.exe'dir, karışmasınlar.
        return rows.GroupBy(r => (proc: r.proc.ToLowerInvariant(), pid: r.pid is null or "-" ? null : r.pid))
            .Select(g =>
            {
                List<ProcPeer> Peers(string dir) => g.Where(r => r.dir == dir)
                    .GroupBy(r => (r.peer, r.port))
                    .Select(x => Peer(target, x.Key.peer!, x.Key.port, x.Sum(r => r.hits), dir, env))
                    .OrderByDescending(p => p.Hits).Take(MaxPeersPerSide).ToList();

                var inbound = Peers("in");
                var listen = g.Where(r => r.dir == "in").Select(r => r.port).Where(p => p != "").Distinct()
                    .OrderBy(p => int.TryParse(p, out var n) ? n : int.MaxValue).ToList();
                var localApps = env == null ? [] : listen
                    .SelectMany(p => env.Match(target, p).Apps.Where(a => a.Port == p).Select(a => a.AppName))
                    .OfType<string>().Distinct().ToList();

                return new ProcNode(g.Key.proc + "|" + g.Key.pid, g.First().proc, g.Key.pid,
                    g.Where(r => r.dir == "in").Sum(r => r.hits), g.Where(r => r.dir == "out").Sum(r => r.hits),
                    listen, localApps, inbound, Peers("out"));
            })
            .OrderByDescending(p => p.InHits + p.OutHits)
            .ToList();
    }

    public static ProcPeer Peer(string target, string ip, string port, long hits, string dir, EnvanterSnapshot? env)
    {
        if (env == null) return new ProcPeer(ip, port, hits, null, [], false, false);
        bool lbGw = env.IsVipGw(ip, out _);
        // Load balancer GW'den gelen: istemciler bu porta hangi VIP üzerinden geliyor
        if (dir == "in" && lbGw)
        {
            var vips = env.MemberOf(target).Where(m => m.Port == null || m.Port == port)
                .Select(m => $"VIP {m.LbIp}:{m.LbPort}").Distinct().ToList();
            return new ProcPeer(ip, port, hits, env.FindSegment(ip), vips.Count > 0 ? vips : ["Load balancer GW"], false, true);
        }
        var members = dir == "out" ? env.VipMembers(ip, port == "" ? null : port) : [];
        var apps = members.Count > 0
            ? members.SelectMany(m => env.AppNamesOnIp(m.Ip)).Distinct().Order().ToList()
            : dir == "out"
                ? env.Match(ip, port == "" ? null : port).Apps.Select(a => a.AppName).OfType<string>().Distinct().ToList()
                : env.AppNamesOnIp(ip);
        return new ProcPeer(ip, port, hits, env.FindSegment(ip), apps, members.Count > 0, lbGw);
    }
}
