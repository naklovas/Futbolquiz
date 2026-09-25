using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

// ---------------------------------------------------------------------------
// Şirket içi AI servisi (OpenAI uyumlu /chat/completions) ile etki analizi.
// Arayüz sorgu sonucunun özetini gönderir; burada prompt kurulur ve servise iletilir.
// ---------------------------------------------------------------------------
// Apps: karşı sunucudaki uygulama(lar); LocalApps: gelen trafikte sorgulanan sunucuda karşılayan uygulama;
// Processes: giden trafikte bağlantıyı açan süreç(ler) (Splunk); Hop2: karşı sunucunun 2. seviye segmentleri ve uygulamaları.
record AiPeer(string Ip, string? Segment, List<string>? Apps, List<string>? Owners, List<string>? Ports,
    long Hits, List<string>? LocalApps, List<string>? Processes, List<AiHop>? Hop2);

record AiHop(string? Segment, List<string>? Apps, int IpCount);

record AiRequest(string? Question, string? Ip, string? Segment, List<string>? Apps, string? Period,
    List<AiPeer>? Inbound, List<AiPeer>? Outbound, int? InboundTotal, int? OutboundTotal);

record AiResponse(string Answer, string Model, long ElapsedMs, string Prompt);

static class AiEtkiService
{
    const int MaxPeers = 30, MaxText = 120, MaxList = 6, MaxQuestion = 500;

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

    public static string BuildPrompt(AiRequest r)
    {
        string question = Trunc(string.IsNullOrWhiteSpace(r.Question) ? DefaultQuestion : r.Question.Trim(), MaxQuestion);
        var sb = new StringBuilder();

        sb.AppendLine("Sen kurumsal bir bankanın BT altyapı ve uygulama bağımlılık analistisin.");
        sb.AppendLine("Aşağıda bir sunucunun gerçek ağ trafiği özeti var (kaynak: Splunk/Carbon Black ve Riverbed AppResponse,");
        sb.AppendLine("segment ve uygulama bilgisi kurum envanterinden). Sadece bu verilere dayan. Veride olmayan bir");
        sb.AppendLine("çıkarım yapıyorsan başına \"(tahmin)\" yaz. IP, segment ve uygulama adlarını aynen kullan.");
        sb.AppendLine();
        sb.AppendLine($"SORU: {question}");
        sb.AppendLine();
        sb.AppendLine($"SORGULANAN SUNUCU: {Clean(r.Ip)}");
        sb.AppendLine($"- Segment: {Clean(r.Segment) ?? "envanterde yok"}");
        sb.AppendLine($"- Üzerindeki uygulamalar: {JoinOr(r.Apps, "envanterde yok")}");
        if (Clean(r.Period) is { } period) sb.AppendLine($"- İncelenen zaman aralığı: {period}");
        sb.AppendLine();

        string targetApps = JoinOr(r.Apps, "envanterde yok");
        AppendPeers(sb, "GELEN BAĞLANTILAR (bu sunucuyu kullanan sunucular; değişiklikten DOĞRUDAN etkilenirler)",
            r.Inbound, r.InboundTotal, inbound: true, targetApps);
        AppendPeers(sb, "GİDEN BAĞLANTILAR (bu sunucunun bağımlı olduğu servisler; değişiklik sonrası bu bağlantılar kontrol edilmeli)",
            r.Outbound, r.OutboundTotal, inbound: false, targetApps);

        sb.AppendLine("YANITI TÜRKÇE, KISA VE MADDE MADDE, AŞAĞIDAKİ BAŞLIKLARLA VER:");
        sb.AppendLine("## Özet");
        sb.AppendLine("(2-3 cümle: sunucunun rolü ve değişikliğin genel etki büyüklüğü)");
        sb.AppendLine("## Doğrudan etkilenecekler");
        sb.AppendLine("(gelen bağlantılardaki uygulamalar/segmentler, trafiği en yüksekten başlayarak)");
        sb.AppendLine("## Dolaylı etkilenebilecekler");
        sb.AppendLine("(gelen sunuculara bağlanan segmentler üzerinden zincirleme etkiler)");
        sb.AppendLine("## Kontrol edilmesi gereken bağımlılıklar");
        sb.AppendLine("(giden bağlantılar: değişiklik sonrası erişimi test edilmesi gereken servisler)");
        sb.AppendLine("## Bilgilendirilmesi gereken ekipler");
        sb.AppendLine("(varlık sahibi / muhafızı bilgilerinden)");
        sb.AppendLine("## Değişiklik öncesi öneriler");
        sb.AppendLine("(bakım penceresi, test adımları, geri dönüş planı)");
        return sb.ToString();
    }

    // Her bağlantı iki ucundaki uygulamalarla, kaynak → hedef yönünde yazılır.
    static void AppendPeers(StringBuilder sb, string title, List<AiPeer>? peers, int? total, bool inbound, string targetApps)
    {
        peers ??= [];
        int shown = Math.Min(peers.Count, MaxPeers);
        int all = Math.Max(total ?? peers.Count, peers.Count);
        sb.AppendLine($"{title}: toplam {all} sunucu{(all > shown ? $", en yoğun {shown} tanesi" : "")}");
        if (shown == 0) sb.AppendLine("- kayıt yok");

        foreach (var p in peers.Take(MaxPeers))
        {
            string ip = Clean(p.Ip) ?? "?";
            string ports = JoinOr(p.Ports, "?");
            string peerApps = JoinOr(p.Apps, "envanterde yok");

            sb.AppendLine($"- {ip} | segment: {Clean(p.Segment) ?? "bilinmiyor"} | trafik: {p.Hits} bağlantı");
            if (inbound)
            {
                string local = p.LocalApps is { Count: > 0 } ? JoinOr(p.LocalApps, "") : targetApps;
                sb.AppendLine($"  bağlantı: {ip} (uygulama: {peerApps}) → SORGULANAN SUNUCU port {ports} (karşılayan uygulama: {local})");
            }
            else
            {
                string proc = p.Processes is { Count: > 0 } ? $"süreç: {JoinOr(p.Processes, "")}; " : "";
                sb.AppendLine($"  bağlantı: SORGULANAN SUNUCU ({proc}sunucudaki uygulamalar: {targetApps}) → {ip} port {ports} (hedef uygulama: {peerApps})");
            }
            if (p.Owners is { Count: > 0 }) sb.AppendLine($"  {ip} sahip/muhafız: {JoinOr(p.Owners, "")}");
            if (p.Hop2 is { Count: > 0 })
            {
                var hops = p.Hop2.Take(MaxList).Select(h =>
                    $"{Clean(h.Segment) ?? "segment envanterde yok"} ({h.IpCount} IP; uygulama: {JoinOr(h.Apps, "bilinmiyor")})");
                sb.AppendLine(inbound
                    ? $"  {ip} sunucusuna gelen segmentler (dolaylı etki): {string.Join("; ", hops)}"
                    : $"  {ip} sunucusunun gittiği segmentler: {string.Join("; ", hops)}");
            }
        }
        sb.AppendLine();
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
