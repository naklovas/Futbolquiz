using System.Text.RegularExpressions;

// ---------------------------------------------------------------------------
// Açıklık taraması (wwwroot/zafiyet.html). Kullanıcı: "tek tek sunucu işimizi görmez, CB'den toplu almamız lazım";
// "sadece Java için değil herhangi bir process için yapalım" → KURAL tabanlı: kural = aranan exe adları + sürümün nasıl okunacağı +
// etkilenen / düzeltilmiş sürümler + geçici önlem ifadeleri. Hazır kurallar Zafiyet:Kurallar (boşsa varsayılan: CVE-2026-114938
// "Java4Shell"), ayrıca sayfadan anlık kural: exe adları (+ isteğe bağlı en düşük etkilenen / düzeltilmiş sürüm).
//  1) Carbon Black ağ olayları (TOPLU, tüm sunucular, son Zafiyet:Saat=24 saat): kuralın süreçleri local_ip × yön × dinlediği port ×
//     süreç yolu — bağlantı, istemci sayısı, örnek istemciler (dış IP var mı), bilgisayar adı.
//  2) Carbon Black süreç başlangıçları (TOPLU, son Zafiyet:SurecSaat=168 saat): bilgisayar × süreç yolu → komut satırı (önlem var mı,
//     -jar adı, komut satırındaki sürümlü yol).
//  3) Servis imzası kaydı (DF_SERVIS_IMZA, yeni sorgu yok): CB taramasında görünmeyen sunucuların aynı süreçleri.
// Sürüm exe yolundan, yoksa komut satırından (Java kuralında jdk-17.0.10 / jdk1.8.0_202 / java-17-openjdk-… kalıpları; diğerlerinde kuralın
// SurumDesenleri ya da yoldaki ilk a.b[.c] sayısı). Sonuç bellekte (son tarama); DeltaFlow hiçbir yere yazmaz.
// ---------------------------------------------------------------------------
// Min = etkilenen ilk sürüm, Duzeltilmis = düzeltmeyi içeren ilk sürüm, Son = etkilenen SON sürüm, dahil (Oracle CPU duyuruları: "Supported versions that are affected are X")
// Dal: sürüm bu önekle başlıyorsa (ör. "1.30" → 1.30.x) bu aralık geçerli — NGINX gibi ana sürümü hep 1 olan ürünlerde dal başına düzeltilmiş sürüm
record ZfAralik(int? Ana, string? Min, string? Duzeltilmis, string? Son = null, string? Dal = null);
record ZfKural(string Id, string Ad, string? Cve, string? Aciklama, List<string> Exeler, List<string>? SurumDesenleri = null,
    int? AnaMin = null, List<ZfAralik>? Araliklar = null, List<string>? Onlem = null, string? OnlemNot = null, bool Java = false,
    bool DestekDisiEtkilenir = true, string? Anahtar = null, string? Kaynak = null, string? Kaydeden = null, DateTime? Tarih = null,
    List<string>? NessusAnahtarlar = null);   // Nessus'ta plugin adında aranacak kelimeler (kurulu yazılım: "… Detection" bilgi bulguları)
record ZfPort(string Port, long Baglanti, int Istemci, string? Ert, List<string> Vip, List<string> Dis);
record ZfSatir(string? Ip, string? Sunucu, string? Bilgisayar, string? Kurum, string? Segment, bool Test, List<string> Uygulamalar, string? Muhafiz,
    string Exe, string Yol, string? Urun, string? Surum, string? SurumKaynak, string Durum, string DurumNot,
    List<ZfPort> Portlar, long Giden, string Onlem, string? Komut, string? Jar, string? Son, string? Baslangic, string Oncelik, string Kaynak,
    string? Vlan = null, string? Domain = null, string? Cidr = null, string? Dagitim = null, string? Nessus = null,
    bool Dmz = false, List<string>? DisGiden = null, int DisGidenSay = 0, string? Repo = null,
    List<string>? NessusPortlar = null);   // Nessus bulgularının portları (CB portu olmayan kutularda uygulama tahmini bu portlarla)   // Repo: Nessus bulgusunun repository adı (id)   // Dmz: Nessus DMZ asset'inde; DisGiden: CB'de süreç dış IP'lere bağlanıyor ("derin dış")
record ZfIs(string Durum, string Asama, DateTime Baslangic, DateTime? Bitis, string? Hata, string? Kaydeden, int Saat, int SurecSaat, ZfKural Kural, string? Kapsam = null,
    int? KayitId = null, DateTime? KayitTarihi = null, bool Kayittan = false, bool Dmz = false);
// Kayıtlı tarama (DF_ZAFIYET_TARAMA): liste satırı (sonuç gövdesi yok) ve gzip JSON gövdesi
record ZfKayit(int Id, string KuralId, string? KuralAd, string? Cve, string? Kapsam, int Saat, int Satir, Dictionary<string, int> Oncelik, DateTime Tarih, string? Kaydeden);
record ZfKayitVeri(ZfIs Is, List<ZfSatir> Satirlar, List<string> Mesajlar);
record ZfKayitListe(List<ZfKayit> Kayitlar, string? Uyari);
// Sayfadan elle / AI ile doldurulmuş kural: exe adları virgülle, önlem ifadeleri virgülle (DÜZ METİN; regex'e kaçışlanır)
record ZfOzel(string? Ad, string? Cve, string? Aciklama, string? Exeler, List<ZfAralik>? Araliklar, string? Onlem, bool DigerEtkilenir, string? NessusAnahtarlar = null);
// Ipler: yalnız bu IP'ler (tekil tarama); Ekle: sonuç mevcut listeye eklenir (o IP'lerin eski satırları değişir); SonKural: son taramanın kuralı
// Dmz: yalnız Nessus DMZ asset'indeki IP'ler (Zafiyet:DmzAsset)
record ZafiyetIstek(int? Saat, string? Kural, ZfOzel? Ozel, string? Ipler = null, bool Ekle = false, bool SonKural = false, bool Dmz = false);
record ZafiyetAiIstek(string? Metin);
// AI ile doldurma sonucu (kullanıcı onaylayıp düzenler; tarama kendiliğinden başlamaz)
record ZfAiExe(string Ad, string? Neden, string Kaynak, int Sunucu, List<string> Ornek);
record ZfAiSonuc(string? Urun, string? Cve, string? Ozet, bool Kutuphane, string? KutuphaneNot, List<ZfAiExe> Exeler, List<ZfAralik> Araliklar,
    List<string> Onlem, bool DigerEtkilenir, List<string> Uyarilar, string? EnvanterTarihi);
// Mesgul: başka bir kullanıcının taraması çalışıyor (aynı anda tek tarama; bu kullanıcı kendi görünümünü ve kayıtlarını kullanabilir)
record ZfSonuc(ZfIs? Is, List<ZfSatir> Satirlar, List<string> Mesajlar, List<ZfKural> Kurallar, string? Mesgul = null);

sealed class ZafiyetService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ServisImzaService imza, IslevService islev, NessusService nessus,
    ILogger<ZafiyetService> log)
{
    readonly object _gate = new();
    ZfIs? _is;
    List<ZfSatir> _satir = [];
    List<string> _mesaj = [];
    CancellationTokenSource? _cts;

    // Kullanıcı başına görünüm (kullanıcı: "herkes kendi kayıtlı taramalarını görmeli"): _is / _satir / _mesaj çalışan (son) işin canlı durumu, sahibi _isSahip;
    // iş bitince ya da kullanıcı kayıttan açınca o kullanıcının görünümüne (_gor) yazılır. Aynı anda tek tarama (Carbon Black sorguları ağır).
    string? _isSahip;
    readonly Dictionary<string, (ZfIs? Is, List<ZfSatir> Satir, List<string> Mesaj)> _gor = new(StringComparer.OrdinalIgnoreCase);
    static string Kul(string? u) => (u ?? "").Split('\\').Last().Trim().ToLowerInvariant();
    static bool Ayni(string? a, string? b) => Kul(a) == Kul(b);
    // kayıt bu kullanıcının mı: yetki kapalıyken (kullanıcı yok) ya da sahibi bilinmeyen eski kayıtta herkes
    static bool Sahibi(string? kaydeden, string? user) => string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(kaydeden) || Ayni(kaydeden, user);
    (ZfIs? Is, List<ZfSatir> Satir, List<string> Mesaj) Gorunum(string? user)
    {
        // lock (_gate) içinde çağrılır
        if (_is != null && Ayni(_isSahip, user) && (_is.Durum == "calisiyor" || !_gor.ContainsKey(Kul(user)))) return (_is, _satir, _mesaj);
        return _gor.TryGetValue(Kul(user), out var g) ? g : (null, [], []);
    }
    void IsBitti() { if (_is != null) _gor[Kul(_isSahip)] = (_is, _satir, _mesaj); }   // lock (_gate) içinde

    public async Task<ZfSonuc> SonucAsync(CancellationToken ct, string? user = null)
    {
        var k = await KurallarAsync(ct);
        lock (_gate)
        {
            var g = Gorunum(user);
            string? mesgul = _is is { Durum: "calisiyor" } && !Ayni(_isSahip, user)
                ? $"{_is.Kaydeden ?? "başka bir kullanıcı"} şu an tarama yapıyor ({(_is.Kural.Cve != null ? _is.Kural.Cve + " · " : "")}{_is.Kural.Ad}, {_is.Baslangic:HH:mm} başladı); aynı anda tek tarama çalışır — bitince yeni tarama başlatabilirsiniz. Bu sırada kendi kayıtlı taramalarınızı açabilirsiniz."
                : null;
            return new ZfSonuc(g.Is, g.Satir, [.. g.Mesaj, .. new[] { _kayitUyari, _taramaUyari }.OfType<string>()], k, mesgul);
        }
    }
    // Yalnız taramayı başlatan durdurabilir (yetki kapalıyken herkes)
    public bool Durdur(string? user = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(user) && _is is { Durum: "calisiyor" } && !Ayni(_isSahip, user)) return false;
            _cts?.Cancel(); return true;
        }
    }

    // ---------------- Kurallar ----------------
    static readonly ZfKural Java4Shell = new("java4shell", "Java4Shell — OpenJDK Base64 çözücüsünde heap taşması", "CVE-2026-114938",
        "Java 17.0.6 ve sonrası, 18+ (21, 25, 27); x86-64 + AVX-512 VBMI. Java 8 / 11 etkilenmez. AVX-512 bilgisi yok: sürümü aralıktaki her süreç etkilenen sayılır.",
        ["java", "javaw", "tomcat*", "prunsrv"], Java: true, AnaMin: 17,
        Araliklar: [new(17, "17.0.6", "17.0.21.1"), new(21, null, "21.0.13.1"), new(25, null, "25.0.5.1"), new(27, null, "27.0.1.1")],
        Onlem: [@"-XX:-UseBASE64Intrinsics", @"-XX:UseAVX=[0-2]\b"], OnlemNot: "-XX:-UseBASE64Intrinsics, -XX:UseAVX=2", NessusAnahtarlar: JavaNessus);
    // CVE-2026-123007 (HTTP/2 tamsayı taşması) + CVE-2026-123633 (HTTP/2 use-after-free): NGINX OSS 1.9.5–1.31.8; düzeltilmiş 1.30.7 (stable) / 1.31.9 (mainline);
    // diğer dallar (1.28, 1.29…) yamasız → son etkilenen 1.31.8. NGINX Plus R37 ayrıca (37.0.8.1 / 37.2.0.1 / 37.3.0.1 — sürüm "nginx-plus-r37" biçiminde, yoldan çıkmaz).
    static readonly ZfKural NginxHttp2 = new("nginx-http2-2026", "NGINX HTTP/2 uzaktan kod çalıştırma", "CVE-2026-123007, CVE-2026-123633",
        "NGINX Open Source 1.9.5–1.31.8 ve NGINX Plus; HTTP/2 açık ve upstream'e (proxy) yönlendiren yapılandırmada. Düzeltilmiş: 1.30.7 (stable), 1.31.9 (mainline). "
        + "Geçici önlem: etkilenen dinleyicilerde HTTP/2'yi kapatmak — yapılandırma dosyasında, komut satırında görünmez. OpenResty / Kong / ingress gibi NGINX tabanlı ürünler de etkilenir.",
        ["nginx", "openresty"],
        SurumDesenleri: [@"nginx[-_/]?(\d+\.\d+\.\d+)", @"openresty[-_/]?(\d+\.\d+\.\d+)", @"nginx/(\d+\.\d+\.\d+)"],
        Araliklar: [new(null, null, "1.30.7", Dal: "1.30"), new(null, null, "1.31.9", Dal: "1.31"), new(null, "1.9.5", null, Son: "1.31.8")],
        DestekDisiEtkilenir: false, NessusAnahtarlar: ["nginx", "OpenResty"]);
    static readonly ZfKural[] Yerlesik = [Java4Shell, NginxHttp2];

    static readonly List<string> JavaNessus = ["Java", "OpenJDK", "JDK", "JRE", "Corretto", "Temurin", "Zulu", "GraalVM"];
    // Kuralda Nessus kelimesi yoksa (eski kayıtlı kurallar da): Java kuralında Java kelimeleri, diğerlerinde exe adları
    static List<string> NessusKelimeleri(ZfKural k) => k.NessusAnahtarlar is { Count: > 0 } l ? l
        : k.Java ? JavaNessus : Temiz(k.Exeler).Where(e => !e.Contains('*') && e.Length >= 4).Take(6).ToList();

    // Yerleşik / ayardaki kurallar + kayıtlı kurallar (DF_ZAFIYET_KURAL; aynı Id'de kayıtlı olan geçerli)
    List<ZfKural> AyarKurallari()
    {
        var l = cfg.GetSection("Zafiyet:Kurallar").Get<List<ZfKural>>()?.Where(k => !string.IsNullOrWhiteSpace(k.Id) && k.Exeler is { Count: > 0 })
            .Select(k => k with { Kaynak = "ayar" }).ToList();
        var ayar = l ?? [];
        // yerleşik kurallar her zaman listede (ayarda aynı Id'li kural varsa o geçerli)
        return ayar.Concat(Yerlesik.Where(y => !ayar.Any(a => a.Id.Equals(y.Id, StringComparison.OrdinalIgnoreCase))).Select(y => y with { Kaynak = "yerleşik" })).ToList();
    }
    public async Task<List<ZfKural>> KurallarAsync(CancellationToken ct)
    {
        var kay = await KayitliAsync(ct);
        return kay.Concat(AyarKurallari().Where(a => !kay.Any(k => k.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    // Exe adı → regex (yalnız harf / rakam . _ - ve * joker; .exe isteğe bağlı). SPL'de çift kaçışlı, C#'ta tek.
    static string ExeAd0(string e) => Regex.Replace(e.Trim().ToLowerInvariant(), @"\.exe$", "");
    static List<string> Temiz(IEnumerable<string> exeler) => exeler.Select(ExeAd0).Where(e => Regex.IsMatch(e, @"^[a-z0-9_.\-*]+$")).Distinct().ToList();
    static string ExeDesen(ZfKural k, bool spl)
    {
        string bs = spl ? @"\\\\" : @"\\";
        var adlar = Temiz(k.Exeler).Select(e => e.Replace(".", "[.]").Replace("*", "[a-z0-9_.-]*"));
        return $@"(^|[{bs}/])({string.Join("|", adlar)})([.]exe)?$";
    }
    static string Anahtar(ZfKural k) => k.Anahtar ?? "(" + string.Join(" OR ", Temiz(k.Exeler).Select(e => e.Contains('*') ? e : $"\"{e}\"")) + ")";

    // "8u511b33-perf" → 1.8.0.511 (Java 8 / 7 biçimi); düz sürüm olduğu gibi; geçersizse null
    static string? NormSurum(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        if (Regex.Match(v, @"^(?:1\.)?([5-8])u(\d+)", RegexOptions.IgnoreCase) is { Success: true } m) return $"1.{m.Groups[1].Value}.0.{m.Groups[2].Value}";
        return Regex.IsMatch(v, @"^\d+([._\-+]\d+)*$") ? v : null;
    }

    static List<int> Parca(string v) => Regex.Split(v, @"[._+\-]").Select(x => int.TryParse(x, out int n) ? n : -1).TakeWhile(n => n >= 0).ToList();
    static int Karsilastir(string a, string b)
    {
        var x = Parca(a); var y = Parca(b);
        for (int i = 0; i < Math.Max(x.Count, y.Count); i++)
        {
            int p = i < x.Count ? x[i] : 0, q = i < y.Count ? y[i] : 0;
            if (p != q) return p.CompareTo(q);
        }
        return 0;
    }
    // Java'da "1.8.0_202" → 8; diğerlerinde ilk sayı
    static int Ana(ZfKural k, string v) { var p = Parca(v); return p.Count == 0 ? -1 : k.Java && p[0] == 1 && p.Count > 1 ? p[1] : p[0]; }

    static (string Durum, string Not) Degerlendir(ZfKural k, string? surum)
    {
        bool kuralVar = k.Araliklar is { Count: > 0 } || k.AnaMin != null;
        if (!kuralVar) return ("bilgi", surum != null ? $"sürüm {surum} (bu aramada etkilenen sürüm aralığı verilmedi)" : "sürüm aralığı verilmedi");
        if (surum == null) return ("bilinmiyor", "sürüm yoldan / komut satırından okunamadı");
        int ana = Ana(k, surum);
        if (ana < 0) return ("bilinmiyor", "sürüm çözülemedi");
        if (k.AnaMin is { } am && ana < am) return ("etkilenmez", $"{ana} < {am}");
        bool altYok = Parca(surum).Count < 2;
        var a = k.Araliklar?.FirstOrDefault(x => x.Dal != null && (surum == x.Dal || surum.StartsWith(x.Dal + ".")))
            ?? k.Araliklar?.FirstOrDefault(x => x.Dal == null && x.Ana == ana) ?? k.Araliklar?.FirstOrDefault(x => x.Dal == null && x.Ana == null);
        if (a == null)
            return k.DestekDisiEtkilenir && k.AnaMin != null ? ("etkilenen", $"{ana} için düzeltilmiş sürüm yok (destek dışı sürüm)") : ("etkilenmez", $"{ana} aralıkta değil");
        if (a.Dal != null && surum == a.Dal) return ("olasi", $"{surum} — {a.Dal} dalı, alt sürüm bilinmiyor" + (a.Duzeltilmis != null ? $"; düzeltilmiş {a.Duzeltilmis}" : ""));
        if (altYok) return ("olasi", $"{surum} — alt sürüm bilinmiyor" + (a.Duzeltilmis != null ? $"; düzeltilmiş {a.Duzeltilmis}" : a.Son != null ? $"; son etkilenen {a.Son}" : ""));
        if (a.Min != null && Karsilastir(surum, a.Min) < 0) return ("etkilenmez", $"{surum} < {a.Min} (etkilenen ilk sürüm)");
        if (a.Duzeltilmis != null && Karsilastir(surum, a.Duzeltilmis) >= 0) return ("yamali", $"{surum} ≥ {a.Duzeltilmis} (dağıtıcının yamasıyla doğrulayın)");
        if (a.Son != null)
            return Karsilastir(surum, a.Son) <= 0 ? ("etkilenen", $"{surum} ≤ {a.Son} (son etkilenen sürüm)")
                : ("yamali", $"{surum} > {a.Son} (son etkilenen sürümden yeni; dağıtıcının yamasıyla doğrulayın)");
        return ("etkilenen", a.Duzeltilmis != null ? $"{surum} < {a.Duzeltilmis}" : $"{surum} etkilenen aralıkta (düzeltilmiş sürüm verilmedi)");
    }

    // ---------------- Sürüm / ürün ----------------
    static readonly Regex[] JavaSurumRx =
    [
        new(@"java-(\d{1,2})-openjdk-?(\d+(?:\.\d+){1,4})", RegexOptions.IgnoreCase),        // java-17-openjdk-17.0.9.0.9-1.el8
        new(@"jdk-?(1\.\d\.\d+(?:_\d+)?|\d{1,2}(?:\.\d+){0,4})", RegexOptions.IgnoreCase),    // jdk-17.0.10, jdk1.8.0_202, jdk-21
        new(@"jre-?(1\.\d\.\d+(?:_\d+)?|\d{1,2}(?:\.\d+){0,4})", RegexOptions.IgnoreCase),
        new(@"(?:zulu|corretto|temurin|semeru|openjdk|microsoft-jdk|graalvm[\w-]*?|java)[-_]?(\d{1,2}(?:\.\d+){1,4})", RegexOptions.IgnoreCase),
        new(@"java-(\d{1,2})-openjdk", RegexOptions.IgnoreCase),                              // Debian: java-17-openjdk-amd64
    ];
    // Java dağıtımı exe / JVM yolundan (kesin değil: "c:\program files\java\jdk…" çoğunlukla Oracle'dır ama başka kurulumlar da oraya kurulabilir)
    static readonly (string Desen, string Ad)[] Dagitimlar =
    [
        (@"eclipse adoptium|temurin|adoptopenjdk", "Eclipse Temurin"),
        (@"amazon corretto|corretto", "Amazon Corretto"),
        (@"[\\/]zulu|azul", "Azul Zulu"),
        (@"microsoft[\\/]jdk|msopenjdk|microsoft-jdk", "Microsoft OpenJDK"),
        (@"semeru|[\\/]ibm[\\/].*java|ibm-java|openj9", "IBM Semeru / IBM Java"),
        (@"liberica|bellsoft", "BellSoft Liberica"),
        (@"sapmachine", "SapMachine"),
        (@"graalvm", "GraalVM"),
        (@"java-\d+-openjdk-\d[^\\/]*\.el\d|redhat|red hat", "Red Hat OpenJDK"),
        (@"/usr/lib/jvm/java-\d+-openjdk-(amd64|arm64|i386)", "Debian / Ubuntu OpenJDK"),
        (@"java-\d+-openjdk", "OpenJDK (Linux paketi)"),
        (@"program files( \(x86\))?[\\/]java[\\/](jdk|jre)|/usr/java/(jdk|jre|latest)|/opt/oracle", "Oracle (muhtemel)"),
    ];
    static string? Dagitim(string? metin)
    {
        if (string.IsNullOrEmpty(metin)) return null;
        string m = metin.ToLowerInvariant();
        foreach (var (d, ad) in Dagitimlar) if (Regex.IsMatch(m, d)) return ad;
        return Regex.IsMatch(m, @"[\\/](jre|jbr|jdk)[\\/]bin[\\/]") ? "ürünle gelen (gömülü)" : null;
    }

    static readonly Regex GenelSurumRx = new(@"(?<![\d.])v?(\d+\.\d+(?:[._]\d+){0,3})(?![\d])", RegexOptions.IgnoreCase);
    static string? Surum(ZfKural k, string? metin)
    {
        if (string.IsNullOrEmpty(metin)) return null;
        if (k.SurumDesenleri is { Count: > 0 } d)
        {
            foreach (var p in d)
                try { if (Regex.Match(metin, p, RegexOptions.IgnoreCase) is { Success: true } m) return (m.Groups.Count > 1 ? m.Groups[1] : m.Groups[0]).Value.Replace('_', '.'); }
                catch (ArgumentException) { }
            return null;
        }
        if (k.Java)
        {
            foreach (var rx in JavaSurumRx)
                if (rx.Match(metin) is { Success: true } m)
                {
                    var gr = m.Groups[m.Groups.Count > 2 && m.Groups[2].Success ? 2 : 1];
                    string v = gr.Value.Replace('_', '.');
                    // Temurin / Microsoft OpenJDK klasörü "jdk-17.0.21.9-hotspot": son parça YAPI numarası (17.0.21+9) → atılır
                    // (yoksa 17.0.21.9 > 17.0.21.1 diye yamalı sayılırdı)
                    if (Regex.IsMatch(metin[(gr.Index + gr.Length)..], @"^-(hotspot|openj9)", RegexOptions.IgnoreCase) && Parca(v) is { Count: >= 4 } pp)
                        v = string.Join('.', pp.Take(pp.Count - 1));
                    if (Ana(k, v) is >= 5 and <= 40) return v;
                }
            return null;
        }
        return GenelSurumRx.Match(metin) is { Success: true } g ? g.Groups[1].Value.Replace('_', '.') : null;
    }

    static readonly (string Desen, string Ad)[] Urunler =
    [
        ("jira", "Atlassian Jira"), ("confluence", "Atlassian Confluence"), ("bitbucket", "Atlassian Bitbucket"), ("bamboo", "Atlassian Bamboo"),
        ("jenkins", "Jenkins"), ("keycloak", "Keycloak"), ("elasticsearch", "Elasticsearch"), ("logstash", "Logstash"), ("opensearch", "OpenSearch"),
        ("kafka", "Kafka"), ("zookeeper", "ZooKeeper"), ("solr", "Solr"), ("cassandra", "Cassandra"), ("sonarqube", "SonarQube"), ("nexus", "Nexus"),
        ("artifactory", "Artifactory"), ("weblogic", "WebLogic"), ("websphere", "WebSphere"), ("wlp", "WebSphere Liberty"), ("wildfly", "WildFly"),
        ("jboss", "JBoss"), ("catalina", "Tomcat"), ("tomcat", "Tomcat"), ("activemq", "ActiveMQ"), ("rabbitmq", "RabbitMQ"), ("odi", "Oracle Data Integrator"),
        ("informatica", "Informatica"), ("datastage", "DataStage"), ("controlm", "Control-M"), ("ctm", "Control-M"), ("mulesoft", "MuleSoft"), ("mule", "Mule"),
        ("glassfish", "GlassFish"), ("payara", "Payara"), ("jetty", "Jetty"), ("spring", "Spring Boot"), ("dbeaver", "DBeaver"), ("sqldeveloper", "SQL Developer"),
    ];
    static string? Urun(string yol, string? komut, string? jar)
    {
        string m = (yol + " " + (jar ?? "") + " " + (komut ?? "")).ToLowerInvariant();
        foreach (var (d, ad) in Urunler) if (Regex.IsMatch(m, $@"(^|[^a-z]){Regex.Escape(d)}([^a-z]|$)")) return ad;
        return EksikService.BilinenUrun(yol);
    }

    // ---------------- Tarama ----------------
    // İstekte kural id'si ya da sayfada doldurulmuş kural
    public async Task<(ZfKural? Kural, string? Hata)> KuralBulAsync(ZafiyetIstek r, CancellationToken ct, string? user = null)
    {
        if (r.SonKural) { lock (_gate) if (Gorunum(user).Is?.Kural is { } son) return (son, null); return (null, "Önce bir tarama yapılmalı (son taramanın kuralı yok)."); }
        if (r.Ozel != null) return OzelKural(r.Ozel, "anlik");
        var l = await KurallarAsync(ct);
        return (l.FirstOrDefault(x => string.Equals(x.Id, r.Kural ?? "", StringComparison.OrdinalIgnoreCase)) ?? l.First(), null);
    }

    static (ZfKural? Kural, string? Hata) OzelKural(ZfOzel o, string id)
    {
        var ex = Temiz((o.Exeler ?? "").Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        if (ex.Count == 0) return (null, "Geçerli exe adı yok (harf, rakam, . _ - ve * kullanılabilir).");
        if (ex.Any(e => e.Trim('*').Length < 3)) return (null, "Exe adları en az 3 harf olmalı (çok geniş arama olmasın).");
        static string? T(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
        var ham = (o.Araliklar ?? []).Where(a => T(a.Min) != null || T(a.Duzeltilmis) != null || T(a.Son) != null).ToList();
        if (ham.Any(a => T(a.Dal) is { } d && !Regex.IsMatch(d, @"^\d+(\.\d+){0,2}$"))) return (null, "Dal \"1.30\" biçiminde olmalı.");
        if (ham.SelectMany(a => new[] { T(a.Min), T(a.Duzeltilmis), T(a.Son) }).OfType<string>().Any(v => NormSurum(v) == null))
            return (null, "Sürümler rakam ve . _ - + ile yazılmalı (ör. 17.0.6) ya da Java 8 biçiminde (8u511).");
        var ar = ham.Select(a => new ZfAralik(a.Ana, NormSurum(T(a.Min)), NormSurum(T(a.Duzeltilmis)), NormSurum(T(a.Son)), T(a.Dal))).ToList();
        var onlem = (o.Onlem ?? "").Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 3).Distinct().ToList();
        bool java = ex.Any(e => e is "java" or "javaw");
        int? anaMin = ar.Any(a => a.Ana != null) ? ar.Where(a => a.Ana != null).Min(a => a.Ana) : null;
        string ad = T(o.Ad) ?? (T(o.Cve) is { } c ? c : "Süreç araması: " + string.Join(", ", ex));
        return (new ZfKural(id, ad, T(o.Cve)?.ToUpperInvariant(), T(o.Aciklama), ex, Araliklar: ar.Count > 0 ? ar : null, AnaMin: anaMin,
            Onlem: onlem.Count > 0 ? onlem.Select(Regex.Escape).ToList() : null, OnlemNot: onlem.Count > 0 ? string.Join(", ", onlem) : null,
            Java: java, DestekDisiEtkilenir: o.DigerEtkilenir, Kaynak: "anlık",
            NessusAnahtarlar: (o.NessusAnahtarlar ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length >= 3)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList() is { Count: > 0 } na ? na : null), null);
    }

    // IP listesi: virgül / boşluk / satırla; yalnız IPv4, en çok Zafiyet:MaxIp (50)
    public List<string>? IpListesi(string? metin, out string? hata)
    {
        hata = null;
        if (string.IsNullOrWhiteSpace(metin)) return null;
        var l = Regex.Matches(metin, @"\b(?:\d{1,3}\.){3}\d{1,3}\b").Select(m => m.Value).Where(v => v.Split('.').All(x => int.Parse(x) <= 255)).Distinct().ToList();
        int max = cfg.GetValue("Zafiyet:MaxIp", 50);
        if (l.Count == 0) hata = "Geçerli IPv4 adresi bulunamadı.";
        else if (l.Count > max) hata = $"En çok {max} IP girilebilir (Zafiyet:MaxIp); tüm sunucular için IP kutusunu boş bırakın.";
        return hata == null ? l : null;
    }

    public ZfIs Baslat(ZfKural kural, int saat, string? user, List<string>? ipler = null, bool ekle = false, bool dmz = false)
    {
        lock (_gate)
        {
            if (_is is { Durum: "calisiyor" }) return _is;
            // "listeye ekle": bu kullanıcının görünümündeki liste
            var gor = Gorunum(user);
            // Listeye eklenen tekil IP taraması listenin kapsamını (DMZ) korur; IP kipinde DMZ süzgeci uygulanmaz
            if (ipler != null) dmz = ekle && gor.Satir.Count > 0 && gor.Is?.Dmz == true;
            int maxSaat = cfg.GetValue("Zafiyet:MaxSaat", 168);
            saat = Math.Clamp(saat <= 0 ? cfg.GetValue("Zafiyet:Saat", 24) : saat, 1, maxSaat);
            int surecSaat = Math.Max(saat, cfg.GetValue("Zafiyet:SurecSaat", 168));
            _cts = new CancellationTokenSource();
            string kapsam = ipler == null ? (dmz ? "DMZ asset'leri" : "tüm sunucular") : $"{ipler.Count} IP: {string.Join(", ", ipler.Take(5))}{(ipler.Count > 5 ? " …" : "")}" + (ekle ? " (listeye ekleniyor)" : "");
            _is = new ZfIs("calisiyor", "Başlıyor", DateTime.Now, null, null, user, saat, surecSaat, kural, kapsam, Dmz: dmz);
            _isSahip = user;
            var eski = ekle && ipler != null ? gor.Satir : [];
            var eskiMesaj = ekle && ipler != null ? gor.Mesaj : [];
            _satir = eski; _mesaj = eskiMesaj;
            var ct = _cts.Token;
            bool dmzKip = dmz && ipler == null;
            _ = Task.Run(() => CalisAsync(kural, saat, surecSaat, ct, ipler, eski, eskiMesaj, dmzKip));
            return _is;
        }
    }
    void Set(Func<ZfIs, ZfIs> f) { lock (_gate) if (_is != null) _is = f(_is); }

    async Task CalisAsync(ZfKural k, int saat, int surecSaat, CancellationToken ct, List<string>? ipler = null, List<ZfSatir>? eski = null, List<string>? eskiMesaj = null,
        bool dmzKip = false)
    {
        var mesaj = new List<string>();
        var ipSet = ipler?.ToHashSet();
        try
        {
            // DMZ asset'i (Nessus ekibinin DMZ asset seti): satırlar işaretlenir; DMZ kipinde yalnız bu IP'ler
            Set(i => i with { Asama = "Nessus: DMZ asset'i okunuyor" });
            var (dmzA, dmzHata) = await DmzAsync(ct);
            var dmzRepo = DmzRepolar;
            if (dmzA != null) mesaj.Add($"DMZ asset'i: {dmzA.Asset.Ad} (id {dmzA.Asset.Id}){(dmzA.Asset.IpSayisi is { } n ? $" — Tenable'da {n:N0} IP" : "")} · {dmzA.Araliklar.Count:N0} IP aralığı, "
                + $"{dmzA.RepoOzet.Count()} repository ({dmzA.Kaynak})" + (dmzRepo.Count > 0 ? $" · yalnız repository {string.Join(", ", dmzRepo)} (Zafiyet:DmzRepolar)" : "") + ".");
            else if (dmzHata != null) mesaj.Add("DMZ asset'i okunamadı: " + dmzHata);
            if (dmzKip && dmzA == null) throw new InvalidOperationException("Yalnız DMZ taraması yapılamadı — " + (dmzHata ?? "Zafiyet:DmzAsset tanımlı değil."));
            var s = cfg.GetSection("Splunk");
            string index = s["Index"] ?? "carbonblack", st = s["Sourcetype"] ?? "bit9:carbonblack:json";
            string anahtar = Anahtar(k), desen = ExeDesen(k, true);
            var sure = TimeSpan.FromSeconds(cfg.GetValue("Zafiyet:SureSaniye", 900));
            var http = factory.CreateClient("splunk");
            var bit = DateTimeOffset.Now;
            string exeler = string.Join(", ", Temiz(k.Exeler));

            // Tekil / birkaç IP: TERM(ip) ile indeksten süzülür + local_ip eşitliği
            string ipAra = ipler == null ? "" : "(" + string.Join(" OR ", ipler.Select(i => $"TERM({i})")) + ")";
            string ipKos = ipler == null ? "" : "| where " + string.Join(" OR ", ipler.Select(i => $"local_ip=\"{i}\""));
            Set(i => i with { Asama = $"Carbon Black: {exeler} süreçlerinin ağ olayları (son {saat} saat, {(ipler == null ? "tüm sunucular" : ipler.Count + " IP")})" });
            string spl1 = $"""
                search index={index} sourcetype="{st}" {anahtar} {ipAra}
                {ipKos}
                | eval yol=coalesce(process_path, process_name, "")
                | where match(lower(yol), "{desen}")
                | eval yon=if(lower(direction)=="outbound", "out", "in"), lp=if(yon=="in", local_port, "-")
                | eval dis=if(yon=="out" AND isnotnull(remote_ip) AND NOT match(remote_ip, ":") AND NOT (cidrmatch("10.0.0.0/8", remote_ip) OR cidrmatch("172.16.0.0/12", remote_ip) OR cidrmatch("192.168.0.0/16", remote_ip) OR cidrmatch("127.0.0.0/8", remote_ip) OR cidrmatch("169.254.0.0/16", remote_ip) OR cidrmatch("0.0.0.0/8", remote_ip) OR cidrmatch("224.0.0.0/3", remote_ip)), remote_ip.":".remote_port, null())
                | stats count dc(remote_ip) as peers values(remote_ip) as ornek max(_time) as son values(computer_name) as pc values(dis) as dis by local_ip yon lp yol
                | eval ornek=mvjoin(mvindex(ornek, 0, 199), " "), pc=mvjoin(mvindex(pc, 0, 2), "|||"), dis=mvjoin(mvindex(dis, 0, 299), " ")
                """;
            var r1 = await SplunkService.ExportAsync(spl1, new LookupQuery("0.0.0.0", bit.AddHours(-saat), bit), cfg, http, ct, "Carbon Black (ağ)", sure);
            mesaj.AddRange(r1.Messages);

            Set(i => i with { Asama = $"Carbon Black: süreç başlangıçları / komut satırları (son {surecSaat} saat)" });
            string onlemRx = k.Onlem is { Count: > 0 } o ? string.Join("|", o).Replace("\\", "\\\\").Replace("\"", "\\\"") : "";
            // IP kipinde süreç başlangıcında IP yok: bilgisayar adları (ağ olaylarından + ERT UNIXNAME) ile süzülür
            string pcAra = "";
            bool surecAtla = false;
            if (ipler != null)
            {
                var envOn = await envanter.GetAsync(false, ct);
                var pcler = r1.Rows.Select(r => V(r, "pc")?.Split("|||")[0]).OfType<string>()
                    .Concat(ipler.SelectMany(ip => envOn.AppsOnIp(ip).Select(a => a.UnixName).OfType<string>()))
                    .Select(KisaPc).Where(x => Regex.IsMatch(x, @"^[a-z0-9_\-]{3,}$")).Distinct().Take(50).ToList();
                if (pcler.Count == 0) { surecAtla = true; mesaj.Add("Süreç başlangıçları atlandı: bu IP'lerin Carbon Black bilgisayar adı / ERT sunucu adı bulunamadı."); }
                else pcAra = "(" + string.Join(" OR ", pcler.Select(x => $"TERM({x})")) + ")";
            }
            string spl2 = $"""
                search index={index} sourcetype="{st}" TERM(ingress.event.procstart) {anahtar} {pcAra}
                | eval yol=coalesce(process_path, process_name, ""), cmd=coalesce(cmdline, command_line, process_cmdline, "")
                | where match(lower(yol), "{desen}")
                | eval onlem={(onlemRx == "" ? "-1" : $"if(match(cmd, \"{onlemRx}\"), 1, 0)")}
                | rex field=cmd "-jar\s+\"?(?<jar>[^\"\s]+)"
                | rex field=cmd "(?i)(?<jhome>[^\"\s=]*(?:jdk|jre|java|zulu|corretto|temurin|openjdk)[^\"\s]*)"
                | eval cmd=substr(cmd, 1, 1500)
                | stats latest(cmd) as cmd max(onlem) as onlem latest(jar) as jar latest(jhome) as jhome max(_time) as bas count by computer_name yol
                """;
            List<Dictionary<string, string>> r2Rows = [];
            if (!surecAtla) try
            {
                var r2 = await SplunkService.ExportAsync(spl2, new LookupQuery("0.0.0.0", bit.AddHours(-surecSaat), bit), cfg, http, ct, "Carbon Black (süreç başlangıcı)", sure);
                r2Rows = r2.Rows; mesaj.AddRange(r2.Messages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Süreç başlangıçları okunamadı (komut satırı / önlem bilinmiyor): " + ex.Message); }

            // 3) Nessus (Carbon Black ajanı olmayan sunucular / kutular — API gateway vb. — ve kurulu ama çalışmayan yazılım için)
            var nCve = new List<NessusBulgu>(); var nUrun = new List<NessusBulgu>();
            var dmzNessus = new HashSet<string>();
            if (nessus.Acik && cfg.GetValue("Zafiyet:Nessus", true))
            {
                try
                {
                    // Sorgu takımları: normal (Nessus:Repolar) + DMZ asset'i (repository süzgeçsiz; DMZ başka repoda taranmış olabilir). DMZ kipinde yalnız asset.
                    var takim = new List<(string? Asset, string Et)>();
                    if (!dmzKip) takim.Add((null, ""));
                    if (dmzA != null && ipler == null) takim.Add((dmzA.Asset.Id, " (DMZ asset'i)"));
                    var gor = new HashSet<(string, string, string?)>();
                    int dmzAtilan = 0;
                    void Ekle(List<NessusBulgu> hedef, List<NessusBulgu> l, string? asset)
                    {
                        // DMZ asset sorgusu tüm repository'lerde çalışır; kullanıcı: "fazla olanlar (Londra / Bulgaristan) olsun, repo adı yazsın" → ayıklanmaz,
                        // satırda repository adı. İsteğe bağlı Zafiyet:DmzRepolar verilirse yalnız o repository'ler.
                        if (asset != null && dmzRepo.Count > 0)
                        {
                            int once = l.Count;
                            l = l.Where(b => b.RepoId == null || dmzRepo.Contains(b.RepoId)).ToList();
                            dmzAtilan += once - l.Count;
                        }
                        foreach (var b in l) if (gor.Add((b.Ip, b.PluginId, b.Port))) hedef.Add(b);
                        if (asset != null) dmzNessus.UnionWith(l.Select(b => b.Ip));
                    }
                    foreach (var (asset, et) in takim)
                    {
                        foreach (var cve in Regex.Matches(k.Cve ?? "", @"CVE-\d{4}-\d{4,}", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct())
                        {
                            Set(i => i with { Asama = $"Nessus: {cve} bulguları{et}" });
                            var (l, kesik) = await nessus.CveAsync(cve, ct, ipler, asset);
                            Ekle(nCve, l, asset);
                            mesaj.Add(l.Count > 0 ? $"Nessus{et}: {cve} için {l.Select(x => x.Ip).Distinct().Count()} IP'de bulgu."
                                : $"Nessus{et}: {cve} için bulgu yok (Tenable'ın bu CVE için kontrolü henüz yayımlanmamış ya da taranmamış olabilir).");
                            if (kesik) mesaj.Add($"Nessus{et} {cve} bulguları sayfa sınırında kesildi (Zafiyet:NessusMaxSayfa).");
                        }
                        var kelimeler = NessusKelimeleri(k);
                        if (kelimeler.Count > 0)
                        {
                            Set(i => i with { Asama = $"Nessus: plugin adında {string.Join(", ", kelimeler)} geçen bulgular{(asset == null ? " (tüm repo)" : et)}" });
                            var haric = k.Java ? new Regex("javascript|jquery|json", RegexOptions.IgnoreCase) : null;
                            var (l, kelSay, kesik) = await nessus.UrunlerAsync(kelimeler, haric, ct, ipler, asset);
                            Ekle(nUrun, l, asset);
                            mesaj.Add($"Nessus ürün araması{et} — {l.Select(x => x.Ip).Distinct().Count()} IP, {l.Count} bulgu ({l.Count(x => x.Kurulumlar.Count > 0)} sürümlü) · eşleşen plugin: "
                                + string.Join(", ", kelSay.Select(x => $"{x.Key} {x.Value}")) + (kesik ? " · sayfa sınırında kesildi (Zafiyet:NessusMaxSayfa)" : ""));
                        }
                    }
                    if (dmzAtilan > 0) mesaj.Add($"DMZ asset sorgusunda {dmzAtilan} bulgu atıldı: repository Zafiyet:DmzRepolar'da yok.");
                    mesaj.Add($"Nessus bulguları: son {nessus.AcikGun} günde görülenler (Zafiyet:NessusGun).");
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Nessus sorgulanamadı (tarama Carbon Black'le devam etti): " + ex.Message); }
            }
            else if (!nessus.Acik) mesaj.Add("Nessus tanımlı değil (Nessus:BaseUrl): Carbon Black ajanı olmayan sunucular / kutular kapsam dışı.");

            Set(i => i with { Asama = "Envanter ve servis imzası kaydıyla birleştiriliyor" });
            var env = await envanter.GetAsync(false, ct);
            ImzaTarama? imzaT = null;
            try { imzaT = await imza.TaramaAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Servis imzası kaydı okunamadı: " + ex.Message); }
            if (ipSet != null && imzaT != null) imzaT = imzaT with { Sunucular = imzaT.Sunucular.Where(x => ipSet.Contains(x.Ip)).ToList() };
            var satir = Birlestir(k, env, r1.Rows, r2Rows, imzaT, mesaj, nCve, nUrun);
            // Kör nokta: CB'de ve Nessus bulgularında görünmeyen sunucular için Nessus envanteri (süreç / servis / yazılım listesi); IP kipinde hiç görünmeyen IP'ye nedenli satır
            if (nessus.Acik && cfg.GetValue("Zafiyet:NessusEnvanter", true))
                try { satir.AddRange(await KorNoktaAsync(k, env, satir, ipler, dmzA, saat, mesaj, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Nessus envanter araması yapılamadı: " + ex.Message); }
            // DMZ işareti (asset IP listesi ya da DMZ asset sorgusundan gelen Nessus bulgusu) + dışa açıklık önceliği
            satir = satir.Select(r => DisOncelik(r with { Dmz = r.Ip != null && (dmzNessus.Contains(r.Ip) || dmzA?.Icerir(r.Ip, null, dmzRepo) == true) })).ToList();
            if (dmzKip)
            {
                int once = satir.Count;
                satir = satir.Where(r => r.Dmz).ToList();
                mesaj.Insert(0, $"Yalnız DMZ asset'leri: {satir.Count} satır ({satir.Select(r => r.Ip).Distinct().Count()} IP); DMZ dışındaki {once - satir.Count} satır gösterilmiyor.");
            }
            int yeni = satir.Count;
            if (ipSet != null)
            {
                satir = satir.Where(r => r.Ip != null && ipSet.Contains(r.Ip)).ToList();
                yeni = satir.Count;
                var bos = ipler!.Where(ip => !satir.Any(r => r.Ip == ip)).ToList();
                if (bos.Count > 0) mesaj.Add($"Kurala uyan süreç / kurulum bulunamayan IP'ler: {string.Join(", ", bos)} (Carbon Black'te bu aralıkta olay yok ve Nessus'ta bulgu yok).");
                if (eski is { Count: > 0 })
                {
                    string[] sira = ["kritik", "yuksek", "orta", "incele", "dusuk"];
                    satir = eski.Where(r => r.Ip == null || !ipSet.Contains(r.Ip)).Concat(satir)
                        .OrderBy(x => Array.IndexOf(sira, x.Oncelik)).ThenByDescending(x => x.Portlar.Sum(p => p.Baglanti)).ThenBy(x => x.Ip).ToList();
                    mesaj.Insert(0, $"Tekil tarama listeye eklendi: {string.Join(", ", ipler!)} → {yeni} satır (bu IP'lerin eski satırları yenilendi).");
                }
            }
            var tumMesaj = (eskiMesaj ?? []).Concat(mesaj).Distinct().ToList();
            ZfIs son;
            lock (_gate)
            {
                _satir = satir; _mesaj = tumMesaj;
                son = _is! with { Durum = "bitti", Asama = ipSet != null ? $"Bitti: {yeni} satır ({ipler!.Count} IP)" : $"Bitti: {satir.Count} süreç", Bitis = DateTime.Now };
                _is = _is with { Asama = "Sonuç kaydediliyor" };
            }
            // Sonuç kaydı ("bitti"den ÖNCE, sayfa listeyi hemen görsün): tüm sunucu taraması ya da mevcut listeye eklenen tekil tarama
            // (yalnız birkaç IP'lik liste kaydedilmez — "Kayıtlıdan getir" tam listeyi açsın)
            if (ipSet == null || eski is { Count: > 0 })
            {
                var (kid, kt) = await TaramaKaydetAsync(son, satir, tumMesaj);
                son = son with { KayitId = kid, KayitTarihi = kt };
            }
            lock (_gate) { _is = son; IsBitti(); }
        }
        catch (OperationCanceledException) { lock (_gate) { _mesaj = mesaj; _is = _is! with { Durum = "durduruldu", Asama = "Durduruldu", Bitis = DateTime.Now }; IsBitti(); } }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Açıklık taraması hatası");
            lock (_gate) { _mesaj = mesaj; _is = _is! with { Durum = "hata", Asama = "Hata", Hata = ex.Message, Bitis = DateTime.Now }; IsBitti(); }
        }
    }

    static string? V(Dictionary<string, string> r, string k) => r.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
    static string? Zaman(string? t) => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var u)
        ? DateTimeOffset.FromUnixTimeSeconds((long)u).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : t;
    static string YolAnahtar(string yol) => yol.Replace('/', '\\').Trim().ToLowerInvariant();
    static string KisaPc(string? pc) => (pc ?? "").Split('.')[0].Trim().ToLowerInvariant();
    static string ExeAd(string yol) => yol.Replace('/', '\\').Split('\\').Last();

    sealed class Top
    {
        public string? Ip, Pc; public string Yol = "";
        public Dictionary<string, (long N, HashSet<string> Ist)> Port = new();
        public long Giden; public HashSet<string> GidenHedef = new(); public HashSet<string> DisHedef = new();
        public double Son; public string Kaynak = "Carbon Black";
    }

    List<ZfSatir> Birlestir(ZfKural k, EnvanterSnapshot env, List<Dictionary<string, string>> ag, List<Dictionary<string, string>> bas, ImzaTarama? imzaT, List<string> mesaj,
        List<NessusBulgu>? nCve = null, List<NessusBulgu>? nUrun = null)
    {
        var exeRx = new Regex(ExeDesen(k, false));
        var gruplar = new Dictionary<string, Top>();
        Top Al(string? ip, string? pc, string yol)
        {
            string key = (ip ?? "pc:" + KisaPc(pc)) + "|" + YolAnahtar(yol);
            if (!gruplar.TryGetValue(key, out var t)) gruplar[key] = t = new Top { Ip = ip, Pc = pc, Yol = yol };
            t.Pc ??= pc;
            return t;
        }
        foreach (var r in ag)
        {
            string? ip = V(r, "local_ip"), yol = V(r, "yol");
            if (ip == null || yol == null || ip == "127.0.0.1" || ip.Contains(':')) continue;
            var t = Al(ip, V(r, "pc")?.Split("|||")[0], yol);
            long n = long.TryParse(V(r, "count"), out var c) ? c : 0;
            var ornek = (V(r, "ornek") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => x != ip && x != "127.0.0.1").ToHashSet();
            if (V(r, "yon") == "in" && V(r, "lp") is { } lp && lp != "0" && lp != "-")
            {
                var (pn, ist) = t.Port.TryGetValue(lp, out var e) ? e : (0, new HashSet<string>());
                ist.UnionWith(ornek);
                t.Port[lp] = (pn + n, ist);
            }
            else { t.Giden += n; t.GidenHedef.UnionWith(ornek); }
            foreach (var d in (V(r, "dis") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (!Ic(env, d.Split(':')[0])) t.DisHedef.Add(d);   // kurum içinde kullanılan genel bloklar (segment / uygulama / LB GW kaydı olan) dış sayılmaz
            if (double.TryParse(V(r, "son"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sn)) t.Son = Math.Max(t.Son, sn);
        }

        // Servis imzası kaydı: CB taramasında hiç görünmeyen IP'lerin aynı süreçleri
        var cbIp = gruplar.Values.Select(g => g.Ip).OfType<string>().ToHashSet();
        int imzaEk = 0;
        if (imzaT != null)
        {
            string kaynak = $"Servis imzası kaydı ({imzaT.Baslangic:dd.MM.yyyy})";
            foreach (var sv in imzaT.Sunucular.Where(x => !cbIp.Contains(x.Ip)))
            {
                foreach (var p in sv.Gelen.Where(p => exeRx.IsMatch(p.Yol.ToLowerInvariant())))
                {
                    var t = Al(sv.Ip, null, p.Yol); t.Kaynak = kaynak;
                    var (pn, ist) = t.Port.TryGetValue(p.Port, out var e) ? e : (0, new HashSet<string>());
                    t.Port[p.Port] = (pn + p.Baglanti, ist); imzaEk++;
                }
                foreach (var y in sv.Giden.Where(y => exeRx.IsMatch(y.ToLowerInvariant())))
                { var t = Al(sv.Ip, null, y); t.Kaynak = kaynak; imzaEk++; }
            }
        }
        if (imzaEk > 0) mesaj.Add($"Servis imzası kaydından {imzaEk} ek süreç / port eklendi (Carbon Black bu aralıkta görmedi).");

        // Süreç başlangıçları: bilgisayar + yol → komut satırı
        var basIdx = new Dictionary<string, Dictionary<string, string>>();
        foreach (var r in bas)
            if (V(r, "computer_name") is { } pc && V(r, "yol") is { } yol) basIdx[KisaPc(pc) + "|" + YolAnahtar(yol)] = r;
        // Ağ olayı olmayan (yalnız başlatılmış) süreçler: bilgisayar adı ERT'deki sunucu adıyla eşleşirse IP'si
        var adIp = new Dictionary<string, string>();
        foreach (var ip in env.HostIps)
            foreach (var h in env.AppsOnIp(ip)) if (!string.IsNullOrWhiteSpace(h.UnixName)) adIp.TryAdd(KisaPc(h.UnixName), ip);
        var kullanilan = new HashSet<string>();
        foreach (var g in gruplar.Values) if (g.Pc != null) kullanilan.Add(KisaPc(g.Pc) + "|" + YolAnahtar(g.Yol));
        foreach (var (key, r) in basIdx.Where(x => !kullanilan.Contains(x.Key)).ToList())
        {
            string pc = V(r, "computer_name")!;
            Al(adIp.GetValueOrDefault(KisaPc(pc)), pc, V(r, "yol")!).Kaynak = "Carbon Black (yalnız süreç başlangıcı, ağ olayı yok)";
        }

        var test = (cfg.GetSection("Eksik:TestIfadeleri").Get<string[]>() ?? ["test"]).Select(x => x.ToLowerInvariant()).ToArray();
        int dyn = cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);
        var sonuc = new List<ZfSatir>();
        foreach (var g in gruplar.Values)
        {
            basIdx.TryGetValue(KisaPc(g.Pc) + "|" + YolAnahtar(g.Yol), out var b);
            string? komut = b == null ? null : V(b, "cmd"), jar = b == null ? null : V(b, "jar");
            string? surum = Surum(k, g.Yol), kaynakS = surum != null ? "exe yolu" : null;
            if (surum == null && k.Java && Surum(k, b == null ? null : V(b, "jhome")) is { } s2) { surum = s2; kaynakS = "komut satırı"; }
            if (surum == null && Surum(k, komut) is { } s3) { surum = s3; kaynakS = "komut satırı"; }
            var (durum, not) = Degerlendir(k, surum);
            string? dagitim = k.Java ? Dagitim(g.Yol) ?? Dagitim(b == null ? null : V(b, "jhome")) : null;
            // Düzeltilmiş sürümler çoğunlukla Oracle numarası: başka dağıtımda yamalı görünen sürüm dağıtıcının duyurusuyla doğrulanmalı
            if (durum == "yamali" && dagitim != null && !dagitim.StartsWith("Oracle")) not += $" — {dagitim}: dağıtıcının kendi düzeltilmiş sürümüyle karşılaştırın";
            if (surum == null && k.Java && Regex.IsMatch(g.Yol.ToLowerInvariant(), @"[\\/](jre|jbr|jdk)[\\/]bin[\\/]"))
                not = "ürünün içindeki gömülü Java (sürüm yolda yok; ürün sürümünden / Nessus'tan bakın)";
            else if (surum == null && k.Java && Regex.IsMatch(ExeAd(g.Yol).ToLowerInvariant(), @"^(tomcat|prunsrv)"))
                not = "Java'yı süreç içinde çalıştıran servis sarmalayıcısı (jvm.dll yolu komut satırında / kayıt defterinde)";
            string onlem = k.Onlem is not { Count: > 0 } ? "—" : b == null ? "bilinmiyor" : V(b, "onlem") == "1" ? "var" : "yok";

            var seg = g.Ip == null ? null : env.FindSegment(g.Ip);
            List<HostApp> apps = g.Ip == null ? [] : env.AppsOnIp(g.Ip);
            List<VipMember> vipler = g.Ip == null ? [] : env.MemberOf(g.Ip);
            var portlar = g.Port.Where(p => !(int.TryParse(p.Key, out int pn) && pn >= dyn && p.Value.Ist.Count <= 1))
                .OrderByDescending(p => p.Value.N).Select(p => new ZfPort(p.Key, p.Value.N, p.Value.Ist.Count,
                    apps.Where(a => a.Port == p.Key).Select(a => a.AppName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                    vipler.Where(v => v.Port == p.Key).Select(v => $"{(v.Pool ?? "").Replace("/Common/", "")} · {v.LbIp}:{v.LbPort}").Distinct().ToList(),
                    p.Value.Ist.Where(x => !Ic(env, x)).Take(10).ToList())).ToList();
            bool dinliyor = portlar.Count > 0, disari = portlar.Any(p => p.Vip.Count > 0 || p.Dis.Count > 0);
            // Öncelik: etkilenen sürümde dışarıdan erişilen > yalnız dinleyen > yalnız giden; sürüm aralığı verilmemiş aramada yalnız maruziyete göre
            bool risk = durum is "etkilenen" or "olasi" or "bilgi";
            string oncelik = risk ? (dinliyor ? (disari ? "kritik" : "yuksek") : "orta")
                : durum == "bilinmiyor" ? (dinliyor ? "incele" : "dusuk") : "dusuk";
            if (onlem == "var" && oncelik is "kritik" or "yuksek") oncelik = "orta";
            string? host = apps.Select(a => a.UnixName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
            string kurum = g.Ip == null ? "Diğer" : KurumBilgi.Of(cfg, env, g.Ip);
            string dom = (seg?.Domain ?? "").ToLowerInvariant();
            sonuc.Add(new ZfSatir(g.Ip, host ?? g.Pc, g.Pc, kurum, seg?.Label, test.Any(x => x.Length > 0 && dom.Contains(x)),
                apps.Select(a => a.AppName).OfType<string>().Where(a => a.Length > 0 && !env.IsSystemApp(a)).Distinct().Take(8).ToList(),   // "Sistem Portudur…" uygulama değil
                apps.Select(a => a.VarlikMuhafizi).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                ExeAd(g.Yol), g.Yol, Urun(g.Yol, komut, jar), surum, kaynakS, durum, not,
                portlar, g.Giden, onlem, komut, jar, g.Son > 0 ? Zaman(g.Son.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null,
                b == null ? null : Zaman(V(b, "bas")), oncelik, g.Kaynak, Vlan: seg?.Vlan, Domain: seg?.Domain, Cidr: seg?.Cidr, Dagitim: dagitim,
                DisGiden: g.DisHedef.Count > 0 ? g.DisHedef.OrderBy(x => x).Take(50).ToList() : null, DisGidenSay: g.DisHedef.Count));
        }
        NessusIsle(k, env, sonuc, nCve ?? [], nUrun ?? [], test, mesaj);
        string[] sira = ["kritik", "yuksek", "orta", "incele", "dusuk"];
        return sonuc.OrderBy(x => Array.IndexOf(sira, x.Oncelik)).ThenByDescending(x => x.Portlar.Sum(p => p.Baglanti)).ThenBy(x => x.Ip).ToList();
    }

    // ---------------- Nessus ile birleştirme ----------------
    static string YolNorm(string? y) => (y ?? "").Replace('/', '\\').Trim().TrimEnd('\\').ToLowerInvariant();
    // Nessus sürümü: "17.0.10+7" → 17.0.10, "1.8.0_202" → 1.8.0.202, "8u202" → 1.8.0.202
    static string? NessusSurum(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var m = Regex.Match(v.Trim(), @"^(?:\d+u\d+|\d+(?:\.\d+)*(?:_\d+)?)");
        // başta değilse ("nginx/1.24.0", "v17.0.10", "OpenJDK 17.0.10+7"): metindeki ilk a.b[.c] sürümü
        if (!m.Success) m = Regex.Match(v, @"(?<![\d.])(?:\d+u\d+|\d+\.\d+(?:\.\d+)*(?:_\d+)?)");
        return m.Success ? NormSurum(m.Value) : null;
    }

    void NessusIsle(ZfKural k, EnvanterSnapshot env, List<ZfSatir> sonuc, List<NessusBulgu> nCve, List<NessusBulgu> nUrun, string[] test, List<string> mesaj)
    {
        // "Java" araması "JavaScript Library Detection" (jQuery…) gibi bulguları da getirir: Java kuralında elenir
        if (k.Java) nUrun = nUrun.Where(b => !Regex.IsMatch(b.PluginAd, "javascript|jquery|json", RegexOptions.IgnoreCase)).ToList();
        if (nCve.Count == 0 && nUrun.Count == 0) return;
        var cveIp = nCve.GroupBy(x => x.Ip).ToDictionary(g => g.Key, g => g.ToList());
        int doldurulan = 0, eklenen = 0;

        // (a) Kurulumlar: aynı IP'de yolu kurulum klasörüyle başlayan CB satırının sürümü yoksa Nessus'tan; eşleşmeyen kurulum yeni satır
        var kurulumlar = nUrun.SelectMany(b => (b.Kurulumlar.Count > 0 ? b.Kurulumlar : []).Select(x => (B: b, K: x)))
            .Where(x => NessusSurum(x.K.Surum) != null)
            .GroupBy(x => x.B.Ip + "|" + YolNorm(x.K.Yol) + "|" + NessusSurum(x.K.Surum)).Select(g => g.First()).ToList();
        foreach (var (b, kur) in kurulumlar)
        {
            string sur = NessusSurum(kur.Surum)!;
            string yn = YolNorm(kur.Yol);
            int i = sonuc.FindIndex(r => r.Ip == b.Ip && yn.Length > 2 && YolNorm(r.Yol).StartsWith(yn));
            if (i >= 0)
            {
                var r = sonuc[i];
                if (r.Surum == null)
                {
                    var (d, n) = Degerlendir(k, sur);
                    sonuc[i] = Oncelikle(r with { Surum = sur, SurumKaynak = "Nessus", Durum = d, DurumNot = n, Dagitim = r.Dagitim ?? (k.Java ? Dagitim(kur.Yol) : null), Repo = r.Repo ?? RepoAdi(b) });
                    doldurulan++;
                }
                continue;
            }
            // CB'de görülmeyen kurulum (ajan yok ya da süreç çalışmıyor)
            var (du, no) = Degerlendir(k, sur);
            sonuc.Add(NessusSatir(k, env, test, b, kur.Yol ?? "(yol yok)", sur, du, no + (kur.Duzeltilmis != null ? $" · Nessus düzeltilmiş: {kur.Duzeltilmis}" : ""),
                $"kurulu: {b.PluginAd} ({b.Son})", "Nessus (kurulu yazılım)"));
            eklenen++;
        }

        // (a2) Ürün bulgusu var ama sürüm yazmıyor ya da yazdığı sürüm okunamıyor (kimliksiz tarama, kutu / appliance): IP'de başka satır yoksa
        // "sürüm bilinmiyor" satırı. HATA DÜZELTİLDİ (kullanıcının Nessus listesiyle karşılaştırması): kurulum satırı olup sürümü okunamayan bulgular hiç satır üretmiyordu.
        foreach (var g in nUrun.GroupBy(b => b.Ip))
        {
            if (sonuc.Any(r => r.Ip == g.Key)) continue;
            var b0 = g.OrderByDescending(x => x.Son).First();
            var ham = g.SelectMany(x => x.Kurulumlar).Select(x => $"{x.Yol} {x.Surum}".Trim()).Distinct().Take(2).ToList();
            string not = "ürün bulgusu (sürüm yok): " + string.Join(" | ", g.Select(x => x.PluginAd).Distinct().Take(3))
                + (ham.Count > 0 ? " · Nessus'un yazdığı (okunamadı): " + string.Join(" | ", ham) : "");
            sonuc.Add(NessusSatir(k, env, test, b0, "(Nessus: sürüm yok)", null, "bilinmiyor",
                "Nessus bu sunucuda ürünü görüyor ama sürüm yazmıyor (kimliksiz tarama / kutu olabilir) — elle doğrulayın", not, "Nessus (ürün bulgusu)"));
            eklenen++;
        }

        // (b) CVE bulguları: satırı olan IP'ler işaretlenir, olmayanlar yeni satır (Nessus açığı doğruladı)
        foreach (var (ip, l) in cveIp)
        {
            string not = "CVE bulgusu: " + string.Join(" | ", l.Select(x => $"{x.PluginAd} (ciddiyet {x.Seviye}, {x.Son})").Distinct().Take(3));
            var idx = sonuc.Select((r, i) => (r, i)).Where(x => x.r.Ip == ip).Select(x => x.i).ToList();
            if (idx.Count > 0)
            {
                foreach (int i in idx)
                {
                    var r = sonuc[i];
                    sonuc[i] = Oncelikle(r with { Nessus = r.Nessus == null ? not : r.Nessus + " · " + not, Repo = r.Repo ?? RepoAdi(l[0]),
                        Durum = r.Durum is "etkilenmez" or "yamali" ? r.Durum : "etkilenen", DurumNot = r.DurumNot + " · Nessus açığı doğruladı" });
                }
                continue;
            }
            var b0 = l[0];
            var kur = l.SelectMany(x => x.Kurulumlar).FirstOrDefault();
            string? sur = NessusSurum(kur?.Surum);
            sonuc.Add(NessusSatir(k, env, test, b0, kur?.Yol ?? "(Nessus bulgusu)", sur, "etkilenen", "Nessus açığı doğruladı" + (kur?.Duzeltilmis != null ? $" · düzeltilmiş: {kur.Duzeltilmis}" : ""),
                not, "Nessus (CVE bulgusu)"));
            eklenen++;
        }
        // IP'nin Nessus bulgu portları (uygulama tahmini için; CB portu olmayan kutularda tek port bilgisi)
        var nPort = nUrun.Concat(nCve).GroupBy(b => b.Ip).ToDictionary(g => g.Key,
            g => g.Select(b => b.Port).OfType<string>().Where(p => p != "0" && int.TryParse(p, out _)).Distinct().OrderBy(p => int.Parse(p)).ToList());
        for (int i = 0; i < sonuc.Count; i++)
            if (sonuc[i].Ip is { } nip && nPort.TryGetValue(nip, out var np) && np.Count > 0) sonuc[i] = sonuc[i] with { NessusPortlar = np };
        mesaj.Add($"Nessus birleştirme: {doldurulan} satırın sürümü Nessus'tan, {eklenen} yeni satır (Carbon Black'te görülmeyen kurulum / bulgu).");
    }

    static string? RepoAdi(NessusBulgu b) => b.RepoAd == null && b.RepoId == null ? null : b.RepoAd == null ? b.RepoId : b.RepoId == null ? b.RepoAd : $"{b.RepoAd} ({b.RepoId})";

    // Nessus'tan gelen satır: dinlediği port bilinmez; maruziyet = VIP üyeliği
    ZfSatir NessusSatir(ZfKural k, EnvanterSnapshot env, string[] test, NessusBulgu b, string yol, string? surum, string durum, string not, string nessusNot, string kaynak)
    {
        var seg = env.FindSegment(b.Ip);
        var apps = env.AppsOnIp(b.Ip);
        var vip = env.MemberOf(b.Ip).Select(v => $"{(v.Pool ?? "").Replace("/Common/", "")} · {v.LbIp}:{v.LbPort}").Distinct().ToList();
        string dom = (seg?.Domain ?? "").ToLowerInvariant();
        string exe = k.Java ? "java (Nessus)" : yol.StartsWith("(") ? $"{Temiz(k.Exeler).FirstOrDefault(x => !x.Contains('*')) ?? ""} (Nessus)".Trim()
            : ExeAd(yol.TrimEnd('\\', '/')) is { Length: > 0 } e ? e : yol;
        var portlar = vip.Count > 0 ? new List<ZfPort> { new("VIP", 0, 0, null, vip, []) } : [];
        var r = new ZfSatir(b.Ip, apps.Select(a => a.UnixName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? b.Dns ?? b.Netbios, b.Netbios ?? b.Dns,
            KurumBilgi.Of(cfg, env, b.Ip), seg?.Label, test.Any(x => x.Length > 0 && dom.Contains(x)),
            apps.Select(a => a.AppName).OfType<string>().Where(a => a.Length > 0 && !env.IsSystemApp(a)).Distinct().Take(8).ToList(),   // "Sistem Portudur…" uygulama değil
            apps.Select(a => a.VarlikMuhafizi).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
            exe, yol, null, surum, surum != null ? "Nessus" : null, durum, not, portlar, 0, "bilinmiyor", null, null, b.Son, null, "dusuk", kaynak,
            Vlan: seg?.Vlan, Domain: seg?.Domain, Cidr: seg?.Cidr, Dagitim: k.Java ? Dagitim(yol) : null, Nessus: nessusNot, Repo: RepoAdi(b));
        return Oncelikle(r);
    }

    // ---------------- Tanı: Nessus'ta görünüp taramada çıkmayan IP'ler (kullanıcı: "eksik var bizimkinde" — Nessus ekibinin listesiyle karşılaştırma) ----------------
    // GET /api/zafiyet/tani?ip=a,b&kural= : her IP için DMZ asset'inde mi, son taramada satırı var mı, Nessus'taki TÜM ilgili bulgular (süzgeçsiz) ve
    // her bulgunun taramada alınıp alınmayacağı (repository, son görülme, plugin adı kelimesi, CVE, sürüm okunuyor mu).
    public async Task<string> TaniAsync(string ipMetin, string? kuralId, CancellationToken ct, string? user = null)
    {
        var ips = Regex.Matches(ipMetin ?? "", @"\b\d{1,3}(?:\.\d{1,3}){3}\b").Select(m => m.Value).Distinct().Take(100).ToList();
        if (ips.Count == 0) return "IP verin: /api/zafiyet/tani?ip=10.1.1.1,10.1.1.2 (isteğe bağlı &kural=<kural id>; boşsa son taramanın kuralı)";
        ZfKural? k = null; ZfIs? son; List<ZfSatir> satir;
        lock (_gate) { var g = Gorunum(user); son = g.Is; satir = g.Satir; }
        if (!string.IsNullOrWhiteSpace(kuralId)) k = (await KurallarAsync(ct)).FirstOrDefault(x => x.Id.Equals(kuralId, StringComparison.OrdinalIgnoreCase));
        k ??= son?.Kural ?? Yerlesik[0];
        var kel = NessusKelimeleri(k);
        var cveler = Regex.Matches(k.Cve ?? "", @"CVE-\d{4}-\d{4,}", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        var haric = k.Java ? new Regex("javascript|jquery|json", RegexOptions.IgnoreCase) : null;
        bool Kelime(string ad, out string? w)
        {
            w = kel.FirstOrDefault(x => Regex.IsMatch(ad, $@"(?<![A-Za-z]){Regex.Escape(x)}", RegexOptions.IgnoreCase));
            return w != null && !(haric?.IsMatch(ad) ?? false);
        }
        int gun = nessus.AcikGun;
        var repolar = nessus.RepoListesi;
        var dmzRepo = DmzRepolar;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Kural: {k.Id} · {k.Ad}");
        sb.AppendLine($"Nessus kelimeleri: {string.Join(", ", kel)} · CVE: {string.Join(", ", cveler)}");
        sb.AppendLine($"Taramadaki Nessus süzgeçleri: repository = {(repolar.Length > 0 ? string.Join(", ", repolar) : "(yok)")} (DMZ asset sorgusunda yok) · son görülme ≤ {gun} gün (Zafiyet:NessusGun)"
            + (dmzRepo.Count > 0 ? $" · DMZ'de yalnız repository {string.Join(", ", dmzRepo)} (Zafiyet:DmzRepolar)" : ""));
        var (dmzA, dmzHata) = await DmzAsync(ct);
        if (dmzA != null)
        {
            sb.AppendLine($"DMZ asset'i: {dmzA.Asset.Ad} (id {dmzA.Asset.Id}) · Tenable ipCount {dmzA.Asset.IpSayisi?.ToString() ?? "?"} · {dmzA.Araliklar.Count} aralık ({dmzA.Kaynak})");
            foreach (var (repo, aralik, n) in dmzA.RepoOzet) sb.AppendLine($"   asset aralıkları · repository {repo}: {aralik} aralık, {n:N0} adres");
        }
        else sb.AppendLine("DMZ asset'i: " + (dmzHata ?? "tanımlı değil"));
        sb.AppendLine(son != null ? $"Son tarama (bellekte): {son.Kural.Id} · {son.Kapsam} · {son.Durum} · {son.Baslangic:dd.MM.yyyy HH:mm}{(son.Kayittan ? " (kayıttan açıldı)" : "")}" : "Bellekte tarama yok.");
        // Carbon Black (kullanıcı: "bir IP gelmiyor, ekip Java var diyor; fazla mı Nessus bazlıyız, CB'den gelmiyor"): neden CB'de görünmediği
        var cbOzet = new Dictionary<string, string>();
        if (cfg.GetValue("Zafiyet:TaniCarbonBlack", true))
            try { await CbTaniAsync(ips, k, sb, cbOzet, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { sb.AppendLine().AppendLine("Carbon Black tanısı yapılamadı: " + ex.Message); }
        if (!nessus.Acik) { sb.AppendLine().AppendLine("Nessus tanımlı değil."); foreach (var (ip, o) in cbOzet) sb.AppendLine($"{ip}: CB: {o}"); return sb.ToString(); }
        List<NessusService.NessusHam> ham; bool kesik;
        try { (ham, kesik) = await nessus.HamBulgularAsync(ips, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { sb.AppendLine("Nessus sorgusu hatası: " + ex.Message); return sb.ToString(); }
        if (kesik) sb.AppendLine("UYARI: bazı IP'lerin bulguları sayfa sınırında kesildi.");
        var simdi = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ozet = new List<string>();
        foreach (var ip in ips)
        {
            sb.AppendLine().AppendLine($"===== {ip} =====");
            var dmzRepolari = dmzA?.Repolari(ip) ?? [];
            bool dmz = dmzA?.Icerir(ip, null, dmzRepo) == true;
            var sat = satir.Where(r => r.Ip == ip).ToList();
            sb.AppendLine($"DMZ asset aralığında: {(dmzRepolari.Count > 0 ? "evet — repository " + string.Join(", ", dmzRepolari) : "HAYIR")} · son taramada: {(sat.Count > 0 ? $"{sat.Count} satır ({string.Join(", ", sat.Select(r => r.Kaynak).Distinct())})" : "SATIR YOK")}");
            var l = ham.Where(x => x.Ip == ip).ToList();
            if (l.Count == 0) { sb.AppendLine("Nessus: bu IP için HİÇ bulgu yok (anahtarın görebildiği repository'lerde taranmamış ya da IP farklı)."); ozet.Add($"{ip}: Nessus'ta hiç bulgu yok"); continue; }
            sb.AppendLine($"Nessus: {l.Count} bulgu · repository: {string.Join(", ", l.Select(x => $"{x.RepoAd} ({x.RepoId})").Distinct())} · DNS: {l.Select(x => x.Dns).FirstOrDefault(x => !string.IsNullOrEmpty(x))}");
            var ilgili = l.Where(x => Kelime(x.PluginAd, out _) || cveler.Any(c => (x.Cve ?? "").Contains(c, StringComparison.OrdinalIgnoreCase))).ToList();
            if (ilgili.Count == 0)
            {
                sb.AppendLine($"İLGİLİ BULGU YOK — plugin adında {string.Join(" / ", kel)} geçmiyor, CVE yok. Bu IP'deki ürün / yazılım bulgularından örnekler:");
                foreach (var x in l.Where(x => Regex.IsMatch(x.PluginAd, "detect|installed|version|server|web|http|java|jdk|nginx|apache|tomcat|gateway", RegexOptions.IgnoreCase))
                    .GroupBy(x => x.PluginId).Select(g => g.First()).Take(25))
                    sb.AppendLine($"   [{x.PluginId}] {x.PluginAd} · port {x.Port} · {Tarih(x.Son)}");
                ozet.Add($"{ip}: ilgili plugin yok (kelime eşleşmiyor)");
                continue;
            }
            var nedenler = new List<string>();
            foreach (var x in ilgili.GroupBy(x => (x.PluginId, x.Port)).Select(g => g.OrderByDescending(y => y.Son).First()))
            {
                var r = new List<string>();
                bool dmzBu = dmzA != null && (dmzRepo.Count == 0 || x.RepoId == null || dmzRepo.Contains(x.RepoId)) && (dmzA.Icerir(ip) || dmzRepolari.Count > 0);
                bool repoOk = repolar.Length == 0 || repolar.Contains(x.RepoId) || dmzBu;
                if (!repoOk) r.Add($"repository {x.RepoAd} ({x.RepoId}) Nessus:Repolar'da yok{(dmzA != null ? " ve IP DMZ asset aralığında değil" + (dmzRepo.Count > 0 ? " / Zafiyet:DmzRepolar dışında" : "") : "")}");
                if (gun > 0 && x.Son > 0 && simdi - x.Son > gun * 86400L) r.Add($"son görülme {Tarih(x.Son)} — {gun} günden eski (Zafiyet:NessusGun)");
                bool cveOk = cveler.Any(c => (x.Cve ?? "").Contains(c, StringComparison.OrdinalIgnoreCase));
                bool kelOk = Kelime(x.PluginAd, out var w);
                string kur = x.Kurulumlar.Count == 0 ? "kurulum / sürüm satırı yok"
                    : string.Join(" | ", x.Kurulumlar.Take(3).Select(z => $"{z.Yol} '{z.Surum}' → {(NessusSurum(z.Surum) is { } ns ? "okunan " + ns : "OKUNAMADI")}"));
                string karar = r.Count == 0 ? "TARAMADA ALINIR" : "ALINMAZ: " + string.Join("; ", r);
                sb.AppendLine($" - [{x.PluginId}] {x.PluginAd} · ciddiyet {x.Seviye} · port {x.Port} · son {Tarih(x.Son)} · {(cveOk ? "CVE eşleşmesi" : "kelime: " + w)}");
                sb.AppendLine($"     {kur}");
                sb.AppendLine($"     → {karar}");
                nedenler.AddRange(r);
            }
            ozet.Add($"{ip}: " + (nedenler.Count == 0 ? (sat.Count > 0 ? "taramada var" : "Nessus bulgusu alınır ama son taramada satır yok (o tarama bu düzeltmeden önceyse yeniden tarayın)")
                : string.Join("; ", nedenler.Distinct())));
        }
        sb.AppendLine().AppendLine("===== ÖZET =====");
        foreach (var o in ozet) sb.AppendLine(o);
        if (cbOzet.Count > 0) { sb.AppendLine().AppendLine("Carbon Black:"); foreach (var (ip, o) in cbOzet) sb.AppendLine($"{ip}: {o}"); }
        return sb.ToString();
    }
    // Carbon Black tanısı (son Zafiyet:TaniSaat=168 saat): IP'nin olayları (ajan çalışıyor mu), sunucudaki süreçler (kurala uyan / izi olup uymayan), olay yoksa ERT adıyla arama
    // (başka IP ile mi görünüyor), komut satırında iz olan süreç başlangıçları.
    async Task CbTaniAsync(List<string> ips, ZfKural k, System.Text.StringBuilder sb, Dictionary<string, string> ozet, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", st = s["Sourcetype"] ?? "bit9:carbonblack:json";
        int saat = Math.Clamp(cfg.GetValue("Zafiyet:TaniSaat", 168), 1, 720), taramaSaat = cfg.GetValue("Zafiyet:Saat", 24);
        var sure = TimeSpan.FromSeconds(cfg.GetValue("Zafiyet:TaniSureSaniye", 180));
        var http = factory.CreateClient("splunk");
        var bit = DateTimeOffset.Now;
        var aralik = new LookupQuery("0.0.0.0", bit.AddHours(-saat), bit);
        var exeRx = new Regex(ExeDesen(k, false));
        // "iz": kuralın exe adları (joker atılmış) + Java kuralında JVM izleri
        string izDesen = string.Join("|", Temiz(k.Exeler).Select(e => Regex.Replace(e.Replace("*", ""), @"[^a-z0-9_-]", ".")).Where(e => e.Length >= 3))
            + (k.Java ? "|java|jre|jdk|jvm|openjdk|zulu|corretto|temurin|graalvm|semeru|-jar" : "");
        var izRx = new Regex(izDesen, RegexOptions.IgnoreCase);
        var env = await envanter.GetAsync(false, ct);
        sb.AppendLine().AppendLine($"===== CARBON BLACK (son {saat} saat; tarama penceresi: ağ olayları {taramaSaat} saat, süreç başlangıçları {cfg.GetValue("Zafiyet:SurecSaat", 168)} saat) =====");
        sb.AppendLine($"Kuralın süreç adları: {string.Join(", ", Temiz(k.Exeler))} · iz araması: {izDesen}");

        string ipList = string.Join(", ", ips.Select(i => $"\"{i}\""));
        string spl1 = $"""
            search index={index} sourcetype="{st}" ({string.Join(" OR ", ips.Select(i => $"TERM({i})"))})
            | where in(local_ip, {ipList})
            | eval yol=coalesce(process_path, process_name, "")
            | stats count max(_time) as son values(computer_name) as pc values(local_port) as lp by local_ip yol
            | eval pc=mvjoin(mvindex(pc, 0, 2), "|||"), lp=mvjoin(mvindex(lp, 0, 9), " ")
            """;
        var r1 = await SplunkService.ExportAsync(spl1, aralik, cfg, http, ct, "Carbon Black (tanı)", sure);
        foreach (var m in r1.Messages) sb.AppendLine("  (Splunk) " + m);
        var ipRows = r1.Rows.GroupBy(r => V(r, "local_ip") ?? "").ToDictionary(g => g.Key, g => g.ToList());

        // bilgisayar adları: CB olaylarından + ERT
        var pcler = new Dictionary<string, HashSet<string>>();
        foreach (var ip in ips)
        {
            var set = new HashSet<string>(ipRows.GetValueOrDefault(ip, []).SelectMany(r => (V(r, "pc") ?? "").Split("|||")).Select(KisaPc).Where(x => x.Length > 0));
            foreach (var a in env.AppsOnIp(ip).Select(a => a.UnixName).OfType<string>()) if (KisaPc(a) is { Length: >= 3 } kp) set.Add(kp);
            pcler[ip] = set;
        }
        var tumPc = pcler.Values.SelectMany(x => x).Where(x => Regex.IsMatch(x, @"^[a-z0-9_\-]{3,}$")).Distinct().Take(60).ToList();

        // olayı olmayan IP'ler: ERT / CB adıyla ara — başka IP ile mi görünüyor
        var adIp = new Dictionary<string, List<(string Pc, string Ipler, string Son, string N)>>();
        var olaysiz = ips.Where(ip => !ipRows.ContainsKey(ip)).ToList();
        var olaysizPc = olaysiz.SelectMany(ip => pcler[ip]).Where(x => Regex.IsMatch(x, @"^[a-z0-9_\-]{3,}$")).Distinct().Take(40).ToList();
        if (olaysizPc.Count > 0)
        {
            string spl2 = $"""
                search index={index} sourcetype="{st}" ({string.Join(" OR ", olaysizPc.Select(x => $"TERM({x})"))})
                | eval pc=lower(mvindex(split(computer_name, "."), 0))
                | where in(pc, {string.Join(", ", olaysizPc.Select(x => $"\"{x}\""))})
                | stats count max(_time) as son values(local_ip) as ipler by pc
                | eval ipler=mvjoin(mvindex(ipler, 0, 9), " ")
                """;
            try
            {
                var r2 = await SplunkService.ExportAsync(spl2, aralik, cfg, http, ct, "Carbon Black (tanı, ad)", sure);
                foreach (var r in r2.Rows) if (V(r, "pc") is { } pc)
                        foreach (var ip in olaysiz.Where(i => pcler[i].Contains(pc)))
                        { if (!adIp.TryGetValue(ip, out var l)) adIp[ip] = l = []; l.Add((pc, V(r, "ipler") ?? "", Zaman(V(r, "son")) ?? "", V(r, "count") ?? "")); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { sb.AppendLine("  ad araması yapılamadı: " + ex.Message); }
        }

        // süreç başlangıçlarında iz (komut satırı)
        var basl = new Dictionary<string, List<Dictionary<string, string>>>();
        if (tumPc.Count > 0)
        {
            string spl3 = $"""
                search index={index} sourcetype="{st}" TERM(ingress.event.procstart) ({string.Join(" OR ", tumPc.Select(x => $"TERM({x})"))})
                | eval yol=coalesce(process_path, process_name, ""), cmd=coalesce(cmdline, command_line, process_cmdline, ""), pc=lower(mvindex(split(computer_name, "."), 0))
                | where match(lower(yol." ".cmd), "{izDesen.Replace("\\", "\\\\").Replace("\"", "")}")
                | eval cmd=substr(cmd, 1, 300)
                | stats count max(_time) as son latest(cmd) as cmd by pc yol
                """;
            try
            {
                var r3 = await SplunkService.ExportAsync(spl3, aralik, cfg, http, ct, "Carbon Black (tanı, süreç başlangıcı)", sure);
                foreach (var r in r3.Rows) if (V(r, "pc") is { } pc) { if (!basl.TryGetValue(pc, out var l)) basl[pc] = l = []; l.Add(r); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { sb.AppendLine("  süreç başlangıcı araması yapılamadı: " + ex.Message); }
        }

        foreach (var ip in ips)
        {
            sb.AppendLine().AppendLine($"----- {ip} · Carbon Black -----");
            var rows = ipRows.GetValueOrDefault(ip, []);
            var bas = pcler[ip].SelectMany(pc => basl.GetValueOrDefault(pc, [])).ToList();
            if (rows.Count == 0)
            {
                sb.AppendLine($"Bu IP'den son {saat} saatte HİÇ ağ olayı yok.");
                if (adIp.TryGetValue(ip, out var al) && al.Count > 0)
                {
                    foreach (var a in al) sb.AppendLine($"  ad '{a.Pc}' ile olaylar VAR: {a.N} olay, son {a.Son}, CB'deki IP'ler: {a.Ipler}");
                    ozet[ip] = $"bu IP ile olay yok ama sunucu adı ({string.Join(", ", al.Select(a => a.Pc))}) CB'de başka IP ile görünüyor: {string.Join(" ", al.Select(a => a.Ipler))} — çok kartlı / NAT; o IP'yi tarayın";
                }
                else
                {
                    sb.AppendLine(pcler[ip].Count > 0 ? $"  ERT adıyla ({string.Join(", ", pcler[ip])}) da olay yok." : "  ERT'de sunucu adı yok; adla aranamadı.");
                    ozet[ip] = "CB bu sunucudan olay almıyor — ajan kurulu değil / çalışmıyor ya da ad farklı; Java yalnız Nessus'tan görülebilir";
                }
                if (bas.Count > 0) foreach (var b in bas.Take(10)) sb.AppendLine($"  süreç başlangıcı: {V(b, "yol")} · {V(b, "count")} kez · son {Zaman(V(b, "son"))} · {V(b, "cmd")}");
                continue;
            }
            long toplam = rows.Sum(r => long.TryParse(V(r, "count"), out var n) ? n : 0);
            double sonT = rows.Max(r => double.TryParse(V(r, "son"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0);
            sb.AppendLine($"Ağ olayı: {toplam:N0} · {rows.Count} süreç · son {Zaman(sonT.ToString(System.Globalization.CultureInfo.InvariantCulture))} · bilgisayar: {string.Join(", ", pcler[ip])}");
            var uyan = rows.Where(r => exeRx.IsMatch((V(r, "yol") ?? "").ToLowerInvariant())).ToList();
            // iz: yolunda iz olan ya da süreç başlangıcının komut satırında iz olan (ör. wrapper.exe … jvm.dll) süreçler
            var izYol = bas.Select(b => YolAnahtar(V(b, "yol") ?? "")).ToHashSet();
            var iz = rows.Where(r => !uyan.Contains(r) && (izRx.IsMatch(V(r, "yol") ?? "") || izYol.Contains(YolAnahtar(V(r, "yol") ?? "")))).ToList();
            string Satir(Dictionary<string, string> r) => $"{V(r, "yol")} · {V(r, "count")} olay · port {V(r, "lp") ?? "-"} · son {Zaman(V(r, "son"))}";
            foreach (var r in uyan) sb.AppendLine("  ✔ KURALA UYUYOR   " + Satir(r));
            foreach (var r in iz) sb.AppendLine("  ☕ İZ VAR, KURALA UYMUYOR   " + Satir(r) + (izRx.IsMatch(V(r, "yol") ?? "") ? "" : "  (iz komut satırında)") + "  ← exe adı kuralda yok");
            foreach (var r in bas) sb.AppendLine($"  ▶ süreç başlangıcı: {V(r, "yol")} · {V(r, "count")} kez · son {Zaman(V(r, "son"))} · {V(r, "cmd")}");
            var diger = rows.Except(uyan).Except(iz).OrderByDescending(r => long.TryParse(V(r, "count"), out var n) ? n : 0).ToList();
            sb.AppendLine($"  Diğer süreçler ({diger.Count}, ilk 25):");
            foreach (var r in diger.Take(25)) sb.AppendLine("     " + Satir(r));
            // sonuç
            double uyanSon = uyan.Select(r => double.TryParse(V(r, "son"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0).DefaultIfEmpty(0).Max();
            ozet[ip] = uyan.Count > 0
                ? (DateTimeOffset.FromUnixTimeSeconds((long)uyanSon) < bit.AddHours(-taramaSaat)
                    ? $"kurala uyan süreç VAR ama son ağ olayı {Zaman(uyanSon.ToString(System.Globalization.CultureInfo.InvariantCulture))} — tarama penceresi {taramaSaat} saat; 'Ağ olayı penceresi'ni 7 gün seçin"
                    : "kurala uyan süreç VAR ve pencerede — taramada çıkması gerekir (son tarama bu IP'yi içeriyor mu, DMZ / IP süzgeci?)")
                : iz.Count > 0 ? $"Java / ürün başka adlı exe içinde çalışıyor: {string.Join(", ", iz.Select(r => ExeAd(V(r, "yol") ?? "")).Distinct())} — bu exe adını kurala ekleyin (Kopyala / düzenle → exe adları)"
                : bas.Count > 0 ? "süreç başlangıcında iz var ama ağ olayı yok — süreç ağ bağlantısı yapmıyor; taramada 'Carbon Black (yalnız süreç başlangıcı)' olarak çıkmalı, çıkmıyorsa bilgisayar adı ERT'deki adla eşleşmiyor"
                : $"CB bu sunucuda olay alıyor ama son {saat} saatte Java / kuraldaki süreç görülmedi — kurulu ama çalışmıyor olabilir (yalnız Nessus görür) ya da 7 günden önce başlamış ve ağ olayı yok";
        }
    }

    static string Tarih(long u) => u > 0 ? DateTimeOffset.FromUnixTimeSeconds(u).ToLocalTime().ToString("dd.MM.yyyy") : "?";

    // ---------------- Kör nokta (Nessus envanteri) ----------------
    async Task<List<ZfSatir>> KorNoktaAsync(ZfKural k, EnvanterSnapshot env, List<ZfSatir> satir, List<string>? ipler, NessusService.NessusAssetIp? dmzA, int saat,
        List<string> mesaj, CancellationToken ct)
    {
        var ek = new List<ZfSatir>();
        if (ipler == null && dmzA == null)
        {
            mesaj.Add("Nessus süreç / servis / yazılım listesi araması (ajan olmayan sunucular) yalnız IP ya da DMZ kapsamında yapılır (tüm sunucularda çok büyük): IP'leri girin ya da Zafiyet:DmzAsset tanımlayın.");
            return ek;
        }
        Set(i => i with { Asama = "Nessus: süreç / servis / yazılım listelerinde kuralın ürünü (kör nokta)" });
        var (kay, kesik) = await nessus.EnvanterAsync(ipler, ipler == null ? dmzA!.Asset.Id : null, ct);
        var olan = satir.Select(r => r.Ip).OfType<string>().ToHashSet();
        var test = (cfg.GetSection("Eksik:TestIfadeleri").Get<string[]>() ?? ["test"]).Select(x => x.ToLowerInvariant()).ToArray();
        // eşleşme: kuralın exe adları (yol / süreç adı olarak) ya da yazılım satırında Nessus kelimeleri (Java'da JVM izleri)
        var adlar = Temiz(k.Exeler).Select(e => Regex.Escape(e).Replace(@"\*", "[a-z0-9_.-]*")).ToList();
        var exeRx = new Regex($@"(?<![a-z0-9_.-])({string.Join("|", adlar)})(\.exe)?(?![a-z0-9_-])", RegexOptions.IgnoreCase);
        var yazRx = new Regex(@"(?<![a-z])(" + string.Join("|", NessusKelimeleri(k).Select(Regex.Escape)) + (k.Java ? "|openjdk|jre|jdk|temurin|corretto|zulu|graalvm|semeru" : "") + @")(?![a-z])", RegexOptions.IgnoreCase);
        var haric = k.Java ? new Regex("javascript|jquery|json", RegexOptions.IgnoreCase) : null;
        var ipGrup = kay.GroupBy(x => x.Ip).ToDictionary(g => g.Key, g => g.ToList());
        int kimlikli = 0, kimliksiz = 0, bulunan = 0;
        var kimliksizIp = new List<string>();
        foreach (var (ip, l) in ipGrup)
        {
            bool? kim = null;
            foreach (var x in l.Where(x => x.PluginId == "19506"))
                if (Regex.Match(x.Metin, @"Credentialed checks\s*:\s*(\w+)", RegexOptions.IgnoreCase) is { Success: true } m)
                    kim = (kim ?? false) || m.Groups[1].Value.StartsWith("yes", StringComparison.OrdinalIgnoreCase);
            if (kim == true) kimlikli++; else if (kim == false) { kimliksiz++; kimliksizIp.Add(ip); }
            if (olan.Contains(ip)) continue;
            // satır satır eşleşme: (yol / ad, ham satır, plugin)
            var es = new List<(string Yol, string Ham, string Plugin, string? Son, string? Repo, string? Dns, string? Nb)>();
            foreach (var x in l.Where(x => x.PluginId != "19506"))
                foreach (var satirMetin in x.Metin.Split('\n').Select(t => t.Trim()).Where(t => t.Length > 2))
                {
                    bool yaz = x.PluginId is "20811" or "22869";
                    bool uy = exeRx.IsMatch(satirMetin) || (yaz && yazRx.IsMatch(satirMetin));
                    if (!uy || (haric?.IsMatch(satirMetin) ?? false)) continue;
                    // yol: satırdaki ilk dosya yolu (servis / dinleyen program), yoksa satırın kendisi
                    string yol = Regex.Match(satirMetin, @"([A-Za-z]:\\[^""|<>\r\n]+?\.exe|/(?:[^\s""]+/)+[^\s""]+)", RegexOptions.IgnoreCase) is { Success: true } ym ? ym.Value.Trim()
                        : exeRx.Match(satirMetin) is { Success: true } em && !yaz ? em.Value : satirMetin;
                    if (yol.Length > 260) yol = yol[..260];
                    es.Add((yol, satirMetin.Length > 300 ? satirMetin[..300] : satirMetin, x.PluginId, x.Son, x.RepoAd, x.Dns, x.Netbios));
                }
            // yalnız adı olan (süreç listesi "java.exe") kayıt, aynı exe'nin yolu bilinen kaydı varsa atılır
            var yollu = es.Where(e => e.Yol.Contains('\\') || e.Yol.Contains('/')).Select(e => ExeAd(e.Yol).ToLowerInvariant()).ToHashSet();
            es = es.Where(e => e.Yol.Contains('\\') || e.Yol.Contains('/') || e.Plugin is "20811" or "22869" || !yollu.Contains(e.Yol.ToLowerInvariant())).ToList();
            foreach (var e in es.GroupBy(e => YolNorm(e.Yol)).Select(g => g.First()).Take(4))
            {
                // sürüm: yol / satır (Java kalıpları), "Java 8 Update 202", sonra ilk a.b.c
                string? sur = Surum(k, e.Yol) ?? Surum(k, e.Ham)
                    ?? (Regex.Match(e.Ham, @"java\D{0,20}?(\d+)\s*update\s*(\d+)", RegexOptions.IgnoreCase) is { Success: true } jm ? NormSurum($"{jm.Groups[1].Value}u{jm.Groups[2].Value}") : null)
                    ?? (e.Plugin is "20811" or "22869" ? NessusSurum(e.Ham) : null);
                var (du, no) = Degerlendir(k, sur);
                string kaynakAd = e.Plugin switch { "70329" => "süreç listesi", "44401" => "Windows servisleri", "34252" or "25221" => "portu dinleyen program", "20811" => "kurulu yazılım", _ => "paket listesi" };
                var b = new NessusBulgu(ip, e.Dns, e.Nb, e.Plugin, kaynakAd, "0", null, e.Son, []);
                var r = NessusSatir(k, env, test, b, e.Yol, sur, du, no + $" · Nessus {kaynakAd}: {e.Ham}", $"envanter ({kaynakAd}, plugin {e.Plugin}, {e.Son})", $"Nessus ({kaynakAd})");
                ek.Add(r with { Repo = e.Repo, Exe = e.Plugin is "20811" or "22869" ? (k.Java ? "java (Nessus yazılım)" : r.Exe) : ExeAd(e.Yol.TrimEnd('\\', '/')) is { Length: > 0 } ea ? ea : r.Exe });
            }
            if (es.Count > 0) { bulunan++; olan.Add(ip); }
        }
        mesaj.Add($"Nessus envanteri ({(ipler != null ? ipler.Count + " IP" : "DMZ asset'i")}; süreç / servis / dinleyen program / yazılım listeleri, son {nessus.AcikGun} gün, tüm repolar): {ipGrup.Count} sunucuda kayıt "
            + $"({kimlikli} kimlikli, {kimliksiz} kimliksiz tarama) → {bulunan} sunucuda kuralın ürünü bulundu, {ek.Count} yeni satır."
            + (kimliksiz > 0 ? $" Kimliksiz taranan {kimliksiz} sunucuda Nessus kurulu yazılım / süreç GÖREMEZ: {string.Join(", ", kimliksizIp.Take(15))}{(kimliksiz > 15 ? " …" : "")}." : "")
            + (kesik ? " Sayfa sınırında kesildi (Zafiyet:NessusEnvanterSayfa)." : ""));

        // IP kipi: verilen ama hiçbir yerde görünmeyen IP → nedenli satır (kullanıcı: "her koşulda getirelim, kısıt varsa belirt")
        if (ipler != null)
        {
            var yok = ipler.Where(ip => !olan.Contains(ip)).ToList();
            if (yok.Count > 0)
            {
                var cbVar = await CbOlayVarAsync(yok, saat, ct);
                foreach (var ip in yok)
                {
                    var l = ipGrup.GetValueOrDefault(ip) ?? [];
                    bool? kim = null;
                    foreach (var x in l.Where(x => x.PluginId == "19506"))
                        if (Regex.Match(x.Metin, @"Credentialed checks\s*:\s*(\w+)", RegexOptions.IgnoreCase) is { Success: true } m)
                            kim = (kim ?? false) || m.Groups[1].Value.StartsWith("yes", StringComparison.OrdinalIgnoreCase);
                    string cb = cbVar == null ? "Carbon Black kontrol edilemedi" : cbVar.Contains(ip) ? $"Carbon Black bu IP'den olay alıyor ama son {saat} saatte kuralın süreci görülmedi"
                        : $"Carbon Black son {saat} saatte bu IP'den HİÇ olay almadı (ajan yok / çalışmıyor ya da IP farklı)";
                    string ns = l.Count == 0 ? $"Nessus'ta son {nessus.AcikGun} günde envanter kaydı yok (taranmamış ya da anahtarın görmediği repo)"
                        : kim == false ? "Nessus KİMLİKSİZ taramış — kurulu yazılım / çalışan süreç göremez (yalnız açık portlar)"
                        : kim == true ? "Nessus kimlikli taramış, süreç / servis / yazılım listesinde kuralın ürünü yok"
                        : "Nessus kaydı var ama tarama bilgisi (19506) yok — kimlikli mi bilinmiyor";
                    var b = new NessusBulgu(ip, l.Select(x => x.Dns).FirstOrDefault(x => x != null), l.Select(x => x.Netbios).FirstOrDefault(x => x != null), "", "", null, null,
                        l.Select(x => x.Son).Max(), []);
                    var r = NessusSatir(k, env, test, b, "(görülmedi)", null, "bilinmiyor", cb + " · " + ns, ns, "Kör nokta (görülmedi)");
                    ek.Add(r with { Exe = "(görülmedi)", Oncelik = "incele", Repo = l.Select(x => x.RepoAd).FirstOrDefault(x => x != null) });
                }
                mesaj.Add($"Hiçbir kaynakta görülmeyen {yok.Count} IP 'Kör nokta' satırı olarak eklendi (nedeni satırda).");
            }
        }
        return ek;
    }

    // CB'de bu IP'lerden pencere içinde herhangi bir olay var mı (ajan çalışıyor mu); hata → null
    async Task<HashSet<string>?> CbOlayVarAsync(List<string> ips, int saat, CancellationToken ct)
    {
        try
        {
            var s = cfg.GetSection("Splunk");
            string index = s["Index"] ?? "carbonblack", st = s["Sourcetype"] ?? "bit9:carbonblack:json";
            var set = new HashSet<string>();
            foreach (var parca in ips.Chunk(50))
            {
                string spl = $"""
                    search index={index} sourcetype="{st}" ({string.Join(" OR ", parca.Select(i => $"TERM({i})"))})
                    | where in(local_ip, {string.Join(", ", parca.Select(i => $"\"{i}\""))})
                    | stats count by local_ip
                    """;
                var bit = DateTimeOffset.Now;
                var r = await SplunkService.ExportAsync(spl, new LookupQuery("0.0.0.0", bit.AddHours(-saat), bit), cfg, factory.CreateClient("splunk"), ct, "Carbon Black (olay var mı)",
                    TimeSpan.FromSeconds(120));
                foreach (var x in r.Rows) if (V(x, "local_ip") is { } ip) set.Add(ip);
            }
            return set;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("CB olay kontrolü: {H}", ex.Message); return null; }
    }

    // Kurum içi mi: özel IP ya da kurumda kullanılan genel blok (uygulama / LB GW / ≥/8 segment kaydı var) — Yolculuk'taki "dış" tanımı
    static bool Ic(EnvanterSnapshot env, string ip)
    {
        if (Yolculuk.IsPrivate(ip)) return true;
        if (env.AppNamesOnIp(ip).Count > 0 || env.IsVipGw(ip, out _)) return true;
        var seg = env.FindSegment(ip);
        return seg != null && int.TryParse(seg.Cidr.Split('/').ElementAtOrDefault(1), out int len) && len >= 8;
    }

    // Kullanıcı: "dışa açık, dış IP'lerle iletişim kuranları cidden kritik ele almalıyız": etkilenen / olası sürümde DMZ, dış istemci / VIP ya da
    // dış IP'ye bağlanan ("derin dış") süreç → kritik; sürümü bilinmeyen DMZ / derin dış → en az incele. Geçici önlem varsa en çok orta (değişmez).
    static ZfSatir DisOncelik(ZfSatir r)
    {
        if (r.Onlem == "var") return r;
        bool disaAcik = r.Dmz || r.DisGidenSay > 0 || r.Portlar.Any(p => p.Dis.Count > 0);
        if (!disaAcik) return r;
        if (r.Durum is "etkilenen" or "olasi" or "bilgi") return r with { Oncelik = "kritik" };
        if (r.Durum == "bilinmiyor" && r.Oncelik == "dusuk") return r with { Oncelik = "incele" };
        return r;
    }

    // DMZ asset'i (Zafiyet:DmzAsset: Tenable.sc asset id'si ya da adı) → IP kümesi; Nessus kapalı / tanımsızsa boş
    // Zafiyet:DmzRepolar: DMZ asset sorgusunda kabul edilen repository id'leri (boş = asset'in kendi repository aralıkları yeter)
    List<string> DmzRepolar => cfg.GetSection("Zafiyet:DmzRepolar").GetChildren().Select(c => c.Value?.Trim()).OfType<string>().Where(v => v != "").ToList();
    async Task<(NessusService.NessusAssetIp? A, string? Hata)> DmzAsync(CancellationToken ct)
    {
        string? tanim = cfg["Zafiyet:DmzAsset"];
        if (string.IsNullOrWhiteSpace(tanim)) return (null, null);
        if (!nessus.Acik) return (null, "Nessus tanımlı değil (Nessus:BaseUrl).");
        try
        {
            var (a, hata) = await nessus.AssetBulAsync(tanim, ct);
            if (a == null) return (null, hata);
            return (await nessus.AssetIpleriAsync(a, ct), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (null, ex.Message); }
    }
    public async Task<object> DmzBilgiAsync(CancellationToken ct)
    {
        var (a, hata) = await DmzAsync(ct);
        return new { tanimli = !string.IsNullOrWhiteSpace(cfg["Zafiyet:DmzAsset"]), asset = a?.Asset, ipSayisi = a?.Asset.IpSayisi ?? a?.IpSayisi ?? 0, aralik = a?.Araliklar.Count,
            repolar = a?.RepoOzet.Select(x => new { x.Repo, x.Aralik, x.Ip }), kaynak = a?.Kaynak, hata };
    }

    // Nessus'la değişen satırların önceliği: CB satırında port / dış istemci bilgisi; Nessus satırında VIP üyeliği maruziyet sayılır
    static ZfSatir Oncelikle(ZfSatir r)
    {
        bool risk = r.Durum is "etkilenen" or "olasi" or "bilgi";
        bool cbPort = r.Portlar.Any(p => p.Port != "VIP");
        bool disari = r.Portlar.Any(p => p.Vip.Count > 0 || p.Dis.Count > 0);
        bool nessusDogru = r.Nessus?.Contains("CVE bulgusu") == true;
        string o = risk ? (cbPort ? (disari ? "kritik" : "yuksek") : disari ? "yuksek" : nessusDogru ? "yuksek" : "orta")
            : r.Durum == "bilinmiyor" ? (cbPort || disari || r.Kaynak.StartsWith("Nessus") ? "incele" : "dusuk") : "dusuk";
        if (nessusDogru && disari && risk) o = "kritik";
        if (r.Onlem == "var" && o is "kritik" or "yuksek") o = "orta";
        return r with { Oncelik = o };
    }

    // ---------------- Kayıtlı kurallar (DF_ZAFIYET_KURAL; sql/zafiyet_kural.sql) ----------------
    string? _kayitUyari;
    (List<ZfKural> L, DateTime At)? _kayitli;
    readonly Dictionary<string, ZfKural> _bellek = new(StringComparer.OrdinalIgnoreCase);
    static readonly System.Text.Json.JsonSerializerOptions Js = new(System.Text.Json.JsonSerializerDefaults.Web);
    string Tablo => cfg["Zafiyet:KuralTablosu"] is { } t && Regex.IsMatch(t, @"^[\w.\[\]]+$") ? t : "dbo.DF_ZAFIYET_KURAL";

    async Task<List<ZfKural>> KayitliAsync(CancellationToken ct)
    {
        lock (_gate) if (_kayitli is { } k && DateTime.UtcNow - k.At < TimeSpan.FromMinutes(5)) return k.L;
        var l = new List<ZfKural>();
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT ID, KURAL, TARIH, KAYDEDEN FROM {Tablo} ORDER BY TARIH DESC", conn);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
                try
                {
                    if (System.Text.Json.JsonSerializer.Deserialize<ZfKural>(rd.GetString(1), Js) is { Exeler.Count: > 0 } z)
                        l.Add(z with { Id = rd.GetString(0), Kaynak = "kayıtlı", Tarih = rd.IsDBNull(2) ? null : rd.GetDateTime(2), Kaydeden = rd.IsDBNull(3) ? null : rd.GetString(3) });
                }
                catch (System.Text.Json.JsonException) { }
            _kayitUyari = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { _kayitUyari = $"Kural tablosu ({Tablo}) yok; kaydedilen kurallar yalnız bellekte tutuluyor (sql/zafiyet_kural.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Kayıtlı açıklık kuralları okunamadı"); _kayitUyari = "Kayıtlı kurallar okunamadı: " + ex.Message; }
        lock (_gate)
        {
            l.AddRange(_bellek.Values.Where(b => !l.Any(x => x.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase))));
            _kayitli = (l, DateTime.UtcNow);
        }
        return l;
    }

    static string Slug(string s)
    {
        string t = Regex.Replace(s.ToLowerInvariant().Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c'), "[^a-z0-9]+", "-").Trim('-');
        return t.Length > 60 ? t[..60] : t.Length == 0 ? "kural" : t;
    }

    public async Task<(ZfKural? Kural, string? Hata)> KaydetAsync(ZfOzel o, string? user, CancellationToken ct)
    {
        var (k0, hata) = OzelKural(o, "x");
        if (k0 == null) return (null, hata);
        string id = Slug(k0.Cve ?? k0.Ad);
        if (id is "anlik" or "java4shell") id += "-k";
        var k = k0 with { Id = id, Kaynak = null, Kaydeden = user, Tarih = DateTime.Now };
        string json = System.Text.Json.JsonSerializer.Serialize(k with { Kaydeden = null, Tarih = null }, Js);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {Tablo} AS t USING (SELECT @id AS ID) AS s ON t.ID = s.ID
                WHEN MATCHED THEN UPDATE SET KURAL = @k, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (ID, KURAL, TARIH, KAYDEDEN) VALUES (@id, @k, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@k", json);
            cmd.Parameters.AddWithValue("@u", (object?)user ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (_gate) _bellek[id] = k with { Kaynak = "kayıtlı (bellekte)" };
            _kayitUyari = (ex is Microsoft.Data.SqlClient.SqlException { Number: 208 } ? $"Kural tablosu ({Tablo}) yok" : "Kural DB'ye yazılamadı (" + ex.Message + ")")
                + "; kural yalnız bellekte tutuldu (uygulama yeniden başlayınca kaybolur; sql/zafiyet_kural.sql).";
        }
        log.LogInformation("Açıklık kuralı kaydedildi: {Id} ({User})", id, user);
        lock (_gate) _kayitli = null;
        return (k with { Kaynak = "kayıtlı" }, null);
    }

    public async Task<bool> SilAsync(string id, string? user, CancellationToken ct)
    {
        bool var;
        lock (_gate) var = _bellek.Remove(id);
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {Tablo} WHERE ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            var |= await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Açıklık kuralı DB'den silinemedi: {H}", ex.Message); }
        log.LogInformation("Açıklık kuralı silindi: {Id} ({User})", id, user);
        lock (_gate) _kayitli = null;
        return var;
    }

    // ---------------- Kayıtlı taramalar (DF_ZAFIYET_TARAMA; sql/zafiyet_tarama.sql) ----------------
    // Kullanıcı: "son taramalar kayıtlı olsun, kayıtlıdan getir / yeni tara seçeneği olsun". Biten tarama gzip JSON olarak yazılır; kural başına son
    // Zafiyet:KayitSayisi (5) tutulur. Tablo yoksa / yazılamazsa bellekte (uygulama yeniden başlayınca kaybolur) + uyarı.
    string TaramaTablo => cfg["Zafiyet:TaramaTablosu"] is { } t && Regex.IsMatch(t, @"^[\w.\[\]]+$") ? t : "dbo.DF_ZAFIYET_TARAMA";
    int KayitSayisi => Math.Clamp(cfg.GetValue("Zafiyet:KayitSayisi", 5), 1, 50);
    string? _taramaUyari;
    readonly List<(ZfKayit Ozet, byte[] Veri)> _taramaBellek = [];
    int _bellekId;
    static string KayitAnahtar(ZfKural k) => k.Id.Equals("anlik", StringComparison.OrdinalIgnoreCase) ? "anlik:" + Slug(k.Cve ?? k.Ad) : k.Id;
    static byte[] Sikistir(ZfKayitVeri v)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) System.Text.Json.JsonSerializer.Serialize(gz, v, Js);
        return ms.ToArray();
    }
    static ZfKayitVeri? Ac(byte[] b)
    {
        using var gz = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
        return System.Text.Json.JsonSerializer.Deserialize<ZfKayitVeri>(gz, Js);
    }
    static Dictionary<string, int> OncelikSay(List<ZfSatir> l) => l.GroupBy(r => r.Oncelik).ToDictionary(g => g.Key, g => g.Count());

    async Task<(int Id, DateTime Tarih)> TaramaKaydetAsync(ZfIs i, List<ZfSatir> satir, List<string> mesaj)
    {
        string anahtar = KayitAnahtar(i.Kural) + (i.Dmz ? ":dmz" : "");
        var veri = Sikistir(new ZfKayitVeri(i with { KayitId = null, KayitTarihi = null, Kayittan = false }, satir, mesaj));
        string ozet = System.Text.Json.JsonSerializer.Serialize(OncelikSay(satir), Js);
        int id; DateTime tarih;
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                INSERT INTO {TaramaTablo} (KURAL_ID, KURAL_AD, CVE, KAPSAM, SAAT, SATIR, OZET, SONUC, TARIH, KAYDEDEN)
                OUTPUT INSERTED.ID, INSERTED.TARIH
                VALUES (@k, @ad, @cve, @kap, @saat, @n, @oz, @s, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@k", anahtar);
            cmd.Parameters.AddWithValue("@ad", Kes(i.Kural.Ad, 400));
            cmd.Parameters.AddWithValue("@cve", (object?)Kes(i.Kural.Cve, 400) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@kap", (object?)Kes(i.Kapsam, 400) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@saat", i.Saat);
            cmd.Parameters.AddWithValue("@n", satir.Count);
            cmd.Parameters.AddWithValue("@oz", ozet);
            cmd.Parameters.Add("@s", System.Data.SqlDbType.VarBinary, -1).Value = veri;
            cmd.Parameters.AddWithValue("@u", (object?)i.Kaydeden ?? DBNull.Value);
            await using (var rd = await cmd.ExecuteReaderAsync())
            {
                await rd.ReadAsync();
                id = rd.GetInt32(0); tarih = rd.GetDateTime(1);
            }
            // kural + kişi başına son N kayıt
            await using var sil = new Microsoft.Data.SqlClient.SqlCommand($"""
                DELETE FROM {TaramaTablo} WHERE KURAL_ID = @k AND ISNULL(KAYDEDEN, '') = @u
                  AND ID NOT IN (SELECT TOP (@n) ID FROM {TaramaTablo} WHERE KURAL_ID = @k AND ISNULL(KAYDEDEN, '') = @u ORDER BY TARIH DESC, ID DESC);
                """, conn);
            sil.Parameters.AddWithValue("@k", anahtar);
            sil.Parameters.AddWithValue("@u", i.Kaydeden ?? "");
            sil.Parameters.AddWithValue("@n", KayitSayisi);
            await sil.ExecuteNonQueryAsync();
            _taramaUyari = null;
        }
        catch (Exception ex)
        {
            _taramaUyari = (ex is Microsoft.Data.SqlClient.SqlException { Number: 208 } ? $"Tarama kayıt tablosu ({TaramaTablo}) yok" : "Tarama sonucu DB'ye yazılamadı (" + ex.Message + ")")
                + "; sonuç yalnız bellekte saklandı (uygulama yeniden başlayınca kaybolur; sql/zafiyet_tarama.sql).";
            log.LogWarning("Açıklık taraması kaydedilemedi: {H}", ex.Message);
            tarih = DateTime.Now;
            lock (_gate)
            {
                id = --_bellekId;   // bellekteki kayıtlar eksi numaralı
                _taramaBellek.Insert(0, (new ZfKayit(id, anahtar, i.Kural.Ad, i.Kural.Cve, i.Kapsam, i.Saat, satir.Count, OncelikSay(satir), tarih, i.Kaydeden), veri));
                var fazla = _taramaBellek.Where(x => x.Ozet.KuralId == anahtar && Ayni(x.Ozet.Kaydeden, i.Kaydeden)).Skip(KayitSayisi).ToList();
                foreach (var f in fazla) _taramaBellek.Remove(f);
            }
        }
        log.LogInformation("Açıklık taraması kaydedildi: {K} #{Id} ({N} satır)", anahtar, id, satir.Count);
        return (id, tarih);
    }
    static string? Kes(string? s, int n) => s == null ? null : s.Length > n ? s[..n] : s;

    // kural verilirse yalnız o kuralın kayıtları (anlık kuralda ZfOzel'den aynı anahtar); verilmezse tüm kuralların son kayıtları
    // Yalnız bu kullanıcının kayıtları (yetki kapalıyken / kullanıcı yokken hepsi)
    public async Task<ZfKayitListe> KayitlarAsync(string? kural, CancellationToken ct, string? user = null)
    {
        bool hepsi = string.IsNullOrWhiteSpace(user);
        var l = new List<ZfKayit>();
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                SELECT TOP 200 ID, KURAL_ID, KURAL_AD, CVE, KAPSAM, SAAT, SATIR, OZET, TARIH, KAYDEDEN FROM {TaramaTablo}
                WHERE 1 = 1 {(kural != null ? "AND KURAL_ID = @k" : "")} {(hepsi ? "" : "AND (KAYDEDEN = @u OR KAYDEDEN = @kisa OR KAYDEDEN LIKE @l OR KAYDEDEN IS NULL)")}
                ORDER BY TARIH DESC, ID DESC
                """, conn);
            if (kural != null) cmd.Parameters.AddWithValue("@k", kural);
            if (!hepsi)
            {
                cmd.Parameters.AddWithValue("@u", user!);
                cmd.Parameters.AddWithValue("@kisa", Kul(user));
                cmd.Parameters.AddWithValue("@l", "%\\" + Kul(user).Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]"));
            }
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                Dictionary<string, int> oz = [];
                if (!rd.IsDBNull(7)) try { oz = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(rd.GetString(7), Js) ?? []; } catch (System.Text.Json.JsonException) { }
                l.Add(new ZfKayit(rd.GetInt32(0), rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3),
                    rd.IsDBNull(4) ? null : rd.GetString(4), rd.IsDBNull(5) ? 0 : rd.GetInt32(5), rd.GetInt32(6), oz, rd.GetDateTime(8), rd.IsDBNull(9) ? null : rd.GetString(9)));
            }
            if (_taramaBellek.Count == 0) _taramaUyari = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { _taramaUyari = $"Tarama kayıt tablosu ({TaramaTablo}) yok; taramalar yalnız bellekte saklanıyor (sql/zafiyet_tarama.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Kayıtlı açıklık taramaları okunamadı"); _taramaUyari = "Kayıtlı taramalar okunamadı: " + ex.Message; }
        lock (_gate) l.AddRange(_taramaBellek.Select(x => x.Ozet).Where(o => (kural == null || o.KuralId.Equals(kural, StringComparison.OrdinalIgnoreCase)) && (hepsi || Sahibi(o.Kaydeden, user))));
        return new ZfKayitListe(l.OrderByDescending(x => x.Tarih).ToList(), _taramaUyari);
    }

    // Kayıtlı taramayı sorgusuz açar: iş durumu "bitti" + Kayittan; süzgeçler, CSV ve "Bu IP'yi yeniden tara" (listeye ekle) bu liste üzerinde çalışır
    // Kayıtlı taramanın gövdesi (servis durumuna dokunmaz)
    async Task<(ZfKayitVeri? V, DateTime Tarih, string? Kaydeden, string? Hata)> KayitOkuAsync(int id, CancellationToken ct)
    {
        byte[]? b = null; DateTime tarih = DateTime.Now; string? kaydeden = null;
        lock (_gate) if (_taramaBellek.FirstOrDefault(x => x.Ozet.Id == id) is { Veri: not null } m) { b = m.Veri; tarih = m.Ozet.Tarih; kaydeden = m.Ozet.Kaydeden; }
        if (b == null && id > 0)
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT SONUC, TARIH, KAYDEDEN FROM {TaramaTablo} WHERE ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (await rd.ReadAsync(ct)) { b = (byte[])rd[0]; tarih = rd.GetDateTime(1); kaydeden = rd.IsDBNull(2) ? null : rd.GetString(2); }
        }
        if (b == null) return (null, tarih, kaydeden, "Kayıtlı tarama bulunamadı (silinmiş olabilir).");
        var v = Ac(b);
        return v == null ? (null, tarih, kaydeden, "Kayıt okunamadı.") : (v, tarih, kaydeden, null);
    }

    // Karşılaştırma (kullanıcı: "satırda eski ve yeni halini göreyim, eski versiyon yeni versiyon; tarih olarak da"): kayıtlı taramanın satırları, açmadan
    public async Task<(object? Veri, string? Hata)> KayitVeriAsync(int id, CancellationToken ct, string? user = null)
    {
        var (v, tarih, kaydeden, hata) = await KayitOkuAsync(id, ct);
        if (v != null && !Sahibi(kaydeden ?? v.Is.Kaydeden, user)) return (null, "Bu kayıt başka bir kullanıcıya ait.");
        return v == null ? (null, hata) : (new { id, tarih, kaydeden = v.Is.Kaydeden ?? kaydeden, kural = v.Is.Kural.Id, kapsam = v.Is.Kapsam, satirlar = v.Satirlar }, null);
    }

    // Kaydı bu kullanıcının görünümüne açar (başkasının çalışan taramasını etkilemez); kendi taraması çalışırken açılamaz
    public async Task<(ZfIs? Is, string? Hata)> KayitAcAsync(int id, CancellationToken ct, string? user = null)
    {
        lock (_gate) if (_is is { Durum: "calisiyor" } && Ayni(_isSahip, user)) return (null, "Taramanız çalışıyor; bitince ya da durdurunca açın.");
        var (v, tarih, kaydeden, hata) = await KayitOkuAsync(id, ct);
        if (v == null) return (null, hata);
        if (!Sahibi(kaydeden ?? v.Is.Kaydeden, user)) return (null, "Bu kayıt başka bir kullanıcıya ait.");
        lock (_gate)
        {
            var i = v.Is with { Durum = "bitti", KayitId = id, KayitTarihi = tarih, Kayittan = true, Kaydeden = v.Is.Kaydeden ?? kaydeden };
            _gor[Kul(user)] = (i, v.Satirlar, v.Mesajlar);
            return (i, null);
        }
    }

    public async Task<bool> KayitSilAsync(int id, string? user, CancellationToken ct)
    {
        var (_, _, kaydeden, _) = await KayitOkuAsync(id, ct);
        if (!Sahibi(kaydeden, user)) return false;
        bool var;
        lock (_gate) var = _taramaBellek.RemoveAll(x => x.Ozet.Id == id) > 0;
        if (id > 0)
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"DELETE FROM {TaramaTablo} WHERE ID = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            var |= await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        lock (_gate)
            if (_gor.TryGetValue(Kul(user), out var g) && g.Is?.KayitId == id) _gor[Kul(user)] = (g.Is with { KayitId = null, KayitTarihi = null }, g.Satir, g.Mesaj);
        log.LogInformation("Açıklık taraması kaydı silindi: #{Id} ({User})", id, user);
        return var;
    }

    // ---------------- AI ile kural doldurma ----------------
    // Kullanıcı: "CVE'nin bir bölümünü yapıştırsak hangi process'lerin aranması gerektiğini önerse". AI yalnız FORMU doldurur, tarama başlatmaz.
    // Doğrulama kodda: sürüm / önlem metinde birebir geçmiyorsa atılır; exe adları servis imzası kaydındaki gerçek exe'lerle sayılır
    // (kaç sunucuda görüldü); ayrıca küçük ürün → exe sözlüğü ve ürün adının exe yollarında geçtiği süreçler (envanter) öneriye eklenir.
    static readonly (string Desen, string[] Exeler, string? Not)[] UrunExe =
    [
        // Java çalışma ortamı açığı: java.exe'nin yanında JVM'i KENDİ İÇİNDE çalıştıran Windows servisleri de (Carbon Black'te java.exe görünmez):
        // Tomcat (tomcatN.exe), Apache procrun (prunsrv.exe), Elasticsearch servisi (elasticsearch-service-x64.exe)
        (@"\b(openjdk|java se|jdk|jre|java runtime|hotspot|jvm|java ?\d{1,2}|java\.(util|lang|io|net|security)\.)", ["java", "javaw", "tomcat*", "prunsrv", "elasticsearch-service-*"],
            "Java'yı çalıştıran süreç (Tomcat / procrun / Elasticsearch servisleri JVM'i kendi içinde çalıştırır; Carbon Black'te java.exe görünmez)"),
        (@"\btomcat\b", ["tomcat*", "java"], "Windows'ta Tomcat servisi tomcatN.exe içinde JVM çalıştırır"),
        (@"\b(jboss|wildfly|weblogic|websphere|jetty|glassfish|payara|elasticsearch|logstash|kafka|zookeeper|jenkins|keycloak|solr|cassandra|activemq|confluence|jira|bitbucket)\b", ["java"], "Java tabanlı ürün"),
        (@"\blog4j\b", ["java", "javaw", "tomcat*"], "log4j bir Java kütüphanesi: onu kullanan Java süreçleri"),
        (@"\bspring\b", ["java"], "Spring bir Java çatısı"),
        (@"\bnginx\b", ["nginx"], null),
        (@"\b(apache http server|httpd)\b", ["httpd", "apache2", "apache"], null),
        (@"\bopenssl\b", ["httpd", "nginx", "openssl", "node"], "OpenSSL bir kütüphane: ayrı süreci yok, onu kullanan süreçler"),
        (@"\bopenssh\b|\bsshd\b", ["sshd"], null),
        (@"\b(sql server|mssql)\b", ["sqlservr"], null),
        (@"\boracle database\b", ["oracle", "tnslsnr"], null),
        (@"\bpostgres(ql)?\b", ["postgres"], null),
        (@"\bmysql\b|\bmariadb\b", ["mysqld", "mariadbd"], null),
        (@"\bmongodb\b", ["mongod"], null),
        (@"\bredis\b", ["redis-server"], null),
        (@"\bexchange\b", ["w3wp", "msexchange*"], null),
        (@"\bsharepoint\b", ["w3wp"], null),
        (@"\b(iis|internet information services)\b", ["w3wp"], null),
        (@"\b(asp\.net core|\.net core|\.net [5-9])\b", ["dotnet", "w3wp"], null),
        (@"\bnode\.?js\b", ["node"], null),
        (@"\bpython\b", ["python", "python3"], null),
        (@"\bphp\b", ["php", "php-cgi"], null),
        (@"\brabbitmq\b", ["erl"], null),
        (@"\bvmware tools\b", ["vmtoolsd"], null),
        (@"\bsamba\b", ["smbd"], null),
        (@"\bbind\b", ["named"], null),
    ];
    static readonly HashSet<string> JdkAraclari = new(StringComparer.OrdinalIgnoreCase)
    { "javac", "jar", "javadoc", "javap", "jarsigner", "keytool", "jshell", "jlink", "jdeps", "jps", "jstack", "jmap", "jstat", "jcmd", "jconsole", "jdb", "jinfo", "jhsdb", "jpackage", "serialver", "rmic" };
    static readonly HashSet<string> GenelUrunKelime = new(StringComparer.OrdinalIgnoreCase)
    { "server", "servers", "service", "services", "microsoft", "oracle", "apache", "windows", "linux", "edition", "enterprise", "standard", "client", "agent",
      "version", "versions", "software", "foundation", "community", "platform", "framework", "runtime", "openjdk", "java", "library", "remote", "code", "execution" };

    (ImzaTarama T, Dictionary<string, (HashSet<string> Ip, List<string> Yol)> Idx)? _exeIdx;
    async Task<(Dictionary<string, (HashSet<string> Ip, List<string> Yol)> Idx, DateTime? Tarih)> ExeIndexAsync(CancellationToken ct)
    {
        ImzaTarama? t = null;
        try { t = await imza.TaramaAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Açıklık AI: servis imzası okunamadı: {H}", ex.Message); }
        if (t == null) return ([], null);
        lock (_gate) if (_exeIdx is { } e && ReferenceEquals(e.T, t)) return (e.Idx, t.Baslangic);
        var idx = new Dictionary<string, (HashSet<string> Ip, List<string> Yol)>();
        void Ekle(string ip, string yol)
        {
            string ad = ExeAd0(ExeAd(yol));
            if (ad.Length == 0) return;
            if (!idx.TryGetValue(ad, out var v)) idx[ad] = v = ([], []);
            v.Ip.Add(ip);
            if (v.Yol.Count < 400 && !v.Yol.Contains(yol, StringComparer.OrdinalIgnoreCase)) v.Yol.Add(yol);
        }
        foreach (var sv in t.Sunucular)
        {
            foreach (var p in sv.Gelen) Ekle(sv.Ip, p.Yol);
            foreach (var y in sv.Giden) Ekle(sv.Ip, y);
        }
        lock (_gate) _exeIdx = (t, idx);
        return (idx, t.Baslangic);
    }

    static (int Sunucu, List<string> Ornek) Say(Dictionary<string, (HashSet<string> Ip, List<string> Yol)> idx, string exe)
    {
        var rx = new Regex("^" + Regex.Escape(exe).Replace(@"\*", "[a-z0-9_.-]*") + "$");
        var ips = new HashSet<string>(); var orn = new List<string>();
        foreach (var (ad, v) in idx)
            if (rx.IsMatch(ad)) { ips.UnionWith(v.Ip); orn.AddRange(v.Yol.Take(2)); }
        return (ips.Count, orn.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList());
    }

    static bool MetindeVar(string metin, string v) =>
        Regex.IsMatch(metin, @"(?<![\d.])" + Regex.Replace(Regex.Escape(v), @"\\\.|_", "[._]") + @"(?![\d])");

    public async Task<ZfAiSonuc> AiDoldurAsync(string metin, CancellationToken ct)
    {
        metin = (metin ?? "").Trim();
        if (metin.Length < 30) throw new ArgumentException("CVE metni çok kısa; duyurunun açıklama ve etkilenen sürümler bölümünü yapıştırın.");
        int max = Math.Clamp(cfg.GetValue("Zafiyet:AiMaxKarakter", 8000), 1000, 30000);
        if (metin.Length > max) metin = metin[..max];
        string prompt = $$"""
            Aşağıda bir güvenlik açığı (CVE) duyurusundan bir bölüm var. Görevin: kurum sunucularındaki Carbon Black süreç (process) kayıtlarında bu açıktan
            etkilenebilecek SÜREÇLERİ aramak için bir arama kuralı çıkarmak.
            YALNIZ şu JSON nesnesini döndür (yorum, açıklama, markdown yok):
            {"urun": "...", "cve": "CVE-....", "ozet": "Türkçe 1-2 cümle: açık ne, ne zaman etkiler", "kutuphane": false, "kutuphaneNot": "",
             "exeler": [{"ad": "...", "neden": "..."}], "araliklar": [{"ana": 17, "min": "17.0.6", "son": null, "duzeltilmis": "17.0.21.1"}],
             "digerAnaSurumler": "etkilenen | etkilenmez | bilinmiyor", "onlem": ["..."]}
            Kurallar:
            - exeler: Windows ve Linux'ta bu ürünü çalıştıran sürecin DOSYA ADI, .exe olmadan, küçük harf (ör. java, javaw, httpd, apache2, nginx, sqlservr, w3wp, tomcat9).
              Joker * kullanılabilir (tomcat*). Emin olmadığın adı yazma. svchost, explorer gibi genel adları yazma.
            - Oracle duyurularındaki "component: Compiler / Hotspot" Java çalışırken kodu derleyen JIT derleyicisidir (her Java sürecinin içinde); javac DEĞİLDİR.
              javac, jar, keytool gibi geliştirme araçlarını yazma (kısa süre çalışırlar, ağdan veri almazlar).
            - Açık Java çalışma ortamındaysa (OpenJDK / JRE / JVM): java ve javaw'ın yanında JVM'i kendi içinde çalıştıran Windows servislerini de yaz
              (tomcat* — Tomcat servisi tomcat9.exe içinde çalışır —, prunsrv, elasticsearch-service-*); bunlarda Carbon Black'te java.exe görünmez.
            - Açık bir KÜTÜPHANEDE ya da çalışma ortamı bileşenindeyse (OpenSSL, log4j, zlib, glibc, Java'nın bir sınıfı…): kutuphane=true; exeler'e o kütüphaneyi
              kullanan bilinen süreçleri yaz; kutuphaneNot'ta kısaca açıkla.
            - araliklar: YALNIZ metinde geçen sürüm numaralarını HARFİ HARFİNE yaz; metinde yoksa boş dizi. Tahmin etme. min = etkilenen ilk sürüm, son = etkilenen SON sürüm
              (dahil). Oracle duyurularındaki "Supported versions that are affected are X, Y" ifadesi o sürüm ve ÖNCESİ demektir: son = X, min = null (min'e YAZMA).
              Java 8 sürümü "8u511" biçimindeyse öyle yaz, ana = 8. duzeltilmis = düzeltmeyi
              içeren ilk sürüm. Birden çok ana sürüm dalı varsa (17, 21, 25…) her dal ayrı satır, "ana" alanı o dalın numarası; tek dal varsa ana null.
            - digerAnaSurumler: listede olmayan diğer (daha yeni / desteği bitmiş) ana sürümler metne göre etkileniyorsa "etkilenen", etkilenmiyorsa "etkilenmez", bilinmiyorsa "bilinmiyor".
            - onlem: metinde geçen geçici önlemin süreç KOMUT SATIRINDA görülebilecek parametreleri, harfi harfine (ör. -XX:-UseBASE64Intrinsics, -Dlog4j2.formatMsgNoLookups=true).
              Komut satırında görünmeyen önlemler (yama, güvenlik duvarı, ayar dosyası) yazılmaz. Yoksa boş dizi.
            METİN:
            {{metin}}
            """;
        string cevap = await islev.ChatAsync(prompt, ct);
        if (IslevService.ExtractJson(cevap) is not { } j || j.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidOperationException("AI cevabı JSON değil: " + (cevap.Length > 200 ? cevap[..200] + "…" : cevap));

        var uyari = new List<string>();
        string? urun = IslevService.Str(j, "urun"), cve = IslevService.Str(j, "cve"), ozet = IslevService.Str(j, "ozet");
        if (cve != null && !metin.Contains(cve, StringComparison.OrdinalIgnoreCase)) { uyari.Add($"AI'ın yazdığı CVE ({cve}) metinde geçmiyor, alınmadı."); cve = null; }
        // metinde birden çok CVE varsa hepsi (ör. NGINX: CVE-2026-123007, CVE-2026-123633)
        var cveler = Regex.Matches(metin, @"CVE-\d{4}-\d{4,}", RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).Distinct().Take(10).ToList();
        if (cveler.Count > 0) cve = string.Join(", ", cveler);
        bool kut = IslevService.Prop(j, "kutuphane") is { ValueKind: System.Text.Json.JsonValueKind.True };
        string? kutNot = IslevService.Str(j, "kutuphaneNot");

        // Sürüm aralıkları: metinde birebir geçmeyen sürüm atılır
        var ar = new List<ZfAralik>();
        if (IslevService.Prop(j, "araliklar") is { ValueKind: System.Text.Json.JsonValueKind.Array } aa)
            foreach (var a in aa.EnumerateArray())
            {
                string? min = IslevService.Str(a, "min"), fix = IslevService.Str(a, "duzeltilmis"), son = IslevService.Str(a, "son");
                string? Dogrula(string ad, string? v)
                {
                    if (v == null) return null;
                    // Java 8 biçimi (8u511b33-perf): metinde "8u511" geçmeli; 1.8.0.511'e çevrilir
                    string? n = NormSurum(v);
                    string kok = Regex.Match(v, @"^(?:1\.)?[5-8]u\d+", RegexOptions.IgnoreCase) is { Success: true } jm ? jm.Value : v;
                    if (n != null && (metin.Contains(kok, StringComparison.OrdinalIgnoreCase) || MetindeVar(metin, v))) return n;
                    uyari.Add($"AI'ın {ad} sürümü \"{v}\" metinde birebir geçmiyor, alınmadı — kontrol edip elle girin.");
                    return null;
                }
                min = Dogrula("etkilenen ilk", min); fix = Dogrula("düzeltilmiş", fix); son = Dogrula("son etkilenen", son);
                int? ana = IslevService.Prop(a, "ana") is { ValueKind: System.Text.Json.JsonValueKind.Number } an && an.TryGetInt32(out int ai) ? ai : null;
                // Oracle "affected are X" yanlışlıkla min'e yazılmışsa (son ve düzeltilmiş boş, metinde "versions that are affected are") son'a taşınır
                if (min != null && son == null && fix == null && Regex.IsMatch(metin, @"versions?\s+that\s+are\s+affected\s+are", RegexOptions.IgnoreCase))
                { son = min; min = null; }
                if (min != null || fix != null || son != null) ar.Add(new ZfAralik(ana, min, fix, son));
            }
        if (ar.Count > 1)   // birden çok dal: ana numarası sürümden (Java'da 1.8 → 8)
        {
            ar = ar.Select(a => a.Ana != null ? a : a with { Ana = (a.Duzeltilmis ?? a.Son ?? a.Min) is { } v && Parca(v) is { Count: > 0 } pp ? (pp[0] == 1 && pp.Count > 1 ? pp[1] : pp[0]) : null }).ToList();
            // ana sürüm hepsinde aynı (NGINX 1.30.7 / 1.31.9): dal = ilk iki parça
            // yalnız düzeltilmiş sürümü olan aralık dala bağlanır; "1.9.5 – 1.31.8" gibi genel aralık dalsız kalır (diğer dallar için)
            if (ar.Select(a => a.Ana).Distinct().Count() == 1 && ar.Count(a => a.Duzeltilmis != null && Parca(a.Duzeltilmis).Count >= 3) >= 2)
                ar = ar.Select(a => a.Duzeltilmis != null && Parca(a.Duzeltilmis).Count >= 3
                    ? a with { Ana = null, Dal = string.Join('.', Parca(a.Duzeltilmis).Take(2)) } : a with { Ana = null }).ToList();
        }
        else if (ar.Count == 1 && ar[0].Ana != null && IslevService.Str(j, "digerAnaSurumler") is not "etkilenen") ar[0] = ar[0] with { Ana = null };
        if (ar.Count == 0) uyari.Add("Metinden sürüm aralığı çıkmadı: tarama yalnız nerede çalıştığını listeler (sürüm değerlendirmesi için aralığı elle girin).");
        bool diger = IslevService.Str(j, "digerAnaSurumler")?.StartsWith("etkilenen", StringComparison.OrdinalIgnoreCase) == true;

        var onlem = new List<string>();
        if (IslevService.Prop(j, "onlem") is { ValueKind: System.Text.Json.JsonValueKind.Array } oa)
            foreach (var o in oa.EnumerateArray())
                if (o.ValueKind == System.Text.Json.JsonValueKind.String && o.GetString()?.Trim() is { Length: >= 3 } ov)
                {
                    if (metin.Contains(ov, StringComparison.OrdinalIgnoreCase)) onlem.Add(ov);
                    else uyari.Add($"AI'ın önlem parametresi \"{ov}\" metinde geçmiyor, alınmadı.");
                }

        // Exe önerileri: AI + sözlük + envanter (ürün adı exe yolunda)
        var (idx, envTarih) = await ExeIndexAsync(ct);
        var exe = new List<(string Ad, string? Neden, string Kaynak)>();
        void Oner(string ad, string? neden, string kaynak)
        {
            ad = ExeAd0(ad);
            if (!Regex.IsMatch(ad, @"^[a-z0-9_.\-*]+$") || ad.Trim('*').Length < 3) return;
            if (exe.Any(e => e.Ad == ad)) return;
            exe.Add((ad, neden, kaynak));
        }
        if (IslevService.Prop(j, "exeler") is { ValueKind: System.Text.Json.JsonValueKind.Array } ea)
            foreach (var e in ea.EnumerateArray())
                if ((e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : IslevService.Str(e, "ad")) is { } ad)
                {
                    // JDK geliştirme araçları (javac, jar, keytool…) kısa süre çalışır, ağdan veri almaz: öneride kalır ama işaretsiz gelir
                    bool arac = JdkAraclari.Contains(ExeAd0(ad));
                    Oner(ad, arac ? "JDK geliştirme aracı — kısa süre çalışır, ağdan veri almaz; aramaya genelde gerek yok"
                        : e.ValueKind == System.Text.Json.JsonValueKind.Object ? IslevService.Str(e, "neden") : null, arac ? "AI · geliştirme aracı" : "AI");
                }
        string bakilan = ((urun ?? "") + " " + (metin.Length > 1500 ? metin[..1500] : metin)).ToLowerInvariant();
        foreach (var (desen, exeler, not) in UrunExe)
            if (Regex.IsMatch(bakilan, desen)) foreach (var e in exeler) Oner(e, not ?? "ürün → süreç sözlüğü", "sözlük");
        if (urun != null && idx.Count > 0)
        {
            var kelimeler = Regex.Split(urun.ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length >= 4 && !GenelUrunKelime.Contains(w)).Distinct().Take(4).ToList();
            foreach (var w in kelimeler)
                foreach (var (ad, v) in idx.Where(x => x.Key.Contains(w) || x.Value.Yol.Any(y => y.ToLowerInvariant().Contains(w)))
                    .Where(x => !imza.Gereksiz(x.Value.Yol[0])).OrderByDescending(x => x.Value.Ip.Count).Take(6))
                    Oner(ad, $"envanterde exe adında / yolunda '{w}' geçiyor", "envanter");
        }
        var exeSon = exe.Select(e => { var (n, o) = Say(idx, e.Ad); return new ZfAiExe(e.Ad, e.Neden, e.Kaynak, n, o); }).ToList();
        if (idx.Count == 0) uyari.Add("Servis imzası kaydı yok: önerilen exe adları envanterle doğrulanamadı (kaç sunucuda görüldüğü bilinmiyor).");
        else foreach (var e in exeSon.Where(e => e.Sunucu == 0 && e.Kaynak == "AI"))
            uyari.Add($"\"{e.Ad}\" servis imzası kaydında hiç görülmedi — adı doğrulayın (Linux sunucuları ya da kayıtta olmayan sunucular olabilir).");
        if (exeSon.Count == 0) uyari.Add("AI süreç adı önermedi; exe adını elle girin.");
        return new ZfAiSonuc(urun, cve, ozet, kut, kutNot, exeSon, ar, onlem, diger, uyari, envTarih?.ToString("dd.MM.yyyy HH:mm"));
    }
}
