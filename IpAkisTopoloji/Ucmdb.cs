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
// Adlar: zincirdeki IIS site / sanal dizin / havuz adları + belge yolundaki klasörler (port eşleşmesi için); Portlar: UCMDB'deki binding / port kolonlarından
// Yol: belge tablosundaki yol kolonu (Ucmdb:YolKolonu ya da bulunan A_…PATH); Ipler: sunucunun UCMDB'deki IP'leri; Eslesme: bu IP'ye nasıl bağlandığı
record UcmdbBelge(string Id, string Ad, string? Sunucu, List<string> Zincir, List<string>? Adlar = null, List<string>? Portlar = null,
    string? Yol = null, List<string>? Ipler = null, string? Eslesme = null);
// Port ataması: Portlar boşsa "port belirlenemedi"; Neden: "UCMDB binding" / "ERT WEBSITE 'X'" / "IIS logu /app" / "web.config baseAddress"
record UcmdbPortlu(UcmdbBelge Belge, List<string> Portlar, string? Neden);
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
        static bool Metin(string tip) => tip is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "CLOB" or "NCLOB";
        s.Yol = C["YolKolonu"] is { Length: > 0 } yk ? Ad(yk)
            : new[] { "A_PATH", "A_DOCUMENT_PATH", "A_FILE_PATH", "A_FULL_PATH", "A_RESOURCE_PATH", "A_DOCUMENT_LOCATION", "A_LOCATION", "A_FILE_LOCATION" }
                  .FirstOrDefault(k => bk.TryGetValue(k, out var t) && Metin(t))
              ?? bk.Where(kv => Metin(kv.Value) && Regex.IsMatch(kv.Key, @"^A_.*(PATH|LOCATION|DIRECTORY|FOLDER)", RegexOptions.IgnoreCase)
                     && !Regex.IsMatch(kv.Key, "CONTEN|CHECKSUM|SIZE|TYPE|ENCODING", RegexOptions.IgnoreCase)).Select(kv => kv.Key).FirstOrDefault();
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
        public Dictionary<string, List<UcmdbBelge>> IpBelge = new();                                   // UCMDB'deki sunucu IP'si → belgeler
        public List<string> Notlar = [];
    }
    Dizin? _dizin;
    Task? _hazirlik;
    string? _dizinHata, _asama;
    DateTime _asamaBas, _hataZaman;
    readonly ConcurrentDictionary<string, (DateTime Z, string? Metin, string Bicim, int Boyut, int Maske)> _icerik = new();

    // Dizin arka planda hazırlanır (ilk seferde tüm web.config'lerin kapsayıcı zinciri yürünür; IIS istek zaman aşımına takılmasın)
    public (bool Hazir, string? Hata, string? Asama, int Gecen) DizinDurumu()
    {
        var d = _dizin;
        bool eski = d == null || (DateTime.UtcNow - d.Zaman).TotalHours >= C.GetValue("OnbellekSaat", 12.0);
        if (eski && Acik && (_dizinHata == null || (DateTime.UtcNow - _hataZaman).TotalMinutes >= 5))
            lock (this)
                if (_hazirlik == null || _hazirlik.IsCompleted)
                {
                    _asamaBas = DateTime.UtcNow; _asama = "başlıyor";
                    _hazirlik = Task.Run(async () =>
                    {
                        try { await DizinAsync(true, CancellationToken.None); _dizinHata = null; _asama = null; }
                        catch (Exception ex) { _dizinHata = ex.Message; _hataZaman = DateTime.UtcNow; _asama = null; log.LogWarning(ex, "UCMDB dizini hazırlanamadı"); }
                    });
                }
        bool suruyor = _hazirlik is { IsCompleted: false };
        return (d != null, d == null ? _dizinHata : null, suruyor ? _asama : null, suruyor ? (int)(DateTime.UtcNow - _asamaBas).TotalSeconds : 0);
    }

    public async Task<(List<UcmdbBelge> Belgeler, List<string> Notlar)> DizinAsync(bool yenile, CancellationToken ct)
    {
        var d = _dizin;
        if (!yenile && d != null && (DateTime.UtcNow - d.Zaman).TotalHours < C.GetValue("OnbellekSaat", 12.0)) return (d.Belgeler, d.Notlar);
        await _kilit.WaitAsync(ct);
        try
        {
            _asama = "Oracle'a bağlanılıyor";
            using var c = await AcAsync(ct);
            _asama = "şema okunuyor (ALL_TAB_COLUMNS)";
            var s = await SemaAsync(c, ct);
            _asama = "Oracle'a bağlanıldı, şema okundu";
            _dizin = d = await DizinOlusturAsync(c, s, null, ct, 0, x => _asama = x);
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
            // 1) UCMDB'de sunucunun IP'si bu IP; 2) yalnız UCMDB'de IP'si OLMAYAN sunucular için ERT sunucu adı (aynı adlı başka IP'li sunucu karışmasın)
            var ipEs = (dz.IpBelge.GetValueOrDefault(ip) ?? []).Select(b => b with { Eslesme = "UCMDB IP" });
            var adlar = env.AppsOnIp(ip).Select(h => h.UnixName).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Append(ip);
            var adEs = adlar.SelectMany(Anahtarlar).Distinct().SelectMany(a => dz.Sunucu.GetValueOrDefault(a) ?? [])
                .Where(b => b.Ipler == null).Select(b => b with { Eslesme = "ERT sunucu adı" });
            var l = ipEs.Concat(adEs).DistinctBy(b => b.Id).OrderBy(b => b.Yol ?? b.Ad).ToList();
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

    // Tablo önceliği: sunucu (düğüm) ve IIS / web / uygulama sunucusu sınıfları önce — web.config'in kapsayıcısı neredeyse hep bunlar
    static readonly Regex _oncelik = new(@"NODE|_NT_1$|UNIX|COMPUTER|HOST|IIS|WEB|APP_?POOL|VIRTUAL_?DIR|J2EE|APPLICATION_SERVER|RUNNING_SOFTWARE|SOFTWARE|DOTNET|ASP|CONFIGURATION_DOCUMENT",
        RegexOptions.IgnoreCase);

    // ornek > 0: yalnız o kadar belgeyle (tanı); asama: ilerleme metni; seviyeSn: seviye başına süre sınırı
    async Task<Dizin> DizinOlusturAsync(OracleConnection c, Sema0 s, List<string>? tani, CancellationToken ct, int ornek = 0, Action<string>? asama = null, int? seviyeSn = null)
    {
        var d = new Dizin();
        string yolK = s.Yol != null ? $", {s.Yol}" : "", ustK = s.Ust != null ? $", {s.Ust}" : "";
        string kosul = string.Join(" OR ", Desenler.Select(x => $"LOWER({s.AdK}) LIKE '%{x.Replace("%", "")}%'"));
        int max = ornek > 0 ? ornek : C.GetValue("MaxBelge", 50000);
        var ham = new List<(string Id, string Ad, string? Yol, string? Ust)>();
        asama?.Invoke("web.config belgeleri listeleniyor");
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT {s.Id}, {s.AdK}{ustK}{yolK} FROM {Sema}.{Tablo} WHERE ({kosul}) AND ROWNUM <= {max}";
            cmd.CommandTimeout = Math.Max(SorguSn, 300);
            using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                string id = Deger(r.GetValue(0)), ad = Deger(r.GetValue(1));
                string? ust = s.Ust != null ? Deger(r.GetValue(2)) : null;
                string? yol = s.Yol != null ? Deger(r.GetValue(s.Ust != null ? 3 : 2)) : null;
                if (id == "") continue;
                ham.Add((id, ad, string.IsNullOrWhiteSpace(yol) ? null : yol.Trim(), string.IsNullOrEmpty(ust) ? null : ust));
            }
        }
        if (ornek == 0 && ham.Count >= max) d.Notlar.Add($"UCMDB: {max} belge sınırına ulaşıldı (Ucmdb:MaxBelge).");
        tani?.Add($"Belge ({string.Join(", ", Desenler)}): {ham.Count}{(ornek > 0 ? " (örnek)" : "")} · üst kolonu dolu: {ham.Count(h => h.Ust != null)}");

        var bilgi = new Dictionary<string, Ci>(StringComparer.OrdinalIgnoreCase);
        var zincir = ham.ToDictionary(h => h.Id, h => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var adlar = ham.ToDictionary(h => h.Id, h => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var portlar = ham.ToDictionary(h => h.Id, h => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var dugumAd = C.GetSection("DugumTablolari").Get<string[]>() is { Length: > 0 } dt ? dt.Select(Ad).ToHashSet() :
            ["CDM_NODE_1", "CDM_HOST_NODE_1", "CDM_NT_1", "CDM_UNIX_1", "CDM_COMPUTER_1", "CDM_HOST_1"];
        bool Dugum(Ci b) => b.T.Any(dugumAd.Contains) || b.Dns != null;
        var aday = s.Kolon.Where(t => t.Value.ContainsKey(s.Id) && !s.Bos.Contains(t.Key) && !t.Key.Equals(Tablo, StringComparison.OrdinalIgnoreCase)
                && (t.Value.ContainsKey(s.AdK) || (s.Ust != null && t.Value.ContainsKey(s.Ust)) || t.Value.ContainsKey("A_PRIMARY_DNS_NAME")))
            .Select(t => t.Key).Take(C.GetValue("MaxTablo", 3000)).ToList();
        int Puan(string t) => dugumAd.Contains(t) ? 0 : _oncelik.IsMatch(t) ? 1 : 2;
        tani?.Add($"Aranacak tablo: {aday.Count} (öncelikli: {aday.Count(t => Puan(t) < 2)})");
        string idTip = s.Kolon[Tablo][s.Id];
        var gorulen = ham.ToDictionary(h => h.Id, h => new HashSet<string>([h.Id], StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var sunucuOf = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var dugumOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // belge → sunucu (düğüm) kimliği
        var isabet = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int seviye = 0, maxSeviye = C.GetValue("MaxSeviye", 6), sn = seviyeSn ?? C.GetValue("SeviyeSureSaniye", 120);
        double yeter = C.GetValue("YeterOran", 0.98);
        var uc = ham.ToDictionary(h => h.Id, h => h.Ust, StringComparer.OrdinalIgnoreCase);   // belgenin yürünecek ucu
        while (seviye++ < maxSeviye)
        {
            var sor = uc.Values.OfType<string>().Where(x => !bilgi.ContainsKey(x) && IdGecerli(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sor.Count > 0)
            {
                var sira = aday.OrderByDescending(t => isabet.GetValueOrDefault(t) > 0).ThenBy(Puan).ThenByDescending(t => isabet.GetValueOrDefault(t)).ThenBy(t => t).ToList();
                var saat = System.Diagnostics.Stopwatch.StartNew();
                int i = 0;
                bool Tamam() => sor.All(x => bilgi.TryGetValue(x, out var bb) && bb.Ad != null && (bb.Ust != null || Dugum(bb)));
                double Oran() => sor.Count(bilgi.ContainsKey) / (double)sor.Count;
                foreach (var t in sira)
                {
                    ct.ThrowIfCancellationRequested();
                    i++;
                    if (Tamam()) break;
                    // öncelikli tablolar bitti ve kimliklerin neredeyse hepsi bulunduysa geri kalan yüzlerce sınıf tablosu taranmaz
                    if (Puan(t) == 2 && isabet.GetValueOrDefault(t) == 0 && Oran() >= yeter) { tani?.Add($"  seviye {seviye}: %{Oran() * 100:0} bulundu, kalan {sira.Count - i + 1} tablo atlandı"); break; }
                    if (saat.Elapsed.TotalSeconds > sn) { d.Notlar.Add($"UCMDB seviye {seviye}: {sn} sn sınırı ({i}/{sira.Count} tablo, %{Oran() * 100:0} bulundu)"); tani?.Add(d.Notlar[^1]); break; }
                    if (i % 10 == 1) asama?.Invoke($"seviye {seviye}/{maxSeviye}: {i}/{sira.Count} tablo · {sor.Count(bilgi.ContainsKey)}/{sor.Count} kapsayıcı bulundu");
                    var k = s.Kolon[t];
                    var sec = new List<string> { s.Id };
                    string? aK = k.ContainsKey(s.AdK) ? s.AdK : null, dK = k.ContainsKey("A_PRIMARY_DNS_NAME") ? "A_PRIMARY_DNS_NAME" : null, uK = s.Ust != null && k.ContainsKey(s.Ust) ? s.Ust : null;
                    foreach (var x in new[] { aK, dK, uK }) if (x != null) sec.Add(x);
                    // IIS site / sanal dizin binding ve port kolonları (düğüm tablolarında değil: sunucunun tüm portları olur)
                    var pK = dugumAd.Contains(t) ? [] : k.Where(kv => PortKolonu(kv.Key, kv.Value)).Select(kv => kv.Key).Take(4).ToList();
                    sec.AddRange(pK);
                    // sunucunun kendi IP kolonları (A_PRIMARY_IP_ADDRESS, A_IP_ADDRESS…): düğüme varınca UCMDB IP'si
                    var iK = k.Where(kv => kv.Value is "VARCHAR2" or "NVARCHAR2" or "CHAR" && Regex.IsMatch(kv.Key, @"^A_(.*_)?IP(_ADDRESS(ES)?|_?ADDR)?(_.*)?$", RegexOptions.IgnoreCase)
                        && !Regex.IsMatch(kv.Key, "TYPE|VERSION|MASK|GATEWAY|LEASE|DHCP|SUBNET|ROUTE", RegexOptions.IgnoreCase)).Select(kv => kv.Key).Take(4).ToList();
                    sec.AddRange(iK);
                    int once = isabet.GetValueOrDefault(t);
                    foreach (var parca in sor.Chunk(1000))
                    {
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = $"SELECT {string.Join(", ", sec)} FROM {Sema}.{t} WHERE {s.Id} IN ({string.Join(",", parca.Select(x => Lit(k.GetValueOrDefault(s.Id) ?? idTip, x)))})";
                        cmd.CommandTimeout = SorguSn;
                        try
                        {
                            using var r = await cmd.ExecuteReaderAsync(ct);
                            while (await r.ReadAsync(ct))
                            {
                                string id = Deger(r.GetValue(0)); int j = 1;
                                string? ad = aK != null ? Deger(r.GetValue(j++)) : null, dns = dK != null ? Deger(r.GetValue(j++)) : null, ust = uK != null ? Deger(r.GetValue(j++)) : null;
                                if (!bilgi.TryGetValue(id, out var e)) bilgi[id] = e = new Ci();
                                if (e.T.Add(t)) isabet[t] = isabet.GetValueOrDefault(t) + 1;
                                if (string.IsNullOrEmpty(e.Ad) && !string.IsNullOrEmpty(ad)) e.Ad = ad;
                                if (string.IsNullOrEmpty(e.Dns) && !string.IsNullOrEmpty(dns)) e.Dns = dns;
                                if (string.IsNullOrEmpty(e.Ust) && !string.IsNullOrEmpty(ust)) e.Ust = ust;
                                foreach (var pk in pK) foreach (var pt in PortCoz(Deger(r.GetValue(j++)))) e.Port.Add(pt);
                                foreach (var ik in iK) foreach (Match im in _ipBul.Matches(Deger(r.GetValue(j++)))) if (IpGecerli(im.Value)) e.Ip.Add(im.Value);
                            }
                        }
                        catch (OracleException ex) { tani?.Add($"  {t}: {ex.Message}"); break; }
                    }
                    if (tani != null && isabet.GetValueOrDefault(t) > once) tani.Add($"  seviye {seviye}: {t} → {isabet[t] - once} kimlik");
                }
            }
            // üstü boş kalanlar için bağ tabloları (END1 = kapsayıcı, END2 = parça); 1. seviyede belgenin kendisi
            var bagSor = uc.Where(kv => kv.Value == null && seviye == 1 && !sunucuOf.ContainsKey(kv.Key)).Select(kv => kv.Key)
                .Concat(sor.Where(x => bilgi.TryGetValue(x, out var b) && b.Ust == null && !Dugum(b))).Where(IdGecerli).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var bagUst = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (bagSor.Count > 0)
            {
                asama?.Invoke($"seviye {seviye}/{maxSeviye}: bağ tabloları ({bagSor.Count} kimlik)");
                foreach (var t in s.Bag)
                    foreach (var parca in bagSor.Chunk(1000))
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
                if (tani != null && bagUst.Count > 0) tani.Add($"  seviye {seviye}: bağ tablolarından {bagUst.Count} kapsayıcı");
            }
            // uçları ilerlet
            bool ilerledi = false;
            foreach (var bid in uc.Keys.ToList())
            {
                var u = uc[bid];
                if (u == null && seviye == 1 && bagUst.TryGetValue(bid, out var bu)) { uc[bid] = bu; ilerledi = true; continue; }
                if (u == null || sunucuOf.ContainsKey(bid)) continue;
                if (!bilgi.TryGetValue(u, out var b)) { uc[bid] = null; continue; }
                zincir[bid].Add($"{string.Join("/", b.T.Select(x => x.Replace("CDM_", "").Replace("_1", "").ToLowerInvariant()))}: {b.Ad ?? "?"}");
                if (Dugum(b)) { sunucuOf[bid] = b.Dns ?? b.Ad; dugumOf[bid] = u; uc[bid] = null; continue; }
                if (b.Ad != null) adlar[bid].Add(b.Ad);
                if (portlar[bid].Count == 0 && b.Port.Count > 0) portlar[bid].AddRange(b.Port.Order());   // en yakın kapsayıcının portları (site binding)
                var sonraki = b.Ust ?? bagUst.GetValueOrDefault(u);
                uc[bid] = sonraki != null && gorulen[bid].Add(u) && !gorulen[bid].Contains(sonraki) ? sonraki : null;
                ilerledi |= uc[bid] != null;
            }
            asama?.Invoke($"seviye {seviye} bitti · sunucusu bulunan belge {sunucuOf.Count}/{ham.Count}");
            if (!ilerledi) break;
        }

        // sunucuların IP'leri doğrudan UCMDB'den (düğüm IP kolonları, IP kaydının üst kolonu, containment / composition bağı)
        Dictionary<string, HashSet<string>> dugumIp = new(StringComparer.OrdinalIgnoreCase);
        try { asama?.Invoke($"sunucu IP'leri UCMDB'den okunuyor ({dugumOf.Values.Distinct().Count()} sunucu)"); dugumIp = await DugumIpAsync(c, s, bilgi, dugumOf.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), tani, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { d.Notlar.Add("UCMDB: sunucu IP'leri okunamadı (ERT sunucu adıyla eşleşir): " + ex.Message); tani?.Add(d.Notlar[^1]); }
        foreach (var h in ham)
        {
            var sv = sunucuOf.GetValueOrDefault(h.Id);
            var ipl = dugumOf.TryGetValue(h.Id, out var dg) && dugumIp.TryGetValue(dg, out var ih) && ih.Count > 0 ? ih.Order().ToList() : null;
            var belge = new UcmdbBelge(h.Id, h.Ad, sv, zincir[h.Id], adlar[h.Id].Concat(Klasorler(h.Yol ?? h.Ad)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), portlar[h.Id],
                h.Yol, ipl);
            d.Belgeler.Add(belge);
            foreach (var ip in ipl ?? [])
            {
                if (!d.IpBelge.TryGetValue(ip, out var li)) d.IpBelge[ip] = li = [];
                li.Add(belge);
            }
            if (string.IsNullOrWhiteSpace(sv)) continue;
            foreach (var anahtar in Anahtarlar(sv))
            {
                if (!d.Sunucu.TryGetValue(anahtar, out var l)) d.Sunucu[anahtar] = l = [];
                l.Add(belge);
            }
        }
        tani?.Add($"UCMDB IP'si bulunan belge: {d.Belgeler.Count(b => b.Ipler != null)} / {d.Belgeler.Count} · yol kolonu: {s.Yol ?? "(yok — Ucmdb:YolKolonu verin)"}");
        int bagli = d.Belgeler.Count(b => b.Sunucu != null);
        if (d.Belgeler.Count > 0 && bagli == 0) d.Notlar.Add("UCMDB: web.config belgeleri bulundu ama hiçbirinin sunucusu bulunamadı (kapsayıcı zinciri) — /api/ucmdb/tani ile bakın.");
        tani?.Add($"Sunucusu bulunan belge: {bagli} / {d.Belgeler.Count}");
        return d;
    }

    sealed class Ci { public HashSet<string> T = new(StringComparer.OrdinalIgnoreCase); public string? Ad, Dns, Ust; public HashSet<string> Port = [], Ip = []; }
    static readonly Regex _ipBul = new(@"(?<![\d.])\d{1,3}(\.\d{1,3}){3}(?![\d.])");
    static bool IpGecerli(string ip) => _ipRx.IsMatch(ip) && ip.Split('.').All(x => int.TryParse(x, out int n) && n <= 255) && !ip.StartsWith("127.") && !ip.StartsWith("0.") && ip != "255.255.255.255";

    // binding / port taşıyan kolonlar (A_SERVER_BINDINGS, A_BINDINGS, A_PORT, A_NETWORK_PORT_NUMBER, A_HTTP_PORT…); tür / protokol / durum kolonları değil
    static bool PortKolonu(string ad, string tip) =>
        tip is "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "NUMBER" or "FLOAT" or "INTEGER"
        && Regex.IsMatch(ad, @"^A_(.*_)?(BINDINGS?|PORTS?|PORT_NUMBER|PORTNUMBER)(_.*)?$", RegexOptions.IgnoreCase)
        && !Regex.IsMatch(ad, "TYPE|PROTOCOL|STATUS|DESC|NAME|COUNT|SUPPORT|REPORT|EXPORT|IMPORT|TRANSPORT|PORTAL", RegexOptions.IgnoreCase);

    // "443", "http/*:80:", "*:8080:host,https/*:443:", "https://ad:8443/" → portlar
    internal static IEnumerable<string> PortCoz(string v)
    {
        v = v.Trim();
        var l = new List<string>();
        if (Regex.IsMatch(v, @"^\d{1,5}(\.0+)?$")) l.Add(v.Split('.')[0]);
        foreach (Match m in Regex.Matches(v, @":(\d{1,5})(?=$|[:/\s,;|\]])")) l.Add(m.Groups[1].Value);
        return l.Where(x => int.TryParse(x, out int n) && n is > 0 and < 65536).Select(x => int.Parse(x).ToString()).Distinct();
    }

    // belge yolundaki anlamlı klasörler: "web.config (D:\inetpub\wwwroot\KrediWS\api\web.config)" → KrediWS, api
    static readonly HashSet<string> _genelKlasor = new(StringComparer.OrdinalIgnoreCase)
        { "inetpub", "wwwroot", "sites", "site", "web", "webs", "websites", "www", "apps", "app", "application", "applications", "iis", "root", "httpdocs", "public",
          "bin", "config", "program files", "program files (x86)", "web.config", "data", "deploy", "publish", "services", "service" };
    internal static IEnumerable<string> Klasorler(string ad)
    {
        var m = Regex.Match(ad, @"\(([^()]*[\\/][^()]*)\)\s*$");
        string yol = m.Success ? m.Groups[1].Value : ad.Contains('\\') || ad.Contains('/') ? ad : "";
        return yol.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())
            .Where(x => x.Length > 1 && !x.EndsWith(':') && !_genelKlasor.Contains(x) && !x.EndsWith(".config", StringComparison.OrdinalIgnoreCase));
    }

    static string Sade(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9ğüşıöç]", "");
    static bool AdEs(string a, string b)
    {
        string x = Sade(a), y = Sade(b);
        if (x.Length < 2 || y.Length < 2) return false;
        if (x == y) return true;
        var (k, u) = x.Length <= y.Length ? (x, y) : (y, x);
        return k.Length >= 5 && u.Contains(k);
    }

    // Sunucu (düğüm) kimliği → IP'ler: (1) düğüm satırlarındaki IP kolonları, (2) IP adresi tablosunda üst kolonu düğüm, (3) containment / composition bağıyla
    // düğüme bağlı IP adresi kayıtları (join ile tek sorgu, iki yön). IP adresi tablosu: adında IP_ADDRESS geçen, kimlik + ad kolonlu dolu CDM tabloları
    async Task<Dictionary<string, HashSet<string>>> DugumIpAsync(OracleConnection c, Sema0 s, Dictionary<string, Ci> bilgi, List<string> dugumler, List<string>? tani, CancellationToken ct)
    {
        var sonuc = dugumler.ToDictionary(x => x, x => new HashSet<string>(bilgi.TryGetValue(x, out var b) ? b.Ip : []), StringComparer.OrdinalIgnoreCase);
        int kolondan = sonuc.Count(kv => kv.Value.Count > 0);
        var ipTab = s.Kolon.Where(t => Regex.IsMatch(t.Key, "IP_?ADDRESS", RegexOptions.IgnoreCase) && t.Value.ContainsKey(s.Id) && t.Value.ContainsKey(s.AdK) && !s.Bos.Contains(t.Key))
            .Select(t => t.Key).ToList();
        tani?.Add($"IP adresi tabloları: {(ipTab.Count == 0 ? "yok" : string.Join(", ", ipTab))} · düğüm IP kolonundan: {kolondan} sunucu");
        var gecerli = dugumler.Where(IdGecerli).ToList();
        if (ipTab.Count == 0 || gecerli.Count == 0) return sonuc;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int sn = C.GetValue("IpSureSaniye", 180);
        void Ekle(string dugum, string ad) { foreach (Match m in _ipBul.Matches(ad)) if (IpGecerli(m.Value) && sonuc.TryGetValue(dugum, out var h)) h.Add(m.Value); }
        int ustten = 0, bagdan = 0;
        foreach (var t in ipTab)
        {
            var k = s.Kolon[t];
            if (s.Ust != null && k.ContainsKey(s.Ust))
                foreach (var parca in gecerli.Chunk(1000))
                {
                    if (sw.Elapsed.TotalSeconds > sn) break;
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = $"SELECT {s.Ust}, {s.AdK} FROM {Sema}.{t} WHERE {s.Ust} IN ({string.Join(",", parca.Select(x => Lit(k[s.Ust], x)))})";
                    cmd.CommandTimeout = SorguSn;
                    try { using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) { Ekle(Deger(r.GetValue(0)), Deger(r.GetValue(1))); ustten++; } }
                    catch (OracleException ex) { tani?.Add($"  {t} üst kolonu: {ex.Message}"); break; }
                }
            foreach (var bt in s.Bag)
                foreach (var (ben, karsi) in new[] { ("END1_ID", "END2_ID"), ("END2_ID", "END1_ID") })
                    foreach (var parca in gecerli.Chunk(1000))
                    {
                        if (sw.Elapsed.TotalSeconds > sn) { tani?.Add($"  IP araması {sn} sn sınırında kesildi"); break; }
                        using var cmd = c.CreateCommand();
                        cmd.CommandText = $"SELECT b.{ben}, i.{s.AdK} FROM {Sema}.{bt} b JOIN {Sema}.{t} i ON i.{s.Id} = b.{karsi} " +
                                          $"WHERE b.{ben} IN ({string.Join(",", parca.Select(x => Lit(s.Kolon[bt][ben], x)))})";
                        cmd.CommandTimeout = SorguSn;
                        try { using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) { Ekle(Deger(r.GetValue(0)), Deger(r.GetValue(1))); bagdan++; } }
                        catch (OracleException ex) { tani?.Add($"  {bt} ↔ {t}: {ex.Message}"); break; }
                    }
        }
        tani?.Add($"Sunucu IP'leri: IP kaydının üst kolonundan {ustten}, bağ tablolarından {bagdan} satır · IP'si bulunan sunucu {sonuc.Count(kv => kv.Value.Count > 0)} / {sonuc.Count}");
        return sonuc;
    }

    // Belge → port: UCMDB binding > ERT WEBSITE (IP + port) ↔ site / klasör adı > IIS logundaki uygulama (/app) ↔ klasör / sanal dizin adı > web.config içindeki baseAddress portu
    public List<UcmdbPortlu> PortAta(string ip, List<UcmdbBelge> l, EnvanterSnapshot env, List<HttpSysIis>? iis = null)
    {
        var web = env.AppsOnIp(ip).Where(h => !string.IsNullOrWhiteSpace(h.Website) && !string.IsNullOrWhiteSpace(h.Port))
            .Select(h => (W: h.Website!.Trim(), P: h.Port!.Trim())).Distinct().ToList();
        var sonuc = new List<UcmdbPortlu>();
        foreach (var b in l)
        {
            var adl = b.Adlar ?? [];
            if (b.Portlar is { Count: > 0 }) { sonuc.Add(new(b, b.Portlar, "UCMDB binding")); continue; }
            var w = web.Where(x => adl.Any(a => AdEs(a, x.W))).ToList();
            if (w.Count > 0) { sonuc.Add(new(b, w.Select(x => x.P).Distinct().OrderBy(PortSira).ToList(), $"ERT WEBSITE '{string.Join("', '", w.Select(x => x.W).Distinct())}'")); continue; }
            var iu = (iis ?? []).SelectMany(p => p.Uygulamalar.Select(u => (p.Port, u.Ad))).Where(x => x.Ad.Length > 1 && adl.Any(a => AdEs(a, x.Ad.TrimStart('/')))).ToList();
            if (iu.Count > 0) { sonuc.Add(new(b, iu.Select(x => x.Port).Distinct().OrderBy(PortSira).ToList(), $"IIS logu /{string.Join(", /", iu.Select(x => x.Ad.TrimStart('/')).Distinct())}")); continue; }
            if (_icerik.TryGetValue(b.Id, out var ic) && ic.Metin != null)
            {
                var gp = Ayristir(ic.Metin, b.Ad).Where(a => a.Tur == "gelen" && a.Port != null).Select(a => a.Port!).Distinct().ToList();
                if (gp.Count > 0) { sonuc.Add(new(b, gp.OrderBy(PortSira).ToList(), "web.config baseAddress")); continue; }
            }
            sonuc.Add(new(b, [], null));
        }
        return sonuc;
    }
    static int PortSira(string p) => int.TryParse(p, out int n) ? n : 99999;

    // Pencere için: belgelerin içeriği önbellekte yoksa tek bağlantıyla okunur (baseAddress portu için)
    public async Task IcerikleriYukleAsync(List<UcmdbBelge> l, CancellationToken ct)
    {
        var eksik = l.Where(b => !_icerik.ContainsKey(b.Id)).Take(C.GetValue("PencereMaxBelge", 200)).ToList();
        if (eksik.Count == 0) return;
        using var c = await AcAsync(ct);
        var s = await SemaAsync(c, ct);
        var adOf = eksik.ToDictionary(b => b.Id, b => b.Ad, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, bayt) in await IcerikAsync(c, s, eksik.Select(b => b.Id).ToList(), ct))
        {
            var (metin, bicim) = Coz(bayt, adOf.GetValueOrDefault(id) ?? "");
            int n = 0;
            if (metin != null) (metin, n) = Maskele(metin);
            _icerik[id] = (DateTime.UtcNow, metin, bicim, bayt.Length, n);
        }
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

    // ---------- döküm (test: kolonlar ve değerler; UCMDB'ye yazılmaz, LOB kolonları okunmaz, gizli adlı kolonlar maskeli) ----------
    static readonly HashSet<string> _lob = ["BLOB", "CLOB", "NCLOB", "LONG", "LONG RAW", "BFILE", "XMLTYPE"];
    static bool GizliKolon(string k) => Regex.IsMatch(k, "PASS|PWD|SECRET|TOKEN|CREDENTIAL|PRIVATE", RegexOptions.IgnoreCase);

    async Task<List<Dictionary<string, string>>> SatirlarAsync(OracleConnection c, Sema0 s, string t, string? kosul, int max, CancellationToken ct)
    {
        var kol = s.Kolon[t].Where(kv => !_lob.Contains(kv.Value) && !kv.Value.StartsWith("TIMESTAMP WITH", StringComparison.Ordinal)).Select(kv => kv.Key).ToList();
        var l = new List<Dictionary<string, string>>();
        if (kol.Count == 0) return l;
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {string.Join(", ", kol)} FROM {Sema}.{t} WHERE {(kosul ?? "1=1")} AND ROWNUM <= {max}";
        cmd.CommandTimeout = SorguSn;
        using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < kol.Count; i++)
            {
                string v;
                try { v = r.IsDBNull(i) ? "" : Deger(r.GetValue(i)); } catch (Exception ex) { v = "(okunamadı: " + ex.GetType().Name + ")"; }
                if (v == "") continue;
                if (GizliKolon(kol[i])) v = "***";
                d[kol[i]] = v.Length > 300 ? v[..300] + "…" : v;
            }
            l.Add(d);
        }
        return l;
    }

    // tablo verilirse: kolonlar + örnek satırlar; verilmezse zincir dökümü (IP'nin ya da örnek web.config'lerin kapsayıcıları, bağları, bağlı uç noktalar)
    public async Task<string> DokumAsync(string? tablo, string? ip, EnvanterSnapshot? env, int ornek, CancellationToken ct)
    {
        var sb = new StringBuilder();
        void S(string x) => sb.AppendLine(x);
        if (!Acik) return "Ucmdb:Baglanti boş.";
        using var c = await AcAsync(ct);
        var s = await SemaAsync(c, ct);
        ornek = Math.Clamp(ornek, 1, 50);
        void Yaz(string t, Dictionary<string, string> d, string girinti) { foreach (var (k, v) in d) S($"{girinti}{k} = {v}"); }

        if (!string.IsNullOrWhiteSpace(tablo))
        {
            string t = Ad(tablo);
            if (!s.Kolon.ContainsKey(t)) return $"{Sema}.{t} bulunamadı (yalnız {Sema} şemasındaki CDM% tabloları dökülebilir).";
            S($"{Sema}.{t} — {s.Kolon[t].Count} kolon{(s.Bos.Contains(t) ? " (istatistiğe göre BOŞ)" : "")}");
            foreach (var (k, tip) in s.Kolon[t]) S($"  {k,-40} {tip}{(_lob.Contains(tip) ? "  (LOB — dökümde okunmaz)" : "")}{(PortKolonu(k, tip) ? "  ← port / binding kolonu" : "")}");
            string? kosul = t.Equals(Tablo, StringComparison.OrdinalIgnoreCase) ? "(" + string.Join(" OR ", Desenler.Select(x => $"LOWER({s.AdK}) LIKE '%{x.Replace("%", "")}%'")) + ")" : null;
            var rows = await SatirlarAsync(c, s, t, kosul, ornek, ct);
            S($"\nÖrnek {rows.Count} satır (boş değerler yazılmaz):");
            for (int i = 0; i < rows.Count; i++) { S($"--- {i + 1}"); Yaz(t, rows[i], "  "); }
            return sb.ToString();
        }

        // zincir dökümü
        var belgeler = new List<(string Id, string Ad)>();
        if (ip != null && env != null && _dizin != null)
            belgeler = Belgeler([ip], env).GetValueOrDefault(ip)?.Take(ornek).Select(b => (b.Id, b.Ad)).ToList() ?? [];
        if (belgeler.Count == 0)
        {
            if (ip != null) S(_dizin == null ? $"(tam liste hazır değil; {ip} yerine örnek belgeler)" : $"({ip} için belge yok; örnek belgeler)");
            var rows = await SatirlarAsync(c, s, Tablo, "(" + string.Join(" OR ", Desenler.Select(x => $"LOWER({s.AdK}) LIKE '%{x.Replace("%", "")}%'")) + ")", ornek, ct);
            belgeler = rows.Where(r => r.ContainsKey(s.Id)).Select(r => (r[s.Id], r.GetValueOrDefault(s.AdK) ?? "")).ToList();
        }
        var dugumAd = C.GetSection("DugumTablolari").Get<string[]>() is { Length: > 0 } dt ? dt.Select(Ad).ToHashSet() :
            ["CDM_NODE_1", "CDM_HOST_NODE_1", "CDM_NT_1", "CDM_UNIX_1", "CDM_COMPUTER_1", "CDM_HOST_1"];
        var aday = s.Kolon.Where(t => t.Value.ContainsKey(s.Id) && !s.Bos.Contains(t.Key)).Select(t => t.Key)
            .OrderBy(t => dugumAd.Contains(t) ? 0 : _oncelik.IsMatch(t) ? 1 : 2).ThenBy(t => t).ToList();
        var baglar = s.Kolon.Where(t => t.Value.ContainsKey("END1_ID") && t.Value.ContainsKey("END2_ID") && !s.Bos.Contains(t.Key)).Select(t => t.Key).ToList();
        var ucTablo = aday.Where(t => Regex.IsMatch(t, "ENDPOINT|PORT|BINDING|URL|URI", RegexOptions.IgnoreCase)).ToList();
        int sure = C.GetValue("DokumSeviyeSaniye", 40);
        string idTip = s.Kolon[Tablo][s.Id];
        var bulunan = new Dictionary<string, List<(string T, Dictionary<string, string> D)>>(StringComparer.OrdinalIgnoreCase);

        async Task<List<(string T, Dictionary<string, string> D)>> BulAsync(string id, List<string> tablolar)
        {
            if (bulunan.TryGetValue(id, out var x)) return x;
            var l = new List<(string, Dictionary<string, string>)>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var t in tablolar)
            {
                if (sw.Elapsed.TotalSeconds > sure) { l.Add(("(süre)", new() { ["not"] = $"{sure} sn sınırı: kalan tablolara bakılmadı" })); break; }
                try { foreach (var d in await SatirlarAsync(c, s, t, $"{s.Id} = {Lit(s.Kolon[t].GetValueOrDefault(s.Id) ?? idTip, id)}", 1, ct)) l.Add((t, d)); }
                catch (OracleException) { }
                // düğüm bulunduysa daha fazla tabloya bakmaya gerek yok (sınıf tabloları birden çok olabilir; ilk 3 isabet yeter)
                if (l.Count >= 3 || l.Any(z => dugumAd.Contains(z.Item1))) break;
            }
            return bulunan[id] = l;
        }

        foreach (var (id, ad) in belgeler)
        {
            S($"\n=================== BELGE {ad} (id {id})");
            foreach (var d in await SatirlarAsync(c, s, Tablo, $"{s.Id} = {Lit(idTip, id)}", 1, ct)) Yaz(Tablo, d, "  ");
            string? simdi = id;
            var gorulen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int seviye = 0; seviye < 6 && simdi != null && IdGecerli(simdi) && gorulen.Add(simdi); seviye++)
            {
                if (seviye > 0)
                {
                    var bl = await BulAsync(simdi, aday);
                    S($"\n  --- seviye {seviye}: kimlik {simdi} → {(bl.Count == 0 ? "hiçbir tabloda bulunamadı" : string.Join(", ", bl.Select(z => z.T)))}");
                    foreach (var (t, d) in bl) { S($"    [{t}]"); Yaz(t, d, "      "); }
                    if (bl.Any(z => dugumAd.Contains(z.T) || z.D.ContainsKey("A_PRIMARY_DNS_NAME"))) { S("    (sunucu düğümü — durduruldu)"); break; }
                }
                // bu kaydın bağları (her iki yön): bağlı uç nokta / port / URL kayıtları ayrıca dökülür
                string? ust = null;
                foreach (var bt in baglar)
                {
                    string tip = s.Kolon[bt]["END1_ID"];
                    try
                    {
                        foreach (var d in await SatirlarAsync(c, s, bt, $"(END1_ID = {Lit(tip, simdi)} OR END2_ID = {Lit(tip, simdi)})", 20, ct))
                        {
                            string e1 = d.GetValueOrDefault("END1_ID") ?? "", e2 = d.GetValueOrDefault("END2_ID") ?? "";
                            bool ben1 = e1.Equals(simdi, StringComparison.OrdinalIgnoreCase);
                            string karsi = ben1 ? e2 : e1;
                            S($"    bağ [{bt}] {(ben1 ? "bu → " : "← ")}{karsi}{(ben1 ? "" : " (kapsayıcı / kullanan)")}");
                            if (!ben1 && Regex.IsMatch(bt, "COMPOSITION|CONTAIN", RegexOptions.IgnoreCase)) ust ??= karsi;
                            if (ben1 && ucTablo.Count > 0 && IdGecerli(karsi))
                                foreach (var (t, ud) in await BulAsync(karsi, ucTablo)) { S($"      → [{t}]"); Yaz(t, ud, "        "); }
                        }
                    }
                    catch (OracleException ex) { S($"    bağ [{bt}]: {ex.Message}"); }
                }
                // üst: kayıttaki A_ROOT_CONTAINER (ya da ayarlı üst kolonu), yoksa composition / containment bağı
                string? kolUst = null;
                var kayitlar = seviye == 0 ? (await SatirlarAsync(c, s, Tablo, $"{s.Id} = {Lit(idTip, id)}", 1, ct)).Select(d => (Tablo, d)).ToList() : bulunan.GetValueOrDefault(simdi) ?? [];
                if (s.Ust != null) kolUst = kayitlar.Select(z => z.Item2.GetValueOrDefault(s.Ust)).FirstOrDefault(v => !string.IsNullOrEmpty(v));
                simdi = kolUst ?? ust;
                if (simdi == null) S("    (üst bulunamadı — durduruldu)");
            }
        }
        return sb.ToString();
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
            var (hz, hh, ha, hg) = DizinDurumu();
            S($"Tam liste: {(hz ? $"hazır · {_dizin!.Belgeler.Count} belge, sunucusu bulunan {_dizin.Belgeler.Count(b => b.Sunucu != null)} · {_dizin.Zaman.ToLocalTime():dd.MM HH:mm}" : ha != null ? $"hazırlanıyor ({hg} sn): {ha}" : hh != null ? "HATA: " + hh : "hazırlanmadı")}");
            foreach (var n in _dizin?.Notlar ?? []) S("  " + n);
            S("5 örnek belgeyle zincir denemesi (seviye başına en çok 20 sn):");
            var notlar = new List<string>();
            Dizin d;
            try { d = await DizinOlusturAsync(c, s, notlar, ct, 5, null, 20); }
            catch (Exception ex) when (ex is not OperationCanceledException) { S("Zincir: " + ex.Message); return sb.ToString(); }
            foreach (var n in notlar) S(n);
            S("Zincir örnekleri:");
            foreach (var b in d.Belgeler.Take(5)) S($"  {b.Ad} → {(b.Zincir.Count == 0 ? "(üst bulunamadı)" : string.Join(" → ", b.Zincir))} ⇒ sunucu: {b.Sunucu ?? "?"}");
            if (ip != null && env != null && _dizin == null) S($"IP {ip}: tam liste hazır olmadığı için belgeleri aranamadı.");
            if (ip != null && env != null && _dizin != null)
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
