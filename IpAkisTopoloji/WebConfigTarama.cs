using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

// web.config taraması (wwwroot/webconfig.html; kullanıcı: "verdiğim IP'lerde ya da segmentlerde ya da tüm ağda web.config taraması; text arayacağız; ayrıca içinde
// http / https geçen adresleri ve exe geçen yerleri her zaman listelesin"). UCMDB dizinindeki belgeler kapsama göre seçilir, içerikler Oracle'dan yüzerli okunur,
// çözülür ve MASKELENİR (şifre / anahtar / token hiçbir listeye ve aramaya girmez); maskeli metin gzip'li bellekte tutulur → yeni arama Oracle'ı yeniden taramaz.
record WcBelge(string Id, string Ad, string? Yol, string? Sunucu, string? Ip, List<string> Ipler, string? Segment, string? Eslesme, int Boyut, string Bicim, int Maske,
    int Url, int Exe, int Eslesen, string? Hata, string Tur = "web.config", int Bulgu = 0);
record WcUrl(string Belge, string Url, string Sema, string Host, string? Port, string? Yol, int Satir, string? Anahtar);
record WcExe(string Belge, string Exe, int Satir, string Metin);
record WcEslesme(string Belge, string Terim, int Satir, string Metin);
// Zafiyet göstergesi: kural (Ucmdb:ZafiyetKurallari) eşleşmesi; Derece kesin | olasi | kontrol
record WcBulgu(string Belge, string Kural, string Derece, int Satir, string Metin);
sealed class WcKural
{
    public string Ad { get; set; } = "";
    public string Desen { get; set; } = "";
    public bool Regex { get; set; }
    public bool BuyukKucuk { get; set; }
    public string Derece { get; set; } = "kontrol";
    public string? Aciklama { get; set; }
}
// Dmz: Nessus asset'indeki IP'ler (Asset boşsa Zafiyet:DmzAsset — açıklık taramasıyla aynı DMZ tanımı)
// Turler: web.config | applicationHost.config | machine.config | diğer (boş = hepsi); Regex: aranacak metinler düzenli ifade
record WcTaramaIstek(List<string>? Ips, List<string>? Segmentler, bool? Tum, List<string>? Aramalar, bool? Dmz = null, string? Asset = null,
    List<string>? Turler = null, bool? Regex = null, bool? BuyukKucuk = null);
record WcAramaIstek(List<string>? Aramalar, bool? BuyukKucuk, bool? Regex = null);

sealed class WcIs
{
    public string Id = Guid.NewGuid().ToString("N")[..12];
    public string Kullanici = "", Kapsam = "";
    public DateTime Basladi = DateTime.UtcNow;
    public DateTime? BittiZaman;
    public bool Bitti;
    public string? Hata, Asama;
    public int Toplam, Okunan;
    public List<string> Aramalar = [];
    public bool AramaRegex, AramaBk;
    public List<(string Ad, Regex R)> AramaDesen = [];
    public List<(WcKural K, Regex R)> Kurallar = [];
    public List<WcBulgu> Bulgular = [];
    public List<string> BelgesizIpler = [];                       // kapsamdaki IP'lerden UCMDB'de hiç config'i bulunamayanlar
    public Dictionary<string, int> KapsamTur = new();             // kapsamdaki belgelerin türleri (filtreden önce)
    public List<WcBelge> Belgeler = [];
    public List<WcUrl> Urller = [];
    public List<WcExe> Exeler = [];
    public List<WcEslesme> Eslesmeler = [];
    public List<string> Mesajlar = [];
    public ConcurrentDictionary<string, byte[]> Icerik = new();   // belge kimliği → maskeli metin (gzip)
    public long IcerikBayt;
    public bool IcerikKesik;                                       // bellek sınırı doldu: bazı belgeler yeni aramaya girmez
    public CancellationTokenSource Cts = new();
}

sealed partial class UcmdbService
{
    static readonly ConcurrentDictionary<string, WcIs> _wcIsler = new();
    public WcIs? WcIs(string id) => _wcIsler.TryGetValue(id, out var j) ? j : null;

    static readonly Regex _wcUrl = new(@"\bhttps?://[^\s""'<>`;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex _wcExe = new(@"\.exe\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // satırın "anahtarı": önce key, sonra name / contract / id öznitelikleri (binding, address değil)
    static readonly Regex[] _wcAnahtar = new[] { "key", "name", "contract", "id" }
        .Select(a => new Regex($@"\b{a}\s*=\s*[""']([^""']{{1,120}})[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)).ToArray();

    // Kapsam: IP'ler (UCMDB IP'si ya da ERT sunucu adı), segment adları / CIDR'lar (belgenin sunucu IP'lerinden biri segmentte), ya da tüm belgeler
    (List<(UcmdbBelge B, string? Ip, string? Segment)> Liste, string Ozet) WcKapsam(WcTaramaIstek req, EnvanterSnapshot env, Func<string, bool>? dmz = null, string? dmzAd = null,
        WcIs? j = null)
    {
        var (l0, oz) = WcKapsam0(req, env, dmz, dmzAd, j);
        var tur = l0.GroupBy(x => DosyaTuru(x.B.Ad)).ToDictionary(g => g.Key, g => g.Count());
        if (j != null) j.KapsamTur = tur;
        var turler = (req.Turler ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (turler.Count == 0 || TurlerHepsi.All(turler.Contains)) return (l0, oz);
        return (l0.Where(x => turler.Contains(DosyaTuru(x.B.Ad))).ToList(), oz + " · dosya: " + string.Join(", ", TurlerHepsi.Where(turler.Contains)));
    }

    internal static readonly string[] TurlerHepsi = ["web.config", "applicationHost.config", "machine.config", "diğer"];
    // dosya türü A_NAME'den (yol değil: "…\\config\\web.config" Framework'ün kök web.config'i de web.config sayılır)
    internal static string DosyaTuru(string ad)
    {
        var a = (ad ?? "").Trim().ToLowerInvariant();
        int i = a.LastIndexOfAny(['\\', '/']); if (i >= 0) a = a[(i + 1)..];
        return a.EndsWith("web.config") ? "web.config" : a.EndsWith("applicationhost.config") ? "applicationHost.config" : a.EndsWith("machine.config") ? "machine.config" : "diğer";
    }

    (List<(UcmdbBelge B, string? Ip, string? Segment)> Liste, string Ozet) WcKapsam0(WcTaramaIstek req, EnvanterSnapshot env, Func<string, bool>? dmz, string? dmzAd, WcIs? j)
    {
        var dz = _dizin ?? throw new InvalidOperationException("UCMDB config listesi henüz hazır değil.");
        string? Seg(string? ip) => ip == null ? null : env.FindSegment(ip)?.Label;
        // sunucu adı → ERT IP'leri (UCMDB'de IP'si olmayan belgeler için)
        var adIp = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ip in env.HostIps)
            foreach (var ad in env.AppsOnIp(ip).Select(h => h.UnixName).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct())
                foreach (var a in Anahtarlar(ad)) { if (!adIp.TryGetValue(a, out var l)) adIp[a] = l = []; if (!l.Contains(ip)) l.Add(ip); }
        List<string> BelgeIpleri(UcmdbBelge b) => b.Ipler is { Count: > 0 } ? b.Ipler
            : b.Sunucu == null ? [] : Anahtarlar(b.Sunucu).SelectMany(a => adIp.GetValueOrDefault(a) ?? []).Distinct().ToList();

        var sonuc = new List<(UcmdbBelge, string?, string?)>();
        if (req.Tum == true)
        {
            foreach (var b in dz.Belgeler)
            {
                var ip = BelgeIpleri(b).FirstOrDefault();
                sonuc.Add((b, ip, Seg(ip)));
            }
            return (sonuc, $"tüm ağ ({dz.Belgeler.Count} config)");
        }
        var ozet = new List<string>();
        var ips = (req.Ips ?? []).Select(x => LookupQuery.TryParseIpv4(x?.Trim() ?? "", out string v) ? v : null).OfType<string>().Distinct().ToList();
        if (ips.Count > 0)
        {
            var bel = Belgeler(ips, env, true);
            foreach (var (ip, l) in bel) foreach (var b in l) sonuc.Add((b, ip, Seg(ip)));
            if (j != null) j.BelgesizIpler = ips.Where(ip => !bel.ContainsKey(ip)).ToList();
            ozet.Add($"{ips.Count} IP");
        }
        var segler = (req.Segmentler ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (segler.Count > 0)
        {
            var cidrler = segler.Select(Cidr).OfType<(uint Ag, uint Maske)>().ToList();
            var adlar = segler.Where(x => Cidr(x) == null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Uyar(string ip)
            {
                if (cidrler.Count > 0 && Ip4(ip) is uint u && cidrler.Any(c => (u & c.Maske) == c.Ag)) return true;
                var s = env.FindSegment(ip);
                return s != null && (adlar.Contains(s.Label) || adlar.Contains(s.Cidr));
            }
            foreach (var b in dz.Belgeler)
                foreach (var ip in BelgeIpleri(b).Where(Uyar).Take(1)) sonuc.Add((b, ip, Seg(ip)));
            ozet.Add($"{segler.Count} segment");
        }
        if (dmz != null)
        {
            foreach (var b in dz.Belgeler)
                foreach (var ip in BelgeIpleri(b).Where(dmz).Take(1)) sonuc.Add((b, ip, Seg(ip)));
            ozet.Add($"Nessus {dmzAd ?? "DMZ"}");
        }
        return (sonuc.DistinctBy(x => x.Item1.Id).ToList(), string.Join(" + ", ozet));
    }

    static uint? Ip4(string ip) => IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
        ? (uint)(a.GetAddressBytes()[0] << 24 | a.GetAddressBytes()[1] << 16 | a.GetAddressBytes()[2] << 8 | a.GetAddressBytes()[3]) : null;
    static (uint, uint)? Cidr(string s)
    {
        var m = Regex.Match(s.Trim(), @"^(\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})$");
        if (!m.Success || Ip4(m.Groups[1].Value) is not uint ag || !int.TryParse(m.Groups[2].Value, out int n) || n > 32) return null;
        uint maske = n == 0 ? 0 : uint.MaxValue << (32 - n);
        return (ag & maske, maske);
    }

    public async Task<WcIs> WcBaslatAsync(WcTaramaIstek req, string kullanici, EnvanterService envanter, NessusService nessus, CancellationToken ct)
    {
        if (!Acik) throw new InvalidOperationException("UCMDB bağlantısı tanımlı değil (Ucmdb:Baglanti).");
        foreach (var kv in _wcIsler.Where(kv => kv.Value.Bitti && (DateTime.UtcNow - (kv.Value.BittiZaman ?? kv.Value.Basladi)).TotalHours > 2).ToList()) _wcIsler.TryRemove(kv.Key, out _);
        if (_wcIsler.Values.Count(x => !x.Bitti) >= C.GetValue("WebConfigAyniAnda", 2))
            throw new InvalidOperationException("Şu an başka web.config taramaları sürüyor; biri bitince yeniden deneyin.");
        if (req.Tum != true && (req.Ips?.Count ?? 0) == 0 && (req.Segmentler?.Count ?? 0) == 0 && req.Dmz != true)
            throw new InvalidOperationException("IP, segment, Nessus DMZ ya da 'tüm ağ' seçin.");
        // Nessus DMZ asset'i (Tenable.sc asset listesi → IP aralıkları); taramadan önce çözülür ki hata hemen görünsün
        Func<string, bool>? dmz = null; string? dmzAd = null;
        if (req.Dmz == true && req.Tum != true)
        {
            if (!nessus.Acik) throw new InvalidOperationException("Nessus tanımlı değil (Nessus:BaseUrl).");
            string tanim = req.Asset is { Length: > 0 } ra ? ra : Kok["Zafiyet:DmzAsset"] ?? throw new InvalidOperationException("DMZ asset'i tanımlı değil (Zafiyet:DmzAsset).");
            var (a, hata) = await nessus.AssetBulAsync(tanim, ct);
            if (a == null) throw new InvalidOperationException(hata ?? "Nessus asset'i bulunamadı.");
            var ai = await nessus.AssetIpleriAsync(a, ct);
            if (ai.IpSayisi == 0) throw new InvalidOperationException($"Nessus asset'i '{a.Ad}' boş (IP aralığı yok).");
            dmz = ip => ai.Icerir(ip); dmzAd = $"{a.Ad} ({ai.IpSayisi:N0} IP)";
        }
        var j = new WcIs { Kullanici = kullanici, Aramalar = Terimler(req.Aramalar), AramaRegex = req.Regex == true, AramaBk = req.BuyukKucuk == true };
        j.AramaDesen = WcDesen(j.Aramalar, j.AramaBk, j.AramaRegex);   // geçersiz regex → ArgumentException (iş başlamadan)
        j.Kurallar = Kurallar().Select(k => (k, WcDesen([k.Desen], k.BuyukKucuk, k.Regex)[0].R)).ToList();
        _wcIsler[j.Id] = j;
        _ = Task.Run(async () =>
        {
            try
            {
                j.Asama = "UCMDB config listesi bekleniyor";
                DizinDurumu();
                if (_dizin == null) await DizinAsync(false, j.Cts.Token);
                var env = await envanter.GetAsync(false, j.Cts.Token);
                var (liste, ozet) = WcKapsam(req, env, dmz, dmzAd, j);
                j.Kapsam = ozet; j.Toplam = liste.Count;
                if (j.BelgesizIpler.Count > 0) lock (j) j.Mesajlar.Add($"{j.BelgesizIpler.Count} IP için UCMDB'de hiç config bulunamadı (Belgeler sekmesinin altında liste).");
                if (liste.Count == 0) { j.Mesajlar.Add("Kapsamda UCMDB'de config bulunamadı."); return; }
                long maxBayt = C.GetValue("WebConfigMaxBellekMB", 300L) * 1024 * 1024;
                int parca = Math.Clamp(C.GetValue("WebConfigParca", 100), 10, 500);
                // Oracle bağlantısı koparsa en çok 3 kez yeniden bağlanılır; tarama kaldığı belgeden (j.Okunan) sürer
                for (int deneme = 0; ; deneme++)
                {
                    try
                    {
                        using var c = await AcAsync(j.Cts.Token);
                        var s = await SemaAsync(c, j.Cts.Token);
                        foreach (var grup in liste.Skip(j.Okunan).Chunk(parca))
                        {
                            j.Cts.Token.ThrowIfCancellationRequested();
                            j.Asama = $"içerikler okunuyor: {j.Okunan + 1}–{j.Okunan + grup.Length} / {j.Toplam}";
                            var icerik = await IcerikAsync(c, s, grup.Select(g => g.B.Id).ToList(), j.Cts.Token);
                            WcGrupIsle(j, grup, icerik, maxBayt);
                            j.Okunan += grup.Length;
                        }
                        break;
                    }
                    catch (Oracle.ManagedDataAccess.Client.OracleException ex) when (deneme < 3)
                    {
                        lock (j) j.Mesajlar.Add("Oracle bağlantısı koptu, yeniden bağlanılıyor: " + ex.Message);
                    }
                }
                if (j.IcerikKesik) lock (j) j.Mesajlar.Add($"Bellek sınırı (Ucmdb:WebConfigMaxBellekMB) doldu: bazı belgeler yeni aramalara girmez; listeler tam.");
            }
            catch (OperationCanceledException) { j.Hata = "Durduruldu."; }
            catch (Exception ex) { j.Hata = ex.Message; }
            finally { j.Asama = null; j.BittiZaman = DateTime.UtcNow; j.Bitti = true; }
        });
        return j;
    }

    void WcGrupIsle(WcIs j, (UcmdbBelge B, string? Ip, string? Segment)[] grup, List<(string Id, byte[] Bayt)> icerik, long maxBayt)
    {
            var bayt = icerik.ToDictionary(x => x.Id, x => x.Bayt, StringComparer.OrdinalIgnoreCase);
            foreach (var (b, ip, seg) in grup)
            {
                WcBelge belge;
                var urller = new List<WcUrl>(); var exeler = new List<WcExe>(); var es = new List<WcEslesme>(); var bulgu = new List<WcBulgu>();
                string tur = DosyaTuru(b.Ad);
                if (!bayt.TryGetValue(b.Id, out var by))
                    belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, 0, "boş", 0, 0, 0, 0, "içerik yok ya da Ucmdb:MaxBoyut'tan büyük", tur);
                else
                {
                    var (metin0, bicim) = Coz(by, b.Ad);
                    if (metin0 == null) belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, by.Length, bicim, 0, 0, 0, 0, "içerik çözülemedi", tur);
                    else
                    {
                        var (metin, maske) = Maskele(metin0);
                        WcCikar(b.Id, metin, urller, exeler);
                        if (j.AramaDesen.Count > 0) es = WcAra(b.Id, metin, j.AramaDesen, 200);
                        bulgu = WcKuralAra(b.Id, metin, j.Kurallar);
                        if (Interlocked.Read(ref j.IcerikBayt) < maxBayt)
                        {
                            var gz = Sikistir(metin);
                            j.Icerik[b.Id] = gz; Interlocked.Add(ref j.IcerikBayt, gz.Length);
                        }
                        else j.IcerikKesik = true;
                        belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, by.Length, bicim, maske, urller.Count, exeler.Count, es.Count, null, tur, bulgu.Count);
                    }
                }
                lock (j) { j.Belgeler.Add(belge); j.Urller.AddRange(urller); j.Exeler.AddRange(exeler); j.Eslesmeler.AddRange(es); j.Bulgular.AddRange(bulgu); }
            }
    }

    static List<string> Terimler(List<string>? l) => (l ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct().Take(50).ToList();

    // http / https adresleri ve ".exe" geçen yerler (satır numarasıyla); metin zaten maskeli
    internal static void WcCikar(string id, string metin, List<WcUrl> urller, List<WcExe> exeler)
    {
        var satirlar = metin.Split('\n');
        for (int i = 0; i < satirlar.Length; i++)
        {
            string s = satirlar[i];
            if (s.Length == 0) continue;
            string? anahtar = null;
            string sd = s.Contains('&') ? WebUtility.HtmlDecode(s) : s;   // &amp; → & (adres ; ile bölünmesin)
            foreach (Match m in _wcUrl.Matches(sd))
            {
                if (urller.Count >= 500) break;
                var u = Temizle(m.Value.TrimEnd(';', ',', '}', ')', '\\'));
                anahtar ??= _wcAnahtar.Select(r => r.Match(s)).FirstOrDefault(x => x.Success)?.Groups[1].Value;
                string sema = u.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http", host = "", yol = "";
                string? port = null;
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri)) { host = uri.Host.ToLowerInvariant(); port = uri.IsDefaultPort ? (sema == "https" ? "443" : "80") : uri.Port.ToString(); yol = uri.AbsolutePath; }
                else host = Regex.Match(u, @"^https?://([^/:?#]+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
                urller.Add(new(id, u, sema, host, port, yol is "" or "/" ? null : yol, i + 1, anahtar));
            }
            foreach (Match m in _wcExe.Matches(s))
            {
                if (exeler.Count >= 300) break;
                // ".exe"den geriye ayraca kadar (boşluklu yollar dahil: C:\Program Files\x\y.exe)
                int bas = m.Index;
                while (bas > 0 && "\"'<>=|,;(\t".IndexOf(s[bas - 1]) < 0) bas--;
                var exe = s[bas..(m.Index + m.Length)].Trim();
                if (exe.Length <= 4) continue;
                exeler.Add(new(id, exe.Length > 300 ? "…" + exe[^300..] : exe, i + 1, Kisa(s.Trim(), 400)));
            }
        }
    }

    static string Kisa(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // Varsayılan zafiyet göstergeleri (kullanıcının e-postası): idunno kesin zafiyetli (düzeltilmiş sürüm yok), <add …anticsrf olası, csrfsettings ek kontrol.
    // Ucmdb:ZafiyetKurallari [{Ad, Desen, Regex, BuyukKucuk, Derece (kesin | olasi | kontrol), Aciklama}] verilirse onlar.
    internal static readonly WcKural[] VarsayilanKurallar =
    [
        new() { Ad = "idunno", Desen = "idunno", Derece = "kesin", Aciklama = "Idunno (AntiCsrf) — kesin zafiyetli, düzeltilmiş sürüm yok" },
        new() { Ad = "<add …anticsrf", Desen = @"<add[^>]*anticsrf", Regex = true, Derece = "olasi", Aciklama = "AntiCSRF modülü kaydı — zafiyetli olabilir (modül başka uygulamaya gömülü olabilir)" },
        new() { Ad = "csrfsettings", Desen = "csrfsettings", Derece = "kontrol", Aciklama = "csrfSettings bölümü — ek kontrol (modül başka config'te olabilir)" },
    ];
    public List<WcKural> Kurallar() => (C.GetSection("ZafiyetKurallari").Get<WcKural[]>() is { Length: > 0 } k ? k : VarsayilanKurallar)
        .Where(k => !string.IsNullOrWhiteSpace(k.Desen)).Select(k => { if (string.IsNullOrWhiteSpace(k.Ad)) k.Ad = k.Desen; k.Derece = (k.Derece ?? "kontrol").Trim().ToLowerInvariant(); return k; }).ToList();

    // düz metin → Regex.Escape; tüm metinde aranır (çok satırlı <add …> da yakalanır), sonuç satır numarasıyla
    internal static List<(string Ad, Regex R)> WcDesen(IEnumerable<string> terimler, bool buyukKucuk, bool regex)
    {
        var l = new List<(string, Regex)>();
        foreach (var t in terimler)
        {
            try { l.Add((t, new Regex(regex ? t : System.Text.RegularExpressions.Regex.Escape(t), (buyukKucuk ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))); }
            catch (ArgumentException ex) { throw new InvalidOperationException($"Geçersiz düzenli ifade '{t}': {ex.Message}"); }
        }
        return l;
    }

    static int[] SatirBaslari(string metin)
    {
        var l = new List<int> { 0 };
        for (int i = 0; i < metin.Length; i++) if (metin[i] == '\n') l.Add(i + 1);
        return l.ToArray();
    }
    static int SatirNo(int[] bas, int idx) { int k = Array.BinarySearch(bas, idx); return (k >= 0 ? k : ~k - 1) + 1; }
    static string SatirMetni(string metin, int[] bas, int satir)
    {
        int s0 = bas[satir - 1], s1 = satir < bas.Length ? bas[satir] - 1 : metin.Length;
        return metin[s0..Math.Max(s0, s1)].Trim();
    }

    // eşleşme başına (desen, satır) bir kez; çok satırlı eşleşmede başladığı satır, metin eşleşmenin kendisi (uzunsa kısaltılmış) satırdan kısaysa satır
    static IEnumerable<(int Satir, string Metin)> Eslesmeler(string metin, int[] bas, Regex r, int max)
    {
        var gorulen = new HashSet<int>();
        Match m;
        try { m = r.Match(metin); } catch (RegexMatchTimeoutException) { yield break; }
        while (m.Success && gorulen.Count < max)
        {
            int sn = SatirNo(bas, m.Index);
            if (gorulen.Add(sn))
            {
                var sat = SatirMetni(metin, bas, sn);
                var mt = m.Value.Contains('\n') ? Regex.Replace(m.Value.Trim(), @"\s+", " ") : sat;
                yield return (sn, Kisa(mt, 400));
            }
            try { m = m.NextMatch(); } catch (RegexMatchTimeoutException) { yield break; }
        }
    }

    internal static List<WcEslesme> WcAra(string id, string metin, List<(string Ad, Regex R)> desenler, int max)
    {
        var bas = SatirBaslari(metin);
        var l = new List<WcEslesme>();
        foreach (var (ad, r) in desenler)
            foreach (var (sn, mt) in Eslesmeler(metin, bas, r, max - l.Count)) l.Add(new(id, ad, sn, mt));
        return l.OrderBy(x => x.Satir).Take(max).ToList();
    }

    internal static List<WcBulgu> WcKuralAra(string id, string metin, List<(WcKural K, Regex R)> kurallar)
    {
        var bas = SatirBaslari(metin);
        var l = new List<WcBulgu>();
        foreach (var (k, r) in kurallar)
            foreach (var (sn, mt) in Eslesmeler(metin, bas, r, 50)) l.Add(new(id, k.Ad, k.Derece, sn, mt));
        return l.OrderBy(x => DereceSira(x.Derece)).ThenBy(x => x.Satir).ToList();
    }
    internal static int DereceSira(string d) => d switch { "kesin" => 0, "olasi" => 1, "kontrol" => 2, _ => 3 };
    internal static string DereceAd(string d) => d switch { "kesin" => "Kesin zafiyetli", "olasi" => "Zafiyetli olabilir", "kontrol" => "Ek kontrol gerekli", _ => d };

    // Yeni arama: Oracle'a gitmeden işin bellekteki maskeli metinlerinde
    public (List<WcEslesme> Eslesmeler, Dictionary<string, int> TerimSayi, int Belge, int Aranan, bool Kesik) WcYenidenAra(WcIs j, List<string>? aramalar, bool buyukKucuk, bool regex = false)
    {
        var t = Terimler(aramalar);
        var d = WcDesen(t, buyukKucuk, regex);
        var l = new List<WcEslesme>();
        int max = C.GetValue("WebConfigMaxEslesme", 20000), aranan = 0;
        bool kesik = false;
        foreach (var (id, gz) in j.Icerik)
        {
            aranan++;
            if (t.Count == 0) break;
            var es = WcAra(id, Ac(gz), d, 200);
            if (l.Count + es.Count > max) { kesik = true; l.AddRange(es.Take(max - l.Count)); break; }
            l.AddRange(es);
        }
        var sira = j.Belgeler.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        l = l.OrderBy(x => sira.GetValueOrDefault(x.Belge, int.MaxValue)).ThenBy(x => x.Satir).ToList();
        var say = t.ToDictionary(x => x, x => l.Count(e => e.Terim == x));
        return (l, say, l.Select(x => x.Belge).Distinct().Count(), aranan, kesik || j.IcerikKesik);
    }

    // Excel: Özet, web.config'ler, adresler (benzersiz + tüm satırlar), exe (benzersiz + tüm), arama (terim verilirse yeniden aranır)
    public byte[] WcExcel(WcIs j, List<string>? aramalar, bool buyukKucuk, bool regex = false)
    {
        List<WcBelge> belgeler; List<WcUrl> urller; List<WcExe> exeler; List<WcEslesme> es; List<WcBulgu> bulgular;
        lock (j) { belgeler = [.. j.Belgeler]; urller = [.. j.Urller]; exeler = [.. j.Exeler]; es = [.. j.Eslesmeler]; bulgular = [.. j.Bulgular]; }
        var terim = Terimler(aramalar);
        if (terim.Count > 0) es = WcYenidenAra(j, terim, buyukKucuk, regex).Eslesmeler; else terim = j.Aramalar;
        var bOf = belgeler.ToDictionary(b => b.Id);
        WcBelge? B(string id) => bOf.GetValueOrDefault(id);
        static object? K(string? x) => x == null ? null : x.Length > 32000 ? x[..32000] + "…" : x;
        string Sunucular(IEnumerable<string> idler) => string.Join(" | ", idler.Select(B).Select(b => b?.Sunucu ?? b?.Ip ?? "?").Distinct().Take(50));
        var ozet = new List<object?[]>
        {
            new object?[] { "Alan", "Değer" },
            new object?[] { "Kapsam", j.Kapsam }, new object?[] { "Başlatan", j.Kullanici }, new object?[] { "Başlangıç", j.Basladi.ToLocalTime().ToString("dd.MM.yyyy HH:mm") },
            new object?[] { "Durum", j.Bitti ? (j.Hata ?? "tamamlandı") : $"sürüyor ({j.Okunan} / {j.Toplam})" },
            new object?[] { "Taranan dosya", belgeler.Count }, new object?[] { "Sunucu", belgeler.Select(b => b.Sunucu ?? b.Ip).Distinct().Count() },
            new object?[] { "http / https adresi (benzersiz)", urller.Select(u => u.Url.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "  bunların http (şifresiz) olanı", urller.Where(u => u.Sema == "http").Select(u => u.Url.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "exe geçen yer (benzersiz)", exeler.Select(x => x.Exe.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "Aranan metinler", string.Join(" | ", terim) }, new object?[] { "Arama eşleşmesi", es.Count },
            new object?[] { "Not", "Şifre / anahtar / token değerleri maskelidir (***)." },
            new object?[] { "Not", "Yalnız UCMDB'nin topladığı dosyalar taranır; UCMDB'de olmayan dosya (ör. machine.config) taranmamıştır. Modül yalnız bir DLL içindeyse config taramasıyla bulunamaz." },
        };
        foreach (var t in TurlerHepsi) ozet.Add(new object?[] { "Dosya: " + t, $"{belgeler.Count(b => b.Tur == t)} tarandı · kapsamda {j.KapsamTur.GetValueOrDefault(t)}" });
        foreach (var (k, _) in j.Kurallar)
        {
            var kb = bulgular.Where(x => x.Kural == k.Ad).ToList();
            ozet.Add(new object?[] { $"Zafiyet: {k.Ad} ({DereceAd(k.Derece)})", $"{kb.Count} satır · {kb.Select(x => x.Belge).Distinct().Count()} dosya · {kb.Select(x => B(x.Belge)?.Sunucu ?? B(x.Belge)?.Ip).Distinct().Count()} sunucu" });
        }
        if (j.BelgesizIpler.Count > 0) ozet.Add(new object?[] { "UCMDB'de config'i olmayan IP", string.Join(", ", j.BelgesizIpler) });
        var zaf = new List<object?[]> { new object?[] { "DERECE", "KURAL", "AÇIKLAMA", "SUNUCU", "IP", "SEGMENT", "DOSYA TÜRÜ", "DOSYA YOLU", "SATIR", "EŞLEŞEN SATIR" } };
        var kAc = j.Kurallar.ToDictionary(x => x.K.Ad, x => x.K.Aciklama);
        zaf.AddRange(bulgular.OrderBy(x => DereceSira(x.Derece)).ThenBy(x => B(x.Belge)?.Sunucu).ThenBy(x => x.Satir).Select(x => { var b = B(x.Belge);
            return new object?[] { DereceAd(x.Derece), x.Kural, kAc.GetValueOrDefault(x.Kural), b?.Sunucu, b?.Ip, b?.Segment, b?.Tur, K(b?.Yol ?? b?.Ad), x.Satir, K(x.Metin) }; }));
        if (bulgular.Count == 0) zaf.Add(new object?[] { "—", "Taranan dosyalarda zafiyet göstergesi bulunamadı" });
        var sun = new List<object?[]> { new object?[] { "SUNUCU", "IP", "SEGMENT", "WEB.CONFIG", "APPLICATIONHOST.CONFIG", "MACHINE.CONFIG", "DİĞER .CONFIG", "KESİN", "OLASI", "KONTROL", "EN YÜKSEK DERECE" } };
        sun.AddRange(belgeler.GroupBy(b => b.Sunucu ?? b.Ip ?? "(sunucusu bilinmeyen)").OrderBy(g => g.Key).Select(g =>
        {
            var ids = g.Select(b => b.Id).ToHashSet();
            var bl = bulgular.Where(x => ids.Contains(x.Belge)).ToList();
            int N(string d) => bl.Count(x => x.Derece == d);
            return new object?[] { g.Key, string.Join(" | ", g.Select(b => b.Ip).OfType<string>().Distinct()), g.Select(b => b.Segment).FirstOrDefault(x => x != null),
                g.Count(b => b.Tur == "web.config"), g.Any(b => b.Tur == "applicationHost.config") ? "var" : "UCMDB'de yok", g.Any(b => b.Tur == "machine.config") ? "var" : "UCMDB'de yok",
                g.Count(b => b.Tur == "diğer"), N("kesin"), N("olasi"), N("kontrol"), bl.Count == 0 ? "—" : DereceAd(bl.MinBy(x => DereceSira(x.Derece))!.Derece) };
        }));
        var bel = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "UCMDB IP'LERİ", "IP EŞLEŞMESİ", "DOSYA TÜRÜ", "DOSYA ADI", "DOSYA YOLU", "BOYUT", "BİÇİM", "ZAFİYET", "HTTP/HTTPS", "EXE", "ARAMA", "MASKELENEN", "NOT" } };
        bel.AddRange(belgeler.Select(b => new object?[] { b.Ip, b.Sunucu, b.Segment, string.Join(" | ", b.Ipler), b.Eslesme, b.Tur, b.Ad, K(b.Yol ?? b.Ad), b.Boyut, b.Bicim, b.Bulgu, b.Url, b.Exe, b.Eslesen, b.Maske, b.Hata }));
        var uTek = new List<object?[]> { new object?[] { "ADRES", "ŞEMA", "HOST", "PORT", "YOL", "DOSYA SAYISI", "SUNUCULAR" } };
        uTek.AddRange(urller.GroupBy(u => u.Url.ToLowerInvariant()).OrderByDescending(g => g.Select(x => x.Belge).Distinct().Count()).ThenBy(g => g.Key)
            .Select(g => { var u = g.First(); return new object?[] { K(u.Url), u.Sema, u.Host, u.Port, K(u.Yol), g.Select(x => x.Belge).Distinct().Count(), Sunucular(g.Select(x => x.Belge).Distinct()) }; }));
        var uTum = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "DOSYA", "SATIR", "ANAHTAR", "ŞEMA", "HOST", "PORT", "ADRES" } };
        uTum.AddRange(urller.Select(u => { var b = B(u.Belge); return new object?[] { b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), u.Satir, u.Anahtar, u.Sema, u.Host, u.Port, K(u.Url) }; }));
        var eTek = new List<object?[]> { new object?[] { "EXE", "DOSYA SAYISI", "SUNUCULAR", "ÖRNEK SATIR" } };
        eTek.AddRange(exeler.GroupBy(x => x.Exe.ToLowerInvariant()).OrderByDescending(g => g.Select(x => x.Belge).Distinct().Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { K(g.First().Exe), g.Select(x => x.Belge).Distinct().Count(), Sunucular(g.Select(x => x.Belge).Distinct()), K(g.First().Metin) }));
        var eTum = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "DOSYA", "SATIR", "EXE", "SATIR METNİ" } };
        eTum.AddRange(exeler.Select(x => { var b = B(x.Belge); return new object?[] { b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), x.Satir, K(x.Exe), K(x.Metin) }; }));
        var ara = new List<object?[]> { new object?[] { "ARANAN", "IP", "SUNUCU", "SEGMENT", "DOSYA", "SATIR", "SATIR METNİ" } };
        ara.AddRange(es.Select(x => { var b = B(x.Belge); return new object?[] { x.Terim, b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), x.Satir, K(x.Metin) }; }));
        return Xlsx.Yaz([("Özet", ozet), ("Zafiyet", zaf), ("Sunucu özeti", sun), ("Dosyalar", bel), ("Adresler (benzersiz)", uTek), ("Adresler (tüm satırlar)", uTum),
            ("EXE (benzersiz)", eTek), ("EXE (tüm satırlar)", eTum), ("Arama", ara)]);
    }

    // TANI: UCMDB tablosunda hangi config dosyaları var (A_NAME'e göre sayım + yolunda applicationhost / machine.config geçenler) ve dizindeki türler.
    // Yalnız sayım (içerik okunmaz); sonuç 1 saat bellekte.
    (DateTime Z, object Sonuc)? _turSayim;
    public async Task<object> DosyaTurleriAsync(bool yenile, CancellationToken ct)
    {
        if (!yenile && _turSayim is { } o && (DateTime.UtcNow - o.Z).TotalMinutes < 60) return o.Sonuc;
        using var c = await AcAsync(ct);
        var s = await SemaAsync(c, ct);
        var adlar = new List<object>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT AD, N FROM (SELECT LOWER({s.AdK}) AD, COUNT(*) N FROM {Sema}.{Tablo} WHERE LOWER({s.AdK}) LIKE '%config%' GROUP BY LOWER({s.AdK}) ORDER BY 2 DESC) WHERE ROWNUM <= 150";
            cmd.CommandTimeout = Math.Max(SorguSn, 300);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) adlar.Add(new { ad = Deger(r.GetValue(0)), sayi = Convert.ToInt64(r.GetValue(1)), tur = DosyaTuru(Deger(r.GetValue(0))) });
        }
        // adı farklı kaydedilmiş olabilir: yol kolonlarında ara
        var yolda = new List<object>();
        foreach (var (ad, desen) in new[] { ("applicationHost.config", "%applicationhost%"), ("machine.config", "%machine.config%") })
        {
            if (s.Yol.Count == 0) break;
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {Sema}.{Tablo} WHERE {string.Join(" OR ", s.Yol.Select(y => $"LOWER({y}) LIKE '{desen}'").Append($"LOWER({s.AdK}) LIKE '{desen}'"))}";
            cmd.CommandTimeout = Math.Max(SorguSn, 300);
            yolda.Add(new { ad, sayi = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) });
        }
        var dz = _dizin;
        var sonuc = new
        {
            zaman = DateTime.Now, tablo = $"{Sema}.{Tablo}", adKolonu = s.AdK, yolKolonlari = s.Yol, dizinDesenleri = DizinDesenleri, adlar, yolda,
            dizin = dz == null ? null : new
            {
                belge = dz.Belgeler.Count, zaman = dz.Zaman.ToLocalTime(),
                turler = TurlerHepsi.Select(t => { var l = dz.Belgeler.Where(b => DosyaTuru(b.Ad) == t).ToList();
                    return new { tur = t, belge = l.Count, sunucusu = l.Count(b => b.Sunucu != null), ipli = l.Count(b => b.Ipler != null),
                        sunucu = l.Select(b => b.Sunucu).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count() }; }).ToList()
            }
        };
        _turSayim = (DateTime.UtcNow, sonuc);
        return sonuc;
    }

    static byte[] Sikistir(string s)
    {
        using var ms = new MemoryStream();
        using (var g = new GZipStream(ms, CompressionLevel.Fastest, true)) { var b = Encoding.UTF8.GetBytes(s); g.Write(b, 0, b.Length); }
        return ms.ToArray();
    }
    static string Ac(byte[] gz)
    {
        using var g = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress);
        using var sr = new StreamReader(g, Encoding.UTF8);
        return sr.ReadToEnd();
    }
}
