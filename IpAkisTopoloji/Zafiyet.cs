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
record ZfAralik(int? Ana, string? Min, string? Duzeltilmis);
record ZfKural(string Id, string Ad, string? Cve, string? Aciklama, List<string> Exeler, List<string>? SurumDesenleri = null,
    int? AnaMin = null, List<ZfAralik>? Araliklar = null, List<string>? Onlem = null, string? OnlemNot = null, bool Java = false,
    bool DestekDisiEtkilenir = true, string? Anahtar = null);
record ZfPort(string Port, long Baglanti, int Istemci, string? Ert, List<string> Vip, List<string> Dis);
record ZfSatir(string? Ip, string? Sunucu, string? Bilgisayar, string? Kurum, string? Segment, bool Test, List<string> Uygulamalar, string? Muhafiz,
    string Exe, string Yol, string? Urun, string? Surum, string? SurumKaynak, string Durum, string DurumNot,
    List<ZfPort> Portlar, long Giden, string Onlem, string? Komut, string? Jar, string? Son, string? Baslangic, string Oncelik, string Kaynak);
record ZfIs(string Durum, string Asama, DateTime Baslangic, DateTime? Bitis, string? Hata, string? Kaydeden, int Saat, int SurecSaat, ZfKural Kural);
record ZafiyetIstek(int? Saat, string? Kural, string? Exeler, string? Min, string? Duzeltilmis, string? Ad);
record ZfSonuc(ZfIs? Is, List<ZfSatir> Satirlar, List<string> Mesajlar, List<ZfKural> Kurallar);

sealed class ZafiyetService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, ServisImzaService imza, ILogger<ZafiyetService> log)
{
    readonly object _gate = new();
    ZfIs? _is;
    List<ZfSatir> _satir = [];
    List<string> _mesaj = [];
    CancellationTokenSource? _cts;

    public ZfSonuc Sonuc() { lock (_gate) return new ZfSonuc(_is, _satir, _mesaj, Kurallar()); }
    public void Durdur() { lock (_gate) _cts?.Cancel(); }

    // ---------------- Kurallar ----------------
    static readonly ZfKural Java4Shell = new("java4shell", "Java4Shell — OpenJDK Base64 çözücüsünde heap taşması", "CVE-2026-114938",
        "Java 17.0.6 ve sonrası, 18+ (21, 25, 27); x86-64 + AVX-512 VBMI. Java 8 / 11 etkilenmez. AVX-512 bilgisi yok: sürümü aralıktaki her süreç etkilenen sayılır.",
        ["java", "javaw", "tomcat*", "prunsrv"], Java: true, AnaMin: 17,
        Araliklar: [new(17, "17.0.6", "17.0.21.1"), new(21, null, "21.0.13.1"), new(25, null, "25.0.5.1"), new(27, null, "27.0.1.1")],
        Onlem: [@"-XX:-UseBASE64Intrinsics", @"-XX:UseAVX=[0-2]\b"], OnlemNot: "-XX:-UseBASE64Intrinsics (ya da x86'da -XX:UseAVX=2)");

    public List<ZfKural> Kurallar()
    {
        var l = cfg.GetSection("Zafiyet:Kurallar").Get<List<ZfKural>>()?.Where(k => !string.IsNullOrWhiteSpace(k.Id) && k.Exeler is { Count: > 0 }).ToList();
        return l is { Count: > 0 } ? l : [Java4Shell];
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
        if (altYok) return ("olasi", $"{surum} — alt sürüm bilinmiyor" + (a.Duzeltilmis != null ? $"; düzeltilmiş {a.Duzeltilmis}" : ""));
        if (a.Min != null && Karsilastir(surum, a.Min) < 0) return ("etkilenmez", $"{surum} < {a.Min} (etkilenen ilk sürüm)");
        if (a.Duzeltilmis != null && Karsilastir(surum, a.Duzeltilmis) >= 0) return ("yamali", $"{surum} ≥ {a.Duzeltilmis} (dağıtıcının yamasıyla doğrulayın)");
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
                    string v = m.Groups[m.Groups.Count > 2 && m.Groups[2].Success ? 2 : 1].Value.Replace('_', '.');
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
    // İstekte kural id'si ya da anlık exe listesi (+ isteğe bağlı Min / Duzeltilmis: tek aralık, tüm ana sürümler)
    public ZfKural? KuralBul(ZafiyetIstek r, out string? hata)
    {
        hata = null;
        if (!string.IsNullOrWhiteSpace(r.Exeler))
        {
            var ex = Temiz(r.Exeler.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries));
            if (ex.Count == 0) { hata = "Geçerli exe adı yok (harf, rakam, . _ - ve * kullanılabilir)."; return null; }
            if (ex.Any(e => e.Trim('*').Length < 3)) { hata = "Exe adları en az 3 harf olmalı (çok geniş arama olmasın)."; return null; }
            string? min = string.IsNullOrWhiteSpace(r.Min) ? null : r.Min.Trim(), fix = string.IsNullOrWhiteSpace(r.Duzeltilmis) ? null : r.Duzeltilmis.Trim();
            return new ZfKural("anlik", string.IsNullOrWhiteSpace(r.Ad) ? "Süreç araması: " + string.Join(", ", ex) : r.Ad.Trim(), null, null, ex,
                Araliklar: min != null || fix != null ? [new(null, min, fix)] : null, Java: ex.Any(e => e is "java" or "javaw"));
        }
        var k = Kurallar().FirstOrDefault(x => string.Equals(x.Id, r.Kural ?? "", StringComparison.OrdinalIgnoreCase)) ?? Kurallar().First();
        return k;
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

            Set(i => i with { Asama = "Envanter ve servis imzası kaydıyla birleştiriliyor" });
            var env = await envanter.GetAsync(false, ct);
            ImzaTarama? imzaT = null;
            try { imzaT = await imza.TaramaAsync(ct); } catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add("Servis imzası kaydı okunamadı: " + ex.Message); }
            var satir = Birlestir(k, env, r1.Rows, r2Rows, imzaT, mesaj);
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

    List<ZfSatir> Birlestir(ZfKural k, EnvanterSnapshot env, List<Dictionary<string, string>> ag, List<Dictionary<string, string>> bas, ImzaTarama? imzaT, List<string> mesaj)
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
                b == null ? null : Zaman(V(b, "bas")), oncelik, g.Kaynak));
        }
        string[] sira = ["kritik", "yuksek", "orta", "incele", "dusuk"];
        return sonuc.OrderBy(x => Array.IndexOf(sira, x.Oncelik)).ThenByDescending(x => x.Portlar.Sum(p => p.Baglanti)).ThenBy(x => x.Ip).ToList();
    }
}
