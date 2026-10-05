using System.Globalization;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// Oturum akışı (aynı oturumda gelen → giden): ağ verisinde istek kimliği olmadığı için eşleştirme zamanla yapılır.
// AppResponse (ms) ve Firewall (Palo Alto, sn; başlangıç = log zamanı - duration) her TCP bağlantısını
// başlangıç / bitiş zamanıyla verir. Sorgulanan sunucuya gelen kısa bir
// bağlantının (bir istek) açık olduğu süre içinde sunucunun açtığı giden bağlantılar o isteğe aday sayılır.
//  - Aynı anda birden çok gelen oturum açıksa giden bağlantı aralarında paylaştırılır (ağırlık 1/k); k = 1 ise "kesin".
//  - Uzun süren gelen bağlantılar (keep-alive, Oturum:MaxOturumSaniye) içine her şey düşeceği için dışarıda bırakılır.
//  - Hiçbir oturumun içine düşmeyen giden bağlantılar (havuz, zamanlanmış işler) ayrıca sayılır.
// ---------------------------------------------------------------------------
// Kaba = saniye çözünürlüklü (Firewall) kayıt.
record FlowRec(double S, double? E, string Cip, string Cport, string Sip, string Sport, bool Kaba = false);

record OturumHedef(ProcPeer Hedef, double Agirlik, int Kesin, double Oran);
record OturumGiris(ProcPeer Kaynak, int Oturum, double SureMsMedyan, List<OturumHedef> Hedefler);
record OturumSonuc(string Ip, string Kutu, string Pencere, string Cozunurluk, int GelenOturum, int UzunOturum,
    int GidenBaglanti, int Eslesmeyen, double OrtAyniAnda, List<OturumGiris> Girisler, List<string> Mesajlar, long SureMs);

static class OturumAkisi
{
    static readonly string[] DefaultCols = ["start_time", "end_time", "cli_tcp.ip", "cli_tcp.port", "srv_tcp.ip", "srv_tcp.port"];
    const int MaxGiris = 20, MaxHedef = 15;

    // Kaynaklar: AppResponse (milisaniye, aynı segment içini de görür) + Firewall (saniye, yalnızca firewall'dan geçen).
    // İkisi aynı bağlantıyı görürse (istemci IP:port → sunucu IP:port aynı) AppResponse kaydı kullanılır.
    public static async Task<OturumSonuc> RunAsync(LookupQuery q, int appliance, bool useAr, bool useFw, IConfiguration cfg,
        HttpClient arHttp, HttpClient splunkHttp, EnvanterSnapshot? env, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var msgs = new List<string>();
        var parts = new List<string>();

        var arTask = useAr ? FetchArAsync(q, appliance, cfg, arHttp, msgs, ct) : Task.FromResult<(string, List<FlowRec>)?>(null);
        var fwTask = useFw && FirewallService.Enabled(cfg) ? FetchFwAsync(q, cfg, splunkHttp, msgs, ct) : Task.FromResult<List<FlowRec>?>(null);
        await Task.WhenAll(arTask, fwTask);
        var ar = arTask.Result;
        var fw = fwTask.Result;
        if (ar == null && fw == null)
            throw new InvalidOperationException("Oturum verisi alınamadı: " + (msgs.Count > 0 ? string.Join(" | ", msgs) : "AppResponse veya Firewall seçili değil."));

        var flows = new List<FlowRec>();
        var seen = new HashSet<(string, string, string, string)>();
        if (ar != null)
        {
            foreach (var f in ar.Value.Item2) { flows.Add(f); seen.Add((f.Cip, f.Cport, f.Sip, f.Sport)); }
            parts.Add($"AppResponse {ar.Value.Item1}: {ar.Value.Item2.Count:N0}");
        }
        if (fw != null)
        {
            int added = 0;
            foreach (var f in fw.Where(f => f.Cport != "" && seen.Add((f.Cip, f.Cport, f.Sip, f.Sport)))) { flows.Add(f); added++; }
            parts.Add($"Firewall: {fw.Count:N0} ({added:N0} yeni)");
        }

        var res = Build(q.Ip, flows, env, cfg.GetValue("Oturum:MaxOturumSaniye", 30.0));
        return res with
        {
            Kutu = string.Join(" + ", parts),
            Pencere = $"{q.Start:HH:mm}-{q.End:HH:mm}",
            Mesajlar = [.. msgs, .. res.Mesajlar],
            SureMs = sw.ElapsedMilliseconds
        };
    }

    // Tüm kutular seçiliyse hepsi sorgulanır, en çok bağlantı gören kutu kullanılır
    // (aynı segmenti iki kutu görüyorsa akışlar iki kez sayılmasın).
    static async Task<(string, List<FlowRec>)?> FetchArAsync(LookupQuery q, int appliance, IConfiguration cfg, HttpClient http,
        List<string> msgs, CancellationToken ct)
    {
        int boxCount = cfg.GetSection("Servers").GetChildren().Count();
        var boxes = appliance >= 0 ? [appliance] : Enumerable.Range(0, boxCount).ToList();
        var results = await Task.WhenAll(boxes.Select(async b =>
        {
            try { return (b, flows: await FetchAsync(q, b, cfg, http, ct), err: (string?)null); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return (b, flows: (name: $"Kutu {b + 1}", list: new List<FlowRec>()), err: (string?)ex.Message); }
        }));
        lock (msgs) foreach (var r in results.Where(r => r.err != null)) msgs.Add($"AppResponse [{r.flows.name}] {r.err}");
        var best = results.Where(r => r.err == null).OrderByDescending(r => r.flows.list.Count).FirstOrDefault();
        return best.flows.list == null ? null : (best.flows.name, best.flows.list);
    }

    static async Task<List<FlowRec>?> FetchFwAsync(LookupQuery q, IConfiguration cfg, HttpClient http, List<string> msgs, CancellationToken ct)
    {
        try
        {
            var sp = await FirewallService.SessionsAsync(q, cfg, http, ct);
            static string V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) ? v : "";
            static double? T(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            lock (msgs) msgs.AddRange(sp.Messages.Select(m => "Firewall: " + m));
            return sp.Rows.Select(r => (s: T(V(r, "s")), e: T(V(r, "e")), r))
                .Where(x => x.s != null)
                .Select(x => new FlowRec(x.s!.Value, x.e, V(x.r, "cip"), V(x.r, "cport"), V(x.r, "sip"), V(x.r, "sport"), true))
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            lock (msgs) msgs.Add("Firewall: " + ex.Message);
            return null;
        }
    }

    static async Task<(string name, List<FlowRec> list)> FetchAsync(LookupQuery q, int appliance, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var cols = cfg.GetSection("AkisTesti:Kolonlar").GetChildren().Select(c => (c.Value ?? "").Trim()).Where(c => c != "").ToList();
        if (cols.Count != 6) cols = [.. DefaultCols];
        var (name, baseUrl, jwt) = await AppResponseService.ConnectAsync(appliance, cfg, http, ct);
        string l4 = cfg["SourceL4"] ?? "flow_tcp", pathType = cfg["SourcePathType"] ?? "jobs";
        string vifg = cfg.GetSection("VifgIds").GetChildren().Select(c => (c.Value ?? "").Trim()).FirstOrDefault(v => v != "") ?? "";
        object source = vifg == "" ? new { name = l4 } : new { name = l4, path = $"{pathType}/{vifg}" };
        var def = new
        {
            source,
            columns = cols,
            time = new { start = q.StartUnix.ToString(), end = q.EndUnix.ToString() },
            filters = new object[] { new { id = "traffic", type = "STEELFILTER", value = $"(cli_tcp.ip == {q.Ip} or srv_tcp.ip == {q.Ip})" } }
        };
        var data = await AppResponseService.RunSingleAsync(baseUrl, jwt, def, cfg.GetValue("DeleteReportInstances", true), http, ct, 90);
        static double? T(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        string C(JsonArray a, int i) => i < a.Count ? a[i]?.ToString() ?? "" : "";
        var list = data.OfType<JsonArray>()
            .Select(a => (s: T(C(a, 0)), e: T(C(a, 1)), a))
            .Where(x => x.s != null)
            .Select(x => new FlowRec(x.s!.Value, x.e, C(x.a, 2), C(x.a, 3), C(x.a, 4), C(x.a, 5)))
            .ToList();
        return (name, list);
    }

    public static OturumSonuc Build(string target, List<FlowRec> flows, EnvanterSnapshot? env, double maxSessionSec)
    {
        var msgs = new List<string>();
        string coz = flows.Count == 0 ? "veri yok"
            : flows.Count(f => f.S % 1 != 0) > flows.Count / 2 ? "milisaniye"
            : flows.Count(f => f.S % 60 == 0) > flows.Count * 0.95 ? "dakika" : "saniye";
        if (coz == "dakika")
            msgs.Add("AppResponse bu kaynakta bağlantıları dakikalık özet olarak veriyor; oturum eşleştirmesi yapılamaz.");

        var inbound = flows.Where(f => f.Sip == target && f.Cip != target && f.E != null).ToList();
        var outbound = flows.Where(f => f.Cip == target && f.Sip != target).OrderBy(f => f.S).ToList();
        var shortIn = inbound.Where(f => f.E!.Value - f.S <= maxSessionSec).ToList();
        int longIn = inbound.Count - shortIn.Count;
        if (inbound.Count > 0 && longIn > inbound.Count / 2)
            msgs.Add($"Gelen bağlantıların çoğu {maxSessionSec:0} sn'den uzun (keep-alive): içine çok sayıda istek düştüğü için eşleştirme zayıf.");

        // Her giden bağlantı hangi kısa gelen oturumların içinde başladı?
        var owners = new List<int>[outbound.Count];
        var starts = outbound.Select(o => o.S).ToArray();
        for (int i = 0; i < shortIn.Count; i++)
        {
            var f = shortIn[i];
            int j = Array.BinarySearch(starts, f.S);
            if (j < 0) j = ~j;
            for (; j < starts.Length && starts[j] <= f.E!.Value; j++)
                (owners[j] ??= []).Add(i);
        }

        // Bir giden bağlantı hem milisaniyelik (AppResponse) hem saniyelik (Firewall) bir oturumun içine düşüyorsa
        // kaba pencere yanlış eşleşme üretmesin: yalnızca hassas oturumlar sayılır.
        for (int j = 0; j < owners.Length; j++)
            if (owners[j] is { } o && o.Any(i => !shortIn[i].Kaba) && o.Any(i => shortIn[i].Kaba))
                owners[j] = o.Where(i => !shortIn[i].Kaba).ToList();

        int unmatched = owners.Count(o => o == null);
        double avgK = owners.Where(o => o != null).Select(o => (double)o!.Count).DefaultIfEmpty(0).Average();

        // Gelen oturum → hedef (ip, port): ağırlık, kesin sayısı, hedefi içeren oturumlar
        var perSession = new Dictionary<int, Dictionary<(string ip, string port), (double w, int exact)>>();
        for (int j = 0; j < outbound.Count; j++)
        {
            if (owners[j] == null) continue;
            var key = (outbound[j].Sip, outbound[j].Sport);
            double w = 1.0 / owners[j]!.Count;
            foreach (int i in owners[j]!)
            {
                if (!perSession.TryGetValue(i, out var d)) perSession[i] = d = [];
                var cur = d.GetValueOrDefault(key);
                d[key] = (cur.w + w, cur.exact + (owners[j]!.Count == 1 ? 1 : 0));
            }
        }

        var girisler = shortIn.Select((f, i) => (f, i))
            .GroupBy(x => (x.f.Cip, x.f.Sport))
            .Select(g =>
            {
                int n = g.Count();
                var durs = g.Select(x => (x.f.E!.Value - x.f.S) * 1000).Order().ToList();
                var targets = g.SelectMany(x => perSession.TryGetValue(x.i, out var d) ? d.Select(kv => (kv.Key, kv.Value, x.i)) : [])
                    .GroupBy(t => t.Key)
                    .Select(t => new OturumHedef(
                        ProcessFlowBuilder.Peer(target, t.Key.ip, t.Key.port, 0, "out", env),
                        Math.Round(t.Sum(x => x.Value.w), 1), t.Sum(x => x.Value.exact),
                        Math.Round((double)t.Select(x => x.i).Distinct().Count() / n, 3)))
                    .OrderByDescending(t => t.Agirlik).Take(MaxHedef).ToList();
                return new OturumGiris(ProcessFlowBuilder.Peer(target, g.Key.Cip, g.Key.Sport, n, "in", env), n,
                    Math.Round(durs[durs.Count / 2]), targets);
            })
            .OrderByDescending(g => g.Oturum).Take(MaxGiris).ToList();

        return new OturumSonuc(target, "", "", coz, inbound.Count, longIn, outbound.Count, unmatched, Math.Round(avgK, 1),
            girisler, msgs, 0);
    }
}
