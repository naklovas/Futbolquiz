using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

// web.config taraması (wwwroot/webconfig.html; kullanıcı: "verdiğim IP'lerde ya da segmentlerde ya da tüm ağda web.config taraması; text arayacağız; ayrıca içinde
// http / https geçen adresleri ve exe geçen yerleri her zaman listelesin"). UCMDB dizinindeki belgeler kapsama göre seçilir, içerikler Oracle'dan yüzerli okunur,
// çözülür ve MASKELENİR (şifre / anahtar / token hiçbir listeye ve aramaya girmez); maskeli metin gzip'li bellekte tutulur → yeni arama Oracle'ı yeniden taramaz.
record WcBelge(string Id, string Ad, string? Yol, string? Sunucu, string? Ip, List<string> Ipler, string? Segment, string? Eslesme, int Boyut, string Bicim, int Maske,
    int Url, int Exe, int Eslesen, string? Hata);
record WcUrl(string Belge, string Url, string Sema, string Host, string? Port, string? Yol, int Satir, string? Anahtar);
record WcExe(string Belge, string Exe, int Satir, string Metin);
record WcEslesme(string Belge, string Terim, int Satir, string Metin);
// Dmz: Nessus asset'indeki IP'ler (Asset boşsa Zafiyet:DmzAsset — açıklık taramasıyla aynı DMZ tanımı)
record WcTaramaIstek(List<string>? Ips, List<string>? Segmentler, bool? Tum, List<string>? Aramalar, bool? Dmz = null, string? Asset = null);
record WcAramaIstek(List<string>? Aramalar, bool? BuyukKucuk);

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
    (List<(UcmdbBelge B, string? Ip, string? Segment)> Liste, string Ozet) WcKapsam(WcTaramaIstek req, EnvanterSnapshot env, Func<string, bool>? dmz = null, string? dmzAd = null)
    {
        var dz = _dizin ?? throw new InvalidOperationException("UCMDB web.config listesi henüz hazır değil.");
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
            return (sonuc, $"tüm ağ ({dz.Belgeler.Count} web.config)");
        }
        var ozet = new List<string>();
        var ips = (req.Ips ?? []).Select(x => LookupQuery.TryParseIpv4(x?.Trim() ?? "", out string v) ? v : null).OfType<string>().Distinct().ToList();
        if (ips.Count > 0)
        {
            foreach (var (ip, l) in Belgeler(ips, env)) foreach (var b in l) sonuc.Add((b, ip, Seg(ip)));
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
        var j = new WcIs { Kullanici = kullanici, Aramalar = Terimler(req.Aramalar) };
        _wcIsler[j.Id] = j;
        _ = Task.Run(async () =>
        {
            try
            {
                j.Asama = "UCMDB web.config listesi bekleniyor";
                DizinDurumu();
                if (_dizin == null) await DizinAsync(false, j.Cts.Token);
                var env = await envanter.GetAsync(false, j.Cts.Token);
                var (liste, ozet) = WcKapsam(req, env, dmz, dmzAd);
                j.Kapsam = ozet; j.Toplam = liste.Count;
                if (liste.Count == 0) { j.Mesajlar.Add("Kapsamda UCMDB'de web.config bulunamadı."); return; }
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
                var urller = new List<WcUrl>(); var exeler = new List<WcExe>(); var es = new List<WcEslesme>();
                if (!bayt.TryGetValue(b.Id, out var by))
                    belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, 0, "boş", 0, 0, 0, 0, "içerik yok ya da Ucmdb:MaxBoyut'tan büyük");
                else
                {
                    var (metin0, bicim) = Coz(by, b.Ad);
                    if (metin0 == null) belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, by.Length, bicim, 0, 0, 0, 0, "içerik çözülemedi");
                    else
                    {
                        var (metin, maske) = Maskele(metin0);
                        WcCikar(b.Id, metin, urller, exeler);
                        if (j.Aramalar.Count > 0) es = WcAra(b.Id, metin, j.Aramalar, false, 200);
                        if (Interlocked.Read(ref j.IcerikBayt) < maxBayt)
                        {
                            var gz = Sikistir(metin);
                            j.Icerik[b.Id] = gz; Interlocked.Add(ref j.IcerikBayt, gz.Length);
                        }
                        else j.IcerikKesik = true;
                        belge = new(b.Id, b.Ad, b.Yol, b.Sunucu, ip, b.Ipler ?? [], seg, b.Eslesme, by.Length, bicim, maske, urller.Count, exeler.Count, es.Count, null);
                    }
                }
                lock (j) { j.Belgeler.Add(belge); j.Urller.AddRange(urller); j.Exeler.AddRange(exeler); j.Eslesmeler.AddRange(es); }
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

    internal static List<WcEslesme> WcAra(string id, string metin, List<string> terimler, bool buyukKucuk, int max)
    {
        var l = new List<WcEslesme>();
        var kar = buyukKucuk ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var satirlar = metin.Split('\n');
        for (int i = 0; i < satirlar.Length && l.Count < max; i++)
            foreach (var t in terimler)
                if (satirlar[i].Contains(t, kar)) { l.Add(new(id, t, i + 1, Kisa(satirlar[i].Trim(), 400))); break; }
        return l;
    }

    // Yeni arama: Oracle'a gitmeden işin bellekteki maskeli metinlerinde
    public (List<WcEslesme> Eslesmeler, Dictionary<string, int> TerimSayi, int Belge, int Aranan, bool Kesik) WcYenidenAra(WcIs j, List<string>? aramalar, bool buyukKucuk)
    {
        var t = Terimler(aramalar);
        var l = new List<WcEslesme>();
        int max = C.GetValue("WebConfigMaxEslesme", 20000), aranan = 0;
        bool kesik = false;
        foreach (var (id, gz) in j.Icerik)
        {
            aranan++;
            if (t.Count == 0) break;
            var es = WcAra(id, Ac(gz), t, buyukKucuk, 200);
            if (l.Count + es.Count > max) { kesik = true; l.AddRange(es.Take(max - l.Count)); break; }
            l.AddRange(es);
        }
        var sira = j.Belgeler.Select((b, i) => (b.Id, i)).ToDictionary(x => x.Id, x => x.i);
        l = l.OrderBy(x => sira.GetValueOrDefault(x.Belge, int.MaxValue)).ThenBy(x => x.Satir).ToList();
        var say = t.ToDictionary(x => x, x => l.Count(e => e.Terim == x));
        return (l, say, l.Select(x => x.Belge).Distinct().Count(), aranan, kesik || j.IcerikKesik);
    }

    // Excel: Özet, web.config'ler, adresler (benzersiz + tüm satırlar), exe (benzersiz + tüm), arama (terim verilirse yeniden aranır)
    public byte[] WcExcel(WcIs j, List<string>? aramalar, bool buyukKucuk)
    {
        List<WcBelge> belgeler; List<WcUrl> urller; List<WcExe> exeler; List<WcEslesme> es;
        lock (j) { belgeler = [.. j.Belgeler]; urller = [.. j.Urller]; exeler = [.. j.Exeler]; es = [.. j.Eslesmeler]; }
        var terim = Terimler(aramalar);
        if (terim.Count > 0) es = WcYenidenAra(j, terim, buyukKucuk).Eslesmeler; else terim = j.Aramalar;
        var bOf = belgeler.ToDictionary(b => b.Id);
        WcBelge? B(string id) => bOf.GetValueOrDefault(id);
        static object? K(string? x) => x == null ? null : x.Length > 32000 ? x[..32000] + "…" : x;
        string Sunucular(IEnumerable<string> idler) => string.Join(" | ", idler.Select(B).Select(b => b?.Sunucu ?? b?.Ip ?? "?").Distinct().Take(50));
        var ozet = new List<object?[]>
        {
            new object?[] { "Alan", "Değer" },
            new object?[] { "Kapsam", j.Kapsam }, new object?[] { "Başlatan", j.Kullanici }, new object?[] { "Başlangıç", j.Basladi.ToLocalTime().ToString("dd.MM.yyyy HH:mm") },
            new object?[] { "Durum", j.Bitti ? (j.Hata ?? "tamamlandı") : $"sürüyor ({j.Okunan} / {j.Toplam})" },
            new object?[] { "web.config", belgeler.Count }, new object?[] { "Sunucu", belgeler.Select(b => b.Sunucu ?? b.Ip).Distinct().Count() },
            new object?[] { "http / https adresi (benzersiz)", urller.Select(u => u.Url.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "  bunların http (şifresiz) olanı", urller.Where(u => u.Sema == "http").Select(u => u.Url.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "exe geçen yer (benzersiz)", exeler.Select(x => x.Exe.ToLowerInvariant()).Distinct().Count() },
            new object?[] { "Aranan metinler", string.Join(" | ", terim) }, new object?[] { "Arama eşleşmesi", es.Count },
            new object?[] { "Not", "Şifre / anahtar / token değerleri maskelidir (***)." },
        };
        var bel = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "UCMDB IP'LERİ", "IP EŞLEŞMESİ", "WEB.CONFIG", "BOYUT", "BİÇİM", "HTTP/HTTPS", "EXE", "ARAMA", "MASKELENEN", "NOT" } };
        bel.AddRange(belgeler.Select(b => new object?[] { b.Ip, b.Sunucu, b.Segment, string.Join(" | ", b.Ipler), b.Eslesme, K(b.Yol ?? b.Ad), b.Boyut, b.Bicim, b.Url, b.Exe, b.Eslesen, b.Maske, b.Hata }));
        var uTek = new List<object?[]> { new object?[] { "ADRES", "ŞEMA", "HOST", "PORT", "YOL", "WEB.CONFIG SAYISI", "SUNUCULAR" } };
        uTek.AddRange(urller.GroupBy(u => u.Url.ToLowerInvariant()).OrderByDescending(g => g.Select(x => x.Belge).Distinct().Count()).ThenBy(g => g.Key)
            .Select(g => { var u = g.First(); return new object?[] { K(u.Url), u.Sema, u.Host, u.Port, K(u.Yol), g.Select(x => x.Belge).Distinct().Count(), Sunucular(g.Select(x => x.Belge).Distinct()) }; }));
        var uTum = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "WEB.CONFIG", "SATIR", "ANAHTAR", "ŞEMA", "HOST", "PORT", "ADRES" } };
        uTum.AddRange(urller.Select(u => { var b = B(u.Belge); return new object?[] { b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), u.Satir, u.Anahtar, u.Sema, u.Host, u.Port, K(u.Url) }; }));
        var eTek = new List<object?[]> { new object?[] { "EXE", "WEB.CONFIG SAYISI", "SUNUCULAR", "ÖRNEK SATIR" } };
        eTek.AddRange(exeler.GroupBy(x => x.Exe.ToLowerInvariant()).OrderByDescending(g => g.Select(x => x.Belge).Distinct().Count()).ThenBy(g => g.Key)
            .Select(g => new object?[] { K(g.First().Exe), g.Select(x => x.Belge).Distinct().Count(), Sunucular(g.Select(x => x.Belge).Distinct()), K(g.First().Metin) }));
        var eTum = new List<object?[]> { new object?[] { "IP", "SUNUCU", "SEGMENT", "WEB.CONFIG", "SATIR", "EXE", "SATIR METNİ" } };
        eTum.AddRange(exeler.Select(x => { var b = B(x.Belge); return new object?[] { b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), x.Satir, K(x.Exe), K(x.Metin) }; }));
        var ara = new List<object?[]> { new object?[] { "ARANAN", "IP", "SUNUCU", "SEGMENT", "WEB.CONFIG", "SATIR", "SATIR METNİ" } };
        ara.AddRange(es.Select(x => { var b = B(x.Belge); return new object?[] { x.Terim, b?.Ip, b?.Sunucu, b?.Segment, K(b?.Yol ?? b?.Ad), x.Satir, K(x.Metin) }; }));
        return Xlsx.Yaz([("Özet", ozet), ("web.config", bel), ("Adresler (benzersiz)", uTek), ("Adresler (tüm satırlar)", uTum),
            ("EXE (benzersiz)", eTek), ("EXE (tüm satırlar)", eTum), ("Arama", ara)]);
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
