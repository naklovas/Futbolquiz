using System.Collections.Concurrent;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// Opinnate (firewall kural yönetimi) — tanımlı kurala göre erişim: "trafik olmuş mu" değil "kural açık mı".
//  - Giriş: Opinnate:Token doluysa doğrudan kullanılır; boşsa POST {BaseUrl}/login {username, password} ile alınır.
//    Yanıtta token / access_token / accessToken / jwt / api_key / apikey / key alanlarından ilki (iç içe de aranır)
//    kullanılır; hiçbiri yoksa ve oturum çerezi döndüyse çerezle devam edilir. Bellekte Opinnate:TokenDakika (30) tutulur,
//    401 / 403'te bir kez yeniden alınır. Başlık: Opinnate:TokenHeader (varsayılan Authorization) + TokenPrefix ("Bearer ").
//  - Sorgu: POST {BaseUrl}/rule-checker/topology { source: ["ip/32"], destination: ["ip/32"], service: ["tcp_443"],
//    virtualAreaId, user_base: false, users: [] }. Port yoksa Opinnate:ServisHepsi ("any"); biçim Opinnate:ServisFormat ("tcp_{port}").
//  - Yanıt: policyResultData[] → PathResult[] (yol üzerindeki her cihaz): src_int, dst_int, device_id,
//    existPolicy (eşleşen kural; null = o cihazda eşleşen kural yok — L3 switch / router da olabilir).
//  - Cihaz adları GET {BaseUrl}/devices'tan (biçim bilinmediği için esnek okunur, 1 saat önbellek; olmazsa "Cihaz #id").
// Yalnızca okuma: kural / görev / cihaz değiştiren uçlar kullanılmaz. Token ve şifre loglanmaz.
// ---------------------------------------------------------------------------
record OpinnateKural(string? Id, string? PolicyId, string Action, bool Aktif, List<string> SrcIntf, List<string> DstIntf,
    List<string> Source, List<string> Destination, List<string> Service, bool SrcNegate, bool DstNegate,
    string? Comment, string? Schedule, string? Direction, string? Nat, string? Log, List<string> Users, string? Application, string? ProfileGroup);
record OpinnateAdim(string? DeviceId, string Cihaz, List<string> Giris, List<string> Cikis, OpinnateKural? Kural);
record OpinnateYol(string? Kaynak, string? Hedef, List<string> Servis, List<OpinnateAdim> Adimlar);
record OpinnateSonuc(List<OpinnateYol> Yollar, string Servis, List<string> Mesajlar, long ElapsedMs)
{
    public IEnumerable<OpinnateAdim> Adimlar => Yollar.SelectMany(y => y.Adimlar);
}

static class OpinnateService
{
    static readonly ConcurrentDictionary<string, (string? token, string? cookie, DateTimeOffset until)> Sessions = new();
    static readonly ConcurrentDictionary<string, (Dictionary<string, string> names, DateTimeOffset until)> Devices = new();
    static readonly SemaphoreSlim LoginLock = new(1, 1);
    static readonly string[] TokenKeys = ["token", "access_token", "accessToken", "jwt", "api_key", "apikey", "apiKey", "key"];

    public static bool Enabled(IConfiguration cfg) =>
        cfg.GetValue("Opinnate:Enabled", true) && !string.IsNullOrWhiteSpace(cfg["Opinnate:BaseUrl"]);

    // "https//x" düzeltilir, şema yoksa eklenir, sondaki / ve yanlışlıkla yazılmış /docs, /login, uç yolu kesilir.
    static string BaseUrl(IConfiguration cfg)
    {
        string u = System.Text.RegularExpressions.Regex.Replace(cfg["Opinnate:BaseUrl"] ?? "", @"[​-‍﻿ <>\s""']", "");
        u = System.Text.RegularExpressions.Regex.Replace(u, @"^(https?)/+", "$1://", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!u.Contains("://")) u = "https://" + u;
        u = u.TrimEnd('/');
        foreach (var suffix in new[] { "/docs", "/login", "/rule-checker/topology", "/swagger" })
            if (u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) u = u[..^suffix.Length];
        if (!Uri.TryCreate(u, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Opinnate:BaseUrl geçersiz (okunan: \"{u}\"). Örnek: https://opinnate.kurum.local/api");
        return u;
    }

    static async Task<(string? token, string? cookie)> SessionAsync(IConfiguration cfg, HttpClient http, CancellationToken ct, bool force = false)
    {
        var s = cfg.GetSection("Opinnate");
        if (!string.IsNullOrWhiteSpace(s["Token"])) return (s["Token"]!.Trim(), null);
        string key = BaseUrl(cfg) + "|" + s["Username"];
        if (!force && Sessions.TryGetValue(key, out var t) && t.until > DateTimeOffset.UtcNow) return (t.token, t.cookie);
        await LoginLock.WaitAsync(ct);
        try
        {
            if (!force && Sessions.TryGetValue(key, out t) && t.until > DateTimeOffset.UtcNow) return (t.token, t.cookie);
            if (string.IsNullOrWhiteSpace(s["Username"]))
                throw new InvalidOperationException("Opinnate: Token ya da Username / Password tanımlı değil.");
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl(cfg) + (s["LoginYolu"] ?? "/login"))
            {
                Content = Json(new { username = s["Username"], password = s["Password"] ?? "" })
            };
            using var resp = await SendAsync(http, req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Opinnate giriş HTTP {(int)resp.StatusCode}: kullanıcı / şifre ya da BaseUrl'i kontrol edin.");
            JsonNode? j = null;
            try { j = JsonNode.Parse(body); } catch { }
            string? token = FindToken(j);
            string? cookie = resp.Headers.TryGetValues("Set-Cookie", out var cs)
                ? string.Join("; ", cs.Select(c => c.Split(';')[0]).Where(c => c.Contains('='))) : null;
            if (token == null && string.IsNullOrEmpty(cookie))
            {
                string alanlar = j is JsonObject o ? string.Join(", ", o.Select(p => p.Key)) : "(JSON değil)";
                throw new HttpRequestException($"Opinnate giriş yanıtında token bulunamadı. Yanıttaki alanlar: {alanlar}. " +
                    "Alan adını yazın, kod ona göre ayarlansın; ya da Opinnate:Token'a API anahtarını girin.");
            }
            Sessions[key] = (token, string.IsNullOrEmpty(cookie) ? null : cookie, DateTimeOffset.UtcNow.AddMinutes(s.GetValue("TokenDakika", 30)));
            return (token, cookie);
        }
        finally { LoginLock.Release(); }
    }

    static string? FindToken(JsonNode? n, int depth = 0)
    {
        if (n is not JsonObject o || depth > 3) return null;
        foreach (var k in TokenKeys)
            foreach (var p in o)
                if (string.Equals(p.Key, k, StringComparison.OrdinalIgnoreCase) && p.Value is JsonValue v && v.TryGetValue<string>(out var str) && str.Length > 8)
                    return str;
        foreach (var p in o)
            if (FindToken(p.Value, depth + 1) is { } inner) return inner;
        return null;
    }

    static async Task<HttpResponseMessage> AuthSendAsync(IConfiguration cfg, HttpClient http, Func<HttpRequestMessage> make, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var (token, cookie) = await SessionAsync(cfg, http, ct, force: attempt > 0);
            var req = make();
            if (token != null)
            {
                string header = cfg["Opinnate:TokenHeader"] ?? "Authorization";
                string prefix = cfg["Opinnate:TokenPrefix"] ?? "Bearer ";
                req.Headers.TryAddWithoutValidation(header, prefix + token);
            }
            if (cookie != null) req.Headers.TryAddWithoutValidation("Cookie", cookie);
            var resp = await SendAsync(http, req, ct);
            bool retry = attempt == 0 && resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                && string.IsNullOrWhiteSpace(cfg["Opinnate:Token"]);
            if (!retry) return resp;
            resp.Dispose();
        }
    }

    public static string Servis(IConfiguration cfg, string? port) =>
        port == null ? (cfg["Opinnate:ServisHepsi"] ?? "any") : (cfg["Opinnate:ServisFormat"] ?? "tcp_{port}").Replace("{port}", port);

    public static async Task<OpinnateSonuc> CheckAsync(string src, string dst, string? port, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var msgs = new List<string>();
        string servis = Servis(cfg, port);
        var body = new
        {
            source = new[] { src + "/32" },
            destination = new[] { dst + "/32" },
            service = new[] { servis },
            virtualAreaId = cfg.GetValue("Opinnate:VirtualAreaId", 1),
            user_base = false,
            users = Array.Empty<string>()
        };
        using var resp = await AuthSendAsync(cfg, http,
            () => new HttpRequestMessage(HttpMethod.Post, BaseUrl(cfg) + "/rule-checker/topology") { Content = Json(body) }, ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Opinnate rule-checker HTTP {(int)resp.StatusCode}: {Short(text)}");
        JsonNode? j;
        try { j = JsonNode.Parse(text); } catch { throw new HttpRequestException($"Opinnate rule-checker: JSON olmayan yanıt ({Short(text)})"); }

        var names = await DeviceNamesAsync(cfg, http, msgs, ct);
        var yollar = new List<OpinnateYol>();
        foreach (var pr in j?["policyResultData"] as JsonArray ?? [])
        {
            var adimlar = new List<OpinnateAdim>();
            foreach (var h in pr?["PathResult"] as JsonArray ?? [])
            {
                string? id = Str(h?["device_id"]);
                string cihaz = id != null && names.TryGetValue(id, out var nm) ? nm : id != null ? $"Cihaz #{id}" : "?";
                adimlar.Add(new OpinnateAdim(id, cihaz, Lst(h?["src_int"]), Lst(h?["dst_int"]), ToKural(h?["existPolicy"])));
            }
            yollar.Add(new OpinnateYol(Str(pr?["source"]), Str(pr?["destination"]), Lst(pr?["service"]), adimlar));
        }
        if (yollar.Count == 0) msgs.Add("Opinnate yanıtında policyResultData boş (yol bulunamadı ya da virtualAreaId yanlış olabilir).");
        return new OpinnateSonuc(yollar, servis, msgs, sw.ElapsedMilliseconds);
    }

    static OpinnateKural? ToKural(JsonNode? p)
    {
        if (p is not JsonObject) return null;
        string? status = Str(p["status"]);
        return new OpinnateKural(Str(p["id"]), Str(p["policy_id"]), (Str(p["action"]) ?? "?").ToLowerInvariant(),
            status is null or "1" or "true" or "enable" or "enabled",
            Lst(p["src_interface"]), Lst(p["dst_interface"]), Lst(p["source"]), Lst(p["destination"]), Lst(p["service"]),
            Str(p["src_negate"]) is "enable" or "true", Str(p["dst_negate"]) is "enable" or "true",
            Str(p["comment"]), Str(p["schedule"]), Str(p["direction"]), Str(p["nat"]), Str(p["logtraffic"]),
            Lst(p["users"]), Str(p["application"]), Str(p["profile_grp"]));
    }

    // GET /devices — biçim bilinmiyor: kök dizi ya da nesnedeki ilk dizi; id + ad alanı esnek.
    static async Task<Dictionary<string, string>> DeviceNamesAsync(IConfiguration cfg, HttpClient http, List<string> msgs, CancellationToken ct)
    {
        string key = BaseUrl(cfg);
        if (Devices.TryGetValue(key, out var c) && c.until > DateTimeOffset.UtcNow) return c.names;
        var names = new Dictionary<string, string>();
        try
        {
            using var resp = await AuthSendAsync(cfg, http, () => new HttpRequestMessage(HttpMethod.Get, key + "/devices"), ct);
            if (resp.IsSuccessStatusCode)
            {
                var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
                JsonArray? arr = j as JsonArray ?? (j as JsonObject)?.Select(p => p.Value).OfType<JsonArray>().FirstOrDefault()
                    ?? ((j as JsonObject)?.Select(p => p.Value).OfType<JsonObject>().SelectMany(o => o.Select(p => p.Value)).OfType<JsonArray>().FirstOrDefault());
                foreach (var d in arr ?? [])
                {
                    if (d is not JsonObject o) continue;
                    string? id = Str(o["id"]) ?? Str(o["device_id"]);
                    string? name = Str(o["name"]) ?? Str(o["device_name"]) ?? Str(o["deviceName"]) ?? Str(o["hostname"]) ?? Str(o["host_name"]);
                    if (id != null && !string.IsNullOrWhiteSpace(name)) names[id] = name!;
                }
            }
            else msgs.Add($"Cihaz adları alınamadı (GET /devices HTTP {(int)resp.StatusCode}); cihazlar numarayla gösteriliyor.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { msgs.Add("Cihaz adları alınamadı: " + Short(ex.Message)); }
        Devices[key] = (names, DateTimeOffset.UtcNow.AddMinutes(names.Count > 0 ? 60 : 5));
        return names;
    }

    static string? Str(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v => v.ToJsonString().Trim('"'),
        _ => n.ToJsonString()
    };
    // Dizi ya da tek değer; "['any']" gibi metin içinde liste de açılır
    static List<string> Lst(JsonNode? n) => n switch
    {
        JsonArray a => a.Select(Str).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList(),
        null => [],
        _ => Str(n) is { } s && s != "" ? s.Trim('[', ']').Split(',').Select(x => x.Trim(' ', '\'', '"')).Where(x => x != "").ToList() : []
    };

    static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        try { return await http.SendAsync(req, ct); }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException ae)
        {
            throw new HttpRequestException($"Opinnate SSL hatası: {ae.Message} Çözüm: appsettings Opinnate bölümüne \"IgnoreSslErrors\": true ekleyin " +
                "ya da BaseUrl'de sertifikadaki sunucu adını kullanın.", ex);
        }
    }

    // Content-Length'lı gövde (JsonContent parça parça / chunked gönderir; bazı sunucular gövdeyi boş okur)
    static StringContent Json(object o) => new(System.Text.Json.JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    static string Short(string? s) => string.IsNullOrEmpty(s) ? "hata" : s.Length > 300 ? s[..300] + "…" : s;
}
