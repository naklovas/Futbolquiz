using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppRel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Tüm ayarlar (Splunk, AppResponse, envanter) appsettings.json'dan okunur.

builder.Services.AddHttpClient("splunk", c => c.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(builder.Configuration.GetValue("Splunk:IgnoreSslErrors", false)));

builder.Services.AddHttpClient("appresponse", c => c.Timeout = TimeSpan.FromMinutes(3))
    .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(builder.Configuration.GetValue("AppResponseIgnoreSslErrors", true)));

builder.Services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromMinutes(builder.Configuration.GetValue("Ai:TimeoutMinutes", 3)))
    .ConfigurePrimaryHttpMessageHandler(() => CreateHandler(builder.Configuration.GetValue("Ai:IgnoreSslErrors", true)));

builder.Services.AddSingleton<EnvanterService>();
builder.Services.AddMemoryCache();

// Windows Authentication: IIS'te IIS'in Windows Auth'u, Kestrel'de Negotiate kullanılır.
bool authEnabled = builder.Configuration.GetValue("Auth:Enabled", true);
if (authEnabled)
    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();

var app = builder.Build();

app.Logger.LogInformation("AppResponse cihaz sayısı: {Count}", app.Configuration.GetSection("Servers").GetChildren().Count());
app.Logger.LogInformation("Splunk BaseUrl: {Url}", app.Configuration["Splunk:BaseUrl"]);

if (authEnabled)
{
    app.UseAuthentication();

    // Her istek (sayfa, statik dosyalar, API) DokuPanel yetki kontrolünden geçer.
    // Sonuç kullanıcı başına Auth:CacheMinutes (varsayılan 5 dk) önbellekte tutulur.
    app.Use(async (context, next) =>
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
            return;
        }

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var cfg = context.RequestServices.GetRequiredService<IConfiguration>();
        string user = context.User.Identity.Name ?? "?";
        string key = "yetki:" + user.ToLowerInvariant();

        if (!cache.TryGetValue(key, out (bool ok, string? hata) yetki))
        {
            bool ok = await AuthHelper.YetkiKontrol(context, cfg);
            yetki = (ok, context.Items["AuthHata"] as string);
            // Başarılı sonuç daha uzun, hata kısa süre tutulur (DokuPanel düzelince hemen açılsın).
            cache.Set(key, yetki, ok ? TimeSpan.FromMinutes(cfg.GetValue("Auth:CacheMinutes", 5)) : TimeSpan.FromSeconds(30));
            if (!ok) app.Logger.LogWarning("Yetkisiz erişim: {User} {Path} - {Hata}", user, context.Request.Path, yetki.hata);
        }

        if (!yetki.ok)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            if (context.Request.Path.StartsWithSegments("/api"))
                await context.Response.WriteAsJsonAsync(new { error = $"Yetkiniz yok ({user})." });
            else
                await AuthHelper.YetkisizErisimSayfasi(user, yetki.hata ?? "").ExecuteAsync(context);
            return;
        }

        if (context.Request.Path.StartsWithSegments("/api"))
            app.Logger.LogInformation("{User} {Method} {Path}{Query}", user, context.Request.Method, context.Request.Path, context.Request.QueryString);
        await next();
    });
}

// Kök adres (/) doğrudan DeltaFlow sayfasını açar.
var defaultFiles = new DefaultFilesOptions();
defaultFiles.DefaultFileNames.Clear();
defaultFiles.DefaultFileNames.Add("topoloji.html");
app.UseDefaultFiles(defaultFiles);
app.UseStaticFiles();

// Giriş yapan kullanıcı (başlıkta gösterilir)
app.MapGet("/api/me", (HttpContext ctx) => new { name = ctx.User?.Identity?.Name });

app.MapGet("/api/appliances", (IConfiguration cfg) =>
    cfg.GetSection("Servers").GetChildren()
        .Select((s, i) => new { index = i, name = s["Name"] ?? $"Cihaz {i + 1}" }));

app.MapGet("/api/splunk", async (string? ip, string? start, string? end,
    IConfiguration cfg, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (!LookupQuery.TryParse(ip, start, end, cfg.GetValue("MaxRangeHours", 24), out var q, out var error))
        return Results.BadRequest(new { error });

    try
    {
        return Results.Ok(await SplunkService.QueryAsync(q!, cfg, factory.CreateClient("splunk"), ct));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapGet("/api/appresponse", async (string? ip, string? start, string? end, int? appliance,
    IConfiguration cfg, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (!LookupQuery.TryParse(ip, start, end, cfg.GetValue("MaxRangeHours", 24), out var q, out var error))
        return Results.BadRequest(new { error });

    try
    {
        return Results.Ok(await AppResponseService.QueryAnyAsync(q!, appliance ?? 0, cfg, factory.CreateClient("appresponse"), ct));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

// Tek IP akış + topoloji: Splunk ve AppResponse paralel sorgulanır, envanterle zenginleştirilir.
// sources=splunk,appresponse ile kaynak seçilebilir (varsayılan: ikisi de).
app.MapGet("/api/flow", async (string? ip, string? start, string? end, int? appliance, string? sources,
    IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, CancellationToken ct) =>
{
    if (!LookupQuery.TryParse(ip, start, end, cfg.GetValue("MaxRangeHours", 24), out var q, out var error))
        return Results.BadRequest(new { error });

    var wanted = (sources ?? "splunk,appresponse").ToLowerInvariant();
    var sw = Stopwatch.StartNew();

    // Envanter önce: sorgulanan IP bir VIP ise havuz üyeleri ve GW'leri de aynı sorguya eklenir,
    // çünkü LB üyelere VIP adresinden değil kendi GW IP'sinden gider.
    var (env, envErr) = await Capture(() => envanter.GetAsync(false, ct), ct);
    var vipMembers = env?.VipMembers(q!.Ip) ?? [];
    var queryIps = new List<string> { q!.Ip };
    queryIps.AddRange(vipMembers.SelectMany(m => m.GwIps.Prepend(m.Ip))
        .Where(x => LookupQuery.TryParseIpv4(x, out _)).Distinct().Where(x => x != q.Ip)
        .Take(cfg.GetValue("Hop2MaxPeers", 80)));

    var spTask = wanted.Contains("splunk")
        ? Capture(() => SplunkService.QueryAsync(q, cfg, factory.CreateClient("splunk"), ct, queryIps), ct)
        : Task.FromResult<(SplunkResult?, string?)>((null, null));
    var arTask = wanted.Contains("appresponse")
        ? Capture(() => AppResponseService.QueryAnyAsync(q, appliance ?? 0, cfg, factory.CreateClient("appresponse"), ct, queryIps), ct)
        : Task.FromResult<(AppResponseResult?, string?)>((null, null));

    try
    {
        await Task.WhenAll(spTask, arTask);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    if (ct.IsCancellationRequested)
        return Results.StatusCode(499);

    var (sp, spErr) = spTask.Result;
    var (ar, arErr) = arTask.Result;

    var (inbound, outbound) = FlowBuilder.Build(q.Ip, sp, ar, env);
    var target = new TargetInfo(q.Ip, env?.FindSegment(q.Ip), env?.AppsOnIp(q.Ip) ?? []);

    if (env != null)
    {
        inbound = VipResolver.Annotate(inbound, env);
        outbound = VipResolver.Annotate(outbound, env);
        var memberOf = env.MemberOf(q.Ip);
        if (vipMembers.Count > 0)
        {
            // VIP → üyeler "giden" tarafa eklenir; böylece 2. seviye de üyelerin arkasını gösterir.
            var (members, memberEdges) = VipResolver.MemberEdges(vipMembers, sp, ar, env);
            outbound = outbound.Where(e => !memberEdges.Any(m => m.PeerIp == e.PeerIp && m.Port == e.Port))
                .Concat(memberEdges).OrderByDescending(e => e.Hits).ToList();
            target = target with { Vip = members, MemberOf = memberOf.Count > 0 ? memberOf : null };
        }
        else if (memberOf.Count > 0)
        {
            target = target with { MemberOf = memberOf };
        }
    }

    var (spSt, arSt, envSt) = Statuses(wanted, sp, spErr, ar, arErr, env, envErr);
    return Results.Ok(new FlowResponse(target, inbound, outbound, spSt, arSt, envSt, sw.ElapsedMilliseconds));
});

// Ortam filtreleri (ODM, TEST...): arayüzdeki kutucuklar; segment Domain'inde anahtar kelime aranır.
app.MapGet("/api/filtreler", (IConfiguration cfg) => EnvFilter.Load(cfg));

// Uygulama listesi (ERT_HOSTIPADDRESS.APPNAME; "Sistem Portudur" kayıtları hariç)
app.MapGet("/api/apps", async (EnvanterService envanter, CancellationToken ct) =>
{
    try
    {
        var env = await envanter.GetAsync(false, ct);
        return Results.Ok(env.AppCatalog());
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

// Uygulama topolojisi: uygulamanın tüm sunucuları ve VIP'leri tek Splunk sorgusu / tek AppResponse
// raporuyla sorgulanır; akışlar uygulama seviyesinde gruplanır.
app.MapGet("/api/app", async (string? name, string? start, string? end, int? appliance, string? sources,
    IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();
    var (env, envErr) = await Capture(() => envanter.GetAsync(false, ct), ct);
    if (env == null)
        return Results.Json(new { error = "Envanter okunamadı: " + envErr }, statusCode: 502);
    if (string.IsNullOrWhiteSpace(name) || env.AppRows(name.Trim()).Count == 0)
        return Results.BadRequest(new { error = $"Uygulama envanterde bulunamadı: {name}" });
    name = name.Trim();

    var (servers, vips) = AppTopology.Endpoints(name, env);
    // VIP'ler önce (istemciler oraya gelir), sonra sunucular; limit aşılırsa fazlası sorgulanmaz.
    int max = cfg.GetValue("Uygulama:MaxIp", cfg.GetValue("Hop2MaxPeers", 80));
    var queried = vips.Select(v => v.Ip).Concat(servers.Select(s => s.Ip))
        .Where(x => LookupQuery.TryParseIpv4(x, out _)).Distinct().Take(max).ToList();
    if (queried.Count == 0)
        return Results.BadRequest(new { error = "Uygulamanın envanterde geçerli bir IP'si yok." });

    if (!LookupQuery.TryParse(queried[0], start, end, cfg.GetValue("MaxRangeHours", 24), out var q, out var error))
        return Results.BadRequest(new { error });

    var wanted = (sources ?? "splunk,appresponse").ToLowerInvariant();
    var spTask = wanted.Contains("splunk")
        ? Capture(() => SplunkService.QueryAsync(q!, cfg, factory.CreateClient("splunk"), ct, queried), ct)
        : Task.FromResult<(SplunkResult?, string?)>((null, null));
    var arTask = wanted.Contains("appresponse")
        ? Capture(() => AppResponseService.QueryAnyAsync(q!, appliance ?? 0, cfg, factory.CreateClient("appresponse"), ct, queried), ct)
        : Task.FromResult<(AppResponseResult?, string?)>((null, null));
    try
    {
        await Task.WhenAll(spTask, arTask);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    if (ct.IsCancellationRequested)
        return Results.StatusCode(499);

    var (sp, spErr) = spTask.Result;
    var (ar, arErr) = arTask.Result;
    var (spSt, arSt, envSt) = Statuses(wanted, sp, spErr, ar, arErr, env, envErr);
    return Results.Ok(AppTopology.Build(name, servers, vips, queried, sp, ar, env, AppTopology.InfraPorts(cfg), EnvFilter.Load(cfg),
        spSt, arSt, envSt, sw.ElapsedMilliseconds));
});

// 2. seviye: sorgulanan IP'nin karşısındaki sunucuların kendi trafiği (tek sorguda, tüm sunucular için).
// Her sunucu için gelen/giden trafik segment bazında özetlenir; sorgulanan IP özetten çıkarılır.
app.MapPost("/api/hop2", async (Hop2Request req, IConfiguration cfg, IHttpClientFactory factory,
    EnvanterService envanter, CancellationToken ct) =>
{
    if (!LookupQuery.TryParse(req.Target, req.Start, req.End, cfg.GetValue("MaxRangeHours", 24), out var q, out var error))
        return Results.BadRequest(new { error });

    int maxPeers = cfg.GetValue("Hop2MaxPeers", 80);
    var ips = new List<string>();
    foreach (var raw in req.Ips ?? [])
    {
        if (!LookupQuery.TryParseIpv4(raw, out var ip))
            return Results.BadRequest(new { error = $"Geçersiz IP: {raw}" });
        if (ip != q!.Ip && !ips.Contains(ip)) ips.Add(ip);
    }
    int requested = ips.Count;
    if (ips.Count > maxPeers) ips = ips.Take(maxPeers).ToList();
    if (ips.Count == 0)
        return Results.BadRequest(new { error = "Sorgulanacak sunucu IP'si yok." });

    var wanted = (req.Sources ?? "splunk,appresponse").ToLowerInvariant();
    var sw = Stopwatch.StartNew();

    var spTask = wanted.Contains("splunk")
        ? Capture(() => SplunkService.QueryAsync(q!, cfg, factory.CreateClient("splunk"), ct, ips), ct)
        : Task.FromResult<(SplunkResult?, string?)>((null, null));
    var arTask = wanted.Contains("appresponse")
        ? Capture(() => AppResponseService.QueryAnyAsync(q!, req.Appliance ?? 0, cfg, factory.CreateClient("appresponse"), ct, ips), ct)
        : Task.FromResult<(AppResponseResult?, string?)>((null, null));
    var envTask = Capture(() => envanter.GetAsync(false, ct), ct);

    try
    {
        await Task.WhenAll(spTask, arTask, envTask);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    if (ct.IsCancellationRequested)
        return Results.StatusCode(499);

    var (sp, spErr) = spTask.Result;
    var (ar, arErr) = arTask.Result;
    var (env, envErr) = envTask.Result;

    var peers = ips.Select(ip => Hop2Builder.Build(ip, q!.Ip, sp, ar, env)).ToList();
    var (spSt, arSt, envSt) = Statuses(wanted, sp, spErr, ar, arErr, env, envErr);
    return Results.Ok(new Hop2Response(peers, requested, ips.Count, spSt, arSt, envSt, sw.ElapsedMilliseconds));
});

// Sorgu sonucunun özetiyle şirket içi AI'a "bu sunucuda değişiklik olursa nereler etkilenir" sorusu.
app.MapPost("/api/ai/etki", async (AiRequest req, IConfiguration cfg, IHttpClientFactory factory, CancellationToken ct) =>
{
    if (!LookupQuery.TryParseIpv4(req.Ip, out _))
        return Results.BadRequest(new { error = "Geçerli bir IPv4 adresi yok." });
    try
    {
        return Results.Ok(await AiEtkiService.AskAsync(req, cfg, factory.CreateClient("ai"), ct));
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (TaskCanceledException)
    {
        return Results.Json(new { error = "AI servisi zaman aşımına uğradı." }, statusCode: 504);
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapGet("/api/envanter/durum", async (EnvanterService envanter, CancellationToken ct) =>
{
    try
    {
        var env = await envanter.GetAsync(false, ct);
        return Results.Ok(new { loadedAt = env.LoadedAt, segments = env.SegmentCount, hosts = env.HostCount });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.MapPost("/api/envanter/yenile", async (EnvanterService envanter, CancellationToken ct) =>
{
    try
    {
        var env = await envanter.GetAsync(true, ct);
        return Results.Ok(new { loadedAt = env.LoadedAt, segments = env.SegmentCount, hosts = env.HostCount });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 502);
    }
});

app.Run();

static (SourceStatus sp, SourceStatus ar, SourceStatus env) Statuses(string wanted,
    SplunkResult? sp, string? spErr, AppResponseResult? ar, string? arErr, EnvanterSnapshot? env, string? envErr)
{
    SourceStatus Status(bool asked, string? err, List<string>? msgs, long? ms, int rows) =>
        !asked ? SourceStatus.Skipped : new SourceStatus(err == null, err, msgs ?? [], ms, rows);

    return (
        Status(wanted.Contains("splunk"), spErr, sp?.Messages, sp?.ElapsedMs, sp?.Rows.Count ?? 0),
        Status(wanted.Contains("appresponse"), arErr, ar?.Messages, ar?.ElapsedMs, (ar?.L4.Count ?? 0) + (ar?.L7.Count ?? 0)),
        new SourceStatus(env != null, envErr,
            env == null ? [] : [$"{env.SegmentCount} segment, {env.HostCount} host kaydı ({env.LoadedAt:HH:mm:ss} yüklendi)"],
            null, env?.HostCount ?? 0));
}

// Bir kaynağın hatası diğerlerini durdurmasın: sonucu veya hata mesajını döndür.
// Sadece kullanıcı isteği iptal ettiyse fırlat; HttpClient timeout'u hata mesajı olarak döner.
static async Task<(T? result, string? error)> Capture<T>(Func<Task<T>> run, CancellationToken ct) where T : class
{
    try
    {
        return (await run(), null);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw;
    }
    catch (TaskCanceledException)
    {
        return (null, "Zaman aşımı: kaynak belirlenen sürede yanıt vermedi.");
    }
    catch (Exception ex)
    {
        return (null, ex.Message);
    }
}

static HttpMessageHandler CreateHandler(bool ignoreSslErrors)
{
    // Kurumsal proxy isteğe karışmasın: Splunk ve AppResponse iç adresler.
    var handler = new HttpClientHandler { UseProxy = false };
    if (ignoreSslErrors)
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return handler;
}

// ---------------------------------------------------------------------------
// Ortak sorgu parametreleri
// ---------------------------------------------------------------------------
record LookupQuery(string Ip, DateTimeOffset Start, DateTimeOffset End)
{
    public long StartUnix => Start.ToUnixTimeSeconds();
    public long EndUnix => End.ToUnixTimeSeconds();

    public static bool TryParse(string? ipText, string? startText, string? endText, int maxRangeHours,
        out LookupQuery? query, out string error)
    {
        query = null;
        error = "";

        if (!TryParseIpv4(ipText, out string ip))
        {
            error = "Geçerli bir IPv4 adresi girin, ör. 10.50.20.15.";
            return false;
        }

        var end = ParseTime(endText) ?? DateTimeOffset.Now;
        var start = ParseTime(startText) ?? end.AddHours(-1);

        if (end <= start)
        {
            error = "Bitiş zamanı başlangıçtan sonra olmalı.";
            return false;
        }
        if ((end - start).TotalHours > maxRangeHours)
        {
            error = $"Zaman aralığı en fazla {maxRangeHours} saat olabilir.";
            return false;
        }

        query = new LookupQuery(ip, start, end);
        return true;
    }

    // 4 oktet şartı: IPAddress.TryParse "10" gibi girdileri de kabul ettiği için.
    // IP doğrulaması aynı zamanda SPL / STEELFILTER injection'ını engeller.
    public static bool TryParseIpv4(string? text, out string ip)
    {
        ip = "";
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Split('.').Length != 4 ||
            !IPAddress.TryParse(text, out var addr) || addr.AddressFamily != AddressFamily.InterNetwork)
            return false;
        ip = addr.ToString();
        return true;
    }

    static DateTimeOffset? ParseTime(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? new DateTimeOffset(dt)
            : null;
}

// ---------------------------------------------------------------------------
// Splunk / Carbon Black
// ---------------------------------------------------------------------------
static class SplunkService
{
    // ips verilirse tek sorguda birden fazla IP aranır (2. seviye); verilmezse q.Ip.
    public static async Task<SplunkResult> QueryAsync(LookupQuery q, IConfiguration cfg, HttpClient http, CancellationToken ct,
        IReadOnlyList<string>? ips = null)
    {
        ips ??= [q.Ip];
        string terms = ips.Count == 1 ? $"TERM({ips[0]})" : "(" + string.Join(" OR ", ips.Select(ip => $"TERM({ip})")) + ")";
        var s = cfg.GetSection("Splunk");
        string baseUrl = (s["BaseUrl"] ?? throw new InvalidOperationException("Splunk:BaseUrl tanımlı değil.")).TrimEnd('/');
        string exportPath = s["ExportPath"] ?? "/services/search/v2/jobs/export";
        string index = s["Index"] ?? "carbonblack";
        string sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";

        // TERM(): IP'nin geçmediği olaylar hiç okunmaz (hızın kaynağı).
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" {terms}
            | eval dir=lower(direction),
                   client_ip   = if(dir=="outbound", local_ip, remote_ip),
                   server_ip   = if(dir=="outbound", remote_ip, local_ip),
                   server_port = if(dir=="outbound", remote_port, local_port),
                   process     = replace(process_path, "^.*[\\\\/]", "")
            | stats count min(_time) as first_seen max(_time) as last_seen
                    values(computer_name) as computer_name values(process) as process
                    by client_ip server_ip server_port Protocol
            | sort 0 - count
            | eval first_seen=strftime(first_seen, "%Y-%m-%d %H:%M:%S"), last_seen=strftime(last_seen, "%Y-%m-%d %H:%M:%S")
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + exportPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["search"] = spl,
                ["earliest_time"] = q.StartUnix.ToString(),
                ["latest_time"] = q.EndUnix.ToString(),
                ["output_mode"] = "json"
            })
        };

        string? token = s["Token"];
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            if (string.IsNullOrEmpty(s["Username"]))
                throw new InvalidOperationException("Splunk:Token veya Splunk:Username/Password tanımlı değil.");
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s["Username"]}:{s["Password"]}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        var sw = Stopwatch.StartNew();
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Splunk HTTP {(int)resp.StatusCode}: {Truncate(body, 400)}");
        }

        var rows = new List<Dictionary<string, string>>();
        var messages = new List<string>();

        // Export endpoint'i her satırda ayrı bir JSON nesnesi döner.
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(line); }
            catch (JsonException) { continue; }

            if (node?["preview"] is JsonValue pv && pv.TryGetValue<bool>(out bool isPreview) && isPreview)
                continue;

            if (node?["result"] is JsonObject result)
                rows.Add(result.ToDictionary(kv => kv.Key, kv => NodeToText(kv.Value)));

            if (node?["messages"] is JsonArray msgs)
            {
                foreach (var m in msgs)
                {
                    string? type = m?["type"]?.ToString();
                    if (type is "ERROR" or "FATAL" or "WARN")
                        messages.Add($"{type}: {m?["text"]}");
                }
            }
        }

        return new SplunkResult(rows, messages, sw.ElapsedMilliseconds);
    }

    static string NodeToText(JsonNode? node) => node switch
    {
        null => "",
        JsonArray arr => string.Join(", ", arr.Select(x => x?.ToString())),
        _ => node.ToString()
    };

    static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}

// ---------------------------------------------------------------------------
// Riverbed AppResponse — mantık DeltaFlow Tek IP test aracından birebir alındı
// ---------------------------------------------------------------------------
static class AppResponseService
{
    public const int AllAppliances = -1;

    // applianceIndex = -1: config'deki tüm cihazlar paralel sorgulanır, sonuçlar birleştirilir.
    // Bir cihaz hata verirse diğerleri devam eder; hata mesajlara eklenir.
    public static async Task<AppResponseResult> QueryAnyAsync(LookupQuery q, int applianceIndex, IConfiguration cfg, HttpClient http,
        CancellationToken ct, IReadOnlyList<string>? ips = null)
    {
        if (applianceIndex != AllAppliances)
            return await QueryAsync(q, applianceIndex, cfg, http, ct, ips);

        var names = cfg.GetSection("Servers").GetChildren().Select((s, i) => s["Name"] ?? $"Cihaz {i + 1}").ToList();
        if (names.Count == 0)
            throw new InvalidOperationException("Config'de 'Servers' listesi boş.");

        var results = await Task.WhenAll(names.Select(async (name, i) =>
        {
            try
            {
                return (name, result: await QueryAsync(q, i, cfg, http, ct, ips), error: (string?)null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (name, result: (AppResponseResult?)null, error: ex is TaskCanceledException ? "Zaman aşımı" : ex.Message);
            }
        }));

        var ok = results.Where(r => r.result != null).ToList();
        if (ok.Count == 0)
            throw new InvalidOperationException("Hiçbir AppResponse cihazından sonuç alınamadı: " +
                string.Join(" | ", results.Select(r => $"{r.name}: {r.error}")));

        string Label(string name, string vifg) => string.IsNullOrEmpty(vifg) ? name : $"{name}/{vifg}";
        var messages = results.Where(r => r.error != null).Select(r => $"[{r.name}] {r.error}")
            .Concat(ok.SelectMany(r => r.result!.Messages.Select(m => $"[{r.name}] {m}")))
            .ToList();

        return new AppResponseResult(
            $"Tüm cihazlar ({ok.Count}/{names.Count})",
            ok.Sum(r => r.result!.VifgCount),
            ok.Sum(r => r.result!.RawL4),
            ok.Sum(r => r.result!.RawL7),
            ok.SelectMany(r => r.result!.L4.Select(x => x with { Vifg = Label(r.name, x.Vifg) })).OrderByDescending(x => x.Hit).ToList(),
            ok.SelectMany(r => r.result!.L7.Select(x => x with { Vifg = Label(r.name, x.Vifg) })).OrderByDescending(x => x.Hit).ToList(),
            messages,
            ok.Max(r => r.result!.ElapsedMs));
    }

    // ips verilirse tek raporda birden fazla IP aranır (2. seviye); verilmezse q.Ip.
    public static async Task<AppResponseResult> QueryAsync(LookupQuery q, int applianceIndex, IConfiguration cfg, HttpClient http, CancellationToken ct,
        IReadOnlyList<string>? ips = null)
    {
        ips ??= [q.Ip];
        string? username = cfg["Credentials:Username"];
        string? password = cfg["Credentials:Password"];
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Config'de Credentials.Username veya Credentials.Password eksik.");

        var servers = cfg.GetSection("Servers").GetChildren().ToList();
        if (servers.Count == 0)
            throw new InvalidOperationException("Config'de 'Servers' listesi boş.");
        if (applianceIndex < 0 || applianceIndex >= servers.Count)
            applianceIndex = 0;

        string applianceName = servers[applianceIndex]["Name"] ?? "Riverbed";
        string applianceIp = servers[applianceIndex]["Ip"] is { Length: > 0 } sip && !string.IsNullOrWhiteSpace(sip)
            ? sip.Trim()
            : throw new InvalidOperationException($"'{applianceName}' cihazının Ip değeri tanımlı değil.");
        string baseUrl = $"https://{applianceIp}";

        string sourcePathType = cfg["SourcePathType"] ?? "jobs";
        string sourceL4Name = cfg["SourceL4"] ?? "flow_tcp";
        string sourceL7Name = cfg["SourceL7"] ?? "wtapages";
        bool deleteInstances = cfg.GetValue("DeleteReportInstances", true);

        var vifgIds = cfg.GetSection("VifgIds").GetChildren()
            .Select(c => (c.Value ?? "").Trim())
            .ToList();
        if (vifgIds.Count == 0) vifgIds.Add("");

        var sw = Stopwatch.StartNew();
        var messages = new List<string>();
        var l4 = new List<L4Row>();
        var l7 = new List<L7Row>();
        int rawL4Total = 0, rawL7Total = 0;

        // 1. Token
        var tokenPayload = new { user_credentials = new { username, password }, generate_refresh_token = true };
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token")
        {
            // Content-Type'a charset eklenmesin: bazı AppResponse sürümleri 415 döndürüyor.
            Content = new StringContent(JsonSerializer.Serialize(tokenPayload), Encoding.UTF8)
        };
        tokenReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        tokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var tokenResp = await http.SendAsync(tokenReq, ct);
        if (!tokenResp.IsSuccessStatusCode)
        {
            string tokenBody = await tokenResp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"AppResponse token alınamadı ({applianceName} / {applianceIp}, HTTP {(int)tokenResp.StatusCode}): {Truncate(tokenBody, 400)}");
        }

        string jwt = JsonNode.Parse(await tokenResp.Content.ReadAsStringAsync(ct))?["access_token"]?.ToString()
            ?? throw new InvalidOperationException("AppResponse token yanıtında access_token yok.");

        // 2. IP filtresi: hem client hem server olarak geçen akışlar
        string ipFilter = "(" + string.Join(" or ", ips.Select(ip => $"cli_tcp.ip == {ip} or srv_tcp.ip == {ip}")) + ")";
        var steelFilters = new object[] { new { id = "traffic", type = "STEELFILTER", value = ipFilter } };

        foreach (var vifg in vifgIds)
        {
            string label = string.IsNullOrWhiteSpace(vifg) ? "" : vifg;

            object sourceL4Obj = string.IsNullOrWhiteSpace(vifg)
                ? new { name = sourceL4Name }
                : new { name = sourceL4Name, path = $"{sourcePathType}/{vifg}" };

            object sourceL7Obj = string.IsNullOrWhiteSpace(vifg)
                ? new { name = sourceL7Name }
                : new { name = sourceL7Name, path = $"{sourcePathType}/{vifg}" };

            var sorguPayload = new
            {
                info = new { name = "Tek IP Analiz Raporu", description = $"{(ips.Count == 1 ? ips[0] : $"{ips.Count} IP")} | {q.Start:HH:mm}-{q.End:HH:mm}" },
                data_defs = new object[]
                {
                    new
                    {
                        source = sourceL4Obj,
                        columns = new[] { "start_time", "cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port" },
                        time = new { start = q.StartUnix.ToString(), end = q.EndUnix.ToString() },
                        filters = steelFilters
                    },
                    new
                    {
                        source = sourceL7Obj,
                        columns = new[] { "start_time", "web.client_ip", "web.server_ip", "web.url" },
                        time = new { start = q.StartUnix.ToString(), end = q.EndUnix.ToString() },
                        filters = steelFilters,
                        topn = 50000
                    }
                }
            };

            // 3. Rapor instance oluştur
            using var instancesResp = await http.SendAsync(
                Req(HttpMethod.Post, $"{baseUrl}/api/npm.reports/1.0/instances", jwt, Json(sorguPayload)), ct);

            if (!instancesResp.IsSuccessStatusCode)
            {
                string errBody = await instancesResp.Content.ReadAsStringAsync(ct);
                messages.Add($"{Prefix(label)}Rapor oluşturulamadı (HTTP {(int)instancesResp.StatusCode}): {Truncate(errBody, 300)}");
                continue;
            }

            string? raporId = JsonNode.Parse(await instancesResp.Content.ReadAsStringAsync(ct))?["id"]?.ToString();
            if (string.IsNullOrEmpty(raporId))
            {
                messages.Add($"{Prefix(label)}Rapor ID dönmedi.");
                continue;
            }

            try
            {
                // 4. Tamamlanana kadar bekle (30 x 2 sn)
                bool completed = false, failed = false;
                for (int bekle = 0; bekle < 30; bekle++)
                {
                    await Task.Delay(2000, ct);

                    using var stResp = await http.SendAsync(
                        Req(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{raporId}", jwt), ct);
                    string stBody = await stResp.Content.ReadAsStringAsync(ct);
                    string? durum = JsonNode.Parse(stBody)?["data_defs"]?.AsArray()?[0]?["status"]?["state"]?.ToString();

                    if (durum == "completed") { completed = true; break; }
                    if (durum == "error")
                    {
                        messages.Add($"{Prefix(label)}Rapor hata ile sonuçlandı: {Truncate(stBody, 300)}");
                        failed = true;
                        break;
                    }
                }

                if (!completed)
                {
                    if (!failed)
                        messages.Add($"{Prefix(label)}Rapor 60 saniyede tamamlanmadı.");
                    continue;
                }

                // 5. Verileri çek
                var verilerL4 = await GetDataAsync(http, baseUrl, raporId, 1, jwt, ct);
                var verilerL7 = await GetDataAsync(http, baseUrl, raporId, 2, jwt, ct);
                rawL4Total += verilerL4?.Count ?? 0;
                rawL7Total += verilerL7?.Count ?? 0;

                // 6. Gruplama (konsol uygulamasındaki ile aynı)
                if (verilerL4 != null)
                {
                    l4.AddRange(verilerL4.Where(v => v != null)
                        .Select(v => v!.AsArray())
                        .Select(arr => new
                        {
                            CliIp = arr.Count > 1 ? arr[1]?.ToString() ?? "-" : "-",
                            SrvIp = arr.Count > 2 ? arr[2]?.ToString() ?? "-" : "-",
                            SrvPort = arr.Count > 3 ? arr[3]?.ToString() ?? "-" : "-"
                        })
                        .GroupBy(x => new { x.CliIp, x.SrvIp, x.SrvPort })
                        .Select(g => new L4Row(label, g.Key.CliIp, g.Key.SrvIp, g.Key.SrvPort, g.Count())));
                }

                if (verilerL7 != null)
                {
                    l7.AddRange(verilerL7.Where(v => v != null)
                        .Select(v => v!.AsArray())
                        .Select(arr => new
                        {
                            CliIp = arr.Count > 1 ? arr[1]?.ToString() ?? "-" : "-",
                            SrvIp = arr.Count > 2 ? arr[2]?.ToString() ?? "-" : "-",
                            Url = arr.Count > 3 ? arr[3]?.ToString() ?? "-" : "-"
                        })
                        .GroupBy(x => new { x.CliIp, x.SrvIp, x.Url })
                        .Select(g => new L7Row(label, g.Key.CliIp, g.Key.SrvIp, g.Key.Url, g.Count())));
                }
            }
            finally
            {
                // Cihazda biriken rapor instance'larını temizle; hata olursa yok say.
                if (deleteInstances)
                {
                    try
                    {
                        using var delResp = await http.SendAsync(
                            Req(HttpMethod.Delete, $"{baseUrl}/api/npm.reports/1.0/instances/items/{raporId}", jwt),
                            CancellationToken.None);
                    }
                    catch { /* yok say */ }
                }
            }
        }

        return new AppResponseResult(
            applianceName,
            vifgIds.Count(v => !string.IsNullOrWhiteSpace(v)),
            rawL4Total,
            rawL7Total,
            l4.OrderByDescending(r => r.Hit).ToList(),
            l7.OrderByDescending(r => r.Hit).ToList(),
            messages,
            sw.ElapsedMilliseconds);
    }

    static async Task<JsonArray?> GetDataAsync(HttpClient http, string baseUrl, string raporId, int dataDef, string jwt, CancellationToken ct)
    {
        using var resp = await http.SendAsync(Req(HttpMethod.Get,
            $"{baseUrl}/api/npm.reports/1.0/instances/items/{raporId}/data_defs/items/{dataDef}/data?limit=1000000", jwt), ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(raw)?["data"]?.AsArray();
    }

    static HttpRequestMessage Req(HttpMethod method, string url, string jwt, HttpContent? content = null)
    {
        var r = new HttpRequestMessage(method, url) { Content = content };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return r;
    }

    static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    static string Prefix(string vifg) => string.IsNullOrEmpty(vifg) ? "" : $"[{vifg}] ";

    static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}

// JSON çıktısı (camelCase) önceki anonim nesnelerle birebir aynı: mevcut arayüz etkilenmez.
record SplunkResult(List<Dictionary<string, string>> Rows, List<string> Messages, long ElapsedMs);
record AppResponseResult(string Appliance, int VifgCount, int RawL4, int RawL7,
    List<L4Row> L4, List<L7Row> L7, List<string> Messages, long ElapsedMs);

record L4Row(string Vifg, string ClientIp, string ServerIp, string Port, int Hit);
record L7Row(string Vifg, string ClientIp, string ServerIp, string Url, int Hit);
