using System.Collections.Concurrent;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// Dataskope / Infraskope (v26.2) — veritabanı aktivite (DB audit) logları, salt okunur API.
//  - Token: POST {BaseUrl}/token (form: grant_type=password, username, password) → access_token, expires_in.
//    Token bellekte tutulur, süresi bitmeden 5 dk önce ya da 401'de yeniden alınır.
//  - Arama: GET /api/v1/third-party/search-by-events (yeni, rol izni ister); yetki hatasında /api/v1/nest/events.
//    startDate / endDate (ISO 8601, saat dilimi yok), pageSize, fullTextQuery (alan:değer), nextPageId (olduğu gibi).
//  - Yanıt: { StatusCode, IsValid, Result: { TotalRecords, PageSize, NextPageId, Result: [kayıt…] } }.
//    Başarı = HTTP 200 + StatusCode 200 + IsValid true. Kayıtlar tüm alanlarıyla gelir, gerekenler burada seçilir.
//  - Alan adları Dataskope:Alanlar ile değiştirilebilir (varsayılanlar: TimeCreated, db_type, MachineName, os_user,
//    db_user, db_name, instance_name, client_hostname, client_ip, client_port, server_ip, server_port, client_app_name).
// Sır (kullanıcı / şifre) yalnızca appsettings.Production.json'da; token ve kayıt değerleri loglanmaz.
// ---------------------------------------------------------------------------
record DbKayit(string? Time, string? DbType, string? Machine, string? OsUser, string? DbUser, string? DbName, string? Instance,
    string? ClientHost, string? ClientIp, string? ClientPort, string? ServerIp, string? ServerPort, string? ClientApp);
record DataskopeSonuc(List<DbKayit> Kayitlar, long Toplam, bool Eksik, List<string> Mesajlar, long ElapsedMs);

static class DataskopeService
{
    static readonly ConcurrentDictionary<string, (string token, DateTimeOffset until)> Tokens = new();
    static readonly SemaphoreSlim TokenLock = new(1, 1);
    // search-by-events bir kez yetki hatası verdiyse (rol izni yok) uygulama yeniden başlayana kadar doğrudan nest/events kullanılır
    static readonly ConcurrentDictionary<string, bool> UseLegacy = new();

    public static bool Enabled(IConfiguration cfg) =>
        cfg.GetValue("Dataskope:Enabled", true) && !string.IsNullOrWhiteSpace(cfg["Dataskope:BaseUrl"]);

    // BaseUrl ".../InfraskopeESApi" olmalı; yanlışlıkla uç yolu da yazılmışsa (".../api/v1/nest/events", ".../token") kesilir.
    static string BaseUrl(IConfiguration cfg)
    {
        string u = (cfg["Dataskope:BaseUrl"] ?? "").Trim();
        // Kopyala-yapıştırda gelen biçimler temizlenir: "[https://x](https://x)", tırnak / köşeli parantez, şemasız "10.1.1.1:8443/..."
        var m = System.Text.RegularExpressions.Regex.Match(u, @"https?://[^\s\]\)\(""'<>]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) u = m.Value;
        else
        {
            u = u.Trim('[', ']', '(', ')', '"', '\'', '<', '>', ' ');
            if (u != "") u = "https://" + u;
        }
        u = u.TrimEnd('/');
        if (!Uri.TryCreate(u, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Dataskope:BaseUrl geçersiz. Örnek: https://<sunucu>:8443/InfraskopeESApi");
        int i = u.IndexOf("/api/", StringComparison.OrdinalIgnoreCase);
        if (i > 0) u = u[..i];
        if (u.EndsWith("/token", StringComparison.OrdinalIgnoreCase)) u = u[..^6];
        return u;
    }
    // BaseUrl'de nest/events yazılmışsa doğrudan eski uç kullanılır
    static bool NestInUrl(IConfiguration cfg) => (cfg["Dataskope:BaseUrl"] ?? "").Contains("/nest/events", StringComparison.OrdinalIgnoreCase);

    static async Task<string> TokenAsync(IConfiguration cfg, HttpClient http, CancellationToken ct, bool force = false)
    {
        string key = BaseUrl(cfg) + "|" + cfg["Dataskope:Username"];
        if (!force && Tokens.TryGetValue(key, out var t) && t.until > DateTimeOffset.UtcNow) return t.token;
        await TokenLock.WaitAsync(ct);
        try
        {
            if (!force && Tokens.TryGetValue(key, out t) && t.until > DateTimeOffset.UtcNow) return t.token;
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl(cfg) + "/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"] = cfg["Dataskope:Username"] ?? throw new InvalidOperationException("Dataskope:Username tanımlı değil."),
                    ["password"] = cfg["Dataskope:Password"] ?? ""
                })
            };
            using var resp = await http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(body.Contains("no_accessible_endpoints")
                    ? "Dataskope token: API kullanıcısının rolünde API Access üyeliği ya da endpoint izni yok (no_accessible_endpoints)."
                    : $"Dataskope token HTTP {(int)resp.StatusCode}: kullanıcı / şifre ya da API kullanıcısı ayarlarını kontrol edin.");
            var j = JsonNode.Parse(body);
            string token = j?["access_token"]?.GetValue<string>() ?? throw new HttpRequestException("Dataskope token yanıtında access_token yok.");
            int sec = j?["expires_in"] is JsonValue v && v.TryGetValue<int>(out int s) ? s : 3600;
            Tokens[key] = (token, DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, sec - 300)));
            return token;
        }
        finally { TokenLock.Release(); }
    }

    // fullTextQuery ile arama; NextPageId bitene ya da Dataskope:MaxKayit (varsayılan 5000) dolana kadar sayfalar.
    public static async Task<DataskopeSonuc> SearchAsync(LookupQuery q, string fullTextQuery, IConfiguration cfg, HttpClient http, CancellationToken ct,
        int? maxOverride = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = cfg.GetSection("Dataskope");
        var f = s.GetSection("Alanlar");
        string F(string k, string d) => string.IsNullOrWhiteSpace(f[k]) ? d : f[k]!.Trim();
        int max = maxOverride ?? s.GetValue("MaxKayit", 5000), pageSize = Math.Clamp(s.GetValue("PageSize", 1000), 1, 10000);
        string fmt = s["TarihBicimi"] ?? "yyyy-MM-ddTHH:mm:ss";
        bool utc = s.GetValue("Utc", false);
        string start = (utc ? q.Start.UtcDateTime : q.Start.LocalDateTime).ToString(fmt);
        string end = (utc ? q.End.UtcDateTime : q.End.LocalDateTime).ToString(fmt);

        var msgs = new List<string>();
        var list = new List<DbKayit>();
        long total = 0;
        string? next = null;
        bool legacy = s["Endpoint"] == "nest" || NestInUrl(cfg) || UseLegacy.ContainsKey(BaseUrl(cfg));
        string token = await TokenAsync(cfg, http, ct);
        for (int page = 0; list.Count < max; page++)
        {
            string path = legacy ? "/api/v1/nest/events" : "/api/v1/third-party/search-by-events";
            var qs = new List<string> { "startDate=" + Uri.EscapeDataString(start), "endDate=" + Uri.EscapeDataString(end),
                "pageSize=" + pageSize };
            if (!string.IsNullOrWhiteSpace(fullTextQuery)) qs.Add("fullTextQuery=" + Uri.EscapeDataString(fullTextQuery));
            if (next != null) qs.Add("nextPageId=" + Uri.EscapeDataString(next));
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl(cfg) + path + "?" + string.Join("&", qs));
            req.Headers.Authorization = new("Bearer", token);
            using var resp = await http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && page == 0)
            {
                token = await TokenAsync(cfg, http, ct, force: true); page = -1; continue;
            }
            // Yeni uç için rol izni yoksa eski uca düş (aynı parametreler)
            if (!legacy && page == 0 && (resp.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.NotFound))
            {
                legacy = true; page = -1; UseLegacy[BaseUrl(cfg)] = true;
                msgs.Add("search-by-events kullanılamadı (rol izni ThirdPartyElastic.SearchByEvents yok olabilir); nest/events kullanıldı.");
                continue;
            }
            string body = await resp.Content.ReadAsStringAsync(ct);
            JsonNode? j;
            try { j = JsonNode.Parse(body); } catch { throw new HttpRequestException($"Dataskope HTTP {(int)resp.StatusCode}: JSON olmayan yanıt."); }
            int sc = j?["StatusCode"] is JsonValue scv && scv.TryGetValue<int>(out int x) ? x : (int)resp.StatusCode;
            bool valid = j?["IsValid"] is JsonValue iv && iv.TryGetValue<bool>(out bool b) && b;
            if (!resp.IsSuccessStatusCode || sc != 200 || !valid)
                throw new HttpRequestException($"Dataskope HTTP {(int)resp.StatusCode} / StatusCode {sc}: {Short(j?["ExceptionMessage"]?.ToString())}");

            var r = j?["Result"];
            if (page == 0 && r?["TotalRecords"] is JsonValue tv && tv.TryGetValue<long>(out long tr)) total = tr;
            foreach (var e in r?["Result"] as JsonArray ?? [])
            {
                // Bazı sürümlerde kayıtlar JSON metni olarak gelir
                var o = e is JsonValue ev && ev.TryGetValue<string>(out var str) ? JsonNode.Parse(str) : e;
                if (o is not JsonObject rec) continue;
                string? G(string k) => rec[k] is JsonNode n ? (n is JsonValue nv && nv.TryGetValue<string>(out var sv) ? sv : n.ToJsonString().Trim('"')) : null;
                list.Add(new DbKayit(G(F("Zaman", "TimeCreated")), G(F("DbType", "db_type")), G(F("Machine", "MachineName")),
                    G(F("OsUser", "os_user")), G(F("DbUser", "db_user")), G(F("DbName", "db_name")), G(F("Instance", "instance_name")),
                    G(F("ClientHost", "client_hostname")), G(F("ClientIp", "client_ip")), G(F("ClientPort", "client_port")),
                    G(F("ServerIp", "server_ip")), G(F("ServerPort", "server_port")), G(F("ClientApp", "client_app_name"))));
                if (list.Count >= max) break;
            }
            next = r?["NextPageId"] switch { null => null, JsonValue nv when nv.TryGetValue<string>(out var ns) => ns, var n => n.ToJsonString() };
            if (string.IsNullOrEmpty(next) || next == "null") break;
        }
        bool eksik = next != null && next != "null" && list.Count >= max;
        if (eksik) msgs.Add($"İlk {max:N0} kayıt alındı (toplam {total:N0}); sonuç eksik olabilir, aralığı daraltın.");
        return new DataskopeSonuc(list, Math.Max(total, list.Count), eksik, msgs, sw.ElapsedMilliseconds);
    }

    static string Short(string? s) => string.IsNullOrEmpty(s) ? "hata" : s.Length > 300 ? s[..300] + "…" : s;

    // Erişim sorgusu için filtre: kaynak IP → hedef IP. Sorgu kalıbı Dataskope:ErisimSorgusu ile değiştirilebilir.
    public static string AccessQuery(IConfiguration cfg, string src, string dst) =>
        (cfg["Dataskope:ErisimSorgusu"] ?? "client_ip:\"{src}\" AND server_ip:\"{dst}\"").Replace("{src}", src).Replace("{dst}", dst);
}
