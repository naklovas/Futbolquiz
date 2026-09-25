// ============================================================================
// DeltaFlow · Trafik Servisi (tek dosya)
//
// Bir IPv4 adresinin son N saatlik (varsayılan 24) ağ trafiğini iki kaynaktan çeker:
//   1) CARBON BLACK  : uç nokta (endpoint) bağlantı kayıtları; süreç ve bilgisayar adıyla.
//                      Veri Splunk'taki Carbon Black index'inden (varsayılan "carbonblack",
//                      sourcetype "bit9:carbonblack:json") Splunk export API'siyle okunur.
//   2) APPRESPONSE   : Riverbed AppResponse'un ağdan gördüğü L4 (TCP) akışlar ve L7 (web) URL'ler;
//                      Servers listesindeki tüm kutular paralel sorgulanır.
// Sonuçlar sorgulanan IP'ye göre GELEN / GİDEN olarak birleştirilir ve AI'a gidecek metin üretilir.
//
// ----------------------------------------------------------------------------
// KURULUM
// ----------------------------------------------------------------------------
// 1) Bu dosyayı projeye ekleyin (ek NuGet paketi gerekmez).
// 2) Program.cs:
//        using DeltaFlow.Trafik;
//        builder.Services.AddDeltaFlowTrafik(builder.Configuration);
// 3) Kullanım (endpoint parametresine "TrafikServisi trafikServisi" ekleyerek):
//        var trafik = await trafikServisi.GetirAsync(ip);          // son 24 saat
//        string trafikMetin = TrafikServisi.AiMetni(trafik);        // kayıt yoksa "KAYIT YOK."
//    Farklı aralık: GetirAsync(ip, saat: 6). Ham veri: trafik.Gelen / trafik.Giden (Akis listesi).
//
// ----------------------------------------------------------------------------
// APPSETTINGS.JSON (şifre/token'ları git'e girmeyen bir dosyada tutun, ör. appsettings.Production.json)
// ----------------------------------------------------------------------------
//   "Trafik": {
//     "SaatAraligi": 24,
//     "Kaynaklar": "carbonblack,appresponse",
//
//     "CarbonBlack": {
//       "SplunkUrl": "https://splunk-sunucu:8089",
//       "Token": "",                       // Splunk token (Bearer). Yoksa Username/Password (Basic)
//       "Username": "",
//       "Password": "",
//       "Index": "carbonblack",
//       "Sourcetype": "bit9:carbonblack:json",
//       "ExportPath": "/services/search/v2/jobs/export",
//       "IgnoreSslErrors": false,
//       "TimeoutMinutes": 10
//     },
//
//     "AppResponse": {
//       "Username": "",
//       "Password": "",
//       "Servers": [ { "Name": "Kutu-1", "Ip": "10.x.x.x" }, { "Name": "Kutu-2", "Ip": "10.x.x.x" } ],
//       "Cihaz": "tum",                     // "tum" = hepsi paralel; tek kutu için sıra numarası: "0", "1"...
//       "SourcePathType": "jobs",
//       "SourceL4": "flow_tcp",
//       "SourceL7": "wtapages",
//       "VifgIds": [],                      // doluysa her VIFG için ayrı rapor
//       "DeleteReportInstances": true,
//       "IgnoreSslErrors": true,
//       "BeklemeSaniye": 120,               // 24 saatlik rapor uzun sürebilir; "tamamlanmadı" olursa artırın
//       "TimeoutMinutes": 3
//     }
//   }
//
// ----------------------------------------------------------------------------
// MANTIK
// ----------------------------------------------------------------------------
// Carbon Black : SPL "search index=<Index> sourcetype=<Sourcetype> TERM(<ip>)" (TERM: sadece IP'yi içeren
//                olaylar okunur). direction alanına göre istemci/sunucu ayrılır (outbound: local = istemci),
//                süreç adı process_path'ten alınır; client_ip, server_ip, server_port, Protocol bazında
//                count / first_seen / last_seen / computer_name / process. Yanıt NDJSON; preview satırları atlanır.
// AppResponse  : kutu başına token -> STEELFILTER "(cli_tcp.ip == IP or srv_tcp.ip == IP)" ile L4+L7 rapor
//                -> iki data_def "completed" olana kadar 2 sn aralıkla bekleme -> veri -> rapor silinir.
//                Hata veren kutu diğerlerini durdurmaz; hata Mesajlar'a yazılır.
// Birleştirme  : sunucu = IP -> GELEN (karşı = istemci, port = IP'nin portu); istemci = IP -> GİDEN
//                (karşı = sunucu, port = karşının portu). Aynı (yön, karşı IP, port) iki kaynaktan gelirse
//                hit'ler toplanır (CarbonBlackHit / AppResponseHit ayrıca tutulur). L7 URL'leri aynı karşı
//                IP'nin akışına eklenir; L4 karşılığı yoksa portsuz ("web") akış olur.
// ============================================================================
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DeltaFlow.Trafik
{
    // Sorgulanan IP'ye göre tek bir akış: Yon "in" (karşı IP -> sorgulanan IP:Port) ya da "out" (sorgulanan IP -> karşı IP:Port)
    public sealed record Akis(string Yon, string KarsiIp, string Port, long Hit, long CarbonBlackHit, long AppResponseHit,
        List<string> Protokoller, List<string> Surecler, List<string> Bilgisayarlar, List<string> Urller,
        string? IlkGorulme, string? SonGorulme);

    public sealed record TrafikSonucu(string Ip, DateTimeOffset Baslangic, DateTimeOffset Bitis,
        List<Akis> Gelen, List<Akis> Giden, List<string> Mesajlar, long SureMs)
    {
        public bool KayitVar => Gelen.Count > 0 || Giden.Count > 0;
    }

    public static class TrafikServisiKurulum
    {
        // HttpClient'lar kurumsal proxy'yi kullanmaz (Splunk ve AppResponse iç adresler).
        public static IServiceCollection AddDeltaFlowTrafik(this IServiceCollection services, IConfiguration cfg)
        {
            services.AddHttpClient(TrafikServisi.CarbonBlackClient, c => c.Timeout = TimeSpan.FromMinutes(cfg.GetValue("Trafik:CarbonBlack:TimeoutMinutes", 10)))
                .ConfigurePrimaryHttpMessageHandler(() => Handler(cfg.GetValue("Trafik:CarbonBlack:IgnoreSslErrors", false)));
            services.AddHttpClient(TrafikServisi.AppResponseClient, c => c.Timeout = TimeSpan.FromMinutes(cfg.GetValue("Trafik:AppResponse:TimeoutMinutes", 3)))
                .ConfigurePrimaryHttpMessageHandler(() => Handler(cfg.GetValue("Trafik:AppResponse:IgnoreSslErrors", true)));
            services.AddSingleton<TrafikServisi>();
            return services;
        }

        static HttpMessageHandler Handler(bool ignoreSsl)
        {
            var h = new HttpClientHandler { UseProxy = false };
            if (ignoreSsl) h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            return h;
        }
    }

    public sealed class TrafikServisi(IHttpClientFactory factory, IConfiguration cfg, ILogger<TrafikServisi> log)
    {
        public const string CarbonBlackClient = "deltaflow-carbonblack";
        public const string AppResponseClient = "deltaflow-appresponse";

        // ------------------------------------------------------------------
        // Ana giriş: IP'nin son N saatlik trafiği (N = parametre ?? Trafik:SaatAraligi ?? 24)
        // ------------------------------------------------------------------
        public async Task<TrafikSonucu> GetirAsync(string ip, double? saat = null, CancellationToken ct = default)
        {
            if (!IsIpv4(ip, out ip))
                throw new ArgumentException("Geçerli bir IPv4 adresi değil.", nameof(ip));

            var bitis = DateTimeOffset.Now;
            var baslangic = bitis.AddHours(-(saat ?? cfg.GetValue("Trafik:SaatAraligi", 24.0)));
            string kaynaklar = (cfg["Trafik:Kaynaklar"] ?? "carbonblack,appresponse").ToLowerInvariant();
            var sw = Stopwatch.StartNew();
            var mesajlar = new List<string>();

            // İki kaynak paralel; biri hata verirse diğerinin sonucu yine kullanılır.
            var cbTask = kaynaklar.Contains("carbonblack") || kaynaklar.Contains("splunk")
                ? Guvenli("Carbon Black", () => CarbonBlackAsync(ip, baslangic, bitis, ct), mesajlar, ct) : Task.FromResult<List<Satir>?>(null);
            var arTask = kaynaklar.Contains("appresponse") ? Guvenli("AppResponse", () => AppResponseTumKutularAsync(ip, baslangic, bitis, mesajlar, ct), mesajlar, ct) : Task.FromResult<List<Satir>?>(null);
            await Task.WhenAll(cbTask, arTask);

            var (gelen, giden) = Birlestir(ip, cbTask.Result ?? [], arTask.Result ?? []);
            return new TrafikSonucu(ip, baslangic, bitis, gelen, giden, mesajlar, sw.ElapsedMilliseconds);
        }

        // ------------------------------------------------------------------
        // AI metni: başlık + her akış bir satır. Kayıt yoksa "KAYIT YOK."
        // ------------------------------------------------------------------
        public static string AiMetni(TrafikSonucu s, int maxSatir = 40)
        {
            if (!s.KayitVar)
                return "KAYIT YOK." + (s.Mesajlar.Count > 0 ? $" ({string.Join("; ", s.Mesajlar)})" : "");

            var sb = new StringBuilder();
            sb.AppendLine($"Kaynak: Carbon Black (CB) + Riverbed AppResponse (AR) | {s.Baslangic:yyyy-MM-dd HH:mm} - {s.Bitis:yyyy-MM-dd HH:mm}");
            void Yaz(string baslik, List<Akis> list, bool gelen)
            {
                sb.AppendLine($"{baslik}: {list.Select(a => a.KarsiIp).Distinct().Count()} farklı IP, {list.Count} akış");
                foreach (var a in list.Take(maxSatir))
                {
                    string port = a.Port == "" ? "web" : a.Port;
                    string yon = gelen ? $"{a.KarsiIp} -> {s.Ip}:{port}" : $"{s.Ip} -> {a.KarsiIp}:{port}";
                    var ek = new List<string> { $"{a.Hit} bağlantı (CB: {a.CarbonBlackHit}, AR: {a.AppResponseHit})" };
                    if (a.Bilgisayarlar.Count > 0) ek.Add("bilgisayar: " + string.Join(", ", a.Bilgisayarlar.Take(2)));
                    if (a.Surecler.Count > 0) ek.Add("süreç: " + string.Join(", ", a.Surecler.Take(3)));
                    if (a.Urller.Count > 0) ek.Add("url: " + string.Join(", ", a.Urller.Take(3)));
                    if (a.SonGorulme != null) ek.Add("son: " + a.SonGorulme);
                    sb.AppendLine($"- {yon} | {string.Join(" | ", ek)}");
                }
                if (list.Count > maxSatir) sb.AppendLine($"- ... +{list.Count - maxSatir} akış daha");
            }
            Yaz("GELEN (bu sunucuya bağlananlar)", s.Gelen, gelen: true);
            Yaz("GİDEN (bu sunucunun bağlandıkları)", s.Giden, gelen: false);
            return sb.ToString().TrimEnd();
        }

        // Ham satır: istemci -> sunucu:port (Kaynak "cb" | "l4" | "l7")
        sealed record Satir(string Kaynak, string Istemci, string Sunucu, string Port, long Hit,
            string? Protokol = null, string? Surec = null, string? Bilgisayar = null, string? Url = null,
            string? Ilk = null, string? Son = null);

        // ==================================================================
        // 1) CARBON BLACK (Splunk'taki Carbon Black index'i üzerinden)
        //    Splunk /services/search/v2/jobs/export'a SPL gönderilir; TERM(ip) ile sadece o IP'yi
        //    içeren Carbon Black olayları okunur. direction alanına göre istemci/sunucu ayrılır ve
        //    (istemci, sunucu, port, protokol) bazında gruplanır.
        // ==================================================================
        async Task<List<Satir>> CarbonBlackAsync(string ip, DateTimeOffset bas, DateTimeOffset bit, CancellationToken ct)
        {
            var s = cfg.GetSection("Trafik:CarbonBlack");
            string baseUrl = (s["SplunkUrl"] ?? throw new InvalidOperationException("Trafik:CarbonBlack:SplunkUrl tanımlı değil.")).TrimEnd('/');
            string index = s["Index"] ?? "carbonblack";
            string sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";

            string spl = $"""
                search index={index} sourcetype="{sourcetype}" TERM({ip})
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

            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + (s["ExportPath"] ?? "/services/search/v2/jobs/export"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["search"] = spl,
                    ["earliest_time"] = bas.ToUnixTimeSeconds().ToString(),
                    ["latest_time"] = bit.ToUnixTimeSeconds().ToString(),
                    ["output_mode"] = "json"
                })
            };
            if (!string.IsNullOrWhiteSpace(s["Token"]))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s["Token"]);
            else if (!string.IsNullOrEmpty(s["Username"]))
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($"{s["Username"]}:{s["Password"]}")));
            else
                throw new InvalidOperationException("Trafik:CarbonBlack:Token veya Username/Password tanımlı değil.");

            using var resp = await factory.CreateClient(CarbonBlackClient).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Splunk (Carbon Black) HTTP {(int)resp.StatusCode}: {Kisalt(await resp.Content.ReadAsStringAsync(ct), 300)}");

            // Export endpoint'i her satırda ayrı bir JSON nesnesi döner (NDJSON); preview satırları atlanır.
            var rows = new List<Satir>();
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JsonNode? node;
                try { node = JsonNode.Parse(line); } catch (JsonException) { continue; }
                if (node?["preview"] is JsonValue pv && pv.TryGetValue<bool>(out bool preview) && preview) continue;
                if (node?["result"] is not JsonObject r) continue;

                string? V(string k) => Temiz(r[k] switch { JsonArray a => string.Join(", ", a.Select(x => x?.ToString())), var x => x?.ToString() });
                rows.Add(new Satir("cb", V("client_ip") ?? "", V("server_ip") ?? "", V("server_port") ?? "",
                    long.TryParse(V("count"), out long c) ? c : 1, V("Protocol"), V("process"), V("computer_name"),
                    null, V("first_seen"), V("last_seen")));
            }
            return rows;
        }

        // ==================================================================
        // 2) RIVERBED APPRESPONSE
        //    Her kutu için: token al -> STEELFILTER'lı rapor instance oluştur (L4 flow_tcp + L7 wtapages)
        //    -> tamamlanana kadar bekle -> verileri çek -> instance'ı sil. VIFG listesi varsa her VIFG ayrı.
        //    Trafik:AppResponse:Cihaz = "tum" (varsayılan) ise Servers listesindeki tüm kutular paralel.
        // ==================================================================
        async Task<List<Satir>> AppResponseTumKutularAsync(string ip, DateTimeOffset bas, DateTimeOffset bit, List<string> mesajlar, CancellationToken ct)
        {
            var servers = cfg.GetSection("Trafik:AppResponse:Servers").GetChildren()
                .Select((x, i) => (Name: x["Name"] ?? $"Kutu {i + 1}", Ip: x["Ip"]?.Trim())).ToList();
            if (servers.Count == 0) throw new InvalidOperationException("Trafik:AppResponse:Servers listesi boş.");

            string cihaz = cfg["Trafik:AppResponse:Cihaz"] ?? "tum";
            if (int.TryParse(cihaz, out int idx) && idx >= 0 && idx < servers.Count) servers = [servers[idx]];

            var sonuc = await Task.WhenAll(servers.Select(async sv =>
            {
                try { return await AppResponseKutuAsync(sv.Name, sv.Ip, ip, bas, bit, mesajlar, ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lock (mesajlar) mesajlar.Add($"AppResponse [{sv.Name}]: {(ex is TaskCanceledException ? "zaman aşımı" : ex.Message)}");
                    return null;
                }
            }));
            if (sonuc.All(r => r == null))
                throw new InvalidOperationException("Hiçbir AppResponse kutusundan sonuç alınamadı.");
            return sonuc.Where(r => r != null).SelectMany(r => r!).ToList();
        }

        async Task<List<Satir>> AppResponseKutuAsync(string ad, string? kutuIp, string ip, DateTimeOffset bas, DateTimeOffset bit,
            List<string> mesajlar, CancellationToken ct)
        {
            var s = cfg.GetSection("Trafik:AppResponse");
            if (string.IsNullOrWhiteSpace(kutuIp)) throw new InvalidOperationException("Ip değeri tanımlı değil.");
            string user = s["Username"] ?? throw new InvalidOperationException("Trafik:AppResponse:Username tanımlı değil.");
            string pass = s["Password"] ?? throw new InvalidOperationException("Trafik:AppResponse:Password tanımlı değil.");
            string baseUrl = $"https://{kutuIp}";
            var http = factory.CreateClient(AppResponseClient);

            // 1. Token (Content-Type'a charset eklenmez: bazı sürümler 415 döndürüyor)
            var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { user_credentials = new { username = user, password = pass }, generate_refresh_token = false }))
            };
            tokenReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var tokenResp = await http.SendAsync(tokenReq, ct);
            if (!tokenResp.IsSuccessStatusCode)
                throw new HttpRequestException($"token alınamadı (HTTP {(int)tokenResp.StatusCode})");
            string jwt = JsonNode.Parse(await tokenResp.Content.ReadAsStringAsync(ct))?["access_token"]?.ToString()
                ?? throw new InvalidOperationException("token yanıtında access_token yok");

            // 2. Filtre: IP hem istemci hem sunucu olarak
            var filters = new object[] { new { id = "traffic", type = "STEELFILTER", value = $"(cli_tcp.ip == {ip} or srv_tcp.ip == {ip})" } };
            string l4 = s["SourceL4"] ?? "flow_tcp", l7 = s["SourceL7"] ?? "wtapages", pathType = s["SourcePathType"] ?? "jobs";
            var vifgs = s.GetSection("VifgIds").GetChildren().Select(c => (c.Value ?? "").Trim()).ToList();
            if (vifgs.Count == 0) vifgs.Add("");
            var time = new { start = bas.ToUnixTimeSeconds().ToString(), end = bit.ToUnixTimeSeconds().ToString() };
            int bekleme = s.GetValue("BeklemeSaniye", 120);
            var rows = new List<Satir>();

            foreach (var vifg in vifgs)
            {
                object Kaynak(string name) => vifg == "" ? new { name } : new { name, path = $"{pathType}/{vifg}" };
                var payload = new
                {
                    info = new { name = "DeltaFlow Trafik", description = $"{ip} | son {(bit - bas).TotalHours:0} saat" },
                    data_defs = new object[]
                    {
                        new { source = Kaynak(l4), columns = new[] { "start_time", "cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port" }, time, filters },
                        new { source = Kaynak(l7), columns = new[] { "start_time", "web.client_ip", "web.server_ip", "web.url" }, time, filters, topn = 50000 }
                    }
                };

                // 3. Rapor oluştur
                using var createResp = await http.SendAsync(Istek(HttpMethod.Post, $"{baseUrl}/api/npm.reports/1.0/instances", jwt,
                    new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")), ct);
                string etiket = vifg == "" ? ad : $"{ad}/{vifg}";
                if (!createResp.IsSuccessStatusCode)
                {
                    lock (mesajlar) mesajlar.Add($"AppResponse [{etiket}]: rapor oluşturulamadı (HTTP {(int)createResp.StatusCode})");
                    continue;
                }
                string? id = JsonNode.Parse(await createResp.Content.ReadAsStringAsync(ct))?["id"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;

                try
                {
                    // 4. Tamamlanana kadar bekle (2 sn aralıkla; iki data_def de "completed" olmalı)
                    bool bitti = false;
                    for (int t = 0; t < bekleme / 2 && !bitti; t++)
                    {
                        await Task.Delay(2000, ct);
                        using var st = await http.SendAsync(Istek(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}", jwt), ct);
                        var defs = JsonNode.Parse(await st.Content.ReadAsStringAsync(ct))?["data_defs"]?.AsArray();
                        var durumlar = defs?.Select(d => d?["status"]?["state"]?.ToString()).ToList() ?? [];
                        if (durumlar.Contains("error")) { lock (mesajlar) mesajlar.Add($"AppResponse [{etiket}]: rapor hata verdi"); break; }
                        bitti = durumlar.Count > 0 && durumlar.All(x => x == "completed");
                    }
                    if (!bitti) { lock (mesajlar) mesajlar.Add($"AppResponse [{etiket}]: rapor {bekleme} sn'de tamamlanmadı"); continue; }

                    // 5. Veriler; aynı (istemci, sunucu, port/url) satırları sayılarak gruplanır
                    foreach (var g in (await VeriAsync(http, baseUrl, id, 1, jwt, ct)).GroupBy(a => (a.c, a.s, a.x)))
                        rows.Add(new Satir("l4", g.Key.c, g.Key.s, g.Key.x, g.Count(), "TCP"));
                    foreach (var g in (await VeriAsync(http, baseUrl, id, 2, jwt, ct)).GroupBy(a => (a.c, a.s, a.x)))
                        rows.Add(new Satir("l7", g.Key.c, g.Key.s, "", g.Count(), Url: g.Key.x));
                }
                finally
                {
                    // Cihazda rapor birikmesin
                    if (s.GetValue("DeleteReportInstances", true))
                        try { using var _ = await http.SendAsync(Istek(HttpMethod.Delete, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}", jwt), CancellationToken.None); }
                        catch (Exception ex) { log.LogDebug(ex, "AppResponse rapor silinemedi"); }
                }
            }
            return rows;
        }

        static async Task<List<(string c, string s, string x)>> VeriAsync(HttpClient http, string baseUrl, string id, int def, string jwt, CancellationToken ct)
        {
            using var resp = await http.SendAsync(Istek(HttpMethod.Get,
                $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}/data_defs/items/{def}/data?limit=1000000", jwt), ct);
            if (!resp.IsSuccessStatusCode) return [];
            var data = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))?["data"]?.AsArray();
            return data?.OfType<JsonArray>()
                .Select(a => (c: a.Count > 1 ? a[1]?.ToString() ?? "" : "", s: a.Count > 2 ? a[2]?.ToString() ?? "" : "", x: a.Count > 3 ? a[3]?.ToString() ?? "" : ""))
                .ToList() ?? [];
        }

        static HttpRequestMessage Istek(HttpMethod m, string url, string jwt, HttpContent? content = null)
        {
            var r = new HttpRequestMessage(m, url) { Content = content };
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return r;
        }

        // ==================================================================
        // 3) BİRLEŞTİRME
        //    Her satır sorgulanan IP'ye göre yönlenir: sunucu == IP -> GELEN (karşı = istemci, port = IP'nin portu),
        //    istemci == IP -> GİDEN (karşı = sunucu, port = karşının portu). Aynı (yön, karşı IP, port) toplanır.
        //    L7 URL'leri aynı yöndeki aynı karşı IP'nin akışına eklenir; L4 karşılığı yoksa portsuz ("web") satır olur.
        // ==================================================================
        static (List<Akis> gelen, List<Akis> giden) Birlestir(string ip, List<Satir> cb, List<Satir> ar)
        {
            var agg = new Dictionary<(string yon, string karsi, string port), Toplam>();
            var urls = new Dictionary<(string yon, string karsi), Dictionary<string, long>>();

            foreach (var r in cb.Concat(ar))
            {
                string yon, karsi;
                if (r.Sunucu == ip && r.Istemci != ip && r.Istemci is not ("" or "-")) { yon = "in"; karsi = r.Istemci; }
                else if (r.Istemci == ip && r.Sunucu != ip && r.Sunucu is not ("" or "-")) { yon = "out"; karsi = r.Sunucu; }
                else continue;

                if (r.Kaynak == "l7")
                {
                    if (string.IsNullOrEmpty(r.Url) || r.Url == "-") continue;
                    var u = urls.TryGetValue((yon, karsi), out var d) ? d : urls[(yon, karsi)] = new();
                    u[r.Url] = u.GetValueOrDefault(r.Url) + r.Hit;
                    continue;
                }
                var key = (yon, karsi, r.Port == "-" ? "" : r.Port);
                var t = agg.TryGetValue(key, out var x) ? x : agg[key] = new Toplam();
                if (r.Kaynak == "cb") t.Cb += r.Hit; else t.Ar += r.Hit;
                if (r.Protokol != null) t.Protokol.Add(r.Protokol);
                foreach (var p in Parcala(r.Surec)) t.Surec.Add(p);
                foreach (var p in Parcala(r.Bilgisayar)) t.Bilgisayar.Add(p);
                if (r.Ilk != null && (t.Ilk == null || string.CompareOrdinal(r.Ilk, t.Ilk) < 0)) t.Ilk = r.Ilk;
                if (r.Son != null && (t.Son == null || string.CompareOrdinal(r.Son, t.Son) > 0)) t.Son = r.Son;
            }
            foreach (var (k, u) in urls)
                if (!agg.Keys.Any(a => a.yon == k.yon && a.karsi == k.karsi))
                    agg[(k.yon, k.karsi, "")] = new Toplam { Ar = u.Values.Sum() };

            var list = agg.Select(kv => new Akis(kv.Key.yon, kv.Key.karsi, kv.Key.port, kv.Value.Cb + kv.Value.Ar, kv.Value.Cb, kv.Value.Ar,
                    kv.Value.Protokol.Order().ToList(), kv.Value.Surec.Order().ToList(), kv.Value.Bilgisayar.Order().ToList(),
                    urls.TryGetValue((kv.Key.yon, kv.Key.karsi), out var uu) ? uu.OrderByDescending(x => x.Value).Take(10).Select(x => x.Key).ToList() : [],
                    kv.Value.Ilk, kv.Value.Son))
                .OrderByDescending(a => a.Hit).ToList();
            return (list.Where(a => a.Yon == "in").ToList(), list.Where(a => a.Yon == "out").ToList());
        }

        sealed class Toplam
        {
            public long Cb, Ar;
            public string? Ilk, Son;
            public HashSet<string> Protokol = new(StringComparer.OrdinalIgnoreCase), Surec = new(StringComparer.OrdinalIgnoreCase),
                Bilgisayar = new(StringComparer.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        async Task<List<Satir>?> Guvenli(string kaynak, Func<Task<List<Satir>>> f, List<string> mesajlar, CancellationToken ct)
        {
            try { return await f(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                log.LogWarning(ex, "{Kaynak} trafik sorgusu başarısız", kaynak);
                lock (mesajlar) mesajlar.Add($"{kaynak}: {(ex is TaskCanceledException ? "zaman aşımı" : ex.Message)}");
                return null;
            }
        }

        // 4 oktet şartı + IPAddress doğrulaması: SPL / STEELFILTER'a girdi enjekte edilmesini de engeller.
        static bool IsIpv4(string? text, out string ip)
        {
            ip = "";
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Split('.').Length != 4 || !IPAddress.TryParse(text, out var a) || a.AddressFamily != AddressFamily.InterNetwork)
                return false;
            ip = a.ToString();
            return true;
        }

        static string? Temiz(string? v) { v = v?.Trim(); return string.IsNullOrEmpty(v) || v is "NULL" or "-" ? null : v; }
        static IEnumerable<string> Parcala(string? v) => v == null ? [] : v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        static string Kisalt(string t, int n) => t.Length <= n ? t : t[..n] + "…";
    }
}
