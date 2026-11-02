// AppResponse TLS sürümü deneme aracı (DeltaFlow'a eklemeden önce test için).
// Kullanım:
//   dotnet run -- kaynaklar
//        Cihazdaki rapor kaynaklarını (source) ve adında / etiketinde SSL veya TLS geçen kolonları listeler.
//        Ham yanıtı kaynaklar.dump.json dosyasına yazar.
//   dotnet run -- tls <sunucu-ip> [dakika=60] [kaynak=flow_tcp] [kolon1,kolon2,...]
//        Sunucuya gelen bağlantılarda TLS kolonlarını raporlar: port × TLS değeri × bağlantı sayısı.
//        Kolon verilmezse "kaynaklar" ile bulunan SSL/TLS kolonları tek tek denenir; geçersiz olan atlanır.
//   dotnet run -- ham <sunucu-ip> <dakika> <kaynak> <kolon1,kolon2,...>
//        Verilen kolonlarla ham satırları yazar (ilk 50).
// Ayarlar: DeltaFlow'un appsettings.json'u (aynı yapı) bu klasöre kopyalanır. Şifre repoya girmez.
//   Birden fazla AppResponse cihazı varsa: dotnet run -- --cihaz 1 kaynaklar   (ya da --cihaz Riverbed-2)
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Ayarlar: DeltaFlow'un appsettings.json'u ile AYNI yapı (dosya olduğu gibi kopyalanabilir). Kullanılan anahtarlar:
//   Credentials:Username / Password, Servers[{Name, Ip}], VifgIds, SourcePathType, AppResponseIgnoreSslErrors
// appsettings.Production.json varsa üzerine yazılır (DeltaFlow gibi). Birden fazla cihaz: --cihaz <sıra (0'dan) | ad>, yoksa ilki.
var jsonSec = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
JsonObject? Oku(string ad) =>
    new[] { Path.Combine(AppContext.BaseDirectory, ad), ad }.FirstOrDefault(File.Exists) is string y ? JsonNode.Parse(File.ReadAllText(y), null, jsonSec) as JsonObject : null;
static void Birlestir(JsonObject hedef, JsonObject ek)
{
    foreach (var kv in ek.ToList())
        if (kv.Value is JsonObject eo && hedef[kv.Key] is JsonObject ho) Birlestir(ho, eo);
        else hedef[kv.Key] = kv.Value?.DeepClone();
}
var ayar = Oku("appsettings.json") ?? throw new FileNotFoundException("appsettings.json bulunamadı (DeltaFlow'unkini bu klasöre kopyalayın).");
if (Oku("appsettings.Production.json") is JsonObject prod) Birlestir(ayar, prod);

// Visual Studio'dan argümansız başlatılırsa komut sorulur
if (args.Length == 0)
{
    Console.WriteLine("Komut girin (ör: kaynaklar  |  tls 10.1.2.3 60  |  --cihaz 1 kaynaklar  |  ham 10.1.2.3 30 flow_tcp cli_tcp.ip,srv_tcp.ip,srv_tcp.port):");
    Console.Write("> ");
    args = (Console.ReadLine() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
var argl = args.ToList();
string? cihazSec = null;
if (argl.IndexOf("--cihaz") is int ci and >= 0 && ci + 1 < argl.Count) { cihazSec = argl[ci + 1]; argl.RemoveRange(ci, 2); }
args = argl.ToArray();

var sunucular = (ayar["Servers"] as JsonArray ?? []).OfType<JsonObject>().ToList();
var secili = cihazSec == null ? sunucular.FirstOrDefault()
    : int.TryParse(cihazSec, out int si) ? sunucular.ElementAtOrDefault(si)
    : sunucular.FirstOrDefault(x => string.Equals(x["Name"]?.ToString(), cihazSec, StringComparison.OrdinalIgnoreCase));
string cihaz = secili?["Ip"]?.ToString()?.Trim() ?? "", cihazAd = secili?["Name"]?.ToString() ?? "AppResponse";
string kul = ayar["Credentials"]?["Username"]?.ToString() ?? "", sifre = ayar["Credentials"]?["Password"]?.ToString() ?? "";
if (cihaz == "") { Console.WriteLine($"Servers listesinde {(cihazSec == null ? "Ip'si dolu cihaz yok" : $"'{cihazSec}' yok")}. Cihazlar: {string.Join(", ", sunucular.Select((x, i) => $"{i}={x["Name"]}"))}"); return 1; }
if (kul == "" || sifre == "") { Console.WriteLine("Credentials:Username / Password boş (appsettings.json ya da appsettings.Production.json)."); return 1; }
var vifgler = (ayar["VifgIds"] as JsonArray ?? []).Select(x => x?.ToString()?.Trim() ?? "").Where(x => x != "").ToList();
string yolTuru = ayar["SourcePathType"]?.ToString() ?? "jobs";

var handler = new HttpClientHandler();
if (ayar["AppResponseIgnoreSslErrors"]?.ToString()?.ToLowerInvariant() != "false") handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
string baseUrl = $"https://{cihaz}";

if (args.Length == 0) { Console.WriteLine("Komut: kaynaklar | tls <ip> [dakika] [kaynak] [kolonlar] | ham <ip> <dakika> <kaynak> <kolonlar>"); return 1; }

string jwt = await TokenAsync();
Console.WriteLine($"Token alındı ({cihazAd} / {cihaz}).");

switch (args[0].ToLowerInvariant())
{
    case "kaynaklar":
        {
            var (_, kolonlar) = await KaynaklarAsync(true);
            Console.WriteLine($"\nSSL / TLS kolonları ({kolonlar.Count}):");
            foreach (var k in kolonlar) Console.WriteLine($"  {k.Kaynak,-22} {k.Id,-45} {k.Etiket}");
            if (kolonlar.Count == 0) Console.WriteLine("  bulunamadı — kaynaklar.dump.json'da 'ssl' / 'tls' aratın.");
            break;
        }
    case "tls":
        {
            if (args.Length < 2) { Console.WriteLine("tls <sunucu-ip> [dakika] [kaynak] [kolonlar]"); return 1; }
            string ip = args[1]; int dk = args.Length > 2 ? int.Parse(args[2]) : 60; string kaynak = args.Length > 3 ? args[3] : "flow_tcp";
            List<string> aday;
            if (args.Length > 4) aday = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            else
            {
                var (_, kolonlar) = await KaynaklarAsync(false);
                aday = kolonlar.Where(k => k.Kaynak == kaynak).Select(k => k.Id).Distinct().ToList();
                if (aday.Count == 0)
                {
                    // Kaynak bilgisi alınamadıysa tahmini adlar denenir
                    aday = ["ssl.version", "ssl.protocol", "ssl.protocol_version", "tls.version", "cli_ssl.version", "srv_ssl.version",
                            "ssl.cipher", "ssl.cipher_suite", "srv_tcp.ssl_version", "cli_tcp.ssl_version"];
                    Console.WriteLine($"'{kaynak}' için SSL/TLS kolonu bulunamadı; tahmini adlar denenecek: {string.Join(", ", aday)}");
                }
                else Console.WriteLine($"'{kaynak}' SSL/TLS kolonları: {string.Join(", ", aday)}");
            }
            var anahtar = new[] { "cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port" };
            string filtre = $"srv_tcp.ip == {ip}";
            // önce her kolonu tek tek dene (geçersiz kolon tüm raporu bozar)
            var gecerli = new List<string>();
            foreach (var k in aday)
            {
                try { await RaporAsync(kaynak, [.. anahtar, k], filtre, 5); gecerli.Add(k); Console.WriteLine($"  ✓ {k}"); }
                catch (Exception ex) { Console.WriteLine($"  ✗ {k}: {Kisalt(ex.Message, 160)}"); }
            }
            if (gecerli.Count == 0) { Console.WriteLine("Geçerli TLS kolonu yok. 'kaynaklar' çıktısındaki diğer kaynakları deneyin (4. parametre)."); return 2; }
            var satirlar = await RaporAsync(kaynak, [.. anahtar, .. gecerli], filtre, dk);
            Console.WriteLine($"\n{ip} · son {dk} dk · {satirlar.Count} satır (kaynak {kaynak})");
            // port × TLS değerleri → bağlantı sayısı, istemci sayısı
            var ozet = satirlar.Where(r => r.Count >= 3 + gecerli.Count)
                .GroupBy(r => (Port: Deger(r[2]), Tls: string.Join(" | ", gecerli.Select((k, i) => $"{k}={Deger(r[3 + i])}"))))
                .Select(g => (g.Key.Port, g.Key.Tls, Adet: g.Count(), Istemci: g.Select(r => Deger(r[0])).Distinct().Count()))
                .OrderBy(x => int.TryParse(x.Port, out int p) ? p : 99999).ThenByDescending(x => x.Adet);
            Console.WriteLine($"{"Port",-7} {"Satır",7} {"İstemci",8}  TLS");
            foreach (var o in ozet) Console.WriteLine($"{o.Port,-7} {o.Adet,7} {o.Istemci,8}  {o.Tls}");
            break;
        }
    case "ham":
        {
            if (args.Length < 5) { Console.WriteLine("ham <ip> <dakika> <kaynak> <kolon1,kolon2>"); return 1; }
            var kol = args[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
            var satirlar = await RaporAsync(args[3], kol, $"cli_tcp.ip == {args[1]} or srv_tcp.ip == {args[1]}", int.Parse(args[2]));
            Console.WriteLine(string.Join(" ; ", kol));
            foreach (var r in satirlar.Take(50)) Console.WriteLine(string.Join(" ; ", r.Select(x => Deger(x))));
            Console.WriteLine($"({satirlar.Count} satır)");
            break;
        }
    default: Console.WriteLine("Bilinmeyen komut."); return 1;
}
return 0;

// ---------------------------------------------------------------------------
async Task<string> TokenAsync()
{
    var body = JsonSerializer.Serialize(new { user_credentials = new { username = kul, password = sifre }, generate_refresh_token = false });
    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token") { Content = new StringContent(body, Encoding.UTF8) };
    req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");   // charset'siz (bazı sürümler 415)
    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    using var r = await http.SendAsync(req);
    string s = await r.Content.ReadAsStringAsync();
    if (!r.IsSuccessStatusCode) throw new Exception($"Token alınamadı HTTP {(int)r.StatusCode}: {Kisalt(s, 300)}");
    return JsonNode.Parse(s)?["access_token"]?.ToString() ?? throw new Exception("access_token yok");
}

HttpRequestMessage Req(HttpMethod m, string url, object? json = null)
{
    var r = new HttpRequestMessage(m, url);
    r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
    r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    if (json != null) r.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
    return r;
}

// Kaynak listesi: sürüme göre adres farklı olabilir, sırayla denenir. Kolonlar kaynak öğesinin içinde değilse ayrıca istenir.
async Task<(List<string> Kaynaklar, List<(string Kaynak, string Id, string Etiket)> Kolonlar)> KaynaklarAsync(bool yaz)
{
    var dump = new JsonObject();
    JsonNode? liste = null;
    foreach (var u in new[] { "/api/npm.reports.sources/1.0/sources", "/api/npm.reports.sources/1.0/sources/items", "/api/npm.reports/1.0/sources" })
    {
        using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
        string s = await r.Content.ReadAsStringAsync();
        if (yaz) Console.WriteLine($"GET {u} → HTTP {(int)r.StatusCode}");
        if (!r.IsSuccessStatusCode) continue;
        try { liste = JsonNode.Parse(s); dump[u] = liste?.DeepClone(); break; } catch { }
    }
    var adlar = new List<string>();
    if (liste != null)
        foreach (var it in (liste["items"] as JsonArray) ?? (liste as JsonArray) ?? [])
            if (it?["name"]?.ToString() is string n) adlar.Add(n);
    if (yaz) Console.WriteLine($"Kaynaklar ({adlar.Count}): {string.Join(", ", adlar)}");

    // ayrıntı: flow_tcp, wtapages ve adında ssl / tls geçenler
    foreach (var n in adlar.Where(n => n is "flow_tcp" or "wtapages" || Regex.IsMatch(n, "ssl|tls", RegexOptions.IgnoreCase)))
    {
        foreach (var u in new[] { $"/api/npm.reports.sources/1.0/sources/items/{n}", $"/api/npm.reports.sources/1.0/sources/items/{n}/columns" })
        {
            using var r = await http.SendAsync(Req(HttpMethod.Get, baseUrl + u));
            if (!r.IsSuccessStatusCode) continue;
            try { dump[u] = JsonNode.Parse(await r.Content.ReadAsStringAsync()); } catch { }
        }
    }
    if (yaz) { File.WriteAllText("kaynaklar.dump.json", dump.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Ham yanıt: kaynaklar.dump.json"); }

    // JSON'un her yerinde id / name'inde ya da label'ında ssl / tls geçen kolon nesneleri
    var kolonlar = new List<(string, string, string)>();
    void Gez(JsonNode? n, string kaynak)
    {
        switch (n)
        {
            case JsonObject o:
                string k2 = o["name"]?.GetValueKind() == JsonValueKind.String && o["columns"] != null ? o["name"]!.ToString() : kaynak;
                string? id = o["id"]?.GetValueKind() == JsonValueKind.String ? o["id"]!.ToString() : null;
                string lbl = o["label"]?.ToString() ?? o["description"]?.ToString() ?? "";
                if (id != null && Regex.IsMatch(id + " " + lbl, @"ssl|tls", RegexOptions.IgnoreCase)) kolonlar.Add((k2, id, lbl));
                foreach (var kv in o)
                {
                    // kolonlar sözlük olarak gelirse ({"ssl.version": {...}}) anahtar kolon adıdır
                    if (kv.Value is JsonObject && o.Count > 3 && kv.Key.Contains('.') && Regex.IsMatch(kv.Key, "ssl|tls", RegexOptions.IgnoreCase))
                        kolonlar.Add((k2, kv.Key, kv.Value["label"]?.ToString() ?? ""));
                    Gez(kv.Value, k2);
                }
                break;
            case JsonArray a: foreach (var x in a) Gez(x, kaynak); break;
        }
    }
    foreach (var kv in dump)
    {
        var m = Regex.Match(kv.Key, @"sources/items/([^/]+)");
        Gez(kv.Value, m.Success ? m.Groups[1].Value : "?");
    }
    return (adlar, kolonlar.Distinct().ToList());
}

// Tek data_def'li rapor: oluştur → bekle → veri → sil
async Task<List<List<JsonNode?>>> RaporAsync(string kaynak, string[] kolonlar, string filtre, int dakika)
{
    long bitis = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), bas = bitis - dakika * 60L;
    var sonuc = new List<List<JsonNode?>>();
    foreach (var vifg in vifgler.Count > 0 ? vifgler : [""])
    {
        object src = vifg == "" ? new { name = kaynak } : new { name = kaynak, path = $"{yolTuru}/{vifg}" };
        var payload = new
        {
            info = new { name = "DeltaFlow TLS deneme", description = "" },
            data_defs = new[] { new { source = src, columns = kolonlar, time = new { start = bas.ToString(), end = bitis.ToString() },
                filters = new object[] { new { id = "f", type = "STEELFILTER", value = filtre } }, topn = 10000 } }
        };
        using var cr = await http.SendAsync(Req(HttpMethod.Post, $"{baseUrl}/api/npm.reports/1.0/instances", payload));
        string cb = await cr.Content.ReadAsStringAsync();
        if (!cr.IsSuccessStatusCode) throw new Exception($"Rapor oluşturulamadı HTTP {(int)cr.StatusCode}: {cb}");
        string id = JsonNode.Parse(cb)?["id"]?.ToString() ?? throw new Exception("Rapor ID yok");
        try
        {
            bool bitti = false;
            for (int i = 0; i < 45 && !bitti; i++)
            {
                await Task.Delay(2000);
                using var st = await http.SendAsync(Req(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}"));
                var def = JsonNode.Parse(await st.Content.ReadAsStringAsync())?["data_defs"]?.AsArray()?[0];
                string? durum = def?["status"]?["state"]?.ToString();
                if (durum == "error") throw new Exception("Rapor hata: " + def?["status"]?.ToJsonString());
                bitti = durum == "completed";
            }
            if (!bitti) throw new Exception("Rapor 90 sn'de tamamlanmadı");
            using var dr = await http.SendAsync(Req(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}/data_defs/items/1/data?limit=100000"));
            foreach (var row in JsonNode.Parse(await dr.Content.ReadAsStringAsync())?["data"]?.AsArray() ?? [])
                if (row is JsonArray a) sonuc.Add(a.Select(x => x?.DeepClone()).ToList());
        }
        finally
        {
            try { using var _ = await http.SendAsync(Req(HttpMethod.Delete, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}")); } catch { }
        }
    }
    return sonuc;
}

static string Deger(JsonNode? n) => n switch { null => "", JsonValue v => v.ToString(), _ => n.ToJsonString() } is { Length: > 0 } s ? s : "(boş)";
static string Kisalt(string s, int n) => s.Length <= n ? s : s[..n] + "…";
