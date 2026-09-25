using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// Envanter (SQL Server): segmentler ve IP-port-uygulama kayıtları.
// Tablolar küçük olduğu için tamamı belleğe alınır, CacheMinutes süresince tutulur.
// ---------------------------------------------------------------------------
sealed class EnvanterService(IConfiguration cfg, ILogger<EnvanterService> log)
{
    readonly SemaphoreSlim _lock = new(1, 1);
    EnvanterSnapshot? _snap;

    public async Task<EnvanterSnapshot> GetAsync(bool force, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(cfg.GetValue("Envanter:CacheMinutes", 30));
        var current = _snap;
        if (!force && current != null && DateTimeOffset.Now - current.LoadedAt < ttl)
            return current;

        await _lock.WaitAsync(ct);
        try
        {
            current = _snap;
            if (!force && current != null && DateTimeOffset.Now - current.LoadedAt < ttl)
                return current;

            try
            {
                _snap = await LoadAsync(ct);
                log.LogInformation("Envanter yüklendi: {Seg} segment, {Host} host, {Vip} VIP üyesi, {Gw} VIP GW",
                    _snap.SegmentCount, _snap.HostCount, _snap.VipMemberCount, _snap.VipGwCount);
                return _snap;
            }
            catch (Exception ex) when (current != null && !force && !ct.IsCancellationRequested)
            {
                // Veritabanına ulaşılamazsa eski envanterle devam et.
                log.LogWarning(ex, "Envanter yenilenemedi, önceki kopya kullanılıyor.");
                return current;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task<EnvanterSnapshot> LoadAsync(CancellationToken ct)
    {
        string connStr = cfg.GetConnectionString("Envanter")
            ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
        // Tablo adları yalnızca config'den gelir, kullanıcı girdisi değildir.
        string segTable = cfg["Envanter:SegmentTablosu"] ?? "dbo.KonsolideSegmentler4";
        string hostTable = cfg["Envanter:HostTablosu"] ?? "dbo.ERT_HOSTIPADDRESS";

        var segments = new List<SegmentInfo>();
        var hosts = new List<HostApp>();

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        await using (var cmd = new SqlCommand(
            $"SELECT SEGMENT, VLAN, Tenant, ApplicationProfile, EPGName, BD, GW, Description, Domain FROM {segTable}", conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                string? cidr = Clean(r, 0);
                if (cidr == null) continue;
                segments.Add(new SegmentInfo(cidr, Clean(r, 1), Clean(r, 2), Clean(r, 3), Clean(r, 4),
                    Clean(r, 5), Clean(r, 6), Clean(r, 7), Clean(r, 8)));
            }
        }

        await using (var cmd = new SqlCommand(
            $"""
            SELECT IPADDRESS, PORT, VIP_IP, VIP_PORT, APPNAME, WEBSITE, UNIXNAME, SERVICENAME,
                   VARLIKMUHAFIZI, SUNUCUISLETENBIRIM, UYGULAMAVARLIKSAHIBI
            FROM {hostTable}
            """, conn))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                hosts.Add(new HostApp(Clean(r, 0), Clean(r, 1), Clean(r, 2), Clean(r, 3), Clean(r, 4), Clean(r, 5),
                    Clean(r, 6), Clean(r, 7), Clean(r, 8), Clean(r, 9), Clean(r, 10)));
            }
        }

        // VIP tabloları opsiyonel: okunamazsa diğer envanterle devam edilir.
        string vipTable = cfg["Envanter:VipTablosu"] ?? "dbo.vip_envanteri";
        string vipGwTable = cfg["Envanter:VipGwTablosu"] ?? "dbo.vipgw";
        var vips = new List<VipRow>();
        var vipGws = new List<VipGwRow>();
        try
        {
            await using (var cmd = new SqlCommand(
                $"SELECT hostname, server_ip, server_port, server_type, loadbalancer_ip, loadbalancer_port, loadbalancer_pool FROM {vipTable}", conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    vips.Add(new VipRow(Clean(r, 0), Clean(r, 1), Clean(r, 2), Clean(r, 3), Clean(r, 4), Clean(r, 5), Clean(r, 6)));
            }
            await using (var cmd = new SqlCommand($"SELECT GWIP, FW, Segment FROM {vipGwTable}", conn))
            await using (var r = await cmd.ExecuteReaderAsync(ct))
            {
                while (await r.ReadAsync(ct))
                    vipGws.Add(new VipGwRow(Clean(r, 0), Clean(r, 1), Clean(r, 2)));
            }
        }
        catch (SqlException ex)
        {
            log.LogWarning(ex, "VIP tabloları okunamadı ({Vip}, {Gw}); VIP çözümlemesi kapalı.", vipTable, vipGwTable);
        }

        return new EnvanterSnapshot(segments, hosts, vips, vipGws);
    }

    static string? Clean(SqlDataReader r, int i) => r.IsDBNull(i) ? null : EnvanterSnapshot.Clean(Convert.ToString(r.GetValue(i)));
}

record SegmentInfo(string Cidr, string? Vlan, string? Tenant, string? ApplicationProfile, string? EpgName,
    string? Bd, string? Gw, string? Description, string? Domain)
{
    // Ekranda gösterilecek kısa ad: EPG > ApplicationProfile > Description > CIDR
    public string Label => EpgName ?? ApplicationProfile ?? Description ?? Cidr;
}

record HostApp(string? Ip, string? Port, string? VipIp, string? VipPort, string? AppName, string? Website,
    string? UnixName, string? ServiceName, string? VarlikMuhafizi, string? SunucuIsletenBirim, string? UygulamaVarlikSahibi);

// vip_envanteri satırı ve vipgw satırı (ham)
record VipRow(string? Hostname, string? ServerIp, string? ServerPort, string? ServerType,
    string? LbIp, string? LbPort, string? Pool);
record VipGwRow(string? GwIp, string? Fw, string? Segment);

// VIP (load balancer) havuz üyesi: LB, üyeye kendi segmentindeki GW IP'si üzerinden gider.
record VipMember(string? Hostname, string Ip, string? Port, string? Type, string LbIp, string? LbPort, string? Pool,
    List<string> GwIps, string? Fw, SegmentInfo? Segment)
{
    public long Hits { get; init; }        // üyenin bu portuna gelen trafik
    public long GwHits { get; init; }      // bunun LB GW IP'lerinden gelen kısmı
    public bool Verified => GwHits > 0;    // GW → üye:port akışı görüldü mü
}

// Kind: "ip-port" (birebir), "vip" (VIP_IP+VIP_PORT), "ip" (sadece IP), "vip-ip" (sadece VIP_IP), "yok"
record AppMatch(string Kind, List<HostApp> Apps);

sealed class EnvanterSnapshot
{
    public DateTimeOffset LoadedAt { get; } = DateTimeOffset.Now;
    public int SegmentCount { get; }
    public int HostCount { get; }
    public int VipMemberCount { get; }
    public int VipGwCount { get; }

    // Prefix uzunluğuna göre (0..32) network adresi -> segment; en uzun prefix kazanır.
    readonly Dictionary<uint, SegmentInfo>?[] _segByPrefix = new Dictionary<uint, SegmentInfo>?[33];
    readonly Dictionary<string, List<HostApp>> _byIp = new();
    readonly Dictionary<string, List<HostApp>> _byVip = new();
    readonly Dictionary<string, List<VipMember>> _vipByLb = new();
    readonly Dictionary<string, List<VipMember>> _vipByMember = new();
    readonly Dictionary<string, string?> _gwFw = new();
    readonly Dictionary<uint, List<VipGwRow>>?[] _gwByPrefix = new Dictionary<uint, List<VipGwRow>>?[33];

    public EnvanterSnapshot(List<SegmentInfo> segments, List<HostApp> hosts,
        List<VipRow>? vips = null, List<VipGwRow>? vipGws = null)
    {
        foreach (var s in segments)
        {
            if (!TryParseCidr(s.Cidr, out uint net, out int prefix)) continue;
            var map = _segByPrefix[prefix] ??= new();
            map.TryAdd(net, s);
            SegmentCount++;
        }

        foreach (var h in hosts)
        {
            if (h.Ip != null) Add(_byIp, h.Ip, h);
            if (h.VipIp != null) Add(_byVip, h.VipIp, h);
            HostCount++;
        }

        foreach (var g in vipGws ?? [])
        {
            if (g.GwIp == null || g.Segment == null || !TryParseCidr(g.Segment, out uint net, out int prefix)) continue;
            var map = _gwByPrefix[prefix] ??= new();
            if (!map.TryGetValue(net, out var list)) map[net] = list = [];
            list.Add(g);
            _gwFw.TryAdd(g.GwIp, g.Fw);
            VipGwCount++;
        }

        // Aynı üye birden fazla satırda olabilir: (LB IP, LB port, üye IP, üye port) tekilleştirilir.
        foreach (var v in (vips ?? []).Where(v => v.LbIp != null && v.ServerIp != null)
                     .DistinctBy(v => (v.LbIp, v.LbPort, v.ServerIp, v.ServerPort)))
        {
            var gws = FindGws(v.ServerIp!);
            var m = new VipMember(v.Hostname, v.ServerIp!, v.ServerPort, v.ServerType, v.LbIp!, v.LbPort, v.Pool,
                gws.Select(g => g.GwIp!).Distinct().ToList(), gws.FirstOrDefault()?.Fw, FindSegment(v.ServerIp!));
            if (!_vipByLb.TryGetValue(m.LbIp, out var l1)) _vipByLb[m.LbIp] = l1 = [];
            l1.Add(m);
            if (!_vipByMember.TryGetValue(m.Ip, out var l2)) _vipByMember[m.Ip] = l2 = [];
            l2.Add(m);
            VipMemberCount++;
        }
    }

    // IP bir VIP ise havuz üyeleri (port verilirse o LB portundakiler; yoksa hepsi).
    public List<VipMember> VipMembers(string lbIp, string? lbPort = null)
    {
        if (!_vipByLb.TryGetValue(lbIp, out var all)) return [];
        if (lbPort == null) return all;
        var onPort = all.Where(m => m.LbPort == lbPort).ToList();
        return onPort.Count > 0 ? onPort : all;
    }

    // IP hangi VIP'lerin havuz üyesi?
    public List<VipMember> MemberOf(string ip) => _vipByMember.TryGetValue(ip, out var l) ? l : [];

    // IP bir LB GW IP'si mi? (FW adıyla)
    public bool IsVipGw(string ip, out string? fw) => _gwFw.TryGetValue(ip, out fw);

    // Üyenin segmentindeki VIP GW'leri (en uzun prefix; bir segmentte birden fazla GW olabilir)
    List<VipGwRow> FindGws(string ip)
    {
        if (!TryToUInt(ip, out uint addr)) return [];
        for (int p = 32; p >= 0; p--)
        {
            var map = _gwByPrefix[p];
            if (map != null && map.TryGetValue(addr & Mask(p), out var list)) return list;
        }
        return [];
    }

    public SegmentInfo? FindSegment(string ip)
    {
        if (!TryToUInt(ip, out uint addr)) return null;
        for (int p = 32; p >= 0; p--)
        {
            var map = _segByPrefix[p];
            if (map != null && map.TryGetValue(addr & Mask(p), out var seg))
                return seg;
        }
        return null;
    }

    public List<HostApp> AppsOnIp(string ip) => _byIp.TryGetValue(ip, out var l) ? l : [];

    // Belirli bir IP:port'ta hangi uygulama var? En güçlü eşleşmeden zayıfa doğru arar.
    public AppMatch Match(string ip, string? port)
    {
        _byIp.TryGetValue(ip, out var ipRows);
        _byVip.TryGetValue(ip, out var vipRows);

        if (port != null)
        {
            var exact = ipRows?.Where(h => h.Port == port).ToList();
            if (exact is { Count: > 0 }) return new("ip-port", exact);

            var vip = vipRows?.Where(h => h.VipPort == port).ToList();
            if (vip is { Count: > 0 }) return new("vip", vip);
        }

        if (ipRows is { Count: > 0 }) return new("ip", ipRows);
        if (vipRows is { Count: > 0 }) return new("vip-ip", vipRows);
        return new("yok", []);
    }

    public static string? Clean(string? v)
    {
        v = v?.Trim();
        return string.IsNullOrEmpty(v) || v is "NULL" or "#N/A" or "N/A" or "-" ? null : v;
    }

    static void Add(Dictionary<string, List<HostApp>> d, string key, HostApp h)
    {
        if (!d.TryGetValue(key, out var l)) d[key] = l = [];
        l.Add(h);
    }

    static uint Mask(int prefix) => prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);

    static bool TryParseCidr(string text, out uint net, out int prefix)
    {
        net = 0;
        prefix = 32;
        var parts = text.Split('/', 2);
        if (parts.Length == 2 && (!int.TryParse(parts[1].Trim(), out prefix) || prefix is < 0 or > 32))
            return false;
        if (!TryToUInt(parts[0].Trim(), out uint addr)) return false;
        net = addr & Mask(prefix);
        return true;
    }

    static bool TryToUInt(string ip, out uint value)
    {
        value = 0;
        if (ip.Split('.').Length != 4 || !IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = a.GetAddressBytes();
        value = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        return true;
    }
}

// ---------------------------------------------------------------------------
// Akış birleştirme: Splunk + AppResponse satırlarını girilen IP'ye göre
// GELEN / GİDEN olarak ayırır, envanterle zenginleştirir.
// ---------------------------------------------------------------------------
record SourceStatus(bool Ok, string? Error, List<string> Messages, long? ElapsedMs, int Rows)
{
    public static SourceStatus Skipped => new(false, "Sorgulanmadı", [], null, 0);
}

record TargetInfo(string Ip, SegmentInfo? Segment, List<HostApp> Apps)
{
    public List<VipMember>? Vip { get; init; }       // sorgulanan IP bir VIP ise havuz üyeleri (trafikle)
    public List<VipMember>? MemberOf { get; init; }  // sorgulanan IP hangi VIP'lerin üyesi
}

record FlowEdge(
    string Direction,          // "in" | "out"
    string PeerIp,
    string Port,               // gelen: sorgulanan IP'nin portu, giden: karşının portu; "" = sadece L7 (URL) kaydı
    List<string> Protocols,
    long Hits, long SplunkHits, long AppResponseHits,
    string? FirstSeen, string? LastSeen,
    List<string> Computers, List<string> Processes, List<string> Urls,
    SegmentInfo? PeerSegment,
    AppMatch? PeerApps,        // gelen: karşı hostta kayıtlı uygulamalar (IP), giden: hedef IP:port uygulaması
    AppMatch? LocalApp)        // sadece gelen: sorgulanan IP:port'ta çalışan uygulama
{
    public VipMember? ViaVip { get; init; }          // bu kenar VIP → havuz üyesi (sorgulanan IP bir VIP)
    public List<VipMember>? PeerVip { get; init; }   // karşı IP bir VIP: arkasındaki üyeler
    public string? PeerVipGw { get; init; }          // karşı IP bir LB GW'si: FW adı
}

record FlowResponse(TargetInfo Target, List<FlowEdge> Inbound, List<FlowEdge> Outbound,
    SourceStatus Splunk, SourceStatus AppResponse, SourceStatus Envanter, long ElapsedMs);

static class FlowBuilder
{
    const int MaxUrlsPerEdge = 15;

    sealed class Agg
    {
        public long Splunk, Ar;
        public string? First, Last;
        public readonly HashSet<string> Protocols = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Computers = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Processes = new(StringComparer.OrdinalIgnoreCase);
    }

    public static (List<FlowEdge> inbound, List<FlowEdge> outbound) Build(
        string target, SplunkResult? sp, AppResponseResult? ar, EnvanterSnapshot? env)
    {
        var aggs = new Dictionary<(string dir, string peer, string port), Agg>();
        var urls = new Dictionary<(string dir, string peer), Dictionary<string, long>>();

        Agg Get(string dir, string peer, string port)
        {
            if (!aggs.TryGetValue((dir, peer, port), out var a))
                aggs[(dir, peer, port)] = a = new Agg();
            return a;
        }

        if (sp != null)
        {
            foreach (var row in sp.Rows)
            {
                if (!Orient(target, V(row, "client_ip"), V(row, "server_ip"), out var dir, out var peer)) continue;
                var a = Get(dir, peer, V(row, "server_port") ?? "");
                a.Splunk += long.TryParse(V(row, "count"), out long c) ? c : 1;
                if (V(row, "Protocol") is { } proto) a.Protocols.Add(proto);
                foreach (var x in Split(V(row, "computer_name"))) a.Computers.Add(x);
                foreach (var x in Split(V(row, "process"))) a.Processes.Add(x);
                // "yyyy-MM-dd HH:mm:ss" metin olarak sıralanabilir.
                if (V(row, "first_seen") is { } f && (a.First == null || string.CompareOrdinal(f, a.First) < 0)) a.First = f;
                if (V(row, "last_seen") is { } l && (a.Last == null || string.CompareOrdinal(l, a.Last) > 0)) a.Last = l;
            }
        }

        if (ar != null)
        {
            foreach (var r in ar.L4)
            {
                if (!Orient(target, r.ClientIp, r.ServerIp, out var dir, out var peer)) continue;
                var a = Get(dir, peer, r.Port == "-" ? "" : r.Port);
                a.Ar += r.Hit;
                a.Protocols.Add("TCP");
            }

            foreach (var r in ar.L7)
            {
                if (!Orient(target, r.ClientIp, r.ServerIp, out var dir, out var peer) || r.Url == "-") continue;
                if (!urls.TryGetValue((dir, peer), out var u)) urls[(dir, peer)] = u = new();
                u[r.Url] = u.GetValueOrDefault(r.Url) + r.Hit;
            }

            // L4'te karşılığı olmayan L7 kayıtları portsuz ayrı satır olarak görünsün.
            foreach (var (key, u) in urls)
            {
                if (!aggs.Keys.Any(k => k.dir == key.dir && k.peer == key.peer))
                    Get(key.dir, key.peer, "").Ar += u.Values.Sum();
            }
        }

        var inbound = new List<FlowEdge>();
        var outbound = new List<FlowEdge>();

        foreach (var ((dir, peer, port), a) in aggs)
        {
            string? p = port == "" ? null : port;
            var edgeUrls = urls.TryGetValue((dir, peer), out var u)
                ? u.OrderByDescending(kv => kv.Value).Take(MaxUrlsPerEdge).Select(kv => kv.Key).ToList()
                : [];

            AppMatch? peerApps = null, localApp = null;
            if (env != null)
            {
                if (dir == "in")
                {
                    // Gelen: karşı tarafın kaynak portu geçicidir; host'u IP ile tanı.
                    var onPeer = env.AppsOnIp(peer);
                    peerApps = onPeer.Count > 0 ? new AppMatch("ip", onPeer) : env.Match(peer, null);
                    localApp = env.Match(target, p);
                }
                else
                {
                    peerApps = env.Match(peer, p);
                }
            }

            var edge = new FlowEdge(dir, peer, port,
                [.. a.Protocols.Order()], a.Splunk + a.Ar, a.Splunk, a.Ar, a.First, a.Last,
                [.. a.Computers.Order()], [.. a.Processes.Order()], edgeUrls,
                env?.FindSegment(peer), peerApps, localApp);

            (dir == "in" ? inbound : outbound).Add(edge);
        }

        return (inbound.OrderByDescending(e => e.Hits).ToList(), outbound.OrderByDescending(e => e.Hits).ToList());
    }

    static bool Orient(string target, string? client, string? server, out string dir, out string peer)
    {
        dir = peer = "";
        if (client == target && server == target) return false;
        if (server == target && !string.IsNullOrEmpty(client) && client != "-") { dir = "in"; peer = client; return true; }
        if (client == target && !string.IsNullOrEmpty(server) && server != "-") { dir = "out"; peer = server; return true; }
        return false;
    }

    static string? V(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) ? EnvanterSnapshot.Clean(v) : null;

    static IEnumerable<string> Split(string? v) =>
        v == null ? [] : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// ---------------------------------------------------------------------------
// 2. seviye: karşı sunucuların kendi gelen/giden trafiği, segment bazında özet.
// ---------------------------------------------------------------------------
record Hop2Request(string? Target, List<string>? Ips, string? Start, string? End, int? Appliance, string? Sources);

record SegmentAgg(SegmentInfo? Segment, int IpCount, long Hits, List<string> Ports, List<string> TopIps, List<string> Apps);

// Inbound: bu sunucuya gelenlerin segmentleri, Outbound: bu sunucunun gittiği segmentler
record PeerHop(string Ip, SegmentInfo? Segment, List<SegmentAgg> Inbound, List<SegmentAgg> Outbound);

record Hop2Response(List<PeerHop> Peers, int Requested, int Queried,
    SourceStatus Splunk, SourceStatus AppResponse, SourceStatus Envanter, long ElapsedMs);

static class Hop2Builder
{
    const int TopIps = 10, MaxPorts = 12, MaxApps = 6;

    public static PeerHop Build(string peer, string mainTarget, SplunkResult? sp, AppResponseResult? ar, EnvanterSnapshot? env)
    {
        var (inbound, outbound) = FlowBuilder.Build(peer, sp, ar, env);
        return new PeerHop(peer, env?.FindSegment(peer), Summarize(inbound, mainTarget), Summarize(outbound, mainTarget));
    }

    static List<SegmentAgg> Summarize(List<FlowEdge> edges, string mainTarget) =>
        edges.Where(e => e.PeerIp != mainTarget)
            .GroupBy(e => e.PeerSegment?.Cidr ?? "")
            .Select(g => new SegmentAgg(
                g.First().PeerSegment,
                g.Select(e => e.PeerIp).Distinct().Count(),
                g.Sum(e => e.Hits),
                g.Select(e => e.Port).Where(p => p != "").Distinct().Take(MaxPorts).ToList(),
                g.GroupBy(e => e.PeerIp).OrderByDescending(x => x.Sum(e => e.Hits)).Take(TopIps).Select(x => x.Key).ToList(),
                g.SelectMany(e => e.PeerApps?.Apps ?? []).Select(a => a.AppName ?? a.ServiceName ?? a.UnixName)
                    .OfType<string>().Distinct().Take(MaxApps).ToList()))
            .OrderByDescending(a => a.Hits)
            .ToList();
}

// ---------------------------------------------------------------------------
// VIP (load balancer) çözümlemesi: VIP'in arkasındaki havuz üyeleri ve GW üzerinden trafik.
// ---------------------------------------------------------------------------
static class VipResolver
{
    // Karşı IP bir VIP ise üyelerini, bir LB GW'si ise FW adını kenara ekler.
    public static List<FlowEdge> Annotate(List<FlowEdge> edges, EnvanterSnapshot env) =>
        edges.Select(e =>
        {
            var members = env.VipMembers(e.PeerIp, e.Port == "" ? null : e.Port);
            string? gwFw = env.IsVipGw(e.PeerIp, out var fw) ? (fw ?? "LB GW") : null;
            return members.Count == 0 && gwFw == null ? e : e with { PeerVip = members.Count > 0 ? members : null, PeerVipGw = gwFw };
        }).ToList();

    // Sorgulanan IP bir VIP: her üye için "VIP → üye:port" kenarı. Trafik, üyenin o portuna gelen
    // akışlardan; GW'den gelen kısım doğrulama olarak ayrıca tutulur.
    public static (List<VipMember> members, List<FlowEdge> edges) MemberEdges(
        List<VipMember> vipMembers, SplunkResult? sp, AppResponseResult? ar, EnvanterSnapshot env)
    {
        var members = new List<VipMember>();
        var edges = new List<FlowEdge>();
        var inboundCache = new Dictionary<string, List<FlowEdge>>();

        foreach (var m in vipMembers.DistinctBy(m => (m.Ip, m.Port)))
        {
            if (!inboundCache.TryGetValue(m.Ip, out var mIn))
                inboundCache[m.Ip] = mIn = FlowBuilder.Build(m.Ip, sp, ar, env).inbound;
            var onPort = mIn.Where(e => m.Port == null || e.Port == m.Port).ToList();
            long gwHits = onPort.Where(e => env.IsVipGw(e.PeerIp, out _)).Sum(e => e.Hits);
            var mm = m with { Hits = onPort.Sum(e => e.Hits), GwHits = gwHits };
            members.Add(mm);

            edges.Add(new FlowEdge("out", m.Ip, m.Port ?? "",
                onPort.SelectMany(e => e.Protocols).Distinct().ToList(),
                mm.Hits, onPort.Sum(e => e.SplunkHits), onPort.Sum(e => e.AppResponseHits),
                onPort.Select(e => e.FirstSeen).OfType<string>().DefaultIfEmpty().Min(),
                onPort.Select(e => e.LastSeen).OfType<string>().DefaultIfEmpty().Max(),
                onPort.SelectMany(e => e.Computers).Distinct().ToList(),
                onPort.SelectMany(e => e.Processes).Distinct().ToList(),
                [], m.Segment ?? env.FindSegment(m.Ip), env.Match(m.Ip, m.Port), null) { ViaVip = mm });
        }
        return (members, edges);
    }
}
