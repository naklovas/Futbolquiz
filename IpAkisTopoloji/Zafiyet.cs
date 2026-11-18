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
record ZfAralik(int? Ana, string? Min, string? Duzeltilmis, string? Son = null);
record ZfKural(string Id, string Ad, string? Cve, string? Aciklama, List<string> Exeler, List<string>? SurumDesenleri = null,
    int? AnaMin = null, List<ZfAralik>? Araliklar = null, List<string>? Onlem = null, string? OnlemNot = null, bool Java = false,
    bool DestekDisiEtkilenir = true, string? Anahtar = null, string? Kaynak = null, string? Kaydeden = null, DateTime? Tarih = null,
    List<string>? NessusAnahtarlar = null);   // Nessus'ta plugin adında aranacak kelimeler (kurulu yazılım: "… Detection" bilgi bulguları)
record ZfPort(string Port, long Baglanti, int Istemci, string? Ert, List<string> Vip, List<string> Dis);
record ZfSatir(string? Ip, string? Sunucu, string? Bilgisayar, string? Kurum, string? Segment, bool Test, List<string> Uygulamalar, string? Muhafiz,
    string Exe, string Yol, string? Urun, string? Surum, string? SurumKaynak, string Durum, string DurumNot,
    List<ZfPort> Portlar, long Giden, string Onlem, string? Komut, string? Jar, string? Son, string? Baslangic, string Oncelik, string Kaynak,
    string? Vlan = null, string? Domain = null, string? Cidr = null, string? Dagitim = null, string? Nessus = null);
record ZfIs(string Durum, string Asama, DateTime Baslangic, DateTime? Bitis, string? Hata, string? Kaydeden, int Saat, int SurecSaat, ZfKural Kural);
// Sayfadan elle / AI ile doldurulmuş kural: exe adları virgülle, önlem ifadeleri virgülle (DÜZ METİN; regex'e kaçışlanır)
record ZfOzel(string? Ad, string? Cve, string? Aciklama, string? Exeler, List<ZfAralik>? Araliklar, string? Onlem, bool DigerEtkilenir, string? NessusAnahtarlar = null);
record ZafiyetIstek(int? Saat, string? Kural, ZfOzel? Ozel);
record ZafiyetAiIstek(string? Metin);
// AI ile doldurma sonucu (kullanıcı onaylayıp düzenler; tarama kendiliğinden başlamaz)
record ZfAiExe(string Ad, string? Neden, string Kaynak, int Sunucu, List<string> Ornek);
record ZfAiSonuc(string? Urun, string? Cve, string? Ozet, bool Kutuphane, string? KutuphaneNot, List<ZfAiExe> Exeler, List<ZfAralik> Araliklar,
    List<string> Onlem, bool DigerEtkilenir, List<string> Uyarilar, string? EnvanterTarihi);
record ZfSonuc(ZfIs? Is, List<ZfSatir> Satirlar, List<string> Mesajlar, List<ZfKural> Kurallar);

sealed class ZafiyetService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ServisImzaService imza, IslevService islev, NessusService nessus,
    ILogger<ZafiyetService> log)
{
    readonly object _gate = new();
    ZfIs? _is;
    List<ZfSatir> _satir = [];
    List<string> _mesaj = [];
    CancellationTokenSource? _cts;

    public async Task<ZfSonuc> SonucAsync(CancellationToken ct)
    {
        var k = await KurallarAsync(ct);
        lock (_gate) return new ZfSonuc(_is, _satir, _kayitUyari == null ? _mesaj : [.. _mesaj, _kayitUyari], k);
    }
    public void Durdur() { lock (_gate) _cts?.Cancel(); }

    // ---------------- Kurallar ----------------
    static readonly ZfKural Java4Shell = new("java4shell", "Java4Shell — OpenJDK Base64 çözücüsünde heap taşması", "CVE-2026-114938",
        "Java 17.0.6 ve sonrası, 18+ (21, 25, 27); x86-64 + AVX-512 VBMI. Java 8 / 11 etkilenmez. AVX-512 bilgisi yok: sürümü aralıktaki her süreç etkilenen sayılır.",
        ["java", "javaw", "tomcat*", "prunsrv"], Java: true, AnaMin: 17,
        Araliklar: [new(17, "17.0.6", "17.0.21.1"), new(21, null, "21.0.13.1"), new(25, null, "25.0.5.1"), new(27, null, "27.0.1.1")],
        Onlem: [@"-XX:-UseBASE64Intrinsics", @"-XX:UseAVX=[0-2]\b"], OnlemNot: "-XX:-UseBASE64Intrinsics, -XX:UseAVX=2", NessusAnahtarlar: JavaNessus);
    static readonly List<string> JavaNessus = ["Java", "JDK", "JRE", "Corretto", "Temurin", "Zulu", "GraalVM"];
    // Kuralda Nessus kelimesi yoksa (eski kayıtlı kurallar da): Java kuralında Java kelimeleri, diğerlerinde exe adları
    static List<string> NessusKelimeleri(ZfKural k) => k.NessusAnahtarlar is { Count: > 0 } l ? l
        : k.Java ? JavaNessus : Temiz(k.Exeler).Where(e => !e.Contains('*') && e.Length >= 4).Take(6).ToList();

    // Yerleşik / ayardaki kurallar + kayıtlı kurallar (DF_ZAFIYET_KURAL; aynı Id'de kayıtlı olan geçerli)
    List<ZfKural> AyarKurallari()
    {
        var l = cfg.GetSection("Zafiyet:Kurallar").Get<List<ZfKural>>()?.Where(k => !string.IsNullOrWhiteSpace(k.Id) && k.Exeler is { Count: > 0 })
            .Select(k => k with { Kaynak = "ayar" }).ToList();
        return l is { Count: > 0 } ? l : [Java4Shell with { Kaynak = "yerleşik" }];
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
        var a = k.Araliklar?.FirstOrDefault(x => x.Ana == ana) ?? k.Araliklar?.FirstOrDefault(x => x.Ana == null);
        if (a == null)
            return k.DestekDisiEtkilenir && k.AnaMin != null ? ("etkilenen", $"{ana} için düzeltilmiş sürüm yok (destek dışı sürüm)") : ("etkilenmez", $"{ana} aralıkta değil");
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
    public async Task<(ZfKural? Kural, string? Hata)> KuralBulAsync(ZafiyetIstek r, CancellationToken ct)
    {
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
        if (ham.SelectMany(a => new[] { T(a.Min), T(a.Duzeltilmis), T(a.Son) }).OfType<string>().Any(v => NormSurum(v) == null))
            return (null, "Sürümler rakam ve . _ - + ile yazılmalı (ör. 17.0.6) ya da Java 8 biçiminde (8u511).");
        var ar = ham.Select(a => new ZfAralik(a.Ana, NormSurum(T(a.Min)), NormSurum(T(a.Duzeltilmis)), NormSurum(T(a.Son)))).ToList();
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

    public ZfIs Baslat(ZfKural kural, int saat, string? user)
    {
        lock (_gate)
        {
            if (_is is { Durum: "calisiyor" }) return _is;
            int maxSaat = cfg.GetValue("Zafiyet:MaxSaat", 168);
            saat = Math.Clamp(saat <= 0 ? cfg.GetValue("Zafiyet:Saat", 24) : saat, 1, maxSaat);
            int surecSaat = Math.Max(saat, cfg.GetValue("Zafiyet:SurecSaat", 168));
            _cts = new CancellationTokenSource();
            _is = new ZfIs("calisiyor", "Başlıyor", DateTime.Now, null, null, user, saat, surecSaat, kural);
            _satir = []; _mesaj = [];
            var ct = _cts.Token;
            _ = Task.Run(() => CalisAsync(kural, saat, surecSaat, ct));
            return _is;
        }
    }
    void Set(Func<ZfIs, ZfIs> f) { lock (_gate) if (_is != null) _is = f(_is); }

    async Task CalisAsync(ZfKural k, int saat, int surecSaat, CancellationToken ct)
    {
        var mesaj = new List<string>();
        try
        {
            var s = cfg.GetSection("Splunk");
            string index = s["Index"] ?? "carbonblack", st = s["Sourcetype"] ?? "bit9:carbonblack:json";
            string anahtar = Anahtar(k), desen = ExeDesen(k, true);
            var sure = TimeSpan.FromSeconds(cfg.GetValue("Zafiyet:SureSaniye", 900));
            var http = factory.CreateClient("splunk");
            var bit = DateTimeOffset.Now;
            string exeler = string.Join(", ", Temiz(k.Exeler));

            Set(i => i with { Asama = $"Carbon Black: {exeler} süreçlerinin ağ olayları (son {saat} saat, tüm sunucular)" });
            string spl1 = $"""
                search index={index} sourcetype="{st}" {anahtar}
                | eval yol=coalesce(process_path, process_name, "")
                | where match(lower(yol), "{desen}")
                | eval yon=if(lower(direction)=="outbound", "out", "in"), lp=if(yon=="in", local_port, "-")
                | stats count dc(remote_ip) as peers values(remote_ip) as ornek max(_time) as son values(computer_name) as pc by local_ip yon lp yol
                | eval ornek=mvjoin(mvindex(ornek, 0, 199), " "), pc=mvjoin(mvindex(pc, 0, 2), "|||")
                """;
            var r1 = await SplunkService.ExportAsync(spl1, new LookupQuery("0.0.0.0", bit.AddHours(-saat), bit), cfg, http, ct, "Carbon Black (ağ)", sure);
            mesaj.AddRange(r1.Messages);

            Set(i => i with { Asama = $"Carbon Black: süreç başlangıçları / komut satırları (son {surecSaat} saat)" });
            string onlemRx = k.Onlem is { Count: > 0 } o ? string.Join("|", o).Replace("\\", "\\\\").Replace("\"", "\\\"") : "";
            string spl2 = $"""
                search index={index} sourcetype="{st}" TERM(ingress.event.procstart) {anahtar}
                | eval yol=coalesce(process_path, process_name, ""), cmd=coalesce(cmdline, command_line, process_cmdline, "")
                | where match(lower(yol), "{desen}")
                | eval onlem={(onlemRx == "" ? "-1" : $"if(match(cmd, \"{onlemRx}\"), 1, 0)")}
                | rex field=cmd "-jar\s+\"?(?<jar>[^\"\s]+)"
                | rex field=cmd "(?i)(?<jhome>[^\"\s=]*(?:jdk|jre|java|zulu|corretto|temurin|openjdk)[^\"\s]*)"
                | eval cmd=substr(cmd, 1, 1500)
                | stats latest(cmd) as cmd max(onlem) as onlem latest(jar) as jar latest(jhome) as jhome max(_time) as bas count by computer_name yol
                """;
            List<Dictionary<string, string>> r2Rows = [];
            try
            {
                var r2 = await SplunkService.ExportAsync(spl2, new LookupQuery("0.0.0.0", bit.AddHours(-surecSaat), bit), cfg, http, ct, "Carbon Black (süreç başlangıcı)", sure);
                r2Rows = r2.Rows; mesaj.AddRange(r2.Messages);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Süreç başlangıçları okunamadı (komut satırı / önlem bilinmiyor): " + ex.Message); }

            // 3) Nessus (Carbon Black ajanı olmayan sunucular / kutular — API gateway vb. — ve kurulu ama çalışmayan yazılım için)
            var nCve = new List<NessusBulgu>(); var nUrun = new List<NessusBulgu>();
            if (nessus.Acik && cfg.GetValue("Zafiyet:Nessus", true))
            {
                try
                {
                    if (k.Cve != null)
                    {
                        Set(i => i with { Asama = $"Nessus: {k.Cve} bulguları (tüm repo)" });
                        var (l, kesik) = await nessus.CveAsync(k.Cve, ct);
                        nCve = l;
                        mesaj.Add(l.Count > 0 ? $"Nessus: {k.Cve} için {l.Select(x => x.Ip).Distinct().Count()} IP'de bulgu."
                            : $"Nessus: {k.Cve} için bulgu yok (Tenable'ın bu CVE için kontrolü henüz yayımlanmamış ya da taranmamış olabilir).");
                        if (kesik) mesaj.Add("Nessus CVE bulguları sayfa sınırında kesildi (Zafiyet:NessusMaxSayfa).");
                    }
                    var kelimeler = NessusKelimeleri(k);
                    var say = new List<string>();
                    foreach (var kel in kelimeler.Take(10))
                    {
                        Set(i => i with { Asama = $"Nessus: plugin adında \"{kel}\" geçen bulgular (tüm repo)" });
                        var (l, kesik) = await nessus.UrunAsync(kel, ct);
                        nUrun.AddRange(l);
                        say.Add($"{kel}: {l.Count} bulgu / {l.Select(x => x.Ip).Distinct().Count()} IP / {l.Count(x => x.Kurulumlar.Count > 0)} sürümlü{(kesik ? " (sayfa sınırında kesildi)" : "")}");
                    }
                    mesaj.Add($"Nessus ürün araması — toplam {nUrun.Select(x => x.Ip).Distinct().Count()} IP · " + string.Join(" · ", say));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Nessus sorgulanamadı (tarama Carbon Black'le devam etti): " + ex.Message); }
            }
            else if (!nessus.Acik) mesaj.Add("Nessus tanımlı değil (Nessus:BaseUrl): Carbon Black ajanı olmayan sunucular / kutular kapsam dışı.");

            Set(i => i with { Asama = "Envanter ve servis imzası kaydıyla birleştiriliyor" });
            var env = await envanter.GetAsync(false, ct);
            ImzaTarama? imzaT = null;
            try { imzaT = await imza.TaramaAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Servis imzası kaydı okunamadı: " + ex.Message); }
            var satir = Birlestir(k, env, r1.Rows, r2Rows, imzaT, mesaj, nCve, nUrun);
            lock (_gate)
            {
                _satir = satir; _mesaj = mesaj.Distinct().ToList();
                _is = _is! with { Durum = "bitti", Asama = $"Bitti: {satir.Count} süreç", Bitis = DateTime.Now };
            }
        }
        catch (OperationCanceledException) { lock (_gate) { _mesaj = mesaj; _is = _is! with { Durum = "durduruldu", Asama = "Durduruldu", Bitis = DateTime.Now }; } }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Açıklık taraması hatası");
            lock (_gate) { _mesaj = mesaj; _is = _is! with { Durum = "hata", Asama = "Hata", Hata = ex.Message, Bitis = DateTime.Now }; }
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
        public long Giden; public HashSet<string> GidenHedef = new();
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
                    p.Value.Ist.Where(x => !Yolculuk.IsPrivate(x)).Take(10).ToList())).ToList();
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
                apps.Select(a => a.AppName).OfType<string>().Where(a => a.Length > 0).Distinct().Take(8).ToList(),
                apps.Select(a => a.VarlikMuhafizi).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
                ExeAd(g.Yol), g.Yol, Urun(g.Yol, komut, jar), surum, kaynakS, durum, not,
                portlar, g.Giden, onlem, komut, jar, g.Son > 0 ? Zaman(g.Son.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null,
                b == null ? null : Zaman(V(b, "bas")), oncelik, g.Kaynak, Vlan: seg?.Vlan, Domain: seg?.Domain, Cidr: seg?.Cidr, Dagitim: dagitim));
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
                    sonuc[i] = Oncelikle(r with { Surum = sur, SurumKaynak = "Nessus", Durum = d, DurumNot = n, Dagitim = r.Dagitim ?? (k.Java ? Dagitim(kur.Yol) : null) });
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

        // (a2) Ürün bulgusu var ama sürüm yazmıyor (kimliksiz tarama, kutu / appliance): IP'de başka satır yoksa "sürüm bilinmiyor" satırı
        foreach (var g in nUrun.Where(b => b.Kurulumlar.Count == 0).GroupBy(b => b.Ip))
        {
            if (sonuc.Any(r => r.Ip == g.Key)) continue;
            var b0 = g.OrderByDescending(x => x.Son).First();
            string not = "ürün bulgusu (sürüm yok): " + string.Join(" | ", g.Select(x => x.PluginAd).Distinct().Take(3));
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
                    sonuc[i] = Oncelikle(r with { Nessus = r.Nessus == null ? not : r.Nessus + " · " + not,
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
        mesaj.Add($"Nessus birleştirme: {doldurulan} satırın sürümü Nessus'tan, {eklenen} yeni satır (Carbon Black'te görülmeyen kurulum / bulgu).");
    }

    // Nessus'tan gelen satır: dinlediği port bilinmez; maruziyet = VIP üyeliği
    ZfSatir NessusSatir(ZfKural k, EnvanterSnapshot env, string[] test, NessusBulgu b, string yol, string? surum, string durum, string not, string nessusNot, string kaynak)
    {
        var seg = env.FindSegment(b.Ip);
        var apps = env.AppsOnIp(b.Ip);
        var vip = env.MemberOf(b.Ip).Select(v => $"{(v.Pool ?? "").Replace("/Common/", "")} · {v.LbIp}:{v.LbPort}").Distinct().ToList();
        string dom = (seg?.Domain ?? "").ToLowerInvariant();
        string exe = k.Java ? "java (Nessus)" : yol.StartsWith("(") ? "(Nessus)" : ExeAd(yol.TrimEnd('\\', '/')) is { Length: > 0 } e ? e : yol;
        var portlar = vip.Count > 0 ? new List<ZfPort> { new("VIP", 0, 0, null, vip, []) } : [];
        var r = new ZfSatir(b.Ip, apps.Select(a => a.UnixName).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? b.Dns ?? b.Netbios, b.Netbios ?? b.Dns,
            KurumBilgi.Of(cfg, env, b.Ip), seg?.Label, test.Any(x => x.Length > 0 && dom.Contains(x)),
            apps.Select(a => a.AppName).OfType<string>().Where(a => a.Length > 0).Distinct().Take(8).ToList(),
            apps.Select(a => a.VarlikMuhafizi).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)),
            exe, yol, null, surum, surum != null ? "Nessus" : null, durum, not, portlar, 0, "bilinmiyor", null, null, b.Son, null, "dusuk", kaynak,
            Vlan: seg?.Vlan, Domain: seg?.Domain, Cidr: seg?.Cidr, Dagitim: k.Java ? Dagitim(yol) : null, Nessus: nessusNot);
        return Oncelikle(r);
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
        cve ??= Regex.Match(metin, @"CVE-\d{4}-\d{4,}", RegexOptions.IgnoreCase) is { Success: true } cm ? cm.Value.ToUpperInvariant() : null;
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
            ar = ar.Select(a => a.Ana != null ? a : a with { Ana = (a.Duzeltilmis ?? a.Son ?? a.Min) is { } v && Parca(v) is { Count: > 0 } pp ? (pp[0] == 1 && pp.Count > 1 ? pp[1] : pp[0]) : null }).ToList();
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
