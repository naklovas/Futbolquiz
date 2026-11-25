using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Oracle.ManagedDataAccess.Client;

// UCMDB (Micro Focus) yapılandırma belgeleri: MFUCMDB.CDM_CONFIGURATION_DOCUMENT_1 (Oracle) içindeki web.config'ler → servisin kendi adresleri (gelen),
// çağırdığı servisler (giden: WCF client endpoint, appSettings URL'leri, rewrite hedefleri) ve bağlandığı DB sunucuları (connectionStrings; YALNIZ sunucu /
// DB adı — şifre, kullanıcı, machineKey hiçbir yere yazılmaz). DeltaFlow yalnız OKUR. Şema UCMDB sürümüne göre değişebildiği için kolonlar ALL_TAB_COLUMNS'tan
// bulunur; belgenin hangi sunucuya ait olduğu kapsayıcı zinciri (root_container ya da composition / containment bağları) yukarı yürünerek bulunur.
record UcmdbAdres(string Tur, string Anahtar, string Adres, string? Host, string? Port, string? Yol, string Dosya);
record UcmdbBelge(string Id, string Ad, string? Sunucu, List<string> Zincir);
record UcmdbSonuc(List<UcmdbBelge> Belgeler, List<UcmdbAdres> Adresler, List<string> Notlar);

sealed class UcmdbService(IConfiguration cfg, ILogger<UcmdbService> log)
{
    IConfigurationSection C => cfg.GetSection("Ucmdb");
    public bool Acik => !string.IsNullOrWhiteSpace(C["Baglanti"]);
    string Sema => Ad(C["Sema"] ?? "MFUCMDB");
    string Tablo => Ad(C["BelgeTablosu"] ?? "CDM_CONFIGURATION_DOCUMENT_1");
    int SorguSn => Math.Clamp(C.GetValue("SorguSaniye", 60), 5, 600);

    static string Ad(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9_$#]{1,128}$") ? s.ToUpperInvariant() : throw new InvalidOperationException("Geçersiz Oracle adı: " + s);
    static readonly Regex _ipRx = new(@"^\d{1,3}(\.\d{1,3}){3}$");

    // ---------- şema ----------
    sealed class Sema0
    {
        public Dictionary<string, Dictionary<string, string>> Kolon = new(StringComparer.OrdinalIgnoreCase);   // tablo → kolon → tip
        public HashSet<string> Bos = new(StringComparer.OrdinalIgnoreCase);                                     // NUM_ROWS = 0
        public string Id = "CMDB_ID", AdK = "A_NAME", Icerik = "";
        public string? Ust, Yol;
        public List<string> Bag = [];   // END1_ID / END2_ID tabloları (composition, containment)
    }
    Sema0? _sema;
    readonly SemaphoreSlim _kilit = new(1, 1);

    void Hazirla()
    {
        if (C["WalletKlasoru"] is { Length: > 0 } w) { OracleConfiguration.WalletLocation = w; OracleConfiguration.TnsAdmin ??= w; }
        if (C["TnsAdmin"] is { Length: > 0 } t) OracleConfiguration.TnsAdmin = t;
    }

    async Task<OracleConnection> AcAsync(CancellationToken ct)
    {
        Hazirla();
        var c = new OracleConnection(C["Baglanti"]);
        await c.OpenAsync(ct);
        return c;
    }

    async Task<Sema0> SemaAsync(OracleConnection c, CancellationToken ct)
    {
        if (_sema != null) return _sema;
        var s = new Sema0();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE FROM ALL_TAB_COLUMNS WHERE OWNER = :o AND TABLE_NAME LIKE 'CDM%'";
            cmd.Parameters.Add("o", Sema); cmd.CommandTimeout = SorguSn;
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string t = r.GetString(0), k = r.GetString(1);
                if (!s.Kolon.TryGetValue(t, out var d)) s.Kolon[t] = d = new(StringComparer.OrdinalIgnoreCase);
                d[k] = r.IsDBNull(2) ? "" : r.GetString(2);
            }
        }
        try
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT TABLE_NAME FROM ALL_TABLES WHERE OWNER = :o AND NUM_ROWS = 0";
            cmd.Parameters.Add("o", Sema); cmd.CommandTimeout = SorguSn;
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) s.Bos.Add(r.GetString(0));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug(ex, "ALL_TABLES okunamadı"); }
        if (!s.Kolon.TryGetValue(Tablo, out var bk))
            throw new InvalidOperationException($"{Sema}.{Tablo} bulunamadı ya da bu kullanıcının okuma yetkisi yok (ALL_TAB_COLUMNS'ta görünmüyor).");
        s.Id = C["IdKolonu"] is { Length: > 0 } ik ? Ad(ik) : "CMDB_ID";
        s.AdK = C["AdKolonu"] is { Length: > 0 } ak ? Ad(ak) : "A_NAME";
        s.Icerik = C["IcerikKolonu"] is { Length: > 0 } ic ? Ad(ic)
            : bk.Where(kv => kv.Key.StartsWith("A_DOCUMENT_CONTEN") && kv.Value is "BLOB" or "LONG RAW" or "RAW" or "CLOB").Select(kv => kv.Key).FirstOrDefault()
              ?? bk.Where(kv => kv.Value == "BLOB").Select(kv => kv.Key).FirstOrDefault()
              ?? throw new InvalidOperationException($"{Tablo} içinde BLOB içerik kolonu bulunamadı; Ucmdb:IcerikKolonu verin.");
        s.Ust = C["UstKolonu"] is { Length: > 0 } uk ? Ad(uk) : new[] { "A_ROOT_CONTAINER", "A_CONTAINER" }.FirstOrDefault(bk.ContainsKey);
        s.Yol = bk.Keys.FirstOrDefault(k => Regex.IsMatch(k, @"^A_.*(PATH|LOCATION)", RegexOptions.IgnoreCase) && !k.Contains("CONTEN"));
        foreach (var k in new[] { s.Id, s.AdK, s.Icerik })
            if (!bk.ContainsKey(k)) throw new InvalidOperationException($"{Tablo}.{k} kolonu yok (Ucmdb ayarlarını kontrol edin).");
        s.Bag = s.Kolon.Where(t => t.Value.ContainsKey("END1_ID") && t.Value.ContainsKey("END2_ID") && Regex.IsMatch(t.Key, "COMPOSITION|CONTAINMENT|CONTAIN|COMPOSIT", RegexOptions.IgnoreCase)
            && !s.Bos.Contains(t.Key)).Select(t => t.Key).ToList();
        return _sema = s;
    }

    // RAW kimlik → HEXTORAW('…'), metin → '…', sayı olduğu gibi (değerler veritabanından okunmuş ya da onaltılık doğrulanmış)
    static string Lit(string tip, string id) => tip.Contains("RAW") ? $"HEXTORAW('{id}')" : tip is "NUMBER" or "FLOAT" ? (Regex.IsMatch(id, @"^-?\d+(\.\d+)?$") ? id : "NULL") : "'" + id.Replace("'", "''") + "'";
    static string Deger(object v) => v switch { byte[] b => Convert.ToHexString(b), DBNull => "", _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
    static bool IdGecerli(string s) => Regex.IsMatch(s, @"^[A-Za-z0-9_\-\.]{1,64}$");

    // ---------- dizin: belge → sunucu adı ----------
    sealed class Dizin
    {
        public DateTime Zaman = DateTime.UtcNow;
        public List<UcmdbBelge> Belgeler = [];
        public Dictionary<string, List<UcmdbBelge>> Sunucu = new(StringComparer.OrdinalIgnoreCase);   // kısa ad / tam ad / IP → belgeler
        public List<string> Notlar = [];
    }
    Dizin? _dizin;
    Task? _hazirlik;
    string? _dizinHata;
    readonly ConcurrentDictionary<string, (DateTime Z, string? Metin, string Bicim, int Boyut, int Maske)> _icerik = new();

    // Dizin arka planda hazırlanır (ilk seferde tüm web.config'lerin kapsayıcı zinciri yürünür; IIS istek zaman aşımına takılmasın)
    public (bool Hazir, string? Hata) DizinDurumu()
    {
        var d = _dizin;
        bool eski = d == null || (DateTime.UtcNow - d.Zaman).TotalHours >= C.GetValue("OnbellekSaat", 12.0);
        if (eski && Acik)
            lock (this)
                if (_hazirlik == null || _hazirlik.IsCompleted)
                    _hazirlik = Task.Run(async () =>
                    {
                        try { await DizinAsync(true, CancellationToken.None); _dizinHata = null; }
                        catch (Exception ex) { _dizinHata = ex.Message; log.LogWarning(ex, "UCMDB dizini hazırlanamadı"); }
                    });
        return (d != null, d == null ? _dizinHata : null);
    }

    public async Task<(List<UcmdbBelge> Belgeler, List<string> Notlar)> DizinAsync(bool yenile, CancellationToken ct)
    {
        var d = _dizin;
        if (!yenile && d != null && (DateTime.UtcNow - d.Zaman).TotalHours < C.GetValue("OnbellekSaat", 12.0)) return (d.Belgeler, d.Notlar);
        await _kilit.WaitAsync(ct);
        try
        {
            using var c = await AcAsync(ct);
            var s = await SemaAsync(c, ct);
            _dizin = d = await DizinOlusturAsync(c, s, null, ct);
            return (d.Belgeler, d.Notlar);
        }
        finally { _kilit.Release(); }
    }

    // IP → o sunucunun web.config belgeleri (ERT sunucu adı ya da IP ile; içerik okunmaz)
    public Dictionary<string, List<UcmdbBelge>> Belgeler(IEnumerable<string> ips, EnvanterSnapshot env)
    {
        var dz = _dizin;
        var sonuc = new Dictionary<string, List<UcmdbBelge>>();
        if (dz == null) return sonuc;
        foreach (var ip in ips.Distinct())
        {
            var adlar = env.AppsOnIp(ip).Select(h => h.UnixName).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Append(ip);
            var l = adlar.SelectMany(Anahtarlar).Distinct().SelectMany(a => dz.Sunucu.GetValueOrDefault(a) ?? []).DistinctBy(b => b.Id).OrderBy(b => b.Ad).ToList();
            if (l.Count > 0) sonuc[ip] = l;
        }
        return sonuc;
    }

    // Tek belgenin içeriği (yalnız dizindeki kimlikler; şifre / anahtar değerleri maskelenmiş)
    public async Task<(UcmdbBelge Belge, string? Metin, string Bicim, int Boyut, int Maske)?> BelgeAsync(string id, CancellationToken ct)
    {
        var b = _dizin?.Belgeler.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (b == null) return null;
        if (!_icerik.TryGetValue(b.Id, out var ic) || (DateTime.UtcNow - ic.Z).TotalHours >= C.GetValue("OnbellekSaat", 12.0))
        {
            using var c = await AcAsync(ct);
            var s = await SemaAsync(c, ct);
            var l = await IcerikAsync(c, s, [b.Id], ct);
            if (l.Count == 0) ic = (DateTime.UtcNow, null, "boş", 0, 0);
            else
            {
                var (metin, bicim) = Coz(l[0].Bayt, b.Ad);
                int n = 0;
                if (metin != null) (metin, n) = Maskele(metin);
                ic = (DateTime.UtcNow, metin, bicim, l[0].Bayt.Length, n);
            }
            _icerik[b.Id] = ic;
        }
        return (b, ic.Metin, ic.Bicim, ic.Boyut, ic.Maske);
    }

    // Gösterilen metinde gizli değerler: bağlantı cümlesindeki şifre, şifre / anahtar / token adlı öznitelikler ve appSettings değerleri,
    // machineKey anahtarları, URL'lerdeki kullanıcı:şifre@ ve gizli sorgu parametreleri → ***
    internal static (string Metin, int Sayi) Maskele(string m)
    {
        int n = 0;
        string Say(Match x, string yeni) { n++; return yeni; }
        const string gizli = @"[^""']*(?:pass|pwd|parola|sifre|şifre|secret|token|apikey|api[_\-]?key|privatekey|clientsecret|credential|accesskey|secretkey|sas|signature)[^""']*";
        // bağlantı cümlesi içi: Password=...; Pwd=...
        m = Regex.Replace(m, @"(?i)\b(password|pwd|user password|accountkey|sharedaccesskey)\s*=\s*(?:'[^']*'|""[^""]*""|[^;""'<>]*)", x => Say(x, x.Groups[1].Value + "=***"));
        // <add key="...gizli..." value="..."> (iki sırada da)
        m = Regex.Replace(m, $@"(?i)(<add\b[^>]*?\bkey\s*=\s*[""']{gizli}[""'][^>]*?\bvalue\s*=\s*[""'])[^""']*([""'])", x => Say(x, x.Groups[1].Value + "***" + x.Groups[2].Value));
        m = Regex.Replace(m, $@"(?i)(<add\b[^>]*?\bvalue\s*=\s*[""'])[^""']*([""'][^>]*?\bkey\s*=\s*[""']{gizli}[""'])", x => Say(x, x.Groups[1].Value + "***" + x.Groups[2].Value));
        // gizli adlı öznitelikler: password="..", validationKey="..", decryptionKey="..", clientSecret=".."
        m = Regex.Replace(m, @"(?i)(\b(?:password|pwd|validationKey|decryptionKey|clientSecret|secret|apiKey|accessKey|secretKey|token)\s*=\s*[""'])(?!AutoGenerate)[^""']+([""'])", x => Say(x, x.Groups[1].Value + "***" + x.Groups[2].Value));
        // <setting name="...gizli..."><value>...</value>
        m = Regex.Replace(m, $@"(?is)(<setting\b[^>]*\bname\s*=\s*[""']{gizli}[""'][^>]*>\s*<value>)(.*?)(</value>)", x => Say(x, x.Groups[1].Value + "***" + x.Groups[3].Value));
        // URL kullanıcı:şifre@ ve gizli sorgu parametreleri
        m = Regex.Replace(m, @"(?i)\b((?:https?|ftps?|sftp|net\.tcp)://)[^/@\s""'<>]+:[^/@\s""'<>]+@", x => Say(x, x.Groups[1].Value + "***@"));
        m = Regex.Replace(m, @"(?i)([?&](?:[a-z0-9_\-]*(?:pass|pwd|token|secret|apikey|signature|sig)[a-z0-9_\-]*)=)[^&""'<>\s]+", x => Say(x, x.Groups[1].Value + "***"));
        return (m, n);
    }

    List<string> Desenler => (C.GetSection("DosyaDesenleri").Get<string[]>() is { Length: > 0 } a ? a : ["web.config"])
        .Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0 && !x.Contains('\'')).ToList();

    async Task<Dizin> DizinOlusturAsync(OracleConnection c, Sema0 s, List<string>? tani, CancellationToken ct)
    {
        var d = new Dizin();
        string yolK = s.Yol != null ? $", {s.Yol}" : "", ustK = s.Ust != null ? $", {s.Ust}" : "";
        string kosul = string.Join(" OR ", Desenler.Select(x => $"LOWER({s.AdK}) LIKE '%{x.Replace("%", "")}%'"));
        int max = C.GetValue("MaxBelge", 50000);
        var ham = new List<(string Id, string Ad, string? Ust)>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT {s.Id}, {s.AdK}{ustK}{yolK} FROM {Sema}.{Tablo} WHERE ({kosul}) AND ROWNUM <= {max}";
            cmd.CommandTimeout = SorguSn;
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string id = Deger(r.GetValue(0)), ad = Deger(r.GetValue(1));
                string? ust = s.Ust != null ? Deger(r.GetValue(2)) : null;
                string? yol = s.Yol != null ? Deger(r.GetValue(s.Ust != null ? 3 : 2)) : null;
                if (id == "") continue;
                ham.Add((id, string.IsNullOrEmpty(yol) ? ad : $"{ad} ({yol})", string.IsNullOrEmpty(ust) ? null : ust));
            }
        }
        if (ham.Count >= max) d.Notlar.Add($"UCMDB: {max} belge sınırına ulaşıldı (Ucmdb:MaxBelge).");
        tani?.Add($"Belge sayısı ({string.Join(", ", Desenler)}): {ham.Count}");

        // kapsayıcı zinciri: her seviyede bilinmeyen kimlikler, ilgili kolonları olan tüm CDM tablolarında aranır (birleştirilir)
        var bilgi = new Dictionary<string, (HashSet<string> T, string? Ad, string? Dns, string? Ust)>(StringComparer.OrdinalIgnoreCase);
        var zincir = ham.ToDictionary(h => h.Id, h => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var dugumAd = C.GetSection("DugumTablolari").Get<string[]>() is { Length: > 0 } dt ? dt.Select(Ad).ToHashSet() :
            ["CDM_NODE_1", "CDM_HOST_NODE_1", "CDM_NT_1", "CDM_UNIX_1", "CDM_COMPUTER_1", "CDM_HOST_1"];
        var aday = s.Kolon.Where(t => t.Value.ContainsKey(s.Id) && !s.Bos.Contains(t.Key) && !t.Key.Equals(Tablo, StringComparison.OrdinalIgnoreCase)
                && (t.Value.ContainsKey(s.AdK) || (s.Ust != null && t.Value.ContainsKey(s.Ust)) || t.Value.ContainsKey("A_PRIMARY_DNS_NAME")))
            .Select(t => t.Key).OrderBy(t => dugumAd.Contains(t) ? 0 : 1).ThenBy(t => t).Take(C.GetValue("MaxTablo", 2000)).ToList();
        string idTip = s.Kolon[Tablo][s.Id];
        var gorulen = ham.ToDictionary(h => h.Id, h => new HashSet<string>([h.Id], StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);   // döngü koruması
        var sunucuOf = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        int seviye = 0, maxSeviye = C.GetValue("MaxSeviye", 6);
        var isabet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // belgenin şu anki ucu (yürünecek kimlik)
        var uc = ham.ToDictionary(h => h.Id, h => h.Ust, StringComparer.OrdinalIgnoreCase);
        while (seviye++ < maxSeviye)
        {
            var sor = uc.Values.OfType<string>().Where(x => !bilgi.ContainsKey(x) && IdGecerli(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // belge tablosunda üst kolonu yoksa bağ tablolarından (END2 = belge → END1)
            if (seviye == 1 && s.Ust == null) sor = [];
            if (sor.Count > 0)
            {
                // önce düğüm tabloları ve önceki seviyelerde isabet alan tablolar; her kimliğin adı ve üstü (ya da düğüm olduğu) bulununca durulur
                foreach (var t in aday.OrderBy(t => dugumAd.Contains(t) ? 0 : 1).ThenByDescending(t => isabet.GetValueOrDefault(t)).ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    if (sor.All(x => bilgi.TryGetValue(x, out var bb) && bb.Ad != null && (bb.Ust != null || Dugum(bb)))) break;
                    var k = s.Kolon[t];
                    var sec = new List<string> { s.Id };
                    string? aK = k.ContainsKey(s.AdK) ? s.AdK : null, dK = k.ContainsKey("A_PRIMARY_DNS_NAME") ? "A_PRIMARY_DNS_NAME" : null, uK = s.Ust != null && k.ContainsKey(s.Ust) ? s.Ust : null;
                    foreach (var x in new[] { aK, dK, uK }) if (x != null) sec.Add(x);
                    foreach (var parca in sor.Chunk(500))
                    {
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = $"SELECT {string.Join(", ", sec)} FROM {Sema}.{t} WHERE {s.Id} IN ({string.Join(",", parca.Select(x => Lit(k.GetValueOrDefault(s.Id) ?? idTip, x)))})";
                        cmd.CommandTimeout = SorguSn;
                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(ct);
                            while (await r.ReadAsync(ct))
                            {
                                string id = Deger(r.GetValue(0)); int i = 1;
                                string? ad = aK != null ? Deger(r.GetValue(i++)) : null, dns = dK != null ? Deger(r.GetValue(i++)) : null, ust = uK != null ? Deger(r.GetValue(i++)) : null;
                                var e = bilgi.TryGetValue(id, out var x) ? x : ([], null, null, null);
                                if (e.T.Add(t)) isabet[t] = isabet.GetValueOrDefault(t) + 1;
                                bilgi[id] = (e.T, string.IsNullOrEmpty(e.Ad) ? (string.IsNullOrEmpty(ad) ? null : ad) : e.Ad,
                                    string.IsNullOrEmpty(e.Dns) ? (string.IsNullOrEmpty(dns) ? null : dns) : e.Dns, string.IsNullOrEmpty(e.Ust) ? (string.IsNullOrEmpty(ust) ? null : ust) : e.Ust);
                            }
                        }
                        catch (OracleException ex) { tani?.Add($"  {t}: {ex.Message}"); break; }
                    }
                }
            }
            // üst kolonu boş kalanlar için bağ tabloları (END1 = kapsayıcı, END2 = parça)
            var bagSor = uc.Where(kv => kv.Value == null && !sunucuOf.ContainsKey(kv.Key)).Select(kv => seviye == 1 ? kv.Key : null).OfType<string>()
                .Concat(sor.Where(x => bilgi.TryGetValue(x, out var b) && b.Ust == null && !Dugum(b))).Where(IdGecerli).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bagUst = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bagSor.Count > 0)
                foreach (var t in s.Bag)
                    foreach (var parca in bagSor.Chunk(500))
                    {
                        using var cmd = c.CreateCommand();
                        string tip = s.Kolon[t]["END2_ID"];
                        cmd.CommandText = $"SELECT END2_ID, END1_ID FROM {Sema}.{t} WHERE END2_ID IN ({string.Join(",", parca.Select(x => Lit(tip, x)))})";
                        cmd.CommandTimeout = SorguSn;
                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(ct);
                            while (await r.ReadAsync(ct)) bagUst.TryAdd(Deger(r.GetValue(0)), Deger(r.GetValue(1)));
                        }
                        catch (OracleException ex) { tani?.Add($"  {t}: {ex.Message}"); break; }
                    }
            // uçları ilerlet (1. seviyede üstü boş belgeler bağ tablosundaki kapsayıcıdan başlar)
            bool ilerledi = false;
            foreach (var bid in uc.Keys.ToList())
            {
                var u = uc[bid];
                if (u == null && seviye == 1 && bagUst.TryGetValue(bid, out var bu)) { uc[bid] = bu; ilerledi = true; continue; }
                if (u == null || sunucuOf.ContainsKey(bid)) continue;
                if (!bilgi.TryGetValue(u, out var b)) { uc[bid] = null; continue; }
                zincir[bid].Add($"{string.Join("/", b.T.Select(x => x.Replace("CDM_", "").Replace("_1", "").ToLowerInvariant()))}: {b.Ad ?? "?"}");
                if (Dugum(b)) { sunucuOf[bid] = b.Dns ?? b.Ad; uc[bid] = null; continue; }
                var sonraki = b.Ust ?? bagUst.GetValueOrDefault(u);
                uc[bid] = sonraki != null && gorulen[bid].Add(u) && !gorulen[bid].Contains(sonraki) ? sonraki : null;
                ilerledi |= uc[bid] != null;
            }
            if (!ilerledi) break;
        }
        bool Dugum((HashSet<string> T, string? Ad, string? Dns, string? Ust) b) => b.T.Any(dugumAd.Contains) || b.Dns != null;

        foreach (var h in ham)
        {
            var sv = sunucuOf.GetValueOrDefault(h.Id);
            var belge = new UcmdbBelge(h.Id, h.Ad, sv, zincir[h.Id]);
            d.Belgeler.Add(belge);
            if (string.IsNullOrWhiteSpace(sv)) continue;
            foreach (var anahtar in Anahtarlar(sv))
            {
                if (!d.Sunucu.TryGetValue(anahtar, out var l)) d.Sunucu[anahtar] = l = [];
                l.Add(belge);
            }
        }
        int bagli = d.Belgeler.Count(b => b.Sunucu != null);
        if (d.Belgeler.Count > 0 && bagli == 0) d.Notlar.Add("UCMDB: web.config belgeleri bulundu ama hiçbirinin sunucusu bulunamadı (kapsayıcı zinciri) — /api/ucmdb/tani ile bakın.");
        tani?.Add($"Sunucusu bulunan belge: {bagli} / {d.Belgeler.Count}");
        return d;
    }

    // sunucu adı anahtarları: tam ad, kısa ad (ilk noktaya kadar; IP değilse), IP
    static IEnumerable<string> Anahtarlar(string ad)
    {
        ad = ad.Trim().TrimEnd('.').ToLowerInvariant();
        if (ad == "") yield break;
        yield return ad;
        if (!_ipRx.IsMatch(ad) && ad.Contains('.')) yield return ad[..ad.IndexOf('.')];
    }

    async Task<List<(string Id, byte[] Bayt)>> IcerikAsync(OracleConnection c, Sema0 s, List<string> ids, CancellationToken ct)
    {
        var l = new List<(string, byte[])>();
        string tip = s.Kolon[Tablo][s.Id];
        long maxBoyut = C.GetValue("MaxBoyut", 5_000_000L);
        foreach (var parca in ids.Where(IdGecerli).Chunk(200))
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT {s.Id}, {s.Icerik} FROM {Sema}.{Tablo} WHERE {s.Id} IN ({string.Join(",", parca.Select(x => Lit(tip, x)))})";
            cmd.CommandTimeout = SorguSn;
            cmd.InitialLOBFetchSize = -1;
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(1)) continue;
                var v = r.GetValue(1);
                byte[] b = v switch { byte[] x => x, string x => Encoding.UTF8.GetBytes(x), _ => [] };
                if (b.Length > 0 && b.Length <= maxBoyut) l.Add((Deger(r.GetValue(0)), b));
            }
        }
        return l;
    }

    // ---------- içerik çözme ----------
    internal static (string? Metin, string Bicim) Coz(byte[] b, string ad)
    {
        string bicim = "düz";
        try
        {
            if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 3 && b[3] == 4)
            {
                using var z = new ZipArchive(new MemoryStream(b), ZipArchiveMode.Read);
                var e = z.Entries.FirstOrDefault(x => x.Length > 0 && ad.Contains(x.Name, StringComparison.OrdinalIgnoreCase)) ?? z.Entries.FirstOrDefault(x => x.Length > 0);
                if (e == null) return (null, "zip (boş)");
                using var es = e.Open(); using var ms = new MemoryStream(); es.CopyTo(ms); b = ms.ToArray(); bicim = "zip";
            }
            else if (b.Length >= 2 && b[0] == 0x1F && b[1] == 0x8B)
            {
                using var g = new GZipStream(new MemoryStream(b), CompressionMode.Decompress); using var ms = new MemoryStream(); g.CopyTo(ms); b = ms.ToArray(); bicim = "gzip";
            }
            else if (b.Length >= 2 && b[0] == 0x78 && b[1] is 0x01 or 0x5E or 0x9C or 0xDA)
            {
                using var g = new ZLibStream(new MemoryStream(b), CompressionMode.Decompress); using var ms = new MemoryStream(); g.CopyTo(ms); b = ms.ToArray(); bicim = "zlib";
            }
        }
        catch (Exception) { return (null, bicim + " (açılamadı)"); }
        string metin;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) metin = Encoding.UTF8.GetString(b, 3, b.Length - 3);
        else if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) metin = Encoding.Unicode.GetString(b, 2, b.Length - 2);
        else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) metin = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        else if (b.Length >= 4 && b[1] == 0 && b[3] == 0) metin = Encoding.Unicode.GetString(b);
        else
        {
            try { metin = new UTF8Encoding(false, true).GetString(b); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                metin = Encoding.GetEncoding(1254).GetString(b);
            }
        }
        metin = metin.TrimStart('﻿', '\0', ' ', '\r', '\n', '\t');
        return (metin.Length == 0 ? null : metin, bicim);
    }

    // ---------- ayrıştırma (yalnız adresler; şifre / anahtar değeri hiçbir zaman dönmez) ----------
    static readonly Regex _url = new(@"\b(?:https?|net\.tcp|net\.pipe|ftps?|sftp|wss?)://[^\s""'<>;,]+", RegexOptions.IgnoreCase);
    static readonly Regex _gizliParam = new(@"(?i)([?&;](?:[^=&;]*(?:pass|pwd|token|secret|key|sig|auth|cred)[^=&;]*)=)[^&;#]*");

    internal static string Temizle(string url)
    {
        url = url.Trim().TrimEnd('.', ')', ']');
        url = Regex.Replace(url, @"^([a-z.+]+://)[^/@\s]*@", "$1", RegexOptions.IgnoreCase);   // kullanıcı:şifre@
        return _gizliParam.Replace(url, "$1***");
    }

    internal static List<UcmdbAdres> Ayristir(string metin, string dosya)
    {
        var l = new List<UcmdbAdres>();
        void Url(string tur, string anahtar, string? deger, string? taban = null)
        {
            if (string.IsNullOrWhiteSpace(deger)) return;
            deger = deger.Trim();
            if (!Regex.IsMatch(deger, @"^[a-z.]+://", RegexOptions.IgnoreCase))
            {
                if (taban == null || deger.Contains(' ')) { if (tur == "gelen" && deger.Length > 0 && !deger.Contains("://")) l.Add(new(tur, anahtar, deger, null, null, "/" + deger.TrimStart('/'), dosya)); return; }
                deger = taban.TrimEnd('/') + "/" + deger.TrimStart('/');
            }
            var t = Temizle(deger);
            if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || string.IsNullOrEmpty(u.Host)) return;
            string? port = u.IsDefaultPort ? u.Scheme switch { "http" => "80", "https" => "443", "net.tcp" => "808", "ftp" => "21", "sftp" => "22", "ws" => "80", "wss" => "443", _ => null } : u.Port.ToString();
            if (u.Host is "localhost" or "127.0.0.1" or "+" or "*") { if (tur != "gelen") return; }
            l.Add(new(tur, anahtar, t, u.Host.ToLowerInvariant(), port, u.AbsolutePath is "/" or "" ? null : u.AbsolutePath, dosya));
        }
        XDocument? x = null;
        try
        {
            using var sr = new StringReader(metin);
            using var xr = XmlReader.Create(sr, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
            x = XDocument.Load(xr);
        }
        catch (Exception) { /* XML değil (appsettings.json vb.) ya da bozuk → düz metin taraması */ }
        if (x != null)
        {
            IEnumerable<XElement> E(string ad) => x.Descendants().Where(e => e.Name.LocalName == ad);
            string? A(XElement e, string ad) => e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(ad, StringComparison.OrdinalIgnoreCase))?.Value;
            // WCF istemci uçları (giden)
            foreach (var cl in E("client"))
                foreach (var ep in cl.Elements().Where(e => e.Name.LocalName == "endpoint"))
                    Url("giden", A(ep, "name") ?? A(ep, "contract") ?? "endpoint", A(ep, "address"));
            // WCF servisleri (gelen): baseAddresses + endpoint (göreli adres tabana eklenir)
            foreach (var sv in E("service"))
            {
                var tab = sv.Descendants().Where(e => e.Name.LocalName == "add" && A(e, "baseAddress") != null).Select(e => A(e, "baseAddress")!).ToList();
                foreach (var t in tab) Url("gelen", A(sv, "name") ?? "service", t);
                foreach (var ep in sv.Elements().Where(e => e.Name.LocalName == "endpoint"))
                {
                    var adr = A(ep, "address");
                    if (string.IsNullOrWhiteSpace(adr) || adr.Equals("mex", StringComparison.OrdinalIgnoreCase)) continue;
                    Url("gelen", (A(sv, "name") ?? "service") + (A(ep, "contract") is { } k ? " · " + k : ""), adr, tab.FirstOrDefault());
                }
            }
            // appSettings / applicationSettings: değeri URL olanlar (giden)
            foreach (var ad in E("add").Where(e => A(e, "key") != null && A(e, "value") != null))
                foreach (Match m in _url.Matches(A(ad, "value")!)) Url("giden", A(ad, "key")!, m.Value);
            foreach (var st in E("setting"))
                foreach (Match m in _url.Matches(st.Value)) Url("giden", A(st, "name") ?? "setting", m.Value);
            // URL Rewrite / ARR yönlendirme hedefleri (giden)
            foreach (var ac in E("action").Where(e => (A(e, "type") ?? "").Equals("Rewrite", StringComparison.OrdinalIgnoreCase)))
                foreach (Match m in _url.Matches(A(ac, "url") ?? "")) Url("giden", "rewrite", m.Value.Replace("{R:0}", "").Replace("{R:1}", ""));
            // connectionStrings: yalnız sunucu + DB adı
            foreach (var cs in E("connectionStrings").SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == "add" && A(e, "connectionString") != null))
                foreach (var db in Db(A(cs, "connectionString")!)) l.Add(new("db", A(cs, "name") ?? "connectionString", db.Adres, db.Host, db.Port, null, dosya));
            if (E("EncryptedData").Any()) l.Add(new("not", "şifreli bölüm", "web.config'te şifrelenmiş bölüm var (configProtectedData) — içi okunamaz", null, null, null, dosya));
        }
        else
        {
            foreach (Match m in _url.Matches(metin)) Url("giden", "metin", m.Value);
        }
        return l.DistinctBy(a => (a.Tur, a.Adres, a.Anahtar)).Take(300).ToList();
    }

    // bağlantı cümlesinden sunucu, port, DB / servis adı — başka hiçbir parça alınmaz
    internal static IEnumerable<(string Adres, string? Host, string? Port)> Db(string cs)
    {
        var sonuc = new List<(string, string?, string?)>();
        string? Al(string desen) => Regex.Match(cs, desen, RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
        string? db = Al(@"(?:Initial Catalog|Database)\s*=\s*([^;]+)") ?? Al(@"SERVICE_NAME\s*=\s*([^)\s]+)") ?? Al(@"\bSID\s*=\s*([^)\s]+)");
        var hostlar = Regex.Matches(cs, @"HOST\s*=\s*([^)\s]+)\s*\)\s*\(\s*PORT\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (hostlar.Count > 0)
            foreach (Match m in hostlar) sonuc.Add(($"oracle://{m.Groups[1].Value}:{m.Groups[2].Value}" + (db != null ? "/" + db : ""), m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value));
        else if (Al(@"(?:Data Source|Server|Address|Addr|Network Address|Host)\s*=\s*([^;]+)") is { } ds && !ds.StartsWith("("))
        {
            ds = Regex.Replace(ds, @"^(tcp|np|lpc):", "", RegexOptions.IgnoreCase);
            string? port = Regex.Match(ds, @"[,:](\d{2,5})(?:/|$)") is { Success: true } pm ? pm.Groups[1].Value : null;
            string host = Regex.Split(ds, @"[,:\\/]")[0].Trim();
            string? inst = ds.Contains('\\') ? ds[(ds.IndexOf('\\') + 1)..].Split(',')[0] : null;
            if (Regex.Match(ds, @"^[^/]+/(\S+)$") is { Success: true } ez && db == null) db = ez.Groups[1].Value;   // EZConnect host:port/servis
            if (host.Length > 1 && host != "." && !host.Equals("(local)", StringComparison.OrdinalIgnoreCase) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                sonuc.Add(($"db://{host}{(inst != null ? "\\" + inst : "")}{(port != null ? ":" + port : "")}{(db != null ? "/" + db : "")}", host.ToLowerInvariant(), port));
        }
        return sonuc;
    }

    // ---------- tanı (düz metin; içerik basılmaz, yalnız biçim ve bulunan adresler) ----------
    public async Task<string> TaniAsync(string? ip, EnvanterSnapshot? env, CancellationToken ct)
    {
        var sb = new StringBuilder();
        void S(string x) => sb.AppendLine(x);
        S($"UCMDB tanı — {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        if (!Acik) { S("Ucmdb:Baglanti boş: appsettings.Production.json'a Oracle bağlantı cümlesini ekleyin."); return sb.ToString(); }
        OracleConnection c;
        try { c = await AcAsync(ct); S($"Bağlantı: açık · Oracle {c.ServerVersion}"); }
        catch (Exception ex) { S("Bağlantı AÇILAMADI: " + ex.Message); return sb.ToString(); }
        using (c)
        {
            Sema0 s;
            try { _sema = null; s = await SemaAsync(c, ct); }
            catch (Exception ex) { S("Şema: " + ex.Message); return sb.ToString(); }
            S($"Şema: {Sema} · CDM tablosu: {s.Kolon.Count} (boş: {s.Bos.Count}) · bağ tabloları: {(s.Bag.Count == 0 ? "yok" : string.Join(", ", s.Bag))}");
            S($"{Tablo} kolonları: {string.Join(", ", s.Kolon[Tablo].Select(kv => $"{kv.Key} {kv.Value}"))}");
            S($"Kullanılan: kimlik {s.Id}, ad {s.AdK}, içerik {s.Icerik}, üst {s.Ust ?? "(yok → bağ tabloları)"}, yol {s.Yol ?? "(yok)"}");
            // örnek belgeler: boyut + ilk baytlar + çözülen biçim (içerik basılmaz)
            try
            {
                using var cmd = c.CreateCommand();
                string kosul = string.Join(" OR ", Desenler.Select(x => $"LOWER({s.AdK}) LIKE '%{x.Replace("%", "")}%'"));
                cmd.CommandText = $"SELECT {s.Id}, {s.AdK}, {s.Icerik} FROM {Sema}.{Tablo} WHERE ({kosul}) AND ROWNUM <= 5";
                cmd.CommandTimeout = SorguSn; cmd.InitialLOBFetchSize = -1;
                using var r = await cmd.ExecuteReaderAsync(ct);
                S("Örnek belgeler:");
                while (await r.ReadAsync(ct))
                {
                    var v = r.IsDBNull(2) ? null : r.GetValue(2);
                    byte[] b = v switch { byte[] x => x, string x => Encoding.UTF8.GetBytes(x), _ => [] };
                    var (metin, bicim) = Coz(b, Deger(r.GetValue(1)));
                    var a = metin == null ? [] : Ayristir(metin, Deger(r.GetValue(1)));
                    S($"  {Deger(r.GetValue(1))} · {b.Length} bayt · ilk bayt {Convert.ToHexString(b.AsSpan(0, Math.Min(8, b.Length)))} · biçim {bicim}{(metin == null ? " · METİN ÇIKMADI" : $" · {metin.Length} karakter, XML: {(metin.TrimStart().StartsWith('<') ? "evet" : "hayır")}")} · adres: gelen {a.Count(x => x.Tur == "gelen")}, giden {a.Count(x => x.Tur == "giden")}, db {a.Count(x => x.Tur == "db")}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { S("Örnek belge okunamadı: " + ex.Message); }
            var notlar = new List<string>();
            Dizin d;
            try { d = await DizinOlusturAsync(c, s, notlar, ct); _dizin = d; }
            catch (Exception ex) when (ex is not OperationCanceledException) { S("Dizin: " + ex.Message); return sb.ToString(); }
            foreach (var n in notlar.Concat(d.Notlar)) S(n);
            S("Zincir örnekleri:");
            foreach (var b in d.Belgeler.Take(5)) S($"  {b.Ad} → {(b.Zincir.Count == 0 ? "(üst bulunamadı)" : string.Join(" → ", b.Zincir))} ⇒ sunucu: {b.Sunucu ?? "?"}");
            if (ip != null && env != null)
            {
                var adlar = env.AppsOnIp(ip).Select(h => h.UnixName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
                S($"IP {ip} · ERT sunucu adı: {(adlar.Count == 0 ? "(yok)" : string.Join(", ", adlar))}");
                var bl = Belgeler([ip], env).GetValueOrDefault(ip) ?? [];
                if (bl.Count == 0) S("  Bu sunucu için web.config bulunamadı (ad eşleşmesi yok).");
                foreach (var b in bl)
                {
                    var x = await BelgeAsync(b.Id, ct);
                    S($"  belge: {b.Ad} · {x?.Boyut ?? 0} bayt · biçim {x?.Bicim ?? "?"} · maskelenen değer {x?.Maske ?? 0}");
                    if (x?.Metin is { } mt) foreach (var a in Ayristir(mt, b.Ad)) S($"    [{a.Tur}] {a.Anahtar}: {a.Adres}");
                }
            }
        }
        return sb.ToString();
    }
}
