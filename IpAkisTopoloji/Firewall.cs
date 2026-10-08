using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Firewall (Palo Alto) trafik logları — Splunk üzerinden, Carbon Black ile aynı bağlantıyla.
// Her log satırı bir oturumdur: src_ip → dest_ip:dest_port + action (izin / engel).
// Sonuç satırları FlowBuilder'ın beklediği adlarla döner: client_ip, server_ip, server_port, count,
// first_seen, last_seen, fw_action, fw_denied (0/1), fw_rule, fw_device, fw_app.
// Alan adları sizin Splunk'ınıza göre Firewall:Alanlar ayarından değiştirilebilir.
// ---------------------------------------------------------------------------
static class FirewallService
{
    // Alan adı / indeks yalnızca config'den gelir; yine de SPL'ye yalnızca güvenli karakterler girer.
    static readonly Regex SafeName = new("^[A-Za-z0-9_.:-]+$");

    public static bool Enabled(IConfiguration cfg) => cfg.GetValue("Firewall:Enabled", true);

    public static async Task<SplunkResult> QueryAsync(LookupQuery q, IConfiguration cfg, HttpClient http, CancellationToken ct,
        IReadOnlyList<string>? ips = null)
    {
        ips ??= [q.Ip];
        var s = cfg.GetSection("Firewall");
        var f = s.GetSection("Alanlar");
        string Name(string? value, string fallback)
        {
            string v = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return SafeName.IsMatch(v) ? v : throw new InvalidOperationException($"Firewall ayarında geçersiz ad: {v}");
        }

        string index = Name(s["Index"], "fw_paloalto");
        string src = Name(f["SrcIp"], "src_ip"), dest = Name(f["DestIp"], "dest_ip"), dport = Name(f["DestPort"], "dest_port");
        string action = Name(f["Action"], "action"), rule = Name(f["Rule"], "rule");
        string device = Name(f["Device"], "dvc_name"), app = Name(f["App"], "app");

        // İzin sayılan action değerleri; geri kalanı (deny, drop, reset-both...) engellenmiş sayılır.
        var allowed = s.GetSection("IzinDegerleri").GetChildren().Select(c => (c.Value ?? "").Trim().ToLowerInvariant())
            .Where(v => v != "" && SafeName.IsMatch(v)).ToList();
        if (allowed.Count == 0) allowed = ["allowed", "allow", "accept"];
        string allowList = string.Join(", ", allowed.Select(v => $"\"{v}\""));

        string terms = ips.Count == 1 ? $"TERM({ips[0]})" : "(" + string.Join(" OR ", ips.Select(ip => $"TERM({ip})")) + ")";

        // TERM(): IP'nin geçmediği olaylar okunmaz. Kaynak port gruplamaya girmez (her oturumda değişir).
        string spl = $"""
            search index={index} {terms}
            | eval client_ip='{src}', server_ip='{dest}', server_port='{dport}', fw_action=lower('{action}'),
                   fw_rule='{rule}', fw_device='{device}', fw_app='{app}'
            | eval fw_denied=if(in(fw_action, {allowList}), 0, 1)
            | stats count min(_time) as first_seen max(_time) as last_seen
                    values(fw_rule) as fw_rule values(fw_device) as fw_device values(fw_app) as fw_app
                    by client_ip server_ip server_port fw_action fw_denied
            | sort 0 - count
            | eval first_seen=strftime(first_seen, "%Y-%m-%d %H:%M:%S"), last_seen=strftime(last_seen, "%Y-%m-%d %H:%M:%S")
            """;

        return await SplunkService.ExportAsync(spl, q, cfg, http, ct, "Firewall");
    }

    // Oturum akışı için oturumlar tek tek: başlangıç (log zamanı - duration), bitiş, istemci IP:port, sunucu IP:port.
    // Yalnızca izin verilen oturumlar; en fazla Firewall:MaxOturum (varsayılan 50000) satır.
    // NAT: hedef adresin çevrilmiş hali (dest_translated_ip) varsa sunucu IP'si olarak o kullanılır (dış IP → DMZ/VIP).
    public static async Task<SplunkResult> SessionsAsync(LookupQuery q, IReadOnlyList<string> ips, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Firewall");
        var f = s.GetSection("Alanlar");
        string Name(string? value, string fallback)
        {
            string v = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return SafeName.IsMatch(v) ? v : throw new InvalidOperationException($"Firewall ayarında geçersiz ad: {v}");
        }
        string index = Name(s["Index"], "fw_paloalto");
        string src = Name(f["SrcIp"], "src_ip"), sport = Name(f["SrcPort"], "src_port");
        string dest = Name(f["DestIp"], "dest_ip"), dport = Name(f["DestPort"], "dest_port");
        string action = Name(f["Action"], "action"), dur = Name(f["Duration"], "duration");
        string natDest = Name(f["NatDestIp"], "dest_translated_ip"), natPort = Name(f["NatDestPort"], "dest_translated_port");
        string terms = ips.Count == 1 ? $"TERM({ips[0]})" : "(" + string.Join(" OR ", ips.Select(ip => $"TERM({ip})")) + ")";
        string ipList = string.Join(", ", ips.Select(ip => $"\"{ip}\""));
        var allowed = s.GetSection("IzinDegerleri").GetChildren().Select(c => (c.Value ?? "").Trim().ToLowerInvariant())
            .Where(v => v != "" && SafeName.IsMatch(v)).ToList();
        if (allowed.Count == 0) allowed = ["allowed", "allow", "accept"];
        int max = s.GetValue("MaxOturum", 50000);

        string spl = $"""
            search index={index} {terms}
            | eval nat=if(isnull('{natDest}') OR '{natDest}'="" OR '{natDest}'="0.0.0.0", null(), '{natDest}')
            | eval cip='{src}', cport='{sport}', sip=coalesce(nat, '{dest}'),
                   sport=if(isnull(nat), '{dport}', coalesce(nullif('{natPort}', "0"), '{dport}')), a=lower('{action}')
            | where (in(cip, {ipList}) OR in(sip, {ipList})) AND in(a, {string.Join(", ", allowed.Select(v => $"\"{v}\""))})
            | eval e=_time, s=_time-coalesce(tonumber('{dur}'), 0)
            | table s e cip cport sip sport
            | head {max}
            """;
        return await SplunkService.ExportAsync(spl, q, cfg, http, ct, "Firewall");
    }

    // Erişim sorgusu: kaynak IP'den hedef IP'ye (hedef NAT'lı adres de olabilir) oturumlar.
    // Özet (port + action bazında sayı, ilk/son zaman, kural, cihaz, uygulama, kapanma nedeni) ve son 20 olay.
    public static async Task<(SplunkResult summary, SplunkResult recent)> AccessAsync(LookupQuery q, string srcIp, string dstIp, string? port,
        IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Firewall");
        var f = s.GetSection("Alanlar");
        string Name(string? value, string fallback)
        {
            string v = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return SafeName.IsMatch(v) ? v : throw new InvalidOperationException($"Firewall ayarında geçersiz ad: {v}");
        }
        string index = Name(s["Index"], "fw_paloalto");
        string src = Name(f["SrcIp"], "src_ip"), sport = Name(f["SrcPort"], "src_port");
        string dest = Name(f["DestIp"], "dest_ip"), dport = Name(f["DestPort"], "dest_port");
        string action = Name(f["Action"], "action"), rule = Name(f["Rule"], "rule");
        string device = Name(f["Device"], "dvc_name"), app = Name(f["App"], "app");
        string reason = Name(f["Reason"], "session_end_reason");
        string natDest = Name(f["NatDestIp"], "dest_translated_ip"), natPort = Name(f["NatDestPort"], "dest_translated_port");
        var allowed = s.GetSection("IzinDegerleri").GetChildren().Select(c => (c.Value ?? "").Trim().ToLowerInvariant())
            .Where(v => v != "" && SafeName.IsMatch(v)).ToList();
        if (allowed.Count == 0) allowed = ["allowed", "allow", "accept"];
        string allowList = string.Join(", ", allowed.Select(v => $"\"{v}\""));
        // IP'ler ve port çağıran tarafta doğrulanmış olmalı (yalnızca rakam ve nokta)
        string portFilter = port == null ? "" : $"\n| where '{dport}'=\"{port}\" OR '{natPort}'=\"{port}\"";

        string bas = $"""
            search index={index} TERM({srcIp}) TERM({dstIp})
            | eval nat=if(isnull('{natDest}') OR '{natDest}'="" OR '{natDest}'="0.0.0.0", null(), '{natDest}')
            | where '{src}'="{srcIp}" AND ('{dest}'="{dstIp}" OR nat="{dstIp}"){portFilter}
            | eval a=lower('{action}'), denied=if(in(a, {allowList}), 0, 1), dp='{dport}', ndp=if(isnull(nat), null(), '{natPort}')
            """;
        string summary = bas + $"""

            | stats count min(_time) as first max(_time) as last values('{rule}') as rule values('{device}') as device
                    values('{app}') as app values('{reason}') as reason values(nat) as nat values(ndp) as nat_port by dp a denied
            | sort 0 - last
            | eval first=strftime(first, "%Y-%m-%d %H:%M:%S"), last=strftime(last, "%Y-%m-%d %H:%M:%S")
            """;
        string recent = bas + $"""

            | sort 0 - _time | head 20
            | eval t=strftime(_time, "%Y-%m-%d %H:%M:%S"), sp='{sport}', d='{dest}', r='{rule}', why='{reason}', dev='{device}'
            | table t sp d dp nat ndp a denied r why dev
            """;
        var t1 = SplunkService.ExportAsync(summary, q, cfg, http, ct, "Firewall");
        var t2 = SplunkService.ExportAsync(recent, q, cfg, http, ct, "Firewall");
        await Task.WhenAll(t1, t2);
        return (t1.Result, t2.Result);
    }
}
