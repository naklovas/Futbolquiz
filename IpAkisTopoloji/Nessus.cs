using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Nessus / Tenable.sc (kullanıcı: "sunucuda çalışan uygulamanın teknolojisi ASP mi PHP mi"; diğer projedeki guide'a göre): repository'deki
// BİRİKMİŞ tarama sonuçları okunur (POST /rest/analysis, sourceType "cumulative", tool "vulndetails") — yeni tarama başlatılmaz.
// Yalnız sorulan IP'ler ve teknoloji / envanter bilgisi veren bilgi plugin'leri (Nessus:Pluginler) istenir; bilgi plugin'in çıktısında
// (pluginText) → port başına web sunucusu, teknoloji (ASP.NET / PHP / Java…), SSL sertifika adları, portu dinleyen program, servis;
// sunucu başına işletim sistemi, kimlik bilgili tarama mı, kurulu yazılımlar, CPE'ler. Kimlik: x-apikey (AccessKey / SecretKey
// yalnız appsettings.Production.json). Ayarlar: Nessus:BaseUrl, Repolar ([121]), MaxGun (30, lastSeen), ParcaIp, SayfaBoyutu, CacheDakika,
// IgnoreSslErrors. BaseUrl boşsa kapalı (segment tarama Nessus'suz çalışır).
// ---------------------------------------------------------------------------
record NessusPort(string Port, string Protokol, string? Servis, string? WebSunucu, List<string> Teknoloji, List<string> Sertifika, string? Dinleyen, string? Son);
record NessusSunucu(string Ip, string? Dns, string? Netbios, string? Os, bool? Kimlikli, string? Son, string? Repo,
    List<NessusPort> Portlar, List<string> Yazilim, List<string> Cpe);

sealed class NessusService(IConfiguration cfg, IHttpClientFactory factory, ILogger<NessusService> log)
{
    // 19506 tarama bilgisi (kimlikli mi), 11936 işletim sistemi, 45590 CPE, 10107 web sunucusu, 24260 HTTP başlıkları, 22964 servis,
    // 10863 SSL sertifikası, 34252 / 25221 portu dinleyen program (Windows / Linux), 20811 / 22869 kurulu yazılım,
    // 11219 / 10335 / 34220 / 14272 açık port (SYN / TCP / netstat WMI / netstat SSH)
    static readonly int[] DefaultPluginler = [19506, 11936, 45590, 10107, 24260, 22964, 10863, 34252, 25221, 20811, 22869, 11219, 10335, 34220, 14272];

    public bool Acik => !string.IsNullOrWhiteSpace(cfg["Nessus:BaseUrl"]);
    int[] Pluginler => cfg.GetSection("Nessus:Pluginler").Get<int[]>() is { Length: > 0 } p ? p : DefaultPluginler;
    string[] Repolar => cfg.GetSection("Nessus:Repolar").GetChildren().Select(c => c.Value?.Trim()).OfType<string>().Where(v => v != "").ToArray();

    readonly ConcurrentDictionary<string, (NessusSunucu? S, DateTime T)> _cache = new();

    HttpRequestMessage Istek(HttpMethod m, string yol, JsonNode? govde = null)
    {
        string url = cfg["Nessus:BaseUrl"]!.TrimEnd('/');
        var req = new HttpRequestMessage(m, url + yol);
        // Tenable.sc: x-apikey: accesskey=…; secretkey=…;   (Nessus Pro: X-ApiKeys: accessKey=…; secretKey=…;)
        req.Headers.TryAddWithoutValidation("x-apikey", $"accesskey={cfg["Nessus:AccessKey"]}; secretkey={cfg["Nessus:SecretKey"]};");
        if (govde != null) req.Content = new StringContent(govde.ToJsonString(), Encoding.UTF8, "application/json");
        return req;
    }

    async Task<JsonNode> GonderAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await factory.CreateClient("nessus").SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"Tenable.sc HTTP {(int)res.StatusCode}: {(body.Length > 300 ? body[..300] : body)}");
        var node = JsonNode.Parse(body) ?? throw new InvalidOperationException("Tenable.sc boş yanıt döndü.");
        if (int.TryParse(node["error_code"]?.ToString(), out int ec) && ec != 0)
            throw new InvalidOperationException($"Tenable.sc hata {ec}: {node["error_msg"]}");
        return node;
    }

    // Kurulum testi: anahtarın görebildiği repository'ler
    public async Task<JsonNode?> RepolarAsync(CancellationToken ct)
    {
        if (!Acik) throw new InvalidOperationException("Nessus:BaseUrl tanımlı değil.");
        return (await GonderAsync(Istek(HttpMethod.Get, "/rest/repository?fields=id,name,description,type"), ct))["response"];
    }

    JsonObject Sorgu(IEnumerable<string> ips, int bas, int bit, bool tumPluginler)
    {
        var filtre = new JsonArray { new JsonObject { ["filterName"] = "ip", ["operator"] = "=", ["value"] = string.Join(",", ips) } };
        if (!tumPluginler) filtre.Add(new JsonObject { ["filterName"] = "pluginID", ["operator"] = "=", ["value"] = string.Join(",", Pluginler) });
        if (Repolar.Length > 0)
            filtre.Add(new JsonObject { ["filterName"] = "repository", ["operator"] = "=", ["value"] = new JsonArray(Repolar.Select(r => (JsonNode)new JsonObject { ["id"] = r }).ToArray()) });
        int gun = cfg.GetValue("Nessus:MaxGun", 30);
        if (gun > 0) filtre.Add(new JsonObject { ["filterName"] = "lastSeen", ["operator"] = "=", ["value"] = $"0:{gun}" });
        return new JsonObject
        {
            ["type"] = "vuln", ["sourceType"] = "cumulative",
            ["query"] = new JsonObject { ["type"] = "vuln", ["tool"] = "vulndetails", ["startOffset"] = bas, ["endOffset"] = bit, ["filters"] = filtre }
        };
    }

    async Task<List<JsonNode>> KayitlarAsync(IReadOnlyCollection<string> ips, bool tumPluginler, CancellationToken ct)
    {
        int sayfa = Math.Clamp(cfg.GetValue("Nessus:SayfaBoyutu", 2000), 100, 10000), maxSayfa = cfg.GetValue("Nessus:MaxSayfa", 20);
        var list = new List<JsonNode>();
        for (int i = 0, bas = 0; i < maxSayfa; i++, bas += sayfa)
        {
            var r = (await GonderAsync(Istek(HttpMethod.Post, "/rest/analysis", Sorgu(ips, bas, bas + sayfa, tumPluginler)), ct))["response"];
            var res = r?["results"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
            list.AddRange(res);
            if (!long.TryParse(r?["totalRecords"]?.ToString(), out long top) || bas + sayfa >= top || res.Count == 0) break;
        }
        return list;
    }

    // Deneme ucu: o IP'nin ham kayıtları (tum = plugin filtresi olmadan, ilk sayfa)
    public async Task<object> TestAsync(string ip, bool tum, CancellationToken ct)
    {
        if (!Acik) throw new InvalidOperationException("Nessus:BaseUrl tanımlı değil.");
        var raw = await KayitlarAsync([ip], tum, ct);
        return new
        {
            kayit = raw.Count,
            pluginler = raw.GroupBy(x => S(x, "pluginID")).Select(g => new { id = g.Key, ad = S(g.First(), "pluginName"), adet = g.Count() }).OrderBy(x => x.id).ToList(),
            ozet = Coz(ip, raw),
            ham = raw.Take(tum ? 100 : 300)
        };
    }

    // Sunucu başına özet (önbellekli; kaydı olmayan IP null). Hata fırlatır — çağıran uyarıya çevirir.
    public async Task<Dictionary<string, NessusSunucu>> SunucularAsync(IEnumerable<string> ipler, Action<int, int>? ilerleme, CancellationToken ct)
    {
        var sonuc = new Dictionary<string, NessusSunucu>();
        if (!Acik) return sonuc;
        var ttl = TimeSpan.FromMinutes(cfg.GetValue("Nessus:CacheDakika", 60));
        var sor = new List<string>();
        foreach (var ip in ipler.Distinct())
            if (_cache.TryGetValue(ip, out var c) && DateTime.Now - c.T < ttl) { if (c.S != null) sonuc[ip] = c.S; }
            else sor.Add(ip);
        var parcalar = sor.Chunk(Math.Clamp(cfg.GetValue("Nessus:ParcaIp", 100), 1, 500)).ToList();
        for (int i = 0; i < parcalar.Count; i++)
        {
            ilerleme?.Invoke(i, parcalar.Count);
            var raw = await KayitlarAsync(parcalar[i], false, ct);
            var byIp = raw.GroupBy(x => S(x, "ip") ?? "").ToDictionary(g => g.Key, g => g.ToList());
            foreach (var ip in parcalar[i])
            {
                var s = byIp.TryGetValue(ip, out var rs) ? Coz(ip, rs) : null;
                _cache[ip] = (s, DateTime.Now);
                if (s != null) sonuc[ip] = s;
            }
        }
        log.LogInformation("Nessus: {N} IP soruldu ({Parca} sorgu), {K} IP'de kayıt", sor.Count, parcalar.Count, sonuc.Count);
        return sonuc;
    }

    static string? S(JsonNode? n, string k) => n?[k] switch { null => null, JsonValue v => v.ToString(), var o => o["name"]?.ToString() ?? o.ToJsonString() };

    // pluginText: "<plugin_output>…</plugin_output>" (bazı sürümlerde düz metin)
    static string Cikti(JsonNode r)
    {
        string t = S(r, "pluginText") ?? "";
        var m = Regex.Match(t, @"<plugin_output>(.*?)</plugin_output>", RegexOptions.Singleline);
        return System.Net.WebUtility.HtmlDecode(m.Success ? m.Groups[1].Value : t).Replace("\r", "");
    }

    static string? Zaman(string? t) => long.TryParse(t, out long u) && u > 0 ? DateTimeOffset.FromUnixTimeSeconds(u).ToLocalTime().ToString("yyyy-MM-dd") : t;

    // Kurulu yazılımlarda gürültü (güncellemeler, çalışma zamanları, ajanlar)
    static readonly Regex YazilimGurultu = new(@"visual c\+\+|redistributable|security update|update for|hotfix|\bkb\d{6,}|language pack|microsoft edge|google chrome|mozilla|"
        + @"vmware tools|trend micro|carbon black|splunk|sitescope|veeam|7-zip|notepad\+\+|winrar|adobe|microsoft (office|onedrive|teams)|"
        + @"windows (driver|software development)|sql server .*(setup|browser|vss writer|telemetry|native client|odbc|management objects)", RegexOptions.IgnoreCase);
    // Linux paketlerinden yalnız teknoloji olanlar (22869 listesi çok uzun)
    static readonly Regex LinuxTeknoloji = new(@"^(httpd|nginx|php|tomcat|java|jdk|openjdk|jre|python3?\b|nodejs|postgres|mysql|mariadb|oracle|weblogic|jboss|wildfly|"
        + @"websphere|redis|mongo|kafka|elasticsearch|docker|containerd|kube|haproxy|sas|informatica|ibm|mq|rabbitmq|zookeeper|hadoop|spark|dotnet|aspnetcore|gunicorn|uwsgi)", RegexOptions.IgnoreCase);

    static NessusSunucu? Coz(string ip, List<JsonNode> rs)
    {
        if (rs.Count == 0) return null;
        var portlar = new Dictionary<(string, string), (string? Servis, string? Web, List<string> Tek, List<string> Cert, string? Dinleyen, string? Son)>();
        (string? Servis, string? Web, List<string> Tek, List<string> Cert, string? Dinleyen, string? Son) P(JsonNode r)
        {
            var k = (S(r, "port") ?? "0", (S(r, "protocol") ?? "tcp").ToLowerInvariant());
            if (!portlar.TryGetValue(k, out var v)) portlar[k] = v = (null, null, [], [], null, null);
            return v;
        }
        void Yaz(JsonNode r, Func<(string? Servis, string? Web, List<string> Tek, List<string> Cert, string? Dinleyen, string? Son), (string?, string?, List<string>, List<string>, string?, string?)> f)
        {
            var k = (S(r, "port") ?? "0", (S(r, "protocol") ?? "tcp").ToLowerInvariant());
            var v = f(P(r)); string? son = Zaman(S(r, "lastSeen"));
            portlar[k] = (v.Item1, v.Item2, v.Item3, v.Item4, v.Item5, string.CompareOrdinal(son, v.Item6) > 0 ? son : v.Item6);
        }
        string? os = rs.Select(r => S(r, "operatingSystem")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        bool? kimlikli = null; var yazilim = new List<string>(); var cpe = new List<string>();
        foreach (var r in rs)
        {
            string id = S(r, "pluginID") ?? "", o = Cikti(r);
            switch (id)
            {
                case "19506":
                    if (Regex.Match(o, @"Credentialed checks\s*:\s*(\w+)", RegexOptions.IgnoreCase) is { Success: true } m19)
                        kimlikli = (kimlikli ?? false) || m19.Groups[1].Value.StartsWith("yes", StringComparison.OrdinalIgnoreCase);
                    break;
                case "11936":
                    if (Regex.Match(o, @"Remote operating system\s*:\s*(.+)", RegexOptions.IgnoreCase) is { Success: true } m11) os = m11.Groups[1].Value.Trim();
                    break;
                case "45590":
                    foreach (Match m in Regex.Matches(o, @"cpe:/a:([^:\s]+):([^:\s]+)(?::([^:\s]+))?"))
                        cpe.Add($"{m.Groups[1].Value}:{m.Groups[2].Value}{(m.Groups[3].Success ? " " + m.Groups[3].Value : "")}");
                    break;
                case "10107":
                    if (o.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.EndsWith(':')) is { } srv)
                        Yaz(r, v => (v.Servis ?? "web", srv, Ekle(v.Tek, WebTeknoloji(srv)), v.Cert, v.Dinleyen, v.Son));
                    break;
                case "24260":
                    Yaz(r, v => (v.Servis ?? "web", v.Web ?? Baslik(o, "Server"), Ekle(v.Tek, HttpTeknoloji(o)), v.Cert, v.Dinleyen, v.Son));
                    break;
                case "22964":
                    if (Regex.Match(o, @"An? (.+?) is running on this port", RegexOptions.IgnoreCase) is { Success: true } m22)
                        Yaz(r, v => (m22.Groups[1].Value.Trim(), v.Web, v.Tek, v.Cert, v.Dinleyen, v.Son));
                    break;
                case "10863":
                    var adlar = new List<string>();
                    // Subject Name bölümündeki Common Name + SAN DNS adları (Issuer'ınki değil)
                    var sub = Regex.Match(o, @"Subject Name\s*:(.*?)(Issuer Name|Serial Number)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (Regex.Match(sub.Success ? sub.Groups[1].Value : o, @"Common Name\s*:\s*(\S+)", RegexOptions.IgnoreCase) is { Success: true } cn) adlar.Add(cn.Groups[1].Value);
                    foreach (Match m in Regex.Matches(o, @"DNS\s*:\s*([^\s,]+)", RegexOptions.IgnoreCase)) adlar.Add(m.Groups[1].Value);
                    Yaz(r, v => (v.Servis, v.Web, v.Tek, Ekle(v.Cert, adlar), v.Dinleyen, v.Son));
                    break;
                case "34252": case "25221":
                    var ex = Regex.Match(o, @"(?:Executable|Process name|Process)\s*:\s*(.+)", RegexOptions.IgnoreCase);
                    if (ex.Success) Yaz(r, v => (v.Servis, v.Web, v.Tek, v.Cert, ex.Groups[1].Value.Trim(), v.Son));
                    break;
                case "20811":
                    foreach (var l in o.Split('\n').Select(l => Regex.Replace(l, @"\[.*?\]", "").Trim()))
                        if (l.Length > 2 && !l.EndsWith(':') && !l.StartsWith("The following", StringComparison.OrdinalIgnoreCase) && !YazilimGurultu.IsMatch(l)) yazilim.Add(l);
                    break;
                case "22869":
                    foreach (var l in o.Split('\n').Select(l => l.Trim()))
                        if (LinuxTeknoloji.IsMatch(l)) yazilim.Add(l.Split(' ', '|')[0]);
                    break;
                default:   // port tarayıcıları: yalnız açık port bilgisi
                    if (S(r, "port") is { } pt && pt != "0") Yaz(r, v => v);
                    break;
            }
        }
        var list = portlar.Where(kv => kv.Key.Item1 != "0" || kv.Value.Web != null || kv.Value.Cert.Count > 0)
            .Select(kv => new NessusPort(kv.Key.Item1, kv.Key.Item2, kv.Value.Servis, kv.Value.Web, Tekil(kv.Value.Tek),
                kv.Value.Cert.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(), kv.Value.Dinleyen, kv.Value.Son))
            .OrderBy(p => int.TryParse(p.Port, out int n) ? n : 99999).ToList();
        var r0 = rs[0];
        return new NessusSunucu(ip, S(r0, "dnsName") is { Length: > 0 } d ? d : null, S(r0, "netbiosName") is { Length: > 0 } nb ? nb : null, os, kimlikli,
            rs.Select(r => Zaman(S(r, "lastSeen"))).Max(), S(r0, "repository"), list,
            yazilim.Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList(), cpe.Distinct(StringComparer.OrdinalIgnoreCase).Take(30).ToList());
    }

    // "ASP.NET" + "ASP.NET 4.0.30319" → yalnız sürümlü olan
    static List<string> Tekil(List<string> t)
    {
        var d = t.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return d.Where(x => !d.Any(y => y.Length > x.Length && y.StartsWith(x + " ", StringComparison.OrdinalIgnoreCase))).ToList();
    }
    static List<string> Ekle(List<string> a, IEnumerable<string> b) { a.AddRange(b); return a; }
    static string? Baslik(string o, string ad) => Regex.Match(o, $@"^\s*{Regex.Escape(ad)}\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    // Server başlığından teknoloji
    static IEnumerable<string> WebTeknoloji(string srv)
    {
        string s = srv.ToLowerInvariant();
        if (s.Contains("microsoft-iis")) yield return "IIS" + (Regex.Match(srv, @"IIS/([\d.]+)", RegexOptions.IgnoreCase) is { Success: true } m ? " " + m.Groups[1].Value : "");
        if (s.Contains("microsoft-httpapi")) yield return "HTTP.sys";
        if (s.Contains("kestrel")) yield return "ASP.NET Core (Kestrel)";
        if (s.Contains("apache-coyote") || s.Contains("tomcat")) yield return "Tomcat (Java)";
        else if (s.StartsWith("apache")) yield return "Apache";
        if (s.Contains("nginx")) yield return "nginx";
        if (s.Contains("jetty")) yield return "Jetty (Java)";
        if (s.Contains("weblogic")) yield return "WebLogic (Java)";
        if (s.Contains("websphere") || s.Contains("ibm_http_server")) yield return "IBM WebSphere / IHS";
        if (s.Contains("jboss") || s.Contains("wildfly") || s.Contains("undertow")) yield return "JBoss / WildFly (Java)";
        if (s.Contains("gunicorn") || s.Contains("uvicorn") || s.Contains("werkzeug")) yield return "Python";
        if (s.Contains("node") || s.Contains("express")) yield return "Node.js";
        if (Regex.Match(srv, @"PHP/([\d.]+)", RegexOptions.IgnoreCase) is { Success: true } php) yield return "PHP " + php.Groups[1].Value;
        if (Regex.Match(srv, @"OpenSSL/([\d.]+\w*)", RegexOptions.IgnoreCase) is { Success: true } ssl) yield return "OpenSSL " + ssl.Groups[1].Value;
    }

    // HTTP başlıkları ve çerezlerden uygulama teknolojisi
    static IEnumerable<string> HttpTeknoloji(string o)
    {
        if (Baslik(o, "Server") is { } srv) foreach (var t in WebTeknoloji(srv)) yield return t;
        foreach (Match m in Regex.Matches(o, @"^\s*X-Powered-By\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            string v = m.Groups[1].Value.Trim();
            yield return v.StartsWith("ASP.NET", StringComparison.OrdinalIgnoreCase) ? "ASP.NET" : v.StartsWith("PHP", StringComparison.OrdinalIgnoreCase) ? v.Replace("/", " ") : v;
        }
        if (Baslik(o, "X-AspNet-Version") is { } an) yield return "ASP.NET " + an;
        if (Baslik(o, "X-AspNetMvc-Version") is { } mvc) yield return "ASP.NET MVC " + mvc;
        if (Baslik(o, "X-Generator") is { } gen) yield return gen;
        string l = o.ToLowerInvariant();
        if (l.Contains("asp.net_sessionid") || l.Contains(".aspxauth")) yield return "ASP.NET";
        if (Regex.IsMatch(l, @"aspsessionid")) yield return "Klasik ASP";
        if (l.Contains("phpsessid")) yield return "PHP";
        if (l.Contains("laravel_session")) yield return "PHP (Laravel)";
        if (l.Contains("jsessionid")) yield return "Java (JSESSIONID)";
        if (l.Contains("ci_session")) yield return "PHP (CodeIgniter)";
        if (l.Contains("csrftoken") && l.Contains("sessionid")) yield return "Python (Django)";
        if (l.Contains("connect.sid")) yield return "Node.js (Express)";
    }

    // AI kanıtı (segment tarama AI tahmini, IP kartı)
    public static string Metin(NessusSunucu n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"NESSUS (Tenable.sc, son tarama {n.Son}{(n.Kimlikli == true ? ", kimlik bilgili" : n.Kimlikli == false ? ", kimlik bilgisiz / dış tarama" : "")}):");
        if (n.Os != null) sb.AppendLine($"- İşletim sistemi: {n.Os}");
        if (n.Dns != null || n.Netbios != null) sb.AppendLine($"- DNS / NetBIOS: {string.Join(" · ", new[] { n.Dns, n.Netbios }.OfType<string>())}");
        foreach (var p in n.Portlar.Take(30))
        {
            var parca = new List<string>();
            if (p.Servis != null) parca.Add(p.Servis);
            if (p.WebSunucu != null) parca.Add("Server: " + p.WebSunucu);
            if (p.Teknoloji.Count > 0) parca.Add("teknoloji: " + string.Join(", ", p.Teknoloji));
            if (p.Sertifika.Count > 0) parca.Add("SSL sertifika adları: " + string.Join(", ", p.Sertifika.Take(6)));
            if (p.Dinleyen != null) parca.Add("dinleyen program: " + p.Dinleyen);
            sb.AppendLine($"- port {p.Port}/{p.Protokol}: {(parca.Count > 0 ? string.Join(" · ", parca) : "açık")}");
        }
        if (n.Yazilim.Count > 0) sb.AppendLine($"- Kurulu yazılımlar: {string.Join("; ", n.Yazilim.Take(40))}");
        if (n.Cpe.Count > 0) sb.AppendLine($"- CPE: {string.Join(", ", n.Cpe.Take(20))}");
        sb.AppendLine("(SSL sertifika adı ve kurulu yazılım uygulama adı için güçlü ipucudur; teknoloji bilgisi uygulama adı DEĞİLDİR.)");
        return sb.ToString();
    }
}
