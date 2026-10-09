// ---------------------------------------------------------------------------
// F5 (BIG-IP ASM / Bot Defense) güvenlik logları — Splunk index=f5 (sourcetype f5:bigip:syslog).
// Yalnızca güvenlik olayları loglanıyor (tüm trafik değil): ASM request_status = illegal / challenged / legal / bot_signature,
// action = alarm (geçti, yalnız kayıt) / browser_challenge; Bot Defense / DoS: enforcement_action = block (gerçek engel) / none.
// Erişim sorgusu için: kaynak IP (ip_client / client_ip ya da XFF içinde) → hedef VIP (dest_ip) olayları sonuç bazında.
// Durum alanı ASM'de request_status, engel (Bot Defense / DoS) kayıtlarında req_status. Parola / kullanıcı gibi alanlar okunmaz.
// Sonuç sınıfları: engellendi (enforcement_action=block ya da request_status=blocked), doğrulama istendi (challenge),
// ihlal-geçti (illegal + alarm), geçti (legal), izlendi (enforcement_action=none), bot imzası.
// Alan adları F5:Alanlar ile değiştirilebilir; indeks F5:Index (varsayılan f5).
// ---------------------------------------------------------------------------
using System.Text.RegularExpressions;

record ErisimWaf(string Sonuc, long Count, string? First, string? Last, List<string> VsNames, List<string> AttackTypes,
    List<string> Violations, List<string> Uris, List<string> SupportIds, List<string> EnforcedBy, List<string> DestPorts);
record ErisimWafOlay(string Time, string Sonuc, string? Method, string? Uri, string? DestPort, string? VsName, string? AttackType,
    string? Violations, string? SupportId, string? RespCode);

static class F5Waf
{
    static readonly Regex SafeName = new("^[A-Za-z0-9_.:-]+$");
    public static bool Enabled(IConfiguration cfg) => cfg.GetValue("F5:Enabled", true);

    public static async Task<(SplunkResult summary, SplunkResult recent)> AccessAsync(LookupQuery q, string srcIp, string dstIp, string? port,
        IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("F5");
        var f = s.GetSection("Alanlar");
        string Name(string? value, string fallback)
        {
            string v = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return SafeName.IsMatch(v) ? v : throw new InvalidOperationException($"F5 ayarında geçersiz ad: {v}");
        }
        string index = Name(s["Index"], "f5");
        string cli = Name(f["ClientIp"], "ip_client"), cli2 = Name(f["ClientIp2"], "client_ip"), xff = Name(f["Xff"], "XFF");
        string dest = Name(f["DestIp"], "dest_ip"), dport = Name(f["DestPort"], "dest_port");
        // IP ve port çağıran tarafta doğrulanmış (yalnız rakam / nokta)
        string portFilter = port == null ? "" : $" AND '{dport}'=\"{port}\"";
        string bas = $"""
            search index={index} {dest}="{dstIp}" "{srcIp}"
            | eval cip=coalesce('{cli}', '{cli2}')
            | where (cip="{srcIp}" OR like('{xff}', "%{srcIp}%")) AND '{dest}'="{dstIp}"{portFilter}
            | eval ea=lower(enforcement_action), rs=lower(coalesce(request_status, req_status)), ac=lower(action),
                   sonuc=case(ea="block" OR rs="blocked" OR ac="block" OR ac="blocked", "engellendi",
                              like(ac, "%challenge%") OR rs="challenged", "doğrulama istendi",
                              rs="illegal", "ihlal (geçti)",
                              rs="bot_signature", "bot imzası",
                              rs="legal", "geçti",
                              ea="none", "izlendi (geçti)",
                              true(), coalesce(rs, ea, ac, "?")),
                   u=coalesce(uri, url, client_request_uri), dp='{dport}', vs=coalesce(vs_name, virtual_server_name)
            """;
        string summary = bas + """

            | stats count min(_time) as first max(_time) as last values(vs) as vs values(attack_type) as attack
                    values(violations) as viol values(u) as uri values(support_id) as sid values(enforced_by) as eby values(dp) as dp by sonuc
            | eval uri=mvjoin(mvindex(uri, 0, 9), "|||"), sid=mvjoin(mvindex(sid, 0, 4), "|||"), viol=mvjoin(mvindex(viol, 0, 5), "|||"),
                   attack=mvjoin(mvindex(attack, 0, 5), "|||"), vs=mvjoin(vs, "|||"), eby=mvjoin(eby, "|||"), dp=mvjoin(dp, "|||")
            | sort 0 - last
            | eval first=strftime(first, "%Y-%m-%d %H:%M:%S"), last=strftime(last, "%Y-%m-%d %H:%M:%S")
            """;
        string recent = bas + """

            | sort 0 - _time | head 15
            | eval t=strftime(_time, "%Y-%m-%d %H:%M:%S"), m=coalesce(method, http_method)
            | table t sonuc m u dp vs attack_type violations support_id resp_code
            """;
        var t1 = SplunkService.ExportAsync(summary, q, cfg, http, ct, "F5");
        var t2 = SplunkService.ExportAsync(recent, q, cfg, http, ct, "F5");
        await Task.WhenAll(t1, t2);
        return (t1.Result, t2.Result);
    }

    static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? EnvanterSnapshot.Clean(v) : null;
    static List<string> L(Dictionary<string, string> r, string k) =>
        (V(r, k) ?? "").Split(new[] { "|||", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    public static List<ErisimWaf> Summary(SplunkResult? r) => (r?.Rows ?? []).Select(x => new ErisimWaf(V(x, "sonuc") ?? "?",
        long.TryParse(V(x, "count"), out long c) ? c : 0, V(x, "first"), V(x, "last"), L(x, "vs"), L(x, "attack"), L(x, "viol"),
        L(x, "uri"), L(x, "sid"), L(x, "eby"), L(x, "dp"))).ToList();

    public static List<ErisimWafOlay> Recent(SplunkResult? r) => (r?.Rows ?? []).Select(x => new ErisimWafOlay(V(x, "t") ?? "",
        V(x, "sonuc") ?? "?", V(x, "m"), V(x, "u"), V(x, "dp"), V(x, "vs"), V(x, "attack_type"), V(x, "violations"),
        V(x, "support_id"), V(x, "resp_code"))).ToList();

    // IP görünümü: VIP(ler)e F5 üzerinden gelen istekler — URI + metot bazında sayı, istemci sayısı, sonuçlar, son zaman.
    // Not: F5'te yalnızca güvenlik olayları (ihlal, bot doğrulaması, engel, az sayıda legal) loglanıyor; liste tüm trafik değildir.
    public static async Task<SplunkResult> UrisAsync(LookupQuery q, IReadOnlyList<string> vips, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("F5");
        var f = s.GetSection("Alanlar");
        string Name(string? value, string fallback)
        {
            string v = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return SafeName.IsMatch(v) ? v : throw new InvalidOperationException($"F5 ayarında geçersiz ad: {v}");
        }
        string index = Name(s["Index"], "f5");
        string cli = Name(f["ClientIp"], "ip_client"), cli2 = Name(f["ClientIp2"], "client_ip");
        string dest = Name(f["DestIp"], "dest_ip"), dport = Name(f["DestPort"], "dest_port");
        // TERM() kullanılmaz: ham satırda IP "1.2.3.4:443" gibi yazılıysa TERM eşleşmez; alan filtresi güvenli.
        string terms = "(" + string.Join(" OR ", vips.Select(ip => $"{dest}=\"{ip}\"")) + ")";
        int max = s.GetValue("MaxUri", 300);
        string spl = $"""
            search index={index} {terms}
            | eval cip=coalesce('{cli}', '{cli2}'), u=coalesce(uri, url, client_request_uri), m=coalesce(method, http_method),
                   vip='{dest}', port='{dport}', vsn=coalesce(vs_name, virtual_server_name),
                   ea=lower(enforcement_action), rs=lower(coalesce(request_status, req_status)), ac=lower(action),
                   sonuc=case(ea="block" OR rs="blocked" OR ac="block" OR ac="blocked", "engellendi",
                              like(ac, "%challenge%") OR rs="challenged", "doğrulama",
                              rs="illegal", "ihlal (geçti)", rs="bot_signature", "bot imzası", true(), "geçti")
            | where isnotnull(u) AND u!=""
            | stats count dc(cip) as istemci max(_time) as last values(sonuc) as sonuc values(hostname) as host
                    values(vsn) as vs by vip port u m
            | sort 0 - count | head {max}
            | eval sonuc=mvjoin(sonuc, "|||"), host=mvjoin(mvindex(host, 0, 2), "|||"), vs=mvjoin(vs, "|||"),
                   last=strftime(last, "%Y-%m-%d %H:%M:%S")
            """;
        return await SplunkService.ExportAsync(spl, q, cfg, http, ct, "F5");
    }

    public static List<F5Uri> UriRows(SplunkResult? r) => (r?.Rows ?? []).Select(x => new F5Uri(V(x, "vip") ?? "", V(x, "port"),
        L(x, "vs"), L(x, "host"), V(x, "m"), V(x, "u") ?? "", long.TryParse(V(x, "count"), out long c) ? c : 0,
        int.TryParse(V(x, "istemci"), out int ic) ? ic : 0, L(x, "sonuc"), V(x, "last"))).ToList();
}

record F5Uri(string Vip, string? Port, List<string> VsNames, List<string> Hosts, string? Method, string Uri, long Count, int Clients,
    List<string> Sonuc, string? Last);
record F5UriSonuc(string Ip, List<string> Vipler, List<F5Uri> Satirlar, SourceStatus F5, long ElapsedMs);
