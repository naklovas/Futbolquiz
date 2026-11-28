using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DisErisimServisi;

// Splunk export API (/services/search/v2/jobs/export): DeltaFlow'daki SplunkService.ExportAsync'in sade kopyası.
// Ayarlar DeltaFlow ile aynı: Splunk:BaseUrl, Token ya da Username / Password, IgnoreSslErrors, ExportPath.
// Çok değerli alanlar "|||" ile birleştirilir.
sealed class Splunk : IDisposable
{
    readonly IConfiguration _cfg;
    readonly HttpClient _http;
    public Splunk(IConfiguration cfg)
    {
        _cfg = cfg;
        var h = new HttpClientHandler();
        if (cfg.GetValue("Splunk:IgnoreSslErrors", false)) h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        _http = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public void Dispose() => _http.Dispose();

    public async Task<(List<Dictionary<string, string>> Satirlar, List<string> Mesajlar)> AraAsync(string spl, DateTime bas, DateTime bit, CancellationToken ct0)
    {
        var s = _cfg.GetSection("Splunk");
        string baseUrl = (s["BaseUrl"] ?? throw new InvalidOperationException("Splunk:BaseUrl tanımlı değil.")).TrimEnd('/');
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct0);
        cts.CancelAfter(TimeSpan.FromMinutes(_cfg.GetValue("DisErisim:SorguDakika", 180.0)));
        var ct = cts.Token;
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + (s["ExportPath"] ?? "/services/search/v2/jobs/export"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["search"] = spl.TrimStart().StartsWith("search ") || spl.TrimStart().StartsWith("|") ? spl : "search " + spl,
                ["earliest_time"] = new DateTimeOffset(bas).ToUnixTimeSeconds().ToString(),
                ["latest_time"] = new DateTimeOffset(bit).ToUnixTimeSeconds().ToString(),
                ["output_mode"] = "json"
            })
        };
        if (s["Token"] is { Length: > 0 } token) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        else if (s["Username"] is { Length: > 0 } u)
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{u}:{s["Password"]}")));
        else throw new InvalidOperationException("Splunk:Token veya Splunk:Username/Password tanımlı değil.");

        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (OperationCanceledException) when (!ct0.IsCancellationRequested) { throw new TimeoutException("Splunk sorgusu DisErisim:SorguDakika süresinde bitmedi."); }
        using var _ = resp;
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Splunk HTTP {(int)resp.StatusCode}: {(body.Length > 400 ? body[..400] : body)}");
        }
        var satirlar = new List<Dictionary<string, string>>();
        var mesajlar = new List<string>();
        using var r = new StreamReader(await resp.Content.ReadAsStreamAsync(ct));
        while (true)
        {
            string? line;
            try { line = await r.ReadLineAsync(ct); }
            catch (OperationCanceledException) when (!ct0.IsCancellationRequested) { throw new TimeoutException($"Splunk sorgusu süre sınırında kesildi ({satirlar.Count} satır alınmıştı)."); }
            if (line == null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonNode? n;
            try { n = JsonNode.Parse(line); } catch (JsonException) { continue; }
            if (n?["preview"] is JsonValue pv && pv.TryGetValue<bool>(out bool on) && on) continue;
            if (n?["result"] is JsonObject o) satirlar.Add(o.ToDictionary(kv => kv.Key, kv => Metin(kv.Value)));
            if (n?["messages"] is JsonArray ms)
                foreach (var m in ms)
                    if (m?["text"]?.ToString() is { Length: > 0 } t) mesajlar.Add($"{m["type"]}: {t}");
        }
        return (satirlar, mesajlar);
    }

    static string Metin(JsonNode? n) => n switch
    {
        null => "",
        JsonArray a => string.Join("|||", a.Select(x => x?.ToString() ?? "")),
        _ => n.ToString()
    };
}
