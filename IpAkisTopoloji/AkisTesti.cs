using System.Globalization;
using System.Text.Json.Nodes;

// ---------------------------------------------------------------------------
// AppResponse ham akış testi: "aynı oturumda gelen → giden" eşleştirmesi (zaman kapsaması) mümkün mü?
// Kısa bir aralıkta sorgulanan IP'nin TCP bağlantılarını tek tek (başlangıç, bitiş, kaynak port) çeker ve özetler:
//  - zaman çözünürlüğü: satırlar bağlantı bazında mı, yoksa dakikalık özet mi?
//  - gelen bağlantıların süresi (keep-alive ile saatlerce açık mı, istek başına kısa mı?), aynı anda kaç tane açık.
// Kolon adları AkisTesti:Kolonlar ayarından değiştirilebilir (sıra: başlangıç, bitiş, istemci IP, istemci port, sunucu IP, sunucu port).
// ---------------------------------------------------------------------------
record AkisTestiGrup(string Yon, string KarsiIp, string Port, int Baglanti, int FarkliKaynakPort,
    double SureMsMedyan, double SureMsP90, double SureMsMax, int EnFazlaAyniAnda);

record AkisTestiSonuc(string Kutu, string Ip, List<string> Kolonlar, int Satir, string Cozunurluk,
    List<AkisTestiGrup> Gruplar, List<Dictionary<string, string>> OrnekSatirlar, long SureMs);

static class AkisTesti
{
    static readonly string[] DefaultCols = ["start_time", "end_time", "cli_tcp.ip", "cli_tcp.port", "srv_tcp.ip", "srv_tcp.port"];

    public static async Task<AkisTestiSonuc> RunAsync(LookupQuery q, int appliance, IConfiguration cfg, HttpClient http, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
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

        var rows = data.OfType<JsonArray>().Select(a => cols.Select((c, i) => (c, v: i < a.Count ? a[i]?.ToString() ?? "" : ""))
            .ToDictionary(x => x.c, x => x.v)).ToList();

        static double? T(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        var flows = rows.Select(r => (s: T(r[cols[0]]), e: T(r[cols[1]]), cip: r[cols[2]], cport: r[cols[3]], sip: r[cols[4]], sport: r[cols[5]]))
            .Where(f => f.s != null).ToList();

        // Çözünürlük: başlangıçlar hep tam dakika ise satırlar bağlantı değil dakikalık özettir.
        string coz;
        if (flows.Count == 0) coz = "veri yok";
        else
        {
            int fractional = flows.Count(f => f.s!.Value % 1 != 0);
            int minute = flows.Count(f => f.s!.Value % 60 == 0);
            coz = fractional > flows.Count / 2 ? "milisaniye (bağlantı bazında) — zaman kapsaması için uygun"
                : minute > flows.Count * 0.95 ? "dakikalık özet — tek tek bağlantı yok, zaman kapsaması bu kaynakla yapılamaz"
                : "saniye (bağlantı bazında) — zaman kapsaması için kaba ama kullanılabilir";
        }

        var groups = flows
            .Select(f => f.sip == q.Ip ? (yon: "gelen", peer: f.cip, port: f.sport, f) : f.cip == q.Ip ? (yon: "giden", peer: f.sip, port: f.sport, f) : (yon: "", peer: "", port: "", f))
            .Where(x => x.yon != "")
            .GroupBy(x => (x.yon, x.peer, x.port))
            .Select(g =>
            {
                var durs = g.Where(x => x.f.e != null).Select(x => (x.f.e!.Value - x.f.s!.Value) * 1000).Order().ToList();
                double P(double p) => durs.Count == 0 ? 0 : durs[(int)Math.Min(durs.Count - 1, Math.Floor(p * durs.Count))];
                // Aynı anda açık en fazla bağlantı (başlangıç/bitiş olayları üzerinde tarama)
                var ev = g.Where(x => x.f.e != null).SelectMany(x => new[] { (t: x.f.s!.Value, d: 1), (t: x.f.e!.Value, d: -1) })
                    .OrderBy(x => x.t).ThenBy(x => x.d);
                int cur = 0, max = 0;
                foreach (var (_, d) in ev) { cur += d; max = Math.Max(max, cur); }
                return new AkisTestiGrup(g.Key.yon, g.Key.peer, g.Key.port, g.Count(), g.Select(x => x.f.cport).Distinct().Count(),
                    Math.Round(P(0.5)), Math.Round(P(0.9)), Math.Round(durs.Count == 0 ? 0 : durs[^1]), max);
            })
            .OrderBy(g => g.Yon).ThenByDescending(g => g.Baglanti).Take(40).ToList();

        var sample = rows.OrderBy(r => T(r[cols[0]]) ?? 0).Take(100).ToList();
        return new AkisTestiSonuc(name, q.Ip, cols, rows.Count, coz, groups, sample, sw.ElapsedMilliseconds);
    }
}
