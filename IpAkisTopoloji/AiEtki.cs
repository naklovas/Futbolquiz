using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Şirket içi AI servisi (OpenAI uyumlu /chat/completions) ile etki analizi.
// Arayüz sorgu sonucunun özetini gönderir; burada prompt kurulur ve servise iletilir.
// ---------------------------------------------------------------------------
// Bir bağlantı (karşı IP + port). Apps: karşı sunucudaki uygulama(lar);
// LocalApps: gelen trafikte sorgulanan sunucuda o portu karşılayan uygulama;
// Processes: giden trafikte bağlantıyı açan süreç(ler) (Splunk).
record AiEdge(string Ip, string? Segment, List<string>? Apps, List<string>? Owners, string? Port,
    long Hits, List<string>? LocalApps, List<string>? Processes);

record AiRequest(string? Question, string? Ip, string? Segment, List<string>? Apps, string? Period,
    List<AiEdge>? Inbound, List<AiEdge>? Outbound, int? InboundTotal, int? OutboundTotal);

record AiResponse(string Answer, string Model, long ElapsedMs, string Prompt);

static class AiEtkiService
{
    const int MaxEdges = 80, MaxIpsPerLine = 8, MaxText = 120, MaxList = 6, MaxQuestion = 500;
    const string NoApp = "envanterde uygulama kaydı yok";

    public const string DefaultQuestion =
        "Bu sunucuda bir değişiklik (bakım, yeniden başlatma, sürüm/konfigürasyon değişikliği, kapatma) yapılırsa nereler etkilenir?";

    public static async Task<AiResponse> AskAsync(AiRequest req, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var s = cfg.GetSection("Ai");
        string url = s["Url"] is { Length: > 0 } u ? u : throw new InvalidOperationException("Ai:Url tanımlı değil.");
        string apiKey = s["ApiKey"] ?? "";
        string model = s["Model"] is { Length: > 0 } m ? m : "zt-ga-small-0";

        string prompt = BuildPrompt(req);
        var payload = new
        {
            model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = s.GetValue("Temperature", 0.1)
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var sw = Stopwatch.StartNew();
        using var resp = await http.SendAsync(httpReq, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"AI servisi HTTP {(int)resp.StatusCode}: {Trunc(body, 400)}");

        string answer;
        try
        {
            using var doc = JsonDocument.Parse(body);
            answer = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException($"AI yanıtı beklenen formatta değil: {Trunc(body, 300)}");
        }

        // Bazı modeller düşünme bloğu döndürür; kullanıcıya göstermeye gerek yok.
        answer = System.Text.RegularExpressions.Regex.Replace(answer, "<think>.*?</think>", "",
            System.Text.RegularExpressions.RegexOptions.Singleline).Trim();

        return new AiResponse(answer, model, sw.ElapsedMilliseconds, prompt);
    }

    // Etki listesi kodda hesaplanır (servis → kullanan uygulamalar); AI yalnızca bu tabloyu yorumlar.
    public static string BuildPrompt(AiRequest r)
    {
        string question = Trunc(string.IsNullOrWhiteSpace(r.Question) ? DefaultQuestion : r.Question.Trim(), MaxQuestion);
        var inbound = (r.Inbound ?? []).OrderByDescending(e => e.Hits).Take(MaxEdges).ToList();
        var outbound = (r.Outbound ?? []).OrderByDescending(e => e.Hits).Take(MaxEdges).ToList();
        var sb = new StringBuilder();

        sb.AppendLine("Sen kurumsal bir bankanın BT altyapı ve uygulama bağımlılık analistisin.");
        sb.AppendLine("Aşağıdaki tablolar bir sunucunun gerçek ağ trafiğinden (Splunk/Carbon Black, Riverbed AppResponse) ve");
        sb.AppendLine("kurum envanterinden çıkarıldı. KURALLAR:");
        sb.AppendLine("- Sadece tablolardaki uygulama, IP, port ve ekipleri kullan; tabloda olmayan hiçbir şey ekleme.");
        sb.AppendLine("- \"Bazı sistemler etkilenebilir\" gibi genel ifadeler kullanma; her maddede uygulama adı, IP ve port olsun.");
        sb.AppendLine("- Uygulaması envanterde olmayan sunucuları ayrıca IP ve segmentiyle listele.");
        sb.AppendLine();
        sb.AppendLine($"SORU: {question}");
        sb.AppendLine();
        sb.AppendLine($"SORGULANAN SUNUCU: {Clean(r.Ip)} | segment: {Clean(r.Segment) ?? "envanterde yok"} | uygulamalar: {JoinOr(r.Apps, NoApp)}");
        if (Clean(r.Period) is { } period) sb.AppendLine($"İncelenen zaman aralığı: {period}");
        sb.AppendLine();

        // 1) Sorgulanan sunucudaki her servis (uygulama + port) ve onu kullanan uygulamalar
        int inTotal = Math.Max(r.InboundTotal ?? 0, inbound.Select(e => e.Ip).Distinct().Count());
        sb.AppendLine($"ETKİ TABLOSU — SORGULANAN SUNUCUDAKİ SERVİSLER VE ONLARI KULLANANLAR (toplam {inTotal} kaynak sunucu):");
        if (inbound.Count == 0) sb.AppendLine("- gelen bağlantı yok");
        foreach (var svc in inbound.GroupBy(e => (Port: Clean(e.Port) ?? "?", App: JoinOr(e.LocalApps, NoApp)))
                     .OrderByDescending(g => g.Sum(e => e.Hits)))
        {
            sb.AppendLine($"* Servis: {svc.Key.App} — port {svc.Key.Port} ({svc.Select(e => e.Ip).Distinct().Count()} sunucu, {svc.Sum(e => e.Hits)} bağlantı)");
            foreach (var user in svc.GroupBy(e => JoinOr(e.Apps, "")).OrderBy(g => g.Key == "").ThenByDescending(g => g.Sum(e => e.Hits)))
            {
                string servers = IpList(user, withPort: false);
                string owners = JoinOr(user.SelectMany(e => e.Owners ?? []).ToList(), "");
                sb.AppendLine(user.Key == ""
                    ? $"  - Uygulaması envanterde olmayan kaynak sunucular: {servers} | {user.Sum(e => e.Hits)} bağlantı"
                    : $"  - Kullanan uygulama: {user.Key} | sunucular: {servers}{(owners != "" ? $" | sahip/muhafız: {owners}" : "")} | {user.Sum(e => e.Hits)} bağlantı");
            }
        }
        sb.AppendLine();

        // 2) Sorgulanan sunucunun kullandığı servisler
        int outTotal = Math.Max(r.OutboundTotal ?? 0, outbound.Select(e => e.Ip).Distinct().Count());
        sb.AppendLine($"BAĞIMLILIK TABLOSU — SORGULANAN SUNUCUNUN KULLANDIĞI SERVİSLER (toplam {outTotal} hedef sunucu):");
        if (outbound.Count == 0) sb.AppendLine("- giden bağlantı yok");
        foreach (var dep in outbound.GroupBy(e => JoinOr(e.Apps, "")).OrderBy(g => g.Key == "").ThenByDescending(g => g.Sum(e => e.Hits)))
        {
            string targets = IpList(dep, withPort: true);
            string procs = JoinOr(dep.SelectMany(e => e.Processes ?? []).ToList(), "");
            string owners = JoinOr(dep.SelectMany(e => e.Owners ?? []).ToList(), "");
            string extra = (procs != "" ? $" | bağlantıyı açan süreç: {procs}" : "") + (owners != "" ? $" | sahip/muhafız: {owners}" : "");
            sb.AppendLine(dep.Key == ""
                ? $"- Uygulaması envanterde olmayan hedefler: {targets}{extra} | {dep.Sum(e => e.Hits)} bağlantı"
                : $"- Hedef uygulama: {dep.Key} | hedefler: {targets}{extra} | {dep.Sum(e => e.Hits)} bağlantı");
        }
        sb.AppendLine();

        sb.AppendLine("YANITI TÜRKÇE VE AŞAĞIDAKİ BAŞLIKLARLA VER:");
        sb.AppendLine("## Özet");
        sb.AppendLine("(1-2 cümle: sunucunun rolü; kaç uygulama ve sunucu etkilenir)");
        sb.AppendLine("## Etkilenecek uygulamalar");
        sb.AppendLine("(ETKİ TABLOSU'ndaki her servis için tek madde: \"<servis> (port) kesilirse: <kullanan uygulamalar> (<IP'ler>)\"; en yoğundan başla)");
        sb.AppendLine("## Kontrol edilecek bağımlılıklar");
        sb.AppendLine("(BAĞIMLILIK TABLOSU'ndaki hedefler: değişiklik sonrası erişimi test edilecek uygulama, IP ve port)");
        sb.AppendLine("## Bilgilendirilecek ekipler");
        sb.AppendLine("(sadece tablolardaki sahip/muhafız bilgilerinden)");
        sb.AppendLine("## Öneriler");
        sb.AppendLine("(en fazla 4 madde, somut: bakım penceresi, test edilecek bağlantılar, geri dönüş)");
        return sb.ToString();
    }

    static string IpList(IEnumerable<AiEdge> edges, bool withPort)
    {
        var items = edges
            .GroupBy(e => withPort ? $"{e.Ip}:{Clean(e.Port) ?? "?"}" : e.Ip)
            .OrderByDescending(g => g.Sum(e => e.Hits))
            .Select(g => $"{Clean(g.Key)} ({Clean(g.First().Segment) ?? "segment yok"})")
            .ToList();
        return string.Join(", ", items.Take(MaxIpsPerLine)) + (items.Count > MaxIpsPerLine ? $" +{items.Count - MaxIpsPerLine} sunucu daha" : "");
    }

    static string? Clean(string? v)
    {
        v = v?.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return string.IsNullOrEmpty(v) ? null : Trunc(v, MaxText);
    }

    static string JoinOr(List<string>? list, string empty)
    {
        var items = (list ?? []).Select(Clean).OfType<string>().Distinct().Take(MaxList).ToList();
        return items.Count == 0 ? empty : string.Join(", ", items);
    }

    static string Trunc(string text, int max) => text.Length <= max ? text : text[..max] + "…";
}
