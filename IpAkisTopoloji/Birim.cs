using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Sunucu işleten birim (ERT_HOSTIPADDRESS.SUNUCUISLETENBIRIM). Kullanıcı (açıklık taraması): "SUNUCUISLETENBIRIM'i de ekle; ERT'de yoksa AI tahmini yaptırmamız
// gerekecek, aynı yapıyı (ERT eksikleri) buraya da getirelim". ERT'ye YAZILMAZ: öneriler Birim:OneriTablosu (dbo.DF_BIRIM_ONERI, sql/birim_oneri.sql; yoksa bellekte),
// durum AI / Kural / Onaylandı / Reddedildi / Elle; onaylanan / elle girilene AI dokunmaz.
// Kanıt (envanterden, yeni sorgu yok): sunucudaki uygulamaların ana kaydındaki birim (ERT_APPLICATIONDETAIL) ve aynı uygulamanın diğer sunucuları, adı benzeyen
// sunucular (sondaki numara atılır), aynı VIP havuzu, muhafız, segment; + işlev haritasının sunucu profili (SM talepleri, MSY / ÇSY notu…) + Nessus DNS adı.
// Kanıt tek bir birimi açıkça gösteriyorsa AI'a sorulmaz ("Kural"). AI yalnız ERT'de VAR OLAN birim adlarından birini seçebilir (kodda doğrulanır).
// ---------------------------------------------------------------------------
record BirimOneri(string Ip, string? Birim, int Guven, string? Gerekce, string Kaynak, string Durum, DateTime Tarih, string? Kaydeden);
// Kullanıcı: "yalnız SUNUCUISLETENBIRIM değil, uygulama ve VARLIKMUHAFIZI için de tahmin varsa getir": ERT boşsa uygulama = ERT eksiklerinin kayıtlı önerisi
// (DF_ERT_UYGULAMA_ONERI, port başına; reddedilenler hariç), muhafız / birim = önerilen uygulamanın ana kaydından (ERT_APPLICATIONDETAIL) ya da diğer sunucularından
record BirimUygOneri(string Port, string? Uygulama, int Guven, string Durum, bool Yeni, string? Gerekce);   // Uygulama null: AI sordu ama bulamadı (gerekçe)
record BirimSatir(string Ip, string? Ert, BirimOneri? Oneri, List<string> Uygulamalar, string? Muhafiz, List<BirimUygOneri> UygOneri,
    string? MuhafizTahmin, string? BirimTahmin, string? TahminKaynak, bool ErtKayit = false, List<string>? BosPortlar = null);   // BosPortlar: ERT'de uygulaması boş portlar
record BirimIstek(List<string>? Ips, bool Yeniden = false);
record BirimKarar(string Ip, string Karar, string? Birim);
record BirimIs(string Durum, int Toplam, int Biten, int Kural, int Ai, int Bos, string? Son, string? Hata, DateTime Baslangic, DateTime? Bitis, string? Kaydeden);

sealed class BirimService(IConfiguration cfg, EnvanterService envanter, IslevService islev, EksikService eksik, NessusService nessus, ILogger<BirimService> log)
{
    readonly object _gate = new();
    BirimIs? _is;
    CancellationTokenSource? _cts;
    string? _uyari;
    readonly Dictionary<string, BirimOneri> _bellek = new();
    static readonly JsonSerializerOptions Js = new(JsonSerializerDefaults.Web);
    string Tablo => cfg["Birim:OneriTablosu"] is { } t && Regex.IsMatch(t, @"^[\w.\[\]]+$") ? t : "dbo.DF_BIRIM_ONERI";

    // ---------------- Envanter dizini (anlık görüntü başına) ----------------
    sealed class Dizin
    {
        public Dictionary<string, string> IpBirim = new();                                   // ERT'deki birim (IP'nin satırlarında en sık)
        public Dictionary<string, int> Katalog = new(StringComparer.OrdinalIgnoreCase);      // birim → sunucu sayısı
        public Dictionary<string, Dictionary<string, int>> Uygulama = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, int>> Muhafiz = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Kok = new(StringComparer.OrdinalIgnoreCase);  // ad kökü → IP'ler
    }
    (EnvanterSnapshot Env, Dizin D)? _dizin;
    static string? Bos(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() is "#N/A" or "NULL" or "-" ? null : Regex.Replace(s.Trim(), @"\s+", " ");
    public static string? AdKoku(string? ad)
    {
        if (string.IsNullOrWhiteSpace(ad) || Regex.IsMatch(ad, @"^\d+\.\d+\.\d+\.\d+$")) return null;
        string k = Regex.Replace(ad.Trim().ToLowerInvariant().Split('.')[0], @"[-_]?\d+$", "");
        return k.Length >= 5 ? k : null;
    }
    static void Say(Dictionary<string, Dictionary<string, int>> d, string? anahtar, string birim)
    {
        if (anahtar == null) return;
        if (!d.TryGetValue(anahtar, out var x)) d[anahtar] = x = new(StringComparer.OrdinalIgnoreCase);
        x[birim] = x.GetValueOrDefault(birim) + 1;
    }
    Dizin Dizi(EnvanterSnapshot env)
    {
        lock (_gate) if (_dizin is { } c && ReferenceEquals(c.Env, env)) return c.D;
        var d = new Dizin();
        foreach (var ip in env.HostIps)
        {
            var rows = env.AppsOnIp(ip);
            var b = rows.Select(r => Bos(r.SunucuIsletenBirim)).OfType<string>().GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            foreach (var k in rows.Select(r => AdKoku(r.UnixName)).OfType<string>().Distinct())
            { if (!d.Kok.TryGetValue(k, out var l)) d.Kok[k] = l = []; l.Add(ip); }
            if (b == null) continue;
            d.IpBirim[ip] = b;
            d.Katalog[b] = d.Katalog.GetValueOrDefault(b) + 1;
            foreach (var a in env.AppNamesOnIp(ip).Distinct()) Say(d.Uygulama, a, b);
            Say(d.Muhafiz, rows.Select(r => Bos(r.VarlikMuhafizi)).FirstOrDefault(x => x != null), b);
        }
        // uygulama ana kaydındaki birimler de katalogda (sunucusu olmayan birim adı olmasın diye sayı 0)
        foreach (var u in env.Detay.Values) if (Bos(u.Birim) is { } ub) d.Katalog.TryAdd(ub, 0);
        lock (_gate) _dizin = (env, d);
        return d;
    }

    public string? Ert(EnvanterSnapshot env, string ip) => Dizi(env).IpBirim.GetValueOrDefault(ip);
    public async Task<List<(string Birim, int Sunucu)>> KatalogAsync(CancellationToken ct)
    {
        var d = Dizi(await envanter.GetAsync(false, ct));
        return d.Katalog.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => (x.Key, x.Value)).ToList();
    }

    // ---------------- Kanıt ----------------
    // Puan: her kanıt satırı birime ağırlık verir; metin AI'a gider
    (string Metin, Dictionary<string, double> Puan) Kanit(EnvanterSnapshot env, Dizin d, string ip, string? dns)
    {
        var sb = new StringBuilder();
        var puan = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void P(string b, double w) => puan[b] = puan.GetValueOrDefault(b) + w;
        string Dag(Dictionary<string, int> x) => string.Join(" | ", x.OrderByDescending(y => y.Value).Take(5).Select(y => $"{y.Key} ({y.Value})"));
        var rows = env.AppsOnIp(ip);
        string? host = rows.Select(r => r.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        sb.AppendLine($"SUNUCU: {ip}{(host != null ? " · ERT adı " + host : "")}{(dns != null ? " · DNS (Nessus) " + dns : "")}{(rows.Count == 0 ? " · ERT'de KAYDI YOK" : " · ERT'de kayıtlı ama işleten birim boş")}");
        // 1) Uygulamalar
        foreach (var a in env.AppNamesOnIp(ip).Distinct().Take(8))
        {
            if (env.Detay.TryGetValue(a, out var det) && Bos(det.Birim) is { } ab) { sb.AppendLine($"UYGULAMA ANA KAYDI: {a} → işleten birim {ab}"); P(ab, 4); }
            if (d.Uygulama.TryGetValue(a, out var ud)) { sb.AppendLine($"AYNI UYGULAMANIN ({a}) DİĞER SUNUCULARI: {Dag(ud)}"); foreach (var (b, n) in ud) P(b, 3.0 * n / ud.Values.Sum()); }
        }
        // 2) Adı benzeyen sunucular (sondaki numara atılmış ad kökü; ERT adı ya da Nessus DNS)
        foreach (var k in new[] { AdKoku(host), AdKoku(dns) }.OfType<string>().Distinct())
            if (d.Kok.TryGetValue(k, out var kl))
            {
                var x = kl.Where(i => i != ip && d.IpBirim.ContainsKey(i)).GroupBy(i => d.IpBirim[i], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count());
                if (x.Count > 0) { sb.AppendLine($"ADI BENZEYEN SUNUCULAR ({k}…): {Dag(x)}"); foreach (var (b, n) in x) P(b, 3.0 * n / x.Values.Sum()); }
            }
        // 3) Aynı VIP havuzu
        var havuz = env.MemberOf(ip).SelectMany(v => env.VipMembers(v.LbIp, v.LbPort)).Select(m => m.Ip).Where(i => i != ip && d.IpBirim.ContainsKey(i)).Distinct()
            .GroupBy(i => d.IpBirim[i], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count());
        if (havuz.Count > 0) { sb.AppendLine($"AYNI VIP HAVUZUNDAKİ SUNUCULAR: {Dag(havuz)}"); foreach (var (b, n) in havuz) P(b, 2.0 * n / havuz.Values.Sum()); }
        // 4) Muhafız
        if (rows.Select(r => Bos(r.VarlikMuhafizi)).FirstOrDefault(x => x != null) is { } muh && d.Muhafiz.TryGetValue(muh, out var md))
        { sb.AppendLine($"AYNI VARLIK MUHAFIZININ ({muh}) SUNUCULARI: {Dag(md)}"); foreach (var (b, n) in md) P(b, 1.5 * n / md.Values.Sum()); }
        // 5) Segment (zayıf)
        if (env.FindSegment(ip) is { } seg)
        {
            var sd = env.HostIps.Where(i => i != ip && d.IpBirim.ContainsKey(i) && env.FindSegment(i)?.Label == seg.Label).Take(3000)
                .GroupBy(i => d.IpBirim[i], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count());
            sb.AppendLine($"SEGMENT: {seg.Label} ({seg.Cidr}, Domain {seg.Domain}){(sd.Count > 0 ? " · segmentteki sunucuların birimleri (zayıf ipucu): " + Dag(sd) : "")}");
            foreach (var (b, n) in sd) P(b, 1.0 * n / sd.Values.Sum());
        }
        return (sb.ToString(), puan);
    }

    // ---------------- Öneri kaydı ----------------
    public async Task<Dictionary<string, BirimOneri>> OkuAsync(IEnumerable<string>? ips, CancellationToken ct)
    {
        var set = ips?.ToHashSet();
        var l = new Dictionary<string, BirimOneri>();
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT IP, BIRIM, GUVEN, GEREKCE, KAYNAK, DURUM, TARIH, KAYDEDEN FROM {Tablo}", conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                string ip = rd.GetString(0);
                if (set != null && !set.Contains(ip)) continue;
                l[ip] = new BirimOneri(ip, rd.IsDBNull(1) ? null : rd.GetString(1), rd.IsDBNull(2) ? 0 : rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? "AI" : rd.GetString(4), rd.IsDBNull(5) ? "AI" : rd.GetString(5), rd.GetDateTime(6), rd.IsDBNull(7) ? null : rd.GetString(7));
            }
            if (_bellek.Count == 0) _uyari = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { _uyari = $"Birim öneri tablosu ({Tablo}) yok; öneriler yalnız bellekte (sql/birim_oneri.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { _uyari = "Birim önerileri okunamadı: " + ex.Message; }
        lock (_gate) foreach (var (ip, o) in _bellek) if (set == null || set.Contains(ip)) l[ip] = o;
        return l;
    }

    async Task YazAsync(BirimOneri o, CancellationToken ct)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {Tablo} AS t USING (SELECT @ip AS IP) AS s ON t.IP = s.IP
                WHEN MATCHED THEN UPDATE SET BIRIM = @b, GUVEN = @g, GEREKCE = @r, KAYNAK = @k, DURUM = @d, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (IP, BIRIM, GUVEN, GEREKCE, KAYNAK, DURUM, TARIH, KAYDEDEN) VALUES (@ip, @b, @g, @r, @k, @d, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@ip", o.Ip);
            cmd.Parameters.AddWithValue("@b", (object?)o.Birim ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@g", o.Guven);
            cmd.Parameters.AddWithValue("@r", (object?)(o.Gerekce is { Length: > 1000 } g ? g[..1000] : o.Gerekce) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@k", o.Kaynak);
            cmd.Parameters.AddWithValue("@d", o.Durum);
            cmd.Parameters.AddWithValue("@u", (object?)o.Kaydeden ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            lock (_gate) _bellek.Remove(o.Ip);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) _bellek[o.Ip] = o;
            _uyari = (ex is Microsoft.Data.SqlClient.SqlException { Number: 208 } ? $"Birim öneri tablosu ({Tablo}) yok" : "Birim önerisi DB'ye yazılamadı (" + ex.Message + ")")
                + "; öneriler yalnız bellekte (uygulama yeniden başlayınca kaybolur; sql/birim_oneri.sql).";
        }
    }

    // Satırlar (açıklık taraması sayfası: tablo açılınca): ERT birimi / uygulamaları / muhafızı + kayıtlı birim önerisi + uygulama önerileri ve onlardan türeyen tahminler
    public async Task<(List<BirimSatir> L, string? Uyari)> SatirlarAsync(List<string> ips, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var d = Dizi(env);
        ips = ips.Where(i => Regex.IsMatch(i, @"^\d+\.\d+\.\d+\.\d+$")).Distinct().ToList();
        var on = await OkuAsync(ips, ct);
        Dictionary<string, Dictionary<string, EksikOneri>> uo = [];
        string? uyari = _uyari;
        try { uo = await eksik.OnerilerAsync(ips, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { uyari = (uyari == null ? "" : uyari + " ") + "Uygulama önerileri okunamadı: " + ex.Message; }
        var l = new List<BirimSatir>();
        foreach (var ip in ips)
        {
            var rows = env.AppsOnIp(ip);
            var uyg = env.AppNamesOnIp(ip).Distinct().ToList();
            string? muh = rows.Select(r => Bos(r.VarlikMuhafizi)).FirstOrDefault(x => x != null);
            // bulunamadı (Uygulama null) cevapları da gelir (sayfada gerekçesiyle "bulunamadı"); reddedilenler hariç
            var uol = (uo.GetValueOrDefault(ip) ?? []).Where(x => x.Value.Durum != "Reddedildi")
                .Select(x => new BirimUygOneri(x.Key, x.Value.Uygulama, x.Value.Guven, x.Value.Durum, x.Value.Yeni, x.Value.Gerekce))
                .OrderByDescending(x => x.Uygulama != null).ThenByDescending(x => x.Durum is "Onaylandı" or "Elle").ThenByDescending(x => x.Guven).ToList();
            // Önerilen uygulamadan muhafız / birim (ERT'de boşsa): ana kayıt, yoksa uygulamanın diğer sunucularındaki en sık birim
            string? mT = null, bT = null, kay = null;
            foreach (var u in uol.Where(x => x.Uygulama != null && !x.Uygulama.StartsWith("Altyapı", StringComparison.OrdinalIgnoreCase)))
            {
                string ad = Regex.Replace(u.Uygulama!, @"\s+Veritaban[ıi]$", "", RegexOptions.IgnoreCase);
                env.Detay.TryGetValue(ad, out var det);
                det ??= env.Detay.GetValueOrDefault(u.Uygulama!);
                string? m2 = Bos(det?.Muhafiz);
                string? b2 = Bos(det?.Birim) ?? (d.Uygulama.TryGetValue(ad, out var ud) ? ud.OrderByDescending(x => x.Value).First().Key : null);
                if (m2 == null && b2 == null) continue;
                mT ??= m2; bT ??= b2; kay ??= $"uygulama önerisinden: {u.Uygulama} (port {u.Port}, {u.Durum}, %{u.Guven})";
                if (mT != null && bT != null) break;
            }
            l.Add(new BirimSatir(ip, d.IpBirim.GetValueOrDefault(ip), on.GetValueOrDefault(ip), uyg, muh, uol,
                muh == null ? mT : null, d.IpBirim.ContainsKey(ip) ? null : bT, kay, rows.Count > 0,
                rows.Where(r => string.IsNullOrWhiteSpace(r.AppName) && !string.IsNullOrWhiteSpace(r.Port)).Select(r => r.Port!.Trim()).Distinct().ToList()));
        }
        return (l, uyari);
    }

    // Uygulama tahmini için port (CB portu olmayan sunucular; kullanıcı: "atlama yok"): Nessus'un gördüğü portlar, yoksa ERT portları
    public async Task<Dictionary<string, List<string>>> PortlarAsync(List<string> ips, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        ips = ips.Where(i => Regex.IsMatch(i, @"^\d+\.\d+\.\d+\.\d+$")).Distinct().Take(500).ToList();
        Dictionary<string, NessusSunucu> nes = [];
        if (nessus.Acik)
            try { nes = await nessus.SunucularAsync(ips, null, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Birim: Nessus portları okunamadı: {H}", ex.Message); }
        int dyn = cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);
        return ips.ToDictionary(ip => ip, ip =>
        {
            var l = (nes.GetValueOrDefault(ip)?.Portlar ?? []).Select(p => p.Port).Where(p => int.TryParse(p, out int n) && n > 0 && n < dyn).Distinct().ToList();
            if (l.Count == 0) l = env.AppsOnIp(ip).Select(r => r.Port?.Trim()).OfType<string>().Where(p => int.TryParse(p, out int n) && n > 0 && n < dyn).Distinct().ToList();
            return l.OrderBy(p => int.Parse(p)).Take(20).ToList();
        });
    }

    public async Task<string?> KararAsync(BirimKarar k, string? user, CancellationToken ct)
    {
        var mevcut = (await OkuAsync([k.Ip], ct)).GetValueOrDefault(k.Ip);
        switch (k.Karar)
        {
            case "onay" when mevcut?.Birim != null: await YazAsync(mevcut with { Durum = "Onaylandı", Kaydeden = user }, ct); return null;
            case "red" when mevcut != null: await YazAsync(mevcut with { Durum = "Reddedildi", Kaydeden = user }, ct); return null;
            case "elle":
                var kat = await KatalogAsync(ct);
                string? b = Bos(k.Birim);
                if (b == null) return "Birim adı boş.";
                b = kat.Select(x => x.Birim).FirstOrDefault(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase)) ?? b;
                await YazAsync(new BirimOneri(k.Ip, b, 100, "Elle girildi", "Elle", "Elle", DateTime.Now, user), ct); return null;
            case "sil":
                lock (_gate) _bellek.Remove(k.Ip);
                try
                {
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
                    await conn.OpenAsync(ct);
                    await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {Tablo} WHERE IP = @ip", conn);
                    cmd.Parameters.AddWithValue("@ip", k.Ip);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Birim önerisi silinemedi: {H}", ex.Message); }
                return null;
            default: return "Geçersiz karar ya da öneri yok.";
        }
    }

    // ---------------- Tahmin işi (tek seferde bir tane) ----------------
    public BirimIs? Durum() { lock (_gate) return _is; }
    public void Durdur() { lock (_gate) _cts?.Cancel(); }
    public BirimIs Baslat(List<string> ips, bool yeniden, string? user)
    {
        lock (_gate)
        {
            if (_is is { Durum: "calisiyor" }) return _is;
            int max = cfg.GetValue("Birim:MaxIp", 300);
            var l = ips.Where(i => Regex.IsMatch(i, @"^\d+\.\d+\.\d+\.\d+$")).Distinct().Take(max).ToList();
            _cts = new CancellationTokenSource();
            _is = new BirimIs("calisiyor", l.Count, 0, 0, 0, 0, null, null, DateTime.Now, null, user);
            var ct = _cts.Token;
            _ = Task.Run(() => CalisAsync(l, yeniden, user, ct));
            return _is;
        }
    }
    void Set(Func<BirimIs, BirimIs> f) { lock (_gate) if (_is != null) _is = f(_is); }

    async Task CalisAsync(List<string> ips, bool yeniden, string? user, CancellationToken ct)
    {
        try
        {
            var env = await envanter.GetAsync(false, ct);
            var d = Dizi(env);
            var mevcut = await OkuAsync(ips, ct);
            // ERT'de birimi olan, onaylı / elle girilmiş ya da (yeniden değilse) önerisi olan sunucular sorulmaz
            var todo = ips.Where(ip => !d.IpBirim.ContainsKey(ip)
                && !(mevcut.TryGetValue(ip, out var m) && (m.Durum is "Onaylandı" or "Elle" || !yeniden))).ToList();
            Set(i => i with { Toplam = todo.Count });
            Dictionary<string, NessusSunucu> dns = [];
            var katalog = d.Katalog.Where(x => x.Value > 0).OrderByDescending(x => x.Value).ThenBy(x => x.Key).Take(cfg.GetValue("Birim:KatalogMax", 250)).ToList();
            string katMetin = string.Join("\n", katalog.Select(x => $"- {x.Key} ({x.Value} sunucu)"));
            var (bilgi, _) = eksik.Bilgi();
            using var sem = new SemaphoreSlim(Math.Clamp(cfg.GetValue("Birim:Paralel", 3), 1, 8));
            await Task.WhenAll(todo.Select(async ip =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var o = await TahminAsync(env, d, ip, katalog.Select(x => x.Key).ToList(), katMetin, bilgi, user, ct);
                    await YazAsync(o, ct);
                    Set(i => i with { Biten = i.Biten + 1, Kural = i.Kural + (o.Kaynak == "Kural" ? 1 : 0), Ai = i.Ai + (o.Kaynak == "AI" && o.Birim != null ? 1 : 0),
                        Bos = i.Bos + (o.Birim == null ? 1 : 0), Son = $"{ip} → {o.Birim ?? "bulunamadı"} ({o.Kaynak}, %{o.Guven})" });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Birim tahmini {Ip}: {H}", ip, ex.Message);
                    Set(i => i with { Biten = i.Biten + 1, Bos = i.Bos + 1, Son = $"{ip} → hata: {ex.Message}" });
                }
                finally { sem.Release(); }
            }));
            Set(i => i with { Durum = "bitti", Bitis = DateTime.Now });
        }
        catch (OperationCanceledException) { Set(i => i with { Durum = "durduruldu", Bitis = DateTime.Now }); }
        catch (Exception ex) { log.LogWarning(ex, "Birim tahmini hatası"); Set(i => i with { Durum = "hata", Hata = ex.Message, Bitis = DateTime.Now }); }
    }

    public async Task<string> KanitMetniAsync(string ip, CancellationToken ct)
    {
        var env = await envanter.GetAsync(false, ct);
        var (m, p) = Kanit(env, Dizi(env), ip, null);
        return m + "\nKOD PUANLARI: " + string.Join(" | ", p.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value:0.0}"));
    }

    async Task<BirimOneri> TahminAsync(EnvanterSnapshot env, Dizin d, string ip, List<string> katalog, string katMetin, string? bilgi, string? user, CancellationToken ct)
    {
        string? dns = null;
        var (kanit, puan) = Kanit(env, d, ip, dns);
        // ERT eksiklerinin uygulama önerisi (ERT'de uygulama boşsa en iyi ipucu): önerilen uygulamanın birimi
        try
        {
            var uo = (await eksik.OnerilerAsync([ip], ct)).GetValueOrDefault(ip) ?? [];
            foreach (var (port, o) in uo.Where(x => x.Value.Uygulama != null && x.Value.Durum != "Reddedildi" && !x.Value.Uygulama!.StartsWith("Altyapı", StringComparison.OrdinalIgnoreCase)))
            {
                string ad = Regex.Replace(o.Uygulama!, @"\s+Veritaban[ıi]$", "", RegexOptions.IgnoreCase);
                double w = o.Durum is "Onaylandı" or "Elle" ? 4 : 2.0 * Math.Clamp(o.Guven, 0, 100) / 100;
                var ub = Bos(env.Detay.GetValueOrDefault(ad)?.Birim);
                kanit += $"UYGULAMA ÖNERİSİ (ERT eksikleri, port {port}, {o.Durum}, güven %{o.Guven}): {o.Uygulama}{(ub != null ? " → ana kayıttaki birim " + ub : "")}\n";
                if (ub != null) puan[ub] = puan.GetValueOrDefault(ub) + w;
                else if (d.Uygulama.TryGetValue(ad, out var ud))
                {
                    kanit += $"   önerilen uygulamanın sunucularındaki birimler: {string.Join(" | ", ud.OrderByDescending(x => x.Value).Take(4).Select(x => $"{x.Key} ({x.Value})"))}\n";
                    foreach (var (bb, n) in ud) puan[bb] = puan.GetValueOrDefault(bb) + w * n / ud.Values.Sum();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug("Birim: uygulama önerisi okunamadı: {H}", ex.Message); }
        // Kural: güçlü kanıt (uygulama ana kaydı / aynı uygulama / benzer ad) tek birimi gösteriyor ve ikinci aday çok geride
        var sira = puan.OrderByDescending(x => x.Value).ToList();
        if (sira.Count > 0 && sira[0].Value >= cfg.GetValue("Birim:KuralEsik", 4.0) && (sira.Count == 1 || sira[0].Value >= 3 * sira[1].Value))
            return new BirimOneri(ip, sira[0].Key, Math.Min(90, 60 + (int)(sira[0].Value * 4)),
                "Envanter kanıtı tek birimi gösteriyor: " + Kisalt(kanit.Replace("\n", " · "), 600), "Kural", "AI", DateTime.Now, user);

        string profil = "";
        try { profil = (await islev.ProfileAsync(ip, ct)).Metin; }
        catch (Exception ex) when (ex is not OperationCanceledException) { profil = "(sunucu profili okunamadı: " + ex.Message + ")"; }
        int max = Math.Clamp(cfg.GetValue("Birim:MaxKarakter", 6000), 1500, 30000);
        var prompt = new StringBuilder();
        if (bilgi != null) prompt.AppendLine("GENEL BİLGİLER (kurum):").AppendLine(bilgi).AppendLine();
        prompt.AppendLine("""
            Görev: Bir bankanın sunucusunu hangi BİRİMİN İŞLETTİĞİNİ (ERT SUNUCUISLETENBIRIM) tahmin et.
            Kurallar:
            - Yalnız aşağıdaki BİRİM LİSTESİ'nden birini HARFİ HARFİNE seç. Listede uygun birim yoksa "birim": null yaz.
            - En güçlü kanıt: sunucudaki uygulamanın ana kaydındaki birim ve aynı uygulamanın diğer sunucularının birimi; sonra adı benzeyen sunucular (aynı ad kökü),
              aynı VIP havuzu, varlık muhafızı, SM taleplerindeki talep eden birim / açıklamalar, sanal sunucu notu. Segment zayıf ipucudur.
            - Kanıt yoksa uydurma: "birim": null.
            Cevap YALNIZ JSON: {"birim": "...", "guven": 0-100, "gerekce": "en çok 200 karakter"}
            """);
        prompt.AppendLine("BİRİM LİSTESİ (ERT'de geçen işleten birimler):").AppendLine(katMetin).AppendLine();
        prompt.AppendLine("KANITLAR:").AppendLine(kanit);
        prompt.AppendLine("SUNUCU PROFİLİ:").AppendLine(Kisalt(profil, max));
        string cevap = await islev.ChatAsync(prompt.ToString(), ct);
        var j = IslevService.ExtractJson(cevap);
        string? b = j is { } je ? IslevService.Str(je, "birim") : null;
        int g = j is { } je2 ? IslevService.Int(je2, "guven") : 0;
        string? ger = j is { } je3 ? IslevService.Str(je3, "gerekce") : "AI cevabı JSON değil";
        if (b is "null" or "") b = null;
        if (b != null)
        {
            var es = katalog.FirstOrDefault(x => string.Equals(Regex.Replace(x, @"\s+", " "), Regex.Replace(b, @"\s+", " "), StringComparison.OrdinalIgnoreCase))
                ?? d.Katalog.Keys.FirstOrDefault(x => string.Equals(x, b, StringComparison.OrdinalIgnoreCase));
            if (es == null) { ger = $"Yok sayıldı: AI'ın önerdiği '{b}' ERT birim listesinde yok. {ger}"; b = null; g = 0; }
            else b = es;
        }
        // kod puanıyla çelişen düşük güvenli AI cevabında not
        if (b != null && sira.Count > 0 && !string.Equals(sira[0].Key, b, StringComparison.OrdinalIgnoreCase) && sira[0].Value >= 3)
            ger += $" [envanter kanıtı daha çok {sira[0].Key} gösteriyor]";
        return new BirimOneri(ip, b, Math.Clamp(g, 0, 100), ger, "AI", "AI", DateTime.Now, user);
    }
    static string Kisalt(string s, int n) => s.Length > n ? s[..n] + " …" : s;
}
