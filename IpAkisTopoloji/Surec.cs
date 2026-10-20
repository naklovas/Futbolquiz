// ---------------------------------------------------------------------------
// Sunucu içi akış (gelen → süreç → giden): Carbon Black her bağlantıyı onu açan / kabul eden süreçle
// kaydeder. Sorgulanan sunucunun kendi ajanının olayları (local_ip = IP) süreç bazında gruplanınca
// "şu porttan gelen istekleri hangi süreç karşılıyor, o süreç nereye gidiyor" görülür.
// Bu istek bazında değil süreç bazında bir bağdır: aynı süreçten geçen gelen ve giden trafik birlikte gösterilir.
// ---------------------------------------------------------------------------
record ProcPeer(string Ip, string Port, long Hits, SegmentInfo? Segment, List<string> Apps, bool Vip, bool LbGw);

// Portlar: dinlenen port başına tüm gelen bağlantılar ve farklı karşı IP sayısı (Inbound ilk 40 karşı IP ile sınırlı, bu değil)
record ProcPort(string Port, long Hits, int Peers);
record ProcNode(string Key, string Name, string? Pid, long InHits, long OutHits, List<string> ListenPorts, List<string> LocalApps,
    List<ProcPeer> Inbound, List<ProcPeer> Outbound, List<ProcPort>? Portlar = null, string? Yol = null);

record ProcessFlowResponse(string Ip, List<ProcNode> Processes, SourceStatus Splunk, SourceStatus Envanter, long ElapsedMs);

static class ProcessFlowBuilder
{
    const int MaxPeersPerSide = 40;

    // Satırlar: process, pid, yon (in/out), peer, port, count
    public static List<ProcNode> Build(string target, SplunkResult sp, EnvanterSnapshot? env)
    {
        static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? EnvanterSnapshot.Clean(v) : null;

        var all = sp.Rows
            // Ad: yolun son parçası (Windows "d:\…\x.exe" ve "/…/x" ikisi de); tam yol ayrıca (Yol)
            .Select(r => (proc: ExeName(V(r, "process") ?? V(r, "yol")) ?? "bilinmeyen süreç", pid: V(r, "pid"), dir: V(r, "yon"),
                peer: V(r, "peer"), port: V(r, "port") ?? "", hits: long.TryParse(V(r, "count"), out long c) ? c : 1, yol: V(r, "yol")))
            .Where(r => r.dir is "in" or "out")
            .ToList();
        // Çizim için karşı IP'si olmayan, kendine (aynı IP / 127.0.0.1) giden bağlantılar atlanır; ama süreç listesi ve port
        // sayıları (Portlar, Giden) hepsinden hesaplanır — "Gelen portlar ve çalışan süreçler" tablosunda süreç kaybolmasın.
        static bool Drawn((string proc, string? pid, string? dir, string? peer, string port, long hits, string? yol) r, string target) =>
            r.peer != null && r.peer != target && r.peer != "127.0.0.1";

        // Süreç kopyası (PID) ayrı düğüm: IIS'te her uygulama havuzu ayrı w3wp.exe'dir, karışmasınlar.
        return all.GroupBy(r => (proc: r.proc.ToLowerInvariant(), pid: r.pid is null or "-" ? null : r.pid))
            .Select(g =>
            {
                var drawn = g.Where(r => Drawn(r, target)).ToList();
                List<ProcPeer> Peers(string dir) => drawn.Where(r => r.dir == dir)
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
                    listen, localApps, inbound, Peers("out"),
                    g.Where(r => r.dir == "in").GroupBy(r => r.port)
                        .Select(x => new ProcPort(x.Key, x.Sum(r => r.hits), x.Select(r => r.peer).OfType<string>().Distinct().Count())).ToList(),
                    g.Select(r => r.yol).FirstOrDefault(y => y is { Length: > 1 }));
            })
            .OrderByDescending(p => p.InHits + p.OutHits)
            .ToList();
    }

    static string? ExeName(string? p) => string.IsNullOrWhiteSpace(p) ? null : p.Trim().Split('\\', '/').Last() is { Length: > 0 } n ? n : p.Trim();

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

// ---------------------------------------------------------------------------
// "Bu sunucuda hangi uygulama çalışıyor?" (IP kartı, Gelen portlar ve çalışan süreçler tablosunun üstündeki düğme): Carbon Black'te
// görülen exe'ler (tam yol, dinlediği portlar, gelen / giden bağlantı, karşı uygulamalar) + ERT + segment + genel bilgiler dosyası AI'a
// gider; AI genel Windows / altyapı süreçlerini (svchost, splunkd, antivirüs, Carbon Black ajanı…) ayıklar, iş uygulamalarını
// (ör. paloma.bulksms.exe, odi4.exe) adlandırır ve hangisinin hizmet verdiğini (port) söyler. Veri arayüzde zaten olan /api/surec
// sonucudur (yeniden sorgulanmaz). Sonuç kaydedilmez.
// ---------------------------------------------------------------------------
record SurecAiExe(string Ad, string? Yol, List<string>? Pidler, List<ProcPort>? Portlar, long Giden, List<string>? GelenUygulamalar, List<string>? GidenUygulamalar);
record SurecAiIstek(string? Ip, List<SurecAiExe>? Surecler);
record SurecAiUygulama(string Exe, string? Yol, string? Uygulama, string? Rol, string? ErtKarsiligi, int Guven, string? Gerekce);
record SurecAiSonuc(string Ip, string? Ozet, List<SurecAiUygulama> Uygulamalar, List<string> Ayiklanan, long ElapsedMs);

static class SurecAi
{
    // Kesin genel / altyapı süreçleri: AI'a "ayıklandı" olarak bildirilir, ayrıntısı gönderilmez
    static readonly string[] GenelYol = [@"\windows\system32\", @"\windows\syswow64\", @"\splunkuniversalforwarder\", @"\trend micro\",
        @"\carbon black\", @"\confer\", @"\vmware\vmware tools\", @"\windows defender\", @"\microsoft monitoring agent\", @"\zabbix", @"\nsclient"];
    static readonly HashSet<string> GenelAd = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "services.exe", "wininit.exe", "winlogon.exe", "csrss.exe", "smss.exe", "dwm.exe", "explorer.exe",
        "conhost.exe", "taskhostw.exe", "wmiprvse.exe", "spoolsv.exe", "rdpclip.exe", "splunkd.exe", "wget.exe", "msmpeng.exe",
        "coreserviceshell.exe", "vmtoolsd.exe", "zabbix_agentd.exe", "repmgr.exe", "cb.exe", "lsm.exe", "wermgr.exe", "ccsvchst.exe"
    };
    public static bool GenelExe(string ad, string? yol) =>
        GenelAd.Contains(ad) || (yol != null && GenelYol.Any(g => yol.Contains(g, StringComparison.OrdinalIgnoreCase)) && !(ad is "ntoskrnl.exe" or "System"));
    public static bool Genel(SurecAiExe e) =>
        GenelAd.Contains(e.Ad) || (e.Yol is { } y && GenelYol.Any(g => y.Contains(g, StringComparison.OrdinalIgnoreCase)) && !(e.Ad is "ntoskrnl.exe" or "System"));

    public static async Task<SurecAiSonuc> RunAsync(SurecAiIstek req, EnvanterSnapshot env, IslevService islev, EksikService eksik, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string ip = req.Ip!;
        var all = req.Surecler ?? [];
        var genel = all.Where(Genel).Select(e => e.Ad).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var is_ = all.Where(e => !Genel(e)).ToList();
        if (is_.Count == 0)
            return new SurecAiSonuc(ip, "Carbon Black'te yalnız Windows / altyapı süreçleri görüldü; iş uygulaması exe'si yok.", [], genel, sw.ElapsedMilliseconds);

        var sb = new System.Text.StringBuilder();
        if (eksik.Bilgi().Text is { Length: > 0 } bilgi)
            sb.AppendLine("KURUMA ÖZEL GENEL BİLGİLER (kullanıcının hazırladığı):").AppendLine(bilgi.Trim()).AppendLine("--- genel bilgilerin sonu ---").AppendLine();
        sb.AppendLine("""
            Bir bankanın veri merkezinde kıdemli bir altyapı mimarısın. Aşağıda bir sunucuda Carbon Black'in ağ bağlantısı gördüğü exe'ler
            var (tam yol, dinlediği portlar, gelen / giden bağlantı sayısı, bağlandığı ve kendisine bağlanan uygulamalar). Genel Windows ve
            izleme / güvenlik ajanları zaten ayıklandı. Görevin: bu sunucuda gerçekte HANGİ İŞ UYGULAMALARININ çalıştığını bulmak.
            - Her iş uygulaması exe'si için anlamlı bir uygulama adı ver (exe adından ve klasör yolundan; ör. c:\bbpgm\paloma\paloma.bulksms.exe →
              "Paloma Toplu SMS"; odi4.exe → "Oracle Data Integrator ajanı").
            - rol: port dinleyip gelen bağlantı alıyorsa "hizmet veren (port X)", yalnız dışarı bağlanıyorsa "istemci / arka plan işi".
              ntoskrnl.exe / System bir portta görünüyorsa o port HTTP.sys üzerindendir; asıl uygulama yolu / adı ERT WEBSITE ile örtüşen exe'dir.
            - ertKarsiligi: ERT'deki uygulama listesinden karşılığı varsa harfi harfine yaz, yoksa null.
            - Hâlâ genel / altyapı olduğunu düşündüğün exe'leri listeye KOYMA.
            - ozet: "Bu sunucu asıl olarak … uygulamasını çalıştırıyor; … portundan hizmet veriyor" gibi 1–2 cümle.
            Yalnızca JSON döndür:
            {"ozet":"…","uygulamalar":[{"exe":"…","uygulama":"…","rol":"…","ertKarsiligi":null,"guven":0-100,"gerekce":"tek cümle"}]}
            """);
        var hosts = env.AppsOnIp(ip);
        sb.AppendLine().AppendLine($"### Sunucu {ip}" + (hosts.Select(h => h.UnixName).OfType<string>().FirstOrDefault() is { } hn ? $" ({hn})" : ""));
        if (env.FindSegment(ip) is { } seg) sb.AppendLine($"Segment: {seg.Label}{(seg.Domain != null ? " · " + seg.Domain : "")}");
        var ert = hosts.Where(h => h.AppName != null || h.Website != null || h.ServiceName != null)
            .Select(h => $"port {h.Port ?? "-"}: {h.AppName ?? "(boş)"}{(h.Website != null ? " · WEBSITE " + h.Website : "")}{(h.ServiceName != null ? " · SERVICE " + h.ServiceName : "")}")
            .Distinct().Take(20).ToList();
        sb.AppendLine(ert.Count > 0 ? "ERT kayıtları:\n- " + string.Join("\n- ", ert) : "ERT kaydı yok.");
        sb.AppendLine($"Ayıklanan genel süreçler: {string.Join(", ", genel.Take(25))}");
        sb.AppendLine("İş uygulaması olabilecek exe'ler:");
        foreach (var e in is_.OrderByDescending(e => e.Portlar?.Sum(p => p.Hits) ?? 0).ThenByDescending(e => e.Giden).Take(40))
        {
            string ports = e.Portlar is { Count: > 0 } ps ? string.Join(", ", ps.Select(p => $"{p.Port} ({p.Hits} bağlantı, {p.Peers} istemci)")) : "dinlediği port yok";
            sb.AppendLine($"- {e.Ad} [{e.Yol}] · dinlediği portlar: {ports} · giden bağlantı: {e.Giden}"
                + (e.GelenUygulamalar is { Count: > 0 } gu ? $" · bağlanan uygulamalar: {string.Join(", ", gu.Take(6))}" : "")
                + (e.GidenUygulamalar is { Count: > 0 } gi ? $" · bağlandığı uygulamalar: {string.Join(", ", gi.Take(6))}" : ""));
        }
        string answer = await islev.ChatAsync(sb.ToString(), ct);
        if (IslevService.ExtractJson(answer) is not { } root || root.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException("AI cevabı JSON değil: " + (answer.Length > 200 ? answer[..200] : answer));
        var list = new List<SurecAiUygulama>();
        if (IslevService.Prop(root, "uygulamalar") is { ValueKind: System.Text.Json.JsonValueKind.Array } arr)
            foreach (var x in arr.EnumerateArray())
            {
                string? exe = IslevService.Str(x, "exe");
                if (exe == null) continue;
                var src = is_.FirstOrDefault(e => string.Equals(e.Ad, exe, StringComparison.OrdinalIgnoreCase));
                string? ertK = IslevService.Str(x, "ertKarsiligi") is { } k && k != "null"
                    ? env.AppNames.FirstOrDefault(a => string.Equals(a, k, StringComparison.OrdinalIgnoreCase)) ?? k : null;
                list.Add(new SurecAiUygulama(exe, src?.Yol, IslevService.Str(x, "uygulama"), IslevService.Str(x, "rol"), ertK,
                    Math.Clamp(IslevService.Int(x, "guven"), 0, 100), IslevService.Str(x, "gerekce")));
            }
        return new SurecAiSonuc(ip, IslevService.Str(root, "ozet"), list, genel, sw.ElapsedMilliseconds);
    }
}
