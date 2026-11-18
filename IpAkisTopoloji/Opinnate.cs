using System.Collections.Concurrent;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// Opinnate (firewall kural yönetimi) — tanımlı kurala göre erişim: "trafik olmuş mu" değil "kural açık mı".
//  - Giriş: Username doluysa POST /login ile (oturum çerezi ve/veya token); yalnız Token doluysa o kullanılır. Giriş: POST {BaseUrl}/login {username, password} ile alınır.
//    Yanıtta token / access_token / accessToken / jwt / api_key / apikey / key alanlarından ilki (iç içe de aranır)
//    kullanılır; hiçbiri yoksa ve oturum çerezi döndüyse çerezle devam edilir. Bellekte Opinnate:TokenDakika (30) tutulur,
//    401 / 403'te bir kez yeniden alınır. Başlık: Opinnate:TokenHeader (varsayılan Authorization) + TokenPrefix ("Bearer ").
//  - Sorgu: POST {BaseUrl}/rule-checker/topology { source: ["ip/32"], destination: ["ip/32"], service: ["tcp_443"],
//    virtualAreaId, user_base: false, users: [] }. Port yoksa Opinnate:ServisHepsi ("any"); biçim Opinnate:ServisFormat ("tcp_{port}").
//  - Yanıt: policyResultData[] → PathResult[] (yol üzerindeki her cihaz): src_int, dst_int, device_id,
//    existPolicy (eşleşen kural; null = o cihazda eşleşen kural yok — L3 switch / router da olabilir).
//  - Cihaz adları önce yanıttaki adımdan, yoksa GET {BaseUrl}/devices'tan (esnek okunur, 1 saat önbellek; olmazsa "Cihaz #id").
//  - Yol yoksa Opinnate:VirtualAreaIdler ve /devices'ta görülen alanlar denenir. Tanı: /api/opinnate/tani?src=&dst=&port=
// Yalnızca okuma: kural / görev / cihaz değiştiren uçlar kullanılmaz. Token ve şifre loglanmaz.
// ---------------------------------------------------------------------------
record OpinnateKural(string? Id, string? PolicyId, string Action, bool Aktif, List<string> SrcIntf, List<string> DstIntf,
    List<string> Source, List<string> Destination, List<string> Service, bool SrcNegate, bool DstNegate,
    string? Comment, string? Schedule, string? Direction, string? Nat, string? Log, List<string> Users, string? Application, string? ProfileGroup);
record OpinnateAdim(string? DeviceId, string Cihaz, List<string> Giris, List<string> Cikis, OpinnateKural? Kural);
// Sorgu: gönderilen servis (tcp_445). Port verilmezse firewall / Carbon Black'te görülen portlar ayrı ayrı sorgulanır.
record OpinnateYol(string Sorgu, string? Kaynak, string? Hedef, List<string> Servis, List<OpinnateAdim> Adimlar);
record OpinnateSonuc(List<OpinnateYol> Yollar, string Servis, List<string> Mesajlar, long ElapsedMs)
{
    public bool PortYok { get; init; }
    public IEnumerable<OpinnateAdim> Adimlar => Yollar.SelectMany(y => y.Adimlar);
}

static class OpinnateService
{
    static readonly ConcurrentDictionary<string, (string? token, string? cookie, DateTimeOffset until)> Sessions = new();
    static readonly ConcurrentDictionary<string, (Dictionary<string, string> names, List<int> alanlar, DateTimeOffset until)> Devices = new();
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
        if (StaticToken(cfg)) return (s["Token"]!.Trim(), null);
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

    // Kullanıcı / şifre verilmişse her zaman giriş yapılır (Swagger da oturumla çalışıyor); Token yalnız kullanıcı adı boşsa kullanılır.
    static bool StaticToken(IConfiguration cfg) =>
        !string.IsNullOrWhiteSpace(cfg["Opinnate:Token"]) && string.IsNullOrWhiteSpace(cfg["Opinnate:Username"]);

    static string? CookieValue(string cookie, string name) => cookie.Split(';').Select(c => c.Trim())
        .Where(c => c.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)).Select(c => c[(name.Length + 1)..]).FirstOrDefault();

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

    // Token'ın hangi başlıkla gönderileceği: Opinnate:TokenHeader / TokenPrefix verilmişse yalnızca o; verilmemişse 401 / 403'te
    // yaygın biçimler sırayla denenir (Bearer, x-api-key, düz Authorization…) ve çalışan, uygulama açık kaldıkça hatırlanır.
    static readonly (string header, string prefix)[] Variants =
        [("Authorization", "Bearer "), ("x-api-key", ""), ("Authorization", ""), ("X-Auth-Token", ""), ("token", ""), ("apikey", "")];
    static readonly ConcurrentDictionary<string, (string header, string prefix)> Working = new();
    public static string? LastTried;

    static async Task<HttpResponseMessage> AuthSendAsync(IConfiguration cfg, HttpClient http, Func<HttpRequestMessage> make, CancellationToken ct)
    {
        bool fixedHeader = !string.IsNullOrWhiteSpace(cfg["Opinnate:TokenHeader"]) || cfg["Opinnate:TokenPrefix"] != null;
        string key = BaseUrl(cfg);
        var order = fixedHeader
            ? new List<(string, string)> { (cfg["Opinnate:TokenHeader"] ?? "Authorization", cfg["Opinnate:TokenPrefix"] ?? "Bearer ") }
            : (Working.TryGetValue(key, out var w) ? new[] { w } : []).Concat(Variants.Where(v => v != w)).ToList();
        bool relogged = false;
        var tried = new List<string>();
        HttpResponseMessage? last = null;
        for (int i = 0; i < order.Count; i++)
        {
            var (token, cookie) = await SessionAsync(cfg, http, ct);
            var (header, prefix) = order[i];
            var req = make();
            if (token != null) req.Headers.TryAddWithoutValidation(header, prefix + token);
            if (cookie != null)
            {
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                // Oturum çerezli API'lerde POST için CSRF başlığı (Django: csrftoken → X-CSRFToken, Angular/Spring: XSRF-TOKEN → X-XSRF-TOKEN)
                foreach (var (ck, hd) in new[] { ("csrftoken", "X-CSRFToken"), ("XSRF-TOKEN", "X-XSRF-TOKEN"), ("csrf_token", "X-CSRF-Token") })
                    if (CookieValue(cookie, ck) is { } cv) req.Headers.TryAddWithoutValidation(hd, Uri.UnescapeDataString(cv));
                req.Headers.Referrer ??= new Uri(key + "/");
            }
            last?.Dispose();
            last = await SendAsync(http, req, ct);
            tried.Add($"{header}: {(prefix == "" ? "" : prefix.Trim() + " ")}<token>");
            if (last.StatusCode is not (System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden))
            {
                if (!fixedHeader) Working[key] = (header, prefix);
                return last;
            }
            // Girişle alınan token süresi dolmuş olabilir: ilk 401 / 403'te bir kez yeniden giriş, aynı biçimle tekrar
            if (!relogged && !StaticToken(cfg))
            {
                relogged = true;
                await SessionAsync(cfg, http, ct, force: true);
                i--;
            }
        }
        LastTried = string.Join(" | ", tried.Distinct());
        return last!;
    }

    // Port yokken "any" sorgusu her zaman en sondaki cleanup (any/any/any deny) kuralını döndürüyor (tüm portlara izin veren kural
    // olmadığı için); bu yüzden "any" yalnızca Opinnate:ServisHepsi açıkça verilmişse kullanılır.
    public static string? Servis(IConfiguration cfg, string? port) =>
        port == null ? (string.IsNullOrWhiteSpace(cfg["Opinnate:ServisHepsi"]) ? null : cfg["Opinnate:ServisHepsi"])
            : (cfg["Opinnate:ServisFormat"] ?? "tcp_{port}").Replace("{port}", port);

    // Birden çok port (en çok Opinnate:MaxPort = 5) paralel sorgulanır, sonuçlar birleştirilir.
    public static async Task<OpinnateSonuc> CheckManyAsync(string src, string dst, IReadOnlyList<string> ports, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var list = ports.Distinct().Take(cfg.GetValue("Opinnate:MaxPort", 5)).ToList();
        if (list.Count == 0)
        {
            if (Servis(cfg, null) == null)
                return new OpinnateSonuc([], "", ["Port girilmedi ve firewall / Carbon Black kayıtlarında port görülmedi; kural kontrolü için port girin."], 0) { PortYok = true };
            return await CheckAsync(src, dst, null, cfg, http, ct);
        }
        var results = await Task.WhenAll(list.Select(pt => CheckAsync(src, dst, pt, cfg, http, ct)));
        return new OpinnateSonuc(results.SelectMany(r => r.Yollar).ToList(), string.Join(", ", results.Select(r => r.Servis)),
            results.SelectMany(r => r.Mesajlar).Distinct().ToList(), sw.ElapsedMilliseconds);
    }

    // Yanıt biçimi sürüme göre değişebiliyor: alan adları büyük / küçük harf ve _ farkı gözetmeden, iç içe (data / result sarmalayıcı) aranır.
    static readonly string[] PolicyKeys = ["policyResultData", "policy_result_data", "policyResults", "policy_results", "policyResult"];
    static readonly string[] PathKeys = ["PathResult", "path_result", "pathResults", "path_results", "paths", "path", "hops", "devices"];
    static readonly string[] DevIdKeys = ["device_id", "deviceId", "device", "id", "pk", "_id", "device_pk"];
    static readonly string[] DevNameKeys = ["device_name", "deviceName", "name", "hostname", "host_name", "hostName", "display_name", "displayName", "label", "title", "device_hostname"];
    static readonly string[] AreaKeys = ["virtualAreaId", "virtual_area_id", "virtualArea", "virtual_area", "area_id", "areaId"];

    static string Anahtar(string k) => new string(k.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    static JsonNode? Get(JsonNode? n, params string[] keys)
    {
        if (n is not JsonObject o) return null;
        foreach (var k in keys)
        {
            string a = Anahtar(k);
            foreach (var p in o) if (Anahtar(p.Key) == a && p.Value != null) return p.Value;
        }
        return null;
    }
    // Anahtarlardan biri altındaki ilk dizi (en çok 3 seviye iç içe)
    static JsonArray? FindArr(JsonNode? n, string[] keys, int depth = 0)
    {
        if (n is not JsonObject o || depth > 3) return null;
        if (Get(o, keys) is JsonArray a) return a;
        foreach (var p in o) if (FindArr(p.Value, keys, depth + 1) is { } inner) return inner;
        return null;
    }
    static string? ApiMesaji(JsonNode? j) =>
        Str(Get(j, "message", "detail", "error", "errors", "msg", "description", "status_message")) is { Length: > 0 } m ? Short(m) : null;

    public static async Task<OpinnateSonuc> CheckAsync(string src, string dst, string? port, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var msgs = new List<string>();
        string servis = Servis(cfg, port) ?? "any";
        int alan = cfg.GetValue("Opinnate:VirtualAreaId", 1);
        var (j, text) = await SorgulaAsync(src, dst, servis, alan, cfg, http, ct);
        var yollar = Yollar(j, servis);

        // Yol yoksa: ayarda verilen diğer alanlar (Opinnate:VirtualAreaIdler) ve /devices'ta görülen alanlar sırayla denenir
        if (yollar.Count == 0)
        {
            var denenecek = (cfg.GetSection("Opinnate:VirtualAreaIdler").Get<int[]>() ?? []).ToList();
            var (_, alanlar) = await DeviceNamesAsync(cfg, http, msgs, ct);
            denenecek.AddRange(alanlar);
            foreach (int a in denenecek.Where(a => a != alan).Distinct().Take(cfg.GetValue("Opinnate:MaxAlanDenemesi", 4)))
            {
                var (j2, t2) = await SorgulaAsync(src, dst, servis, a, cfg, http, ct);
                var y2 = Yollar(j2, servis);
                if (y2.Count == 0) continue;
                msgs.Add($"{servis}: virtualAreaId {alan} ile yol çıkmadı, {a} ile bulundu (Opinnate:VirtualAreaId = {a} yapın).");
                (j, text, yollar, alan) = (j2, t2, y2, a);
                break;
            }
        }

        // Cihaz adı yanıtta yoksa GET /devices'tan
        if (yollar.SelectMany(y => y.Adimlar).Any(s => s.Cihaz.StartsWith("Cihaz #")))
        {
            var (names, _) = await DeviceNamesAsync(cfg, http, msgs, ct);
            foreach (var y in yollar)
                for (int i = 0; i < y.Adimlar.Count; i++)
                    if (y.Adimlar[i].DeviceId is { } id && y.Adimlar[i].Cihaz.StartsWith("Cihaz #") && names.TryGetValue(id, out var nm))
                        y.Adimlar[i] = y.Adimlar[i] with { Cihaz = nm };
        }

        if (yollar.Count == 0)
        {
            string alanlar = j is JsonObject o ? string.Join(", ", o.Select(p => p.Key)) : j is JsonArray ja ? $"dizi ({ja.Count} öğe)" : "(boş)";
            msgs.Add($"{servis}: Opinnate yol döndürmedi (virtualAreaId {alan})" +
                (ApiMesaji(j) is { } m ? $" — Opinnate: \"{m}\"" : "") +
                $". Yanıttaki alanlar: {alanlar}. Ham yanıt: {Short(text, 200)} — ayrıntı: /api/opinnate/tani?src={src}&dst={dst}&port={port}");
        }
        return new OpinnateSonuc(yollar, servis, msgs, sw.ElapsedMilliseconds);
    }

    static object Govde(string src, string dst, string servis, int alan) => new
    {
        source = new[] { src + "/32" },
        destination = new[] { dst + "/32" },
        service = new[] { servis },
        virtualAreaId = alan,
        user_base = false,
        users = Array.Empty<string>()
    };

    static async Task<(JsonNode? j, string text)> SorgulaAsync(string src, string dst, string servis, int alan, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var body = Govde(src, dst, servis, alan);
        using var resp = await AuthSendAsync(cfg, http,
            () => new HttpRequestMessage(HttpMethod.Post, BaseUrl(cfg) + "/rule-checker/topology") { Content = Json(body) }, ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        string kaynak = StaticToken(cfg) ? "Opinnate:Token'dan (kullanıcı ile giriş için Username / Password doldurun)" : "girişle (Username / Password) alındı";
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Opinnate rule-checker HTTP {(int)resp.StatusCode}: {Short(text)}" +
                (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    ? $" — token {kaynak}; denenen başlıklar: {LastTried}. " +
                      "Token / API anahtarı geçersiz ya da kullanıcının rule-checker yetkisi yok olabilir."
                    : ""));
        try { return (JsonNode.Parse(text), text); }
        catch { throw new HttpRequestException($"Opinnate rule-checker: JSON olmayan yanıt ({Short(text)})"); }
    }

    // policyResultData[] → PathResult[]; kök dizi de olabilir (yol listesi ya da doğrudan adımlar)
    static List<OpinnateYol> Yollar(JsonNode? j, string servis)
    {
        var yollar = new List<OpinnateYol>();
        JsonArray? prs = FindArr(j, PolicyKeys) ?? j as JsonArray;
        if (prs == null) return yollar;
        bool adimDizisi = prs.Count > 0 && prs.All(x => Get(x, PathKeys) is not JsonArray) && prs.Any(x => Get(x, DevIdKeys) != null);
        IEnumerable<(JsonNode? pr, JsonArray steps)> gruplar = adimDizisi
            ? [(null, prs)]
            : prs.Select(pr => (pr, Get(pr, PathKeys) as JsonArray ?? [])).Where(x => x.Item2.Count > 0);
        foreach (var (pr, steps) in gruplar)
        {
            var adimlar = new List<OpinnateAdim>();
            foreach (var h in steps)
            {
                var dev = Get(h, "device");
                string? id = dev is JsonObject ? Str(Get(dev, "id", "pk", "_id")) : Str(Get(h, DevIdKeys));
                string? ad = Str(Get(h, "device_name", "deviceName", "hostname", "device_hostname")) ?? (dev is JsonObject ? Str(Get(dev, DevNameKeys)) : null);
                string cihaz = !string.IsNullOrWhiteSpace(ad) ? ad! : id != null ? $"Cihaz #{id}" : "?";
                adimlar.Add(new OpinnateAdim(id, cihaz, Lst(Get(h, "src_int", "srcInt", "src_interface", "srcintf")), Lst(Get(h, "dst_int", "dstInt", "dst_interface", "dstintf")),
                    ToKural(Get(h, "existPolicy", "exist_policy", "policy", "matchedPolicy", "matched_policy"))));
            }
            yollar.Add(new OpinnateYol(servis, Str(Get(pr, "source")), Str(Get(pr, "destination")), Lst(Get(pr, "service")), adimlar));
        }
        return yollar;
    }

    static OpinnateKural? ToKural(JsonNode? p)
    {
        if (p is JsonArray a) p = a.FirstOrDefault(x => x is JsonObject);
        if (p is not JsonObject) return null;
        string? status = Str(Get(p, "status", "enabled"));
        return new OpinnateKural(Str(Get(p, "id")), Str(Get(p, "policy_id", "policyId", "name")), (Str(Get(p, "action")) ?? "?").ToLowerInvariant(),
            status is null or "1" or "true" or "True" or "enable" or "enabled",
            Lst(Get(p, "src_interface", "srcintf")), Lst(Get(p, "dst_interface", "dstintf")), Lst(Get(p, "source", "srcaddr")), Lst(Get(p, "destination", "dstaddr")), Lst(Get(p, "service")),
            Str(Get(p, "src_negate")) is "enable" or "true", Str(Get(p, "dst_negate")) is "enable" or "true",
            Str(Get(p, "comment", "comments")), Str(Get(p, "schedule")), Str(Get(p, "direction")), Str(Get(p, "nat")), Str(Get(p, "logtraffic")),
            Lst(Get(p, "users")), Str(Get(p, "application")), Str(Get(p, "profile_grp", "profile_group")));
    }

    // Cihaz listesi: kök dizi, ya da nesne içinde (iç içe) id + ad alanlı nesnelerden oluşan ilk dizi, ya da { "id": {..} / "id": "ad" } sözlüğü.
    static (Dictionary<string, string> names, List<int> alanlar, string? alanAdlari) CihazOku(JsonNode? j)
    {
        var names = new Dictionary<string, string>();
        var alanlar = new List<int>();
        static bool CihazMi(JsonNode? x) => x is JsonObject && Get(x, DevIdKeys) != null && Get(x, DevNameKeys) != null;
        JsonArray? Ara(JsonNode? n, int d)
        {
            if (d > 4) return null;
            if (n is JsonArray a && a.Any(CihazMi)) return a;
            if (n is JsonObject o) foreach (var p in o) if (Ara(p.Value, d + 1) is { } r) return r;
            return null;
        }
        var arr = Ara(j, 0);
        string? ilkAlanlar = null;
        if (arr != null)
            foreach (var d in arr)
            {
                if (d is not JsonObject o) continue;
                string? id = Str(Get(o, DevIdKeys));
                string? name = Str(Get(o, DevNameKeys));
                if (id != null && !string.IsNullOrWhiteSpace(name)) names[id] = name!;
                // Aynı cihazın başka kimlikleri (device_id ayrı alan olabilir) de eşlensin
                foreach (var k in new[] { "device_id", "deviceId", "pk", "_id" })
                    if (Str(Get(o, k)) is { } id2 && !string.IsNullOrWhiteSpace(name)) names.TryAdd(id2, name!);
                if (int.TryParse(Str(Get(o, AreaKeys) is JsonObject ao ? Get(ao, "id") : Get(o, AreaKeys)), out int al)) alanlar.Add(al);
            }
        else if (j is JsonObject so)
        {
            foreach (var p in so)
                if (p.Value is JsonValue v && v.TryGetValue<string>(out var s) && int.TryParse(p.Key, out _)) names[p.Key] = s;
                else if (p.Value is JsonObject po && int.TryParse(p.Key, out _) && Str(Get(po, DevNameKeys)) is { Length: > 0 } nm) names[p.Key] = nm;
        }
        if (names.Count == 0)
        {
            JsonNode? ilk = j is JsonArray ja ? ja.FirstOrDefault() : (j as JsonObject)?.Select(p => p.Value).OfType<JsonArray>().FirstOrDefault()?.FirstOrDefault();
            ilkAlanlar = ilk is JsonObject io ? string.Join(", ", io.Select(p => p.Key)) : j is JsonObject jo ? string.Join(", ", jo.Select(p => p.Key)) : null;
        }
        return (names, alanlar.Distinct().ToList(), ilkAlanlar);
    }

    static string[] CihazSorgulari(IConfiguration cfg)
    {
        string area = cfg.GetValue("Opinnate:VirtualAreaId", 1).ToString();
        var queries = new List<string>();
        if (cfg["Opinnate:CihazSorgusu"] is { Length: > 0 } cq) queries.Add(cq.StartsWith('?') ? cq : "?" + cq);
        queries.AddRange(["", $"?virtualAreaId={area}", $"?virtual_area_id={area}", "?page=1&per_page=1000", $"?virtualAreaId={area}&page=1&per_page=1000",
            "?page=1&page_size=1000", "?limit=1000"]);
        return queries.Distinct().ToArray();
    }

    // GET /devices — biçim bilinmiyor: esnek okunur. 400 / 404 / 405 / 422'de sıradaki sorgu biçimi denenir.
    static async Task<(Dictionary<string, string> names, List<int> alanlar)> DeviceNamesAsync(IConfiguration cfg, HttpClient http, List<string> msgs, CancellationToken ct)
    {
        string key = BaseUrl(cfg);
        if (Devices.TryGetValue(key, out var c) && c.until > DateTimeOffset.UtcNow) return (c.names, c.alanlar);
        var names = new Dictionary<string, string>();
        var alanlar = new List<int>();
        try
        {
            HttpResponseMessage? resp = null;
            string? err = null, yol = cfg["Opinnate:CihazYolu"] ?? "/devices";
            foreach (var qs in CihazSorgulari(cfg))
            {
                resp?.Dispose();
                resp = await AuthSendAsync(cfg, http, () => new HttpRequestMessage(HttpMethod.Get, key + yol + qs), ct);
                if (resp.IsSuccessStatusCode || (int)resp.StatusCode is not (400 or 404 or 405 or 422)) break;
                err ??= Short(await resp.Content.ReadAsStringAsync(ct));
            }
            using var _ = resp;
            if (resp!.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                JsonNode? j = null;
                try { j = JsonNode.Parse(body); } catch { }
                var (n, a, ilk) = CihazOku(j);
                names = n; alanlar = a;
                if (names.Count == 0)
                    msgs.Add($"GET {yol} yanıtında cihaz adı bulunamadı" + (ilk != null ? $" (alanlar: {ilk})" : $" ({Short(body, 150)})") + "; cihazlar numarayla gösteriliyor.");
            }
            else msgs.Add($"Cihaz adları alınamadı (GET {yol} HTTP {(int)resp.StatusCode}{(err != null ? ": " + err : "")}); cihazlar numarayla gösteriliyor. Doğru sorgu Opinnate:CihazSorgusu / CihazYolu ile verilebilir.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { msgs.Add("Cihaz adları alınamadı: " + Short(ex.Message)); }
        Devices[key] = (names, alanlar, DateTimeOffset.UtcNow.AddMinutes(names.Count > 0 ? 60 : 5));
        return (names, alanlar);
    }

    // Tanı: rule-checker ve /devices ham yanıtları (token / şifre yazılmaz)
    public static async Task<string> TaniAsync(string src, string dst, string? port, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        string servis = Servis(cfg, port) ?? "any";
        int alan = cfg.GetValue("Opinnate:VirtualAreaId", 1);
        sb.AppendLine($"BaseUrl: {BaseUrl(cfg)}");
        sb.AppendLine($"Yetkilendirme: {(StaticToken(cfg) ? "Opinnate:Token" : "Username ile giriş")}");
        try
        {
            var (tok, cookie) = await SessionAsync(cfg, http, ct);
            sb.AppendLine($"Giriş: tamam (token {(tok != null ? "var" : "yok")}, çerez {(cookie != null ? "var" : "yok")})");
        }
        catch (Exception ex) { sb.AppendLine("Giriş HATASI: " + ex.Message); return sb.ToString(); }

        foreach (int a in new[] { alan }.Concat(cfg.GetSection("Opinnate:VirtualAreaIdler").Get<int[]>() ?? []).Distinct())
        {
            sb.AppendLine();
            sb.AppendLine($"=== POST /rule-checker/topology (virtualAreaId {a}) ===");
            sb.AppendLine("Gövde: " + System.Text.Json.JsonSerializer.Serialize(Govde(src, dst, servis, a)));
            try
            {
                using var resp = await AuthSendAsync(cfg, http,
                    () => new HttpRequestMessage(HttpMethod.Post, BaseUrl(cfg) + "/rule-checker/topology") { Content = Json(Govde(src, dst, servis, a)) }, ct);
                string text = await resp.Content.ReadAsStringAsync(ct);
                sb.AppendLine($"HTTP {(int)resp.StatusCode}, {text.Length} karakter");
                JsonNode? j = null;
                try { j = JsonNode.Parse(text); } catch { }
                var y = Yollar(j, servis);
                sb.AppendLine($"Okunan yol: {y.Count}, adım: {y.Sum(x => x.Adimlar.Count)}; cihazlar: {string.Join(", ", y.SelectMany(x => x.Adimlar).Select(s => s.Cihaz).Distinct())}");
                sb.AppendLine(text.Length > 6000 ? text[..6000] + "…" : text);
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }
        }

        string yol = cfg["Opinnate:CihazYolu"] ?? "/devices";
        foreach (var qs in CihazSorgulari(cfg))
        {
            sb.AppendLine();
            sb.AppendLine($"=== GET {yol}{qs} ===");
            try
            {
                using var resp = await AuthSendAsync(cfg, http, () => new HttpRequestMessage(HttpMethod.Get, BaseUrl(cfg) + yol + qs), ct);
                string text = await resp.Content.ReadAsStringAsync(ct);
                sb.AppendLine($"HTTP {(int)resp.StatusCode}, {text.Length} karakter");
                if (resp.IsSuccessStatusCode)
                {
                    JsonNode? j = null;
                    try { j = JsonNode.Parse(text); } catch { }
                    var (n, al, ilk) = CihazOku(j);
                    sb.AppendLine($"Okunan cihaz adı: {n.Count}{(n.Count > 0 ? " (ör. " + string.Join(", ", n.Take(5).Select(p => p.Key + " = " + p.Value)) + ")" : "")}; " +
                        $"virtualAreaId'ler: {(al.Count > 0 ? string.Join(", ", al) : "—")}{(ilk != null ? "; alanlar: " + ilk : "")}");
                }
                sb.AppendLine(text.Length > 3000 ? text[..3000] + "…" : text);
                if (resp.IsSuccessStatusCode) break;
            }
            catch (Exception ex) { sb.AppendLine("HATA: " + ex.Message); }
        }
        return sb.ToString();
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

    static string Short(string? s, int max = 300) => string.IsNullOrEmpty(s) ? "hata" : s.Length > max ? s[..max] + "…" : s;
}
