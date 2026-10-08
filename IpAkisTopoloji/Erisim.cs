// ---------------------------------------------------------------------------
// Erişim sorgula: bir IP'den diğerine erişim olmuş mu?
//  - Firewall (Palo Alto): izin / engel, port, ilk ve son görülme, kural, cihaz, uygulama, oturum kapanma nedeni,
//    NAT (hedef çevrilmiş adres olarak da aranır) ve son 20 olay.
//  - Carbon Black: kaynaktan hedefe bağlantıyı uç nokta ajanı görmüş mü (hangi süreç, son ne zaman).
//  - Envanter: iki ucun segmenti / uygulamaları, hedef VIP ise üyeleri; aynı segmentteyse trafik firewall'dan geçmeyebilir.
//  - Dataskope (DB audit): kaynaktan hedef veritabanına hangi DB / OS kullanıcısı, hangi programla bağlanmış, son ne zaman.
// Karar özeti bunlardan çıkarılır.
// ---------------------------------------------------------------------------
record ErisimUc(string Ip, SegmentInfo? Segment, List<string> Apps, List<string> Vip);
record ErisimFw(string Port, string Action, bool Denied, long Count, string? First, string? Last,
    List<string> Rules, List<string> Devices, List<string> Apps, List<string> Reasons, List<string> Nat);
record ErisimOlay(string Time, string SrcPort, string DestIp, string DestPort, string? NatIp, string Action, bool Denied,
    string? Rule, string? Reason, string? Device);
record ErisimCb(string Port, string Process, string Yon, long Count, string? Last);
record ErisimDb(string? DbType, string? Instance, string? DbName, string? DbUser, string? OsUser, string? ClientHost, string? ClientApp,
    string? ServerPort, long Count, string? First, string? Last);
record ErisimSonuc(ErisimUc Kaynak, ErisimUc Hedef, string? Port, string Pencere, string Karar, string KararTur, string KararDetay,
    List<ErisimFw> Firewall, List<ErisimOlay> SonOlaylar, List<ErisimCb> CarbonBlack,
    SourceStatus FirewallDurum, SourceStatus CarbonBlackDurum, SourceStatus Envanter, long ElapsedMs)
{
    public List<ErisimDb> Veritabani { get; init; } = [];
    public SourceStatus VeritabaniDurum { get; init; } = SourceStatus.Skipped;
}

static class Erisim
{
    static readonly System.Globalization.CultureInfo Tr = new("tr-TR");
    static string N(long n) => n.ToString("N0", Tr);
    static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? EnvanterSnapshot.Clean(v) : null;
    // Splunk values() çok değerli alanları satır sonuyla birleştirir
    static List<string> L(Dictionary<string, string> r, string k) =>
        (V(r, k) ?? "").Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    public static async Task<SplunkResult> CarbonBlackAsync(LookupQuery q, string srcIp, string dstIp, string? port,
        IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack";
        string sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string portFilter = port == null ? "" : $" AND server_port=\"{port}\"";
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" TERM({srcIp}) TERM({dstIp})
            | eval dir=lower(direction),
                   client_ip   = if(dir=="outbound", local_ip, remote_ip),
                   server_ip   = if(dir=="outbound", remote_ip, local_ip),
                   server_port = if(dir=="outbound", remote_port, local_port),
                   process     = replace(process_path, "^.*[\\\\/]", "")
            | where client_ip="{srcIp}" AND server_ip="{dstIp}"{portFilter}
            | stats count max(_time) as last by server_port process dir
            | sort 0 - last
            | eval last=strftime(last, "%Y-%m-%d %H:%M:%S")
            """;
        return await SplunkService.ExportAsync(spl, q, cfg, http, ct, "Carbon Black");
    }

    public static ErisimUc Uc(string ip, EnvanterSnapshot? env)
    {
        if (env == null) return new ErisimUc(ip, null, [], []);
        var vip = env.VipMembers(ip, null).Select(m => $"üye {m.Ip}:{m.Port ?? "*"}{(m.Hostname is { } h ? " " + h : "")}").ToList();
        vip.AddRange(env.MemberOf(ip).Select(m => $"VIP {m.LbIp}:{m.LbPort} havuzunda"));
        return new ErisimUc(ip, env.FindSegment(ip), env.AppNamesOnIp(ip), vip.Distinct().ToList());
    }

    public static ErisimSonuc Build(ErisimUc src, ErisimUc dst, string? port, LookupQuery q,
        SplunkResult? fwSum, SplunkResult? fwRecent, SplunkResult? cb, SourceStatus fwSt, SourceStatus cbSt, SourceStatus envSt, long ms,
        DataskopeSonuc? ds = null, SourceStatus? dsSt = null)
    {
        // DB oturumları: kullanıcı + program + veritabanı bazında sayı, ilk / son (zaman yerel "yyyy-MM-dd HH:mm:ss")
        static string? T(string? t) => DateTimeOffset.TryParse(t, out var d) ? d.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") : t;
        var db = (ds?.Kayitlar ?? [])
            .Where(k => port == null || k.ServerPort == null || k.ServerPort == port)
            .GroupBy(k => (k.DbType, k.Instance, k.DbName, k.DbUser, k.OsUser, k.ClientHost, k.ClientApp, k.ServerPort))
            .Select(g => new ErisimDb(g.Key.DbType, g.Key.Instance, g.Key.DbName, g.Key.DbUser, g.Key.OsUser, g.Key.ClientHost, g.Key.ClientApp,
                g.Key.ServerPort, g.Count(), g.Select(k => T(k.Time)).Min(StringComparer.Ordinal), g.Select(k => T(k.Time)).Max(StringComparer.Ordinal)))
            .OrderByDescending(x => x.Last, StringComparer.Ordinal).ToList();
        string? lastDb = db.Select(x => x.Last).Max(StringComparer.Ordinal);
        string dbOzet = db.Count == 0 ? "" : $" Veritabanına {string.Join(", ", db.Select(x => x.DbUser).Where(u => u != null).Distinct().Take(3))} kullanıcısıyla oturum açılmış (Dataskope, son {lastDb}).";
        var fw = (fwSum?.Rows ?? []).Select(r => new ErisimFw(V(r, "dp") ?? "", V(r, "a") ?? "?", V(r, "denied") == "1",
                long.TryParse(V(r, "count"), out long c) ? c : 0, V(r, "first"), V(r, "last"),
                L(r, "rule"), L(r, "device"), L(r, "app"), L(r, "reason"),
                L(r, "nat").Zip(L(r, "nat_port").Concat(Enumerable.Repeat("", 10)), (a, b) => b == "" ? a : $"{a}:{b}").ToList()))
            .ToList();
        var olay = (fwRecent?.Rows ?? []).Select(r => new ErisimOlay(V(r, "t") ?? "", V(r, "sp") ?? "", V(r, "d") ?? "", V(r, "dp") ?? "",
                V(r, "nat"), V(r, "a") ?? "?", V(r, "denied") == "1", V(r, "r"), V(r, "why"), V(r, "dev")))
            .ToList();
        var cbl = (cb?.Rows ?? []).Select(r => new ErisimCb(V(r, "server_port") ?? "", V(r, "process") ?? "?", V(r, "dir") ?? "",
                long.TryParse(V(r, "count"), out long c) ? c : 0, V(r, "last")))
            .ToList();

        // Karar: son izin / son engel zamanlarına göre
        string? lastAllow = fw.Where(x => !x.Denied).Select(x => x.Last).Max(StringComparer.Ordinal);
        string? lastDeny = fw.Where(x => x.Denied).Select(x => x.Last).Max(StringComparer.Ordinal);
        string? lastCb = cbl.Select(x => x.Last).Max(StringComparer.Ordinal);
        bool sameSeg = src.Segment != null && dst.Segment != null && src.Segment.Cidr == dst.Segment.Cidr;
        string karar, tur, detay;
        string segAd = sameSeg ? $"{src.Segment!.Label} ({src.Segment.Cidr}{(src.Segment.Vlan is { } vl ? ", VLAN " + vl : "")})" : "";
        // Aynı alt ağ (VLAN): trafik ağ geçidine / firewall'a uğramadan doğrudan gider, firewall kuralı gerekmez.
        // Firewall'da kayıt varsa (ör. NAT'lı adres üzerinden gidilmiş) normal karar verilir, aynı VLAN notu eklenir.
        if (sameSeg && lastAllow == null && lastDeny == null)
        {
            (karar, tur) = ("Aynı VLAN — firewall gerekmez", "info");
            detay = $"İki IP aynı segmentte: {segAd}. Aradaki trafik firewall'a uğramaz, firewall kuralı / erişim talebi gerekmez."
                + (lastCb != null ? $" Carbon Black bağlantıyı görmüş (son {lastCb})." : lastDb == null ? " Bu aralıkta Carbon Black da bağlantı görmedi (hiç denenmemiş olabilir)." : "")
                + dbOzet
                + " Engel varsa yalnızca sunucunun kendi yerel güvenlik duvarından (Windows Firewall / iptables) olabilir.";
        }
        else if (lastAllow != null && (lastDeny == null || string.CompareOrdinal(lastAllow, lastDeny) >= 0))
        {
            (karar, tur) = ("Erişim var", "ok");
            detay = $"Firewall son izin: {lastAllow} ({N(fw.Where(x => !x.Denied).Sum(x => x.Count))} oturum, port {string.Join(", ", fw.Where(x => !x.Denied).Select(x => x.Port).Distinct())})."
                + (lastDeny != null ? $" Aynı aralıkta engellenen oturumlar da var (son: {lastDeny})." : "");
        }
        else if (lastAllow != null)
        {
            (karar, tur) = ("Son durumda engelleniyor", "warn");
            detay = $"Önce izin verilmiş (son izin {lastAllow}) ama daha sonra engellenmiş (son engel {lastDeny}). Kural değişikliği olabilir.";
        }
        else if (lastDeny != null)
        {
            (karar, tur) = ("Engelleniyor", "err");
            detay = $"Firewall'da yalnızca engellenen oturum var: {N(fw.Sum(x => x.Count))} deneme, son {lastDeny}"
                + $" (kural: {string.Join(", ", fw.SelectMany(x => x.Rules).Distinct().Take(3))}).";
        }
        else if (lastDb != null)
        {
            (karar, tur) = ("Veritabanı oturumu var (firewall kaydı yok)", "ok");
            detay = dbOzet.Trim() + (sameSeg ? "" : " Firewall logu yok: trafik bu firewall'dan geçmiyor olabilir.");
        }
        else if (lastCb != null)
        {
            (karar, tur) = ("Bağlantı görülmüş (firewall kaydı yok)", "ok");
            detay = $"Carbon Black bağlantıyı görmüş (son {lastCb}) ama firewall logu yok"
                + (sameSeg ? ": iki IP aynı segmentte, trafik firewall'dan geçmiyor." : ": trafik bu firewall'dan geçmiyor olabilir.");
        }
        else
        {
            (karar, tur) = ("Kayıt yok", "skip");
            detay = "Bu aralıkta kaynaktan hedefe ne izin verilen ne de engellenen bir oturum görülmedi. Erişim hiç denenmemiş olabilir; aralığı genişletin."
                + (sameSeg ? " İki IP aynı segmentte: aradaki trafik firewall'a uğramaz." : "");
        }

        if (lastAllow != null || lastDeny != null) detay += dbOzet;  // firewall kararlarına DB bilgisi eklenir
        if (sameSeg && tur != "info") detay += $" Not: iki IP aynı segmentte ({segAd}); doğrudan erişimde firewall'a uğramaz.";
        return new ErisimSonuc(src, dst, port, $"{q.Start:dd.MM.yyyy HH:mm} - {q.End:dd.MM.yyyy HH:mm}", karar, tur, detay,
            fw, olay, cbl, fwSt, cbSt, envSt, ms) { Veritabani = db, VeritabaniDurum = dsSt ?? SourceStatus.Skipped };
    }
}
