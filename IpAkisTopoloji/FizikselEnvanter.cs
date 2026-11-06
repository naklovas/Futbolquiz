// ---------------------------------------------------------------------------
// Fiziksel envanter (kullanıcı: oda planı → kabin → host → VM → IP → uygulama; "Bu kabin kapanırsa hangi uygulamalar etkilenir?").
// Veri doku DB'de HAZIR: NlyteCollector (nlyte OData → dbo.NlyteRaw) + EXEC dbo.sp_KabinZincirYenile doldurur. DeltaFlow YALNIZ OKUR
// (tabloya yazmaz, prosedürü çağırmaz). Tablolar `SELECT *` ile okunur, kolonlar adla eşlenir (tip / sıra farkına dayanıklı).
//  - dbo.KabinHostVm: VM başına satır (kabin, U konumu, host, cluster, VM, IP, uygulamalar, kapsam, eşleşme türü)
//  - dbo.NlyteHostEslesme: ESX host başına seçilen nlyte kaydı ("nlyte'ta eksik host'lar" listesi; yoksa KabinHostVm'den türetilir;
//    NlyteAssetID → host'un nlyte cihazı)
//  - dbo.NlyteKabin: tüm kabinler; dbo.NlyteCihaz: kabine monte TÜM cihazlar (CihazTuru: Server, Network, Firewall, PDU, Storage…;
//    yoksa dbo.NlyteSunucu = yalnız sunucular); dbo.NlyteChassis: chassis → kabin / U (blade'lerin UAlt'ı NULL, ChassisAssetID dolu)
// Kabin adı kalıbı `DR{oda}-{sıra harfi}{numara}` (ör. DR4-D3) → oda planı ızgarası; uymayanlar "Diğer".
// Ayarlar `Fiziksel:*` (tablo adları, CacheMinutes=30, AnaKapsam, UstSirasiIfadeleri).
// ---------------------------------------------------------------------------
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

record FizVm(string? Kabin, int? KabinU, string? KabinUSirasi, int? KabinBosU, int? HostUAlt, int? HostUYukseklik, string? HostYuz,
    string? HostModel, string? ChassisAdi, string? BladeSlot, string? Konum, string? Host, string? HostSeri, string? CabinetAssetId,
    string? Cluster, string? HostFqdn, string? Vm, string? Ip, string? DnsName, string? Os, List<string> Uygulamalar, List<string> Muhafizlar,
    string? Kaynak, string? Kapsam, string? Eslesme, DateTime? Yenileme);
// nlyte kabini (dbo.NlyteKabin: tüm kabinler, boş olanlar dahil) ve kabindeki cihaz (dbo.NlyteSunucu: VM'li olsun olmasın tüm sunucular)
record FizKabin(string Ad, string? CabinetAssetId, string? UniqueAd, int? UYukseklik, string? USirasi, int? BosU, string? BitisikBosU);
record FizCihaz(string Ad, List<string> Adlar, string? AssetId, string? Seri, string? CabinetAssetId, string? Record, string? Operational,
    int? UAlt, int? UYukseklik, string? Yuz, string? Model, string? MalzemeTip, string? UniqueAssetName, string? Chassis, string? Slot,
    string Tur = "Server", string? Kaynak = null, string? ChassisId = null)
{
    public bool Aktif => Record == null || Record.Equals("Active", StringComparison.OrdinalIgnoreCase);
}
record FizChassis(string Id, string? Ad, string? CabinetAssetId, int? UAlt, int? UYukseklik, string? Yuz, string? Model);
// sp_KabinZincirYenile'ın eşleştiremediği ama nlyte'ta başka türde (ör. Network) kayıtlı host'lar — DeltaFlow bellekte adla bağlar
record FizTurFarkli(string Host, string? Fqdn, int VmSayisi, string? Cluster, string CihazAd, string Tur, string Kabin, string? AssetId, int? UAlt, string Yontem = "ad", string? EskiKapsam = null);
// MSY fiziksel envanteri (dbo.MsyFizikselEnvanter): host DNS adı ↔ seri numarası, iLO, OS (ESX host'larının donanım kaydı)
record FizMsy(string? Seri, string? DnsName, string? DnsIp, string? IloIp, string? Vendor, string? Model, string? PowerState, string? OsName, string? OsVersion, DateTime? LastReboot);
// Kabine yerleşmeyen VM (satır başına bir VM) + host'unun bilgisi, neden ve nlyte adayları
record FizEslesmeyen(string? Vm, string? Ip, string? Dns, string? Os, List<string> Uygulamalar, string? Kaynak, string? Kapsam,
    string Host, string? Fqdn, string? Cluster, string? HostModel, string? Seri, string? Ilo, int HostVmSayisi, string Neden, List<string> Adaylar);
// nlyte kablo bağlantısı (dbo.NlyteBaglanti; kablo başına satır)
record FizBaglanti(string CableId, string? KabloTuru, string? KabloAdi, string? KaynakId, string? KaynakAdi, string? KaynakTur, string? KaynakPort,
    string? KaynakKabin, string? HedefId, string? HedefAdi, string? HedefTur, string? HedefPort, string? HedefKabin);
// Uçtan uca iz: yerel port → (patch panel'ler) → karşı cihaz
record FizIz(string? YerelPort, string? UzakId, string UzakAd, string UzakTur, string? UzakPort, string? UzakKabin, List<string> Paneller, string? Kablo, bool Kesik, string Sinif = "veri");
record FizEksik(string Host, string? Fqdn, int VmSayisi, string? Kapsam, string? Cluster, string? EslesmeTuru, int? AdaySayisi, string? Not);

sealed class FizikselSnapshot
{
    public required List<FizVm> Vmler { get; set; }
    public required List<FizEksik> Eksik { get; set; }
    public required bool EksikTablodan { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }
    public DateTime? Yenileme => Vmler.Select(v => v.Yenileme).Where(x => x != null).Max();
    public Dictionary<string, List<FizVm>> KabinVm { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<FizVm>> IpVm { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FizKabin> Kabinler { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<FizCihaz>> KabinCihaz { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool NlyteVar { get; set; }   // NlyteKabin / NlyteCihaz okunabildi mi
    public string? CihazTablosu { get; set; }   // okunan cihaz tablosu (NlyteCihaz ya da eski NlyteSunucu)
    public List<FizCihaz> KabinDisi { get; } = [];   // kabine bağlı olmayan cihazlar (klima vb.)
    public Dictionary<string, FizChassis> Chassisler { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FizTurFarkli> TurFarkli { get; } = [];
    public Dictionary<string, FizMsy> MsyAd { get; } = new(StringComparer.OrdinalIgnoreCase);    // Norm(DnsName) → kayıt
    public Dictionary<string, FizMsy> MsySeri { get; } = new(StringComparer.OrdinalIgnoreCase);  // seri → kayıt
    public Dictionary<string, FizMsy> MsyIp { get; } = new(StringComparer.OrdinalIgnoreCase);    // yönetim / iLO IP → kayıt (host alanında IP yazan VM'ler)
    public bool MsyVar { get; set; }
    public List<FizBaglanti> Baglantilar { get; } = [];
    public string? BaglantiDurum { get; set; }   // okunamadıysa / boşsa nedeni (arayüzde gösterilir)
    public Dictionary<string, List<FizBaglanti>> BagIdx { get; } = new(StringComparer.OrdinalIgnoreCase);   // AssetID → kabloları
    public Dictionary<string, FizCihaz> CihazId { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<FizVm>>? AssetVmOnbellek { get; set; }   // nlyte AssetID → o host'taki VM'ler (Kabin() eşleşmesiyle)
    public List<FizEslesmeyen>? EslesmeyenOnbellek { get; set; }
    // Uygulama → değerler (ERT'de uygulamanın dolu satırlarından en yüksek): [Varlık, İş, Erişilebilirlik, Gizlilik, Bütünlük]
    public Dictionary<string, int?[]> AppDeger { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> HostAsset { get; } = new(StringComparer.OrdinalIgnoreCase);   // host adı (kısa, küçük) → nlyte AssetID
    // Planda gösterilecek tüm kabinler: nlyte kabinleri + KabinHostVm'de geçenler
    public IEnumerable<string> TumKabinler => Kabinler.Keys.Union(KabinVm.Keys, StringComparer.OrdinalIgnoreCase);
}

sealed class FizikselService(IConfiguration cfg, ILogger<FizikselService> log)
{
    readonly SemaphoreSlim _lock = new(1, 1);
    FizikselSnapshot? _snap;
    public static readonly Regex KabinKalip = new(@"^(DR\d+)-([A-Z]+)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string AnaKapsam => cfg["Fiziksel:AnaKapsam"] ?? "Ana veri merkezi";

    public async Task<FizikselSnapshot> GetAsync(bool force, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(cfg.GetValue("Fiziksel:CacheMinutes", 30));
        var cur = _snap;
        if (!force && cur != null && DateTimeOffset.Now - cur.LoadedAt < ttl) return cur;
        await _lock.WaitAsync(ct);
        try
        {
            cur = _snap;
            if (!force && cur != null && DateTimeOffset.Now - cur.LoadedAt < ttl) return cur;
            try
            {
                _snap = await LoadAsync(ct);
                log.LogInformation("Fiziksel envanter yüklendi: {Vm} satır, {Kabin} kabin, {Eksik} eksik host", _snap.Vmler.Count, _snap.KabinVm.Count, _snap.Eksik.Count);
                return _snap;
            }
            catch (Exception ex) when (cur != null && !force && !ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Fiziksel envanter yenilenemedi, önceki kopya kullanılıyor.");
                return cur;
            }
        }
        finally { _lock.Release(); }
    }

    async Task<FizikselSnapshot> LoadAsync(CancellationToken ct)
    {
        string connStr = cfg.GetConnectionString(cfg["Fiziksel:Baglanti"] ?? "Envanter")
            ?? throw new InvalidOperationException("ConnectionStrings:Envanter tanımlı değil.");
        // Tablo adları yalnız config'den (kullanıcı girdisi değil)
        string vmTablo = cfg["Fiziksel:Tablolar:KabinHostVm"] ?? "dbo.KabinHostVm";
        string eslTablo = cfg["Fiziksel:Tablolar:NlyteHostEslesme"] ?? "dbo.NlyteHostEslesme";

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);
        var vmler = new List<FizVm>();
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {vmTablo}", conn) { CommandTimeout = 120 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
                vmler.Add(new FizVm(S(r, k, "Kabin"), I(r, k, "KabinUYukseklik"), S(r, k, "KabinUSirasi"), I(r, k, "KabinBosU"),
                    I(r, k, "HostUAlt"), I(r, k, "HostUYukseklik"), S(r, k, "HostYuz"), S(r, k, "HostModel"), S(r, k, "ChassisAdi"), S(r, k, "BladeSlot"),
                    S(r, k, "Konum"), S(r, k, "FizikselHost"), S(r, k, "HostSeri"), Id(S(r, k, "CabinetAssetID")), S(r, k, "Cluster"), S(r, k, "HostFqdn"),
                    S(r, k, "Vm"), S(r, k, "Ip"), S(r, k, "DnsName"), S(r, k, "Os"), Liste(S(r, k, "Uygulamalar")), Liste(S(r, k, "Muhafizlar")),
                    S(r, k, "Kaynak"), S(r, k, "Kapsam"), S(r, k, "EslesmeTuru"), D(r, k, "YenilemeZamani")));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException($"{vmTablo} tablosu yok: NlyteCollector / sp_KabinZincirYenile henüz çalıştırılmamış (sql/05_kabin_zincir_tablolar.sql).");
        }

        // nlyte'ta eksik host'lar: NlyteHostEslesme (EslesmeTuru = 'Yok'); tablo yoksa KabinHostVm'den (kabini olmayan host'lar)
        var eksik = new List<FizEksik>();
        var hostAsset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool tablodan = true;
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {eslTablo}", conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
            {
                if (Id(S(r, k, "NlyteAssetID")) is string aid)
                    foreach (var h in new[] { S(r, k, "HostKey"), S(r, k, "HostFqdn"), S(r, k, "NlyteAdi") }.OfType<string>()) hostAsset.TryAdd(Norm(h), aid);
                if (!string.Equals(S(r, k, "EslesmeTuru"), "Yok", StringComparison.OrdinalIgnoreCase)) continue;
                string host = S(r, k, "HostKey") ?? S(r, k, "HostFqdn") ?? "?";
                eksik.Add(new FizEksik(host, S(r, k, "HostFqdn"), I(r, k, "VmSayisi") ?? 0, S(r, k, "Kapsam"), null, "Yok", I(r, k, "AdaySayisi"), null));
            }
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            tablodan = false;
            log.LogWarning("{Tablo} yok; nlyte'ta eksik host'lar KabinHostVm'den türetiliyor.", eslTablo);
        }
        if (!tablodan)
            eksik = vmler.Where(v => v.Kabin == null && v.Eslesme is "Yok" or null && (v.HostFqdn ?? v.Host) != null)
                .GroupBy(v => v.HostFqdn ?? v.Host!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new FizEksik(g.Key, g.First().HostFqdn, g.Count(), g.First().Kapsam, g.First().Cluster, "Yok", null, "KabinHostVm'den türetildi"))
                .ToList();

        // Tüm kabinler ve cihazlar (nlyte): bunlar yoksa yalnız KabinHostVm'deki (VM'li host'u olan) kabinler görünür
        var kabinler = new List<FizKabin>();
        var cihazlar = new List<FizCihaz>();
        var chassisler = new List<FizChassis>();
        string kabTablo = cfg["Fiziksel:Tablolar:NlyteKabin"] ?? "dbo.NlyteKabin";
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {kabTablo}", conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
                if (S(r, k, "KabinAdi") is string ad)
                    kabinler.Add(new FizKabin(ad, Id(S(r, k, "CabinetAssetID")), S(r, k, "KabinUniqueAd"), I(r, k, "KabinUYukseklik"), S(r, k, "KabinUSirasi"),
                        I(r, k, "BosU"), S(r, k, "BitisikBosU")));
        }
        catch (SqlException ex) when (ex.Number == 208) { log.LogWarning("{K} yok; yalnız KabinHostVm'deki kabinler gösterilecek.", kabTablo); }

        // Cihazlar: önce NlyteCihaz (tüm türler), yoksa NlyteSunucu (yalnız sunucular)
        string? cihazTablo = null;
        foreach (var (tablo, varsayilanTur) in new[] { (cfg["Fiziksel:Tablolar:NlyteCihaz"] ?? "dbo.NlyteCihaz", "Other"), (cfg["Fiziksel:Tablolar:NlyteSunucu"] ?? "dbo.NlyteSunucu", "Server") })
        {
            try
            {
                await using var cmd = new SqlCommand($"SELECT * FROM {tablo}", conn) { CommandTimeout = 120 };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var k = Kolonlar(r);
                while (await r.ReadAsync(ct))
                {
                    var adlar = new[] { "AssetName", "Ad", "AdKisa", "AdOneksiz", "AdTiresiz" }.Select(x => S(r, k, x)).OfType<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (adlar.Count == 0) continue;
                    string? tip = S(r, k, "MalzemeTipi") ?? S(r, k, "MalzemeTip"), kaynak = S(r, k, "Kaynak");
                    cihazlar.Add(new FizCihaz(adlar[0], adlar, Id(S(r, k, "AssetID")), S(r, k, "SerialNumber"), Id(S(r, k, "CabinetAssetID")),
                        S(r, k, "RecordStatus"), S(r, k, "OperationalStatus"), I(r, k, "UAlt"), I(r, k, "UYukseklik"), S(r, k, "Yuz"),
                        S(r, k, "Model") ?? S(r, k, "MalzemeAdi"), tip, S(r, k, "UniqueAssetName"),
                        S(r, k, "ChassisAdi") ?? S(r, k, "Chassis"), S(r, k, "BladeSlot") ?? S(r, k, "Slot"),
                        TurBul(S(r, k, "CihazTuru"), tip, kaynak, varsayilanTur), kaynak, Id(S(r, k, "ChassisAssetID"))));
                }
                cihazTablo = tablo;
                break;
            }
            catch (SqlException ex) when (ex.Number == 208) { log.LogWarning("{T} yok.", tablo); cihazlar.Clear(); }
        }
        string chTablo = cfg["Fiziksel:Tablolar:NlyteChassis"] ?? "dbo.NlyteChassis";
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {chTablo}", conn) { CommandTimeout = 60 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
                if (Id(S(r, k, "ChassisAssetID")) is string id)
                    chassisler.Add(new FizChassis(id, S(r, k, "ChassisAdi"), Id(S(r, k, "CabinetAssetID")), I(r, k, "UAlt"), I(r, k, "UYukseklik"), S(r, k, "Yuz"), S(r, k, "Model")));
        }
        catch (SqlException ex) when (ex.Number == 208) { log.LogInformation("{T} yok; blade'lerin chassis'i NlyteCihaz'dan bulunur.", chTablo); }

        // Fiziksel host envanterleri (MSY + ÇSY): DNS adı ↔ seri ↔ yönetim / iLO IP. Fiziksel:Tablolar:FizikselEnvanterler (liste) ya da
        // eski tekil MsyFiziksel; varsayılan dbo.MsyFizikselEnvanter + dbo.CsyFizikselEnvanter (olmayan atlanır). Kolonlar adla, birkaç adla denenir.
        var msy = new List<FizMsy>();
        var fizTablolar = cfg.GetSection("Fiziksel:Tablolar:FizikselEnvanterler").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (fizTablolar.Count == 0)
            fizTablolar = cfg["Fiziksel:Tablolar:MsyFiziksel"] is string tek ? [tek] : ["dbo.MsyFizikselEnvanter", "dbo.CsyFizikselEnvanter"];
        foreach (var t in fizTablolar)
        {
            try
            {
                await using var cmd = new SqlCommand($"SELECT * FROM {t}", conn) { CommandTimeout = 60 };
                await using var r = await cmd.ExecuteReaderAsync(ct);
                var k = Kolonlar(r);
                string? Ilk(params string[] adlar) { foreach (var a in adlar) if (S(r, k, a) is string v) return v; return null; }
                int n = 0;
                while (await r.ReadAsync(ct))
                {
                    msy.Add(new FizMsy(Ilk("SerialNumber", "SeriNo", "Serial", "SeriNumarasi"), Ilk("DnsName", "HostName", "Hostname", "Fqdn", "Ad", "Name"),
                        Ilk("DnsIp", "Ip", "IpAddress", "YonetimIp", "MgmtIp"), Ilk("IloIp", "IdracIp", "IpmiIp", "OobIp", "ConsoleIp"), Ilk("Vendor", "Uretici"),
                        Ilk("Model"), Ilk("PowerState", "Guc"), Ilk("OsName", "Os"), Ilk("OsVersion", "OsSurum"), D(r, k, "LastReboot") ?? D(r, k, "SonReboot")));
                    n++;
                }
                log.LogInformation("Fiziksel host envanteri {T}: {N} kayıt", t, n);
            }
            catch (SqlException ex) when (ex.Number == 208) { log.LogInformation("{T} yok, atlandı.", t); }
        }

        var snap = Olustur(vmler, eksik, tablodan, kabinler, cihazlar, chassisler, hostAsset, msy, AnaKapsam);
        await DegerleriYukleAsync(conn, snap, ct);
        await BaglantilariYukleAsync(conn, snap, ct);
        snap.CihazTablosu = cihazTablo;
        return snap;
    }

    // Kablo bağlantıları (dbo.NlyteBaglanti: nlyte Connections; kablo başına satır). Tablo yoksa bağlantı gösterilmez.
    async Task BaglantilariYukleAsync(SqlConnection conn, FizikselSnapshot s, CancellationToken ct)
    {
        string t = cfg["Fiziksel:Tablolar:NlyteBaglanti"] ?? "dbo.NlyteBaglanti";
        try
        {
            await using var cmd = new SqlCommand($"SELECT * FROM {t}", conn) { CommandTimeout = 120 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var k = Kolonlar(r);
            while (await r.ReadAsync(ct))
            {
                var b = new FizBaglanti(Id(S(r, k, "CableID")) ?? Guid.NewGuid().ToString("N"), S(r, k, "KabloTuru"), S(r, k, "KabloAdi"),
                    Id(S(r, k, "KaynakAssetID")), S(r, k, "KaynakAdi"), S(r, k, "KaynakCihazTuru"), S(r, k, "KaynakPort"), S(r, k, "KaynakKabin"),
                    Id(S(r, k, "HedefAssetID")), S(r, k, "HedefAdi"), S(r, k, "HedefCihazTuru"), S(r, k, "HedefPort"), S(r, k, "HedefKabin"));
                s.Baglantilar.Add(b);
                foreach (var id in new[] { b.KaynakId, b.HedefId }.OfType<string>().Distinct())
                    (s.BagIdx.TryGetValue(id, out var l) ? l : s.BagIdx[id] = []).Add(b);
            }
            log.LogInformation("nlyte bağlantıları: {N} kablo", s.Baglantilar.Count);
            if (s.Baglantilar.Count == 0) s.BaglantiDurum = $"{t} boş (0 satır)";
        }
        // isteğe bağlı tablo: yoksa, yetki yoksa (229) ya da kolon farklıysa sayfa yine açılır, neden arayüzde yazılır
        catch (SqlException ex) { s.BaglantiDurum = $"{t} okunamadı: {ex.Message}"; log.LogWarning("{T} okunamadı: {M}", t, ex.Message); }
    }

    // Uygulama değerleri (kullanıcı: "VM'deki uygulamaların değerlerinin maksimumu → ESX'in ve kabinin değeri"): ERT'de aynı uygulama
    // çoklanmış, bazı satırlarda NULL → APPNAME başına dolu satırların en yükseği. Kolonlar Fiziksel:DegerKolonlari (sıra: Varlık, İş,
    // Erişilebilirlik, Gizlilik, Bütünlük); tablo Envanter:HostTablosu. Kolon yoksa (207) değerler gösterilmez.
    public static readonly string[] DegerAdlari = ["Varlık", "İş", "Erişilebilirlik", "Gizlilik", "Bütünlük"];
    async Task DegerleriYukleAsync(SqlConnection conn, FizikselSnapshot s, CancellationToken ct)
    {
        var kolonlar = cfg.GetSection("Fiziksel:DegerKolonlari").GetChildren().Select(c => c.Value).OfType<string>().ToArray();
        if (kolonlar.Length != 5) kolonlar = ["VARLIKDEGERI", "ISDEGERI", "ERISILEBILIRLIKDEGERI", "GIZLILIKDEGERI", "BUTUNLUKDEGERI"];
        if (kolonlar.Any(k => !Regex.IsMatch(k, @"^\w+$"))) { log.LogWarning("Fiziksel:DegerKolonlari geçersiz"); return; }
        string ert = cfg["Envanter:HostTablosu"] ?? "dbo.ERT_HOSTIPADDRESS";
        string sql = $"SELECT APPNAME, {string.Join(", ", kolonlar.Select(k => $"MAX(TRY_CONVERT(int, TRY_CONVERT(decimal(9,2), CONVERT(nvarchar(50), {k}))))"))} " +
                     $"FROM {ert} WITH (NOLOCK) WHERE APPNAME IS NOT NULL AND LTRIM(RTRIM(APPNAME)) <> '' GROUP BY APPNAME";
        try
        {
            await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var d = Enumerable.Range(1, 5).Select(i => r.IsDBNull(i) ? (int?)null : Convert.ToInt32(r.GetValue(i))).ToArray();
                if (d.Any(x => x != null)) s.AppDeger[r.GetString(0).Trim()] = d;
            }
            log.LogInformation("Uygulama değerleri: {N} uygulama", s.AppDeger.Count);
        }
        catch (SqlException ex) when (ex.Number is 207 or 208) { log.LogInformation("Uygulama değer kolonları okunamadı: {M}", ex.Message); }
    }

    // Uygulama kümesinin değerleri: her boyutta en yüksek + o değeri veren uygulama
    static object? Deger(FizikselSnapshot s, IEnumerable<string> uygulamalar)
    {
        var mx = new int?[5]; var kaynak = new string?[5];
        foreach (var a in uygulamalar.Distinct(StringComparer.OrdinalIgnoreCase))
            if (s.AppDeger.TryGetValue(a.Trim(), out var d))
                for (int i = 0; i < 5; i++)
                    if (d[i] is int v && (mx[i] == null || v > mx[i])) { mx[i] = v; kaynak[i] = a; }
        if (mx.All(x => x == null)) return null;
        return new { v = mx[0], i = mx[1], e = mx[2], g = mx[3], b = mx[4], kaynak = new { v = kaynak[0], i = kaynak[1], e = kaynak[2], g = kaynak[3], b = kaynak[4] } };
    }

    // Cihaz türü: tablodaki CihazTuru; yoksa malzeme tipi / nlyte entity adından (eski NlyteSunucu → Server)
    public static string TurBul(string? cihazTuru, string? malzemeTipi, string? kaynak, string varsayilan = "Other")
    {
        if (!string.IsNullOrWhiteSpace(cihazTuru)) return cihazTuru.Trim();
        string t = ((malzemeTipi ?? "") + " " + (kaynak ?? "")).ToLowerInvariant();
        if (t.Trim() == "") return varsayilan;
        return t.Contains("panelnetwork") ? "PatchPanel" : t.Contains("network") ? "Network" : t.Contains("server") ? "Server" : t.Contains("storage") ? "Storage"
            : t.Contains("pdu") || t.Contains("powerstrip") ? "PDU" : t.Contains("chassis") ? "Chassis" : t.Contains("kvm") ? "KVM" : t.Contains("ups") ? "UPS"
            : t.Contains("aircondition") ? "Cooling" : t.Contains("staticswitch") ? "StaticSwitch" : t.Contains("auxiliar") ? "Auxiliary"
            : t.Contains("peripheral") ? "Peripheral" : varsayilan;

    }

    // Anlık görüntü + dizinler (kabin → VM'ler, IP → VM'ler); sahte veriyle test için de kullanılır
    public static FizikselSnapshot Olustur(List<FizVm> vmler, List<FizEksik> eksik, bool tablodan, List<FizKabin>? kabinler = null, List<FizCihaz>? cihazlar = null,
        List<FizChassis>? chassisler = null, Dictionary<string, string>? hostAsset = null, List<FizMsy>? msy = null, string anaKapsam = "Ana veri merkezi")
    {
        // Cluster bilgisi eksik listesine KabinHostVm'den eklenir (FQDN ya da kısa ad)
        var clusterOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in vmler.Where(v => v.Cluster != null))
            foreach (var k in new[] { v.HostFqdn, v.HostFqdn?.Split('.')[0], v.Host }.OfType<string>()) clusterOf.TryAdd(k, v.Cluster!);
        eksik = eksik.Select(e => e.Cluster == null && (clusterOf.GetValueOrDefault(e.Fqdn ?? "") ?? clusterOf.GetValueOrDefault(e.Host)) is string c ? e with { Cluster = c } : e)
            .OrderByDescending(e => e.VmSayisi).ThenBy(e => e.Host).ToList();
        var s = new FizikselSnapshot { Vmler = vmler, Eksik = eksik, EksikTablodan = tablodan, LoadedAt = DateTimeOffset.Now };
        s.NlyteVar = kabinler is { Count: > 0 };
        var idAd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kb in kabinler ?? [])
        {
            s.Kabinler.TryAdd(kb.Ad, kb);
            if (kb.CabinetAssetId != null) idAd.TryAdd(kb.CabinetAssetId, kb.Ad);
        }
        foreach (var ch in chassisler ?? []) s.Chassisler.TryAdd(ch.Id, ch);
        // Chassis olarak kayıtlı cihazlar da chassis sözlüğüne (NlyteChassis yoksa blade'in kabini / U'su buradan)
        foreach (var c in (cihazlar ?? []).Where(c => c.Tur.Equals("Chassis", StringComparison.OrdinalIgnoreCase) && c.AssetId != null))
            s.Chassisler.TryAdd(c.AssetId!, new FizChassis(c.AssetId!, c.Ad, c.CabinetAssetId, c.UAlt, c.UYukseklik, c.Yuz, c.Model));
        foreach (var kv in hostAsset ?? []) s.HostAsset.TryAdd(kv.Key, kv.Value);
        foreach (var c in cihazlar ?? []) if (c.AssetId != null) s.CihazId.TryAdd(c.AssetId, c);
        foreach (var c in cihazlar ?? [])
        {
            string? kabinId = c.CabinetAssetId ?? (c.ChassisId != null ? s.Chassisler.GetValueOrDefault(c.ChassisId)?.CabinetAssetId : null);
            if (kabinId != null && idAd.TryGetValue(kabinId, out var kad))
                (s.KabinCihaz.TryGetValue(kad, out var l3) ? l3 : s.KabinCihaz[kad] = []).Add(c);
            else s.KabinDisi.Add(c);
        }

        // Yedek eşleşme: kabini olmayan VM'lerin host'u, nlyte'ta bir kabindeki TEK bir cihazla aynı adlıysa (çoğunlukla Network / başka türde
        // kayıtlı ESX — prosedür yalnız sunucularla eşleştiriyor) VM'ler bellekte o kabine bağlanır. Veritabanına yazılmaz.
        foreach (var m in msy ?? [])
        {
            if (m.DnsName != null) s.MsyAd.TryAdd(Norm(m.DnsName), m);
            if (m.Seri != null) s.MsySeri.TryAdd(m.Seri.Trim(), m);
            foreach (var ip in new[] { m.DnsIp, m.IloIp }.OfType<string>()) s.MsyIp.TryAdd(ip.Trim(), m);
        }
        s.MsyVar = s.MsyAd.Count > 0;
        var adIndeks = new Dictionary<string, List<(FizCihaz C, string Kabin)>>(StringComparer.OrdinalIgnoreCase);
        var seriIndeks = new Dictionary<string, List<(FizCihaz C, string Kabin)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kad, cs) in s.KabinCihaz)
            foreach (var c in cs.Where(c => c.Aktif && c.Seri != null))
                (seriIndeks.TryGetValue(c.Seri!.Trim(), out var l) ? l : seriIndeks[c.Seri!.Trim()] = []).Add((c, kad));
        foreach (var (kad, cs) in s.KabinCihaz)
            foreach (var c in cs.Where(c => c.Aktif))
                foreach (var a in c.Adlar.Select(Norm).Distinct())
                    (adIndeks.TryGetValue(a, out var l) ? l : adIndeks[a] = []).Add((c, kad));
        if (adIndeks.Count > 0)
        {
            var bulunan = new Dictionary<string, (FizCihaz C, string Kabin, string Yontem)>(StringComparer.OrdinalIgnoreCase);
            // 1) ad: kabindeki tek cihazın adı; 2) seri: KabinHostVm.HostSeri ya da MSY fiziksel envanterinde host'un seri numarası → nlyte SerialNumber
            (FizCihaz C, string Kabin, string Yontem)? Bul(FizVm v)
            {
                var hlar = new[] { v.Host, v.HostFqdn }.OfType<string>().Select(HostAnahtar).Distinct().ToList();
                foreach (var h in hlar)
                {
                    if (bulunan.TryGetValue(h, out var b)) return b;
                    if (adIndeks.TryGetValue(h, out var l) && l.Select(x => x.C).Distinct().Count() == 1) return bulunan[h] = (l[0].C, l[0].Kabin, "ad");
                }
                // host alanında IP yazıyorsa (Kapsam "Host adı yok"): MSY yönetim / iLO IP → seri; ya da o kaydın DNS adı nlyte'ta
                var ipKayit = hlar.Select(h => s.MsyIp.GetValueOrDefault(h)).FirstOrDefault(x => x != null);
                if (ipKayit?.DnsName != null && adIndeks.TryGetValue(Norm(ipKayit.DnsName), out var li) && li.Select(x => x.C).Distinct().Count() == 1)
                {
                    var b = (li[0].C, li[0].Kabin, "IP → ad (MSY fiziksel envanter)");
                    foreach (var h in hlar) bulunan[h] = b;
                    return b;
                }
                foreach (var (seri, yontem) in new[] { (v.HostSeri, "seri"), (ipKayit?.Seri, "IP → seri (MSY fiziksel envanter)") }
                    .Concat(hlar.Select(h => (s.MsyAd.GetValueOrDefault(h)?.Seri, "seri (MSY fiziksel envanter)"))))
                    if (seri != null && seriIndeks.TryGetValue(seri.Trim(), out var l) && l.Select(x => x.C).Distinct().Count() == 1)
                    {
                        var b = (l[0].C, l[0].Kabin, yontem);
                        foreach (var h in hlar) bulunan[h] = b;
                        return b;
                    }
                return null;
            }
            var yeni = new List<FizVm>(vmler.Count);
            var eslenen = new Dictionary<string, (FizCihaz C, string Kabin, string Yontem, string? EskiKapsam, List<FizVm> Vm)>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vmler)
            {
                if (v.Kabin == null && Bul(v) is { } b)
                {
                    var kb = s.Kabinler.GetValueOrDefault(b.Kabin);
                    var nv = v with
                    {
                        Kabin = b.Kabin, KabinU = kb?.UYukseklik, KabinUSirasi = kb?.USirasi, KabinBosU = kb?.BosU, HostUAlt = b.C.UAlt, HostUYukseklik = b.C.UYukseklik,
                        HostYuz = b.C.Yuz, HostModel = v.HostModel ?? b.C.Model, HostSeri = v.HostSeri ?? b.C.Seri, CabinetAssetId = b.C.CabinetAssetId,
                        Eslesme = b.Yontem == "ad" ? $"Ad (DeltaFlow; nlyte'ta {b.C.Tur})" : $"{char.ToUpper(b.Yontem[0])}{b.Yontem[1..]} (DeltaFlow; nlyte adı {b.C.Ad})",
                        // nlyte yalnız ana veri merkezini kapsar: kabine bağlanan VM ana kapsama geçer (ör. "Host adı yok")
                        Kapsam = anaKapsam, Konum = v.Konum,
                    };
                    yeni.Add(nv);
                    string key = v.HostFqdn ?? v.Host ?? "?";
                    (eslenen.TryGetValue(key, out var e) ? e : eslenen[key] = (b.C, b.Kabin, b.Yontem, v.Kapsam, [])).Vm.Add(nv);
                }
                else yeni.Add(v);
            }
            if (eslenen.Count > 0)
            {
                s.Vmler = yeni;
                foreach (var (key, e) in eslenen.OrderByDescending(x => x.Value.Vm.Count))
                    s.TurFarkli.Add(new FizTurFarkli(e.Vm[0].Host ?? key, e.Vm[0].HostFqdn, e.Vm.Count, e.Vm[0].Cluster, e.C.Ad, e.C.Tur, e.Kabin, e.C.AssetId, e.C.UAlt, e.Yontem, e.EskiKapsam));
                var anahtar = eslenen.Values.SelectMany(e => new[] { e.Vm[0].Host, e.Vm[0].HostFqdn }).OfType<string>().Select(Norm).ToHashSet(StringComparer.OrdinalIgnoreCase);
                s.Eksik = s.Eksik.Where(e => !anahtar.Contains(Norm(e.Host)) && !(e.Fqdn != null && anahtar.Contains(Norm(e.Fqdn)))).ToList();
            }
        }
        foreach (var v in s.Vmler)
        {
            if (v.Kabin != null) (s.KabinVm.TryGetValue(v.Kabin, out var l) ? l : s.KabinVm[v.Kabin] = []).Add(v);
            if (v.Ip != null) foreach (var ip in v.Ip.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    (s.IpVm.TryGetValue(ip, out var l2) ? l2 : s.IpVm[ip] = []).Add(v);
        }
        return s;
    }

    // ---- okuma yardımcıları (kolon yoksa null) ----
    static Dictionary<string, int> Kolonlar(SqlDataReader r)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.FieldCount; i++) d.TryAdd(r.GetName(i), i);
        return d;
    }
    static string? S(SqlDataReader r, Dictionary<string, int> k, string ad)
    {
        if (!k.TryGetValue(ad, out int i) || r.IsDBNull(i)) return null;
        string v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)?.Trim() ?? "";
        return v == "" || v is "#N/A" or "NULL" ? null : v;
    }
    // Kimlik kolonları tablolar arasında farklı tipte olabilir (int / decimal / nvarchar): "123.00" → "123"
    static string? Id(string? v) => v != null && Regex.IsMatch(v, @"^\d+\.0+$") ? v[..v.IndexOf('.')] : v;
    static int? I(SqlDataReader r, Dictionary<string, int> k, string ad) =>
        S(r, k, ad) is string v && double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? (int)Math.Round(d) : null;
    static DateTime? D(SqlDataReader r, Dictionary<string, int> k, string ad) =>
        k.TryGetValue(ad, out int i) && !r.IsDBNull(i) && r.GetValue(i) is DateTime dt ? dt
        : S(r, k, ad) is string v && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static List<string> Liste(string? v) => v == null ? [] :
        v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ---- görünümler ----
    // Kabin adından oda / sıra / numara. Kalıplar (kullanıcı örnekleri; "Eski " ve "ZB." öneki atılır):
    //  1) oda + sıra + no: DR4-D3, ZB.DR1.B10, ZB.DR2.A12-1, ZB.DR1.E13_43_46U, ZB.DR3.A1_A2.45_46U (→ A1), ZB.DR4.F8.46U, EF1-A1, ZB.TR1.B2, YZ-A1
    //  2) oda + sıra (no yok): DR1-B-EMC-1, DR4-A-HUAWEI-1, ZB.DR3.C.NS6.45_46U, ZB.DR4.A.NS2, TR-B-EMC-1, CO-1-A-HITACHI-1
    //  3) yalnız oda: CO2-ORACLE-1, ZB.DR1.DZON.42
    // Oda kodu büyük harf, 2–3 harf + isteğe bağlı numara (CO-1 → CO1). Asset_… gibi adlar "Diğer".
    static readonly Regex Kalip1 = new(@"^(?:ZB\.)?([A-Z]{2,3}(?:-?\d+)?)[-.]([A-Z])(\d+)(?=$|[_.\-\s(])", RegexOptions.Compiled);
    static readonly Regex Kalip2 = new(@"^(?:ZB\.)?([A-Z]{2,3}(?:-?\d+)?)[-.]([A-Z])[-.]", RegexOptions.Compiled);
    static readonly Regex Kalip3 = new(@"^(?:ZB\.)?([A-Z]{2,3}(?:-?\d+)?)[-.][A-Z0-9]", RegexOptions.Compiled);
    public static (string? Oda, string? Sira, int? No) Parcala(string kabin)
    {
        string k = Regex.Replace(kabin.Trim(), @"^Eski\s+", "", RegexOptions.IgnoreCase);
        static string O(string x) => x.Replace("-", "");
        if (Kalip1.Match(k) is { Success: true } m1) return (O(m1.Groups[1].Value), m1.Groups[2].Value, int.Parse(m1.Groups[3].Value));
        if (Kalip2.Match(k) is { Success: true } m2) return (O(m2.Groups[1].Value), m2.Groups[2].Value, null);
        if (Kalip3.Match(k) is { Success: true } m3) return (O(m3.Groups[1].Value), null, null);
        return (null, null, null);
    }
    static string OdaAdi(string? konum) => konum == null ? "" : konum.Split(" - ")[0].Trim();

    // U1 üstte mi? (nlyte CabinetUOrder). Değerler kesin bilinmiyor: "top"/"yukarı" ile başlayan ya da Fiziksel:UstSirasiIfadeleri'nde geçen → U1 üstte
    public bool U1Ustte(string? sira)
    {
        if (string.IsNullOrWhiteSpace(sira)) return false;
        var ifadeler = cfg.GetSection("Fiziksel:UstSirasiIfadeleri").GetChildren().Select(c => c.Value).OfType<string>().ToList();
        if (ifadeler.Count > 0) return ifadeler.Any(x => string.Equals(x.Trim(), sira.Trim(), StringComparison.OrdinalIgnoreCase));
        string s = sira.Trim().ToLowerInvariant();
        return s.StartsWith("top") || s.StartsWith("yukar") || s.Contains("toptobottom") || s.Contains("descending");
    }

    public object Ozet(FizikselSnapshot s)
    {
        var kapsamlar = s.Vmler.GroupBy(v => v.Kapsam ?? "(boş)").Select(g => new
        {
            kapsam = g.Key, vm = g.Count(), kabinli = g.Count(v => v.Kabin != null),
            host = g.Select(v => v.HostFqdn ?? v.Host).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
        }).OrderByDescending(x => x.kapsam == AnaKapsam).ThenByDescending(x => x.vm).ToList();
        var odalar = s.TumKabinler.Select(k => (k, p: Parcala(k))).GroupBy(x => x.p.Oda ?? "Diğer")
            .Select(g => new
            {
                ad = g.Key, kabin = g.Count(), vm = g.Sum(x => s.KabinVm.GetValueOrDefault(x.k)?.Count ?? 0),
                vmliKabin = g.Count(x => s.KabinVm.ContainsKey(x.k)),
                konum = g.Select(x => KabinKonum(s, x.k)).Where(x => x != null).Select(x => OdaAdi(x)).Where(x => x != "")
                    .GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault(),
            })
            .OrderBy(x => x.ad == "Diğer").ThenBy(x => x.ad.StartsWith("DR") ? 0 : 1).ThenBy(x => Regex.Replace(x.ad, @"\d+$", ""))
            .ThenBy(x => int.TryParse(Regex.Match(x.ad, @"\d+$").Value, out int n) ? n : 0).ToList();
        return new { yenileme = s.Yenileme, yuklendi = s.LoadedAt, anaKapsam = AnaKapsam, kapsamlar, odalar, nlyte = s.NlyteVar, istatistik = Istatistik(s),
            toplamKabin = s.TumKabinler.Count(), vmliKabin = s.KabinVm.Count, cihaz = s.KabinCihaz.Values.Sum(l => l.Count(c => c.Aktif)),
            cihazTablosu = s.CihazTablosu, turlar = TurSayim(s.KabinCihaz.Values.SelectMany(l => l).Where(c => c.Aktif)),
            kabinDisi = s.KabinDisi.Count(c => c.Aktif), kabinDisiTurlar = TurSayim(s.KabinDisi.Where(c => c.Aktif)), turFarkliSayisi = s.TurFarkli.Count, msy = s.MsyVar, eksikSayisi = s.Eksik.Count(e => e.Kapsam == null || e.Kapsam == AnaKapsam) };
    }

    // Alt istatistik (kullanıcı: "toplam yerleşen sunucu ve uygulama sayısı önemli"): ana veri merkezi kapsamında
    object Istatistik(FizikselSnapshot s)
    {
        bool Ana(FizVm v) => v.Kapsam == null || v.Kapsam == AnaKapsam;
        var vm = s.Vmler.Where(Ana).ToList();
        string HostKey(FizVm v) => Norm(v.HostFqdn ?? v.Host ?? "?");
        var hostlar = vm.Where(v => v.Host != null || v.HostFqdn != null).GroupBy(HostKey).ToList();
        int hostYerlesen = hostlar.Count(g => g.Any(v => v.Kabin != null));
        var aktifCihaz = s.KabinCihaz.Values.SelectMany(l => l).Where(c => c.Aktif).ToList();
        int sunucu = aktifCihaz.Count(c => c.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase));
        int sunucuU = aktifCihaz.Count(c => c.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase) && (c.UAlt != null || c.ChassisId != null));
        // uygulama → VM'ler (kabinli / toplam) ve kabin sayısı
        var uyg = vm.SelectMany(v => v.Uygulamalar.Select(a => (a, v))).GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Ad: g.Key, Toplam: g.Count(), Kabinli: g.Count(x => x.v.Kabin != null),
                Kabin: g.Select(x => x.v.Kabin).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count())).ToList();
        int vmKabinli = vm.Count(v => v.Kabin != null);
        return new
        {
            kabin = s.TumKabinler.Count(), kabinCihazli = s.TumKabinler.Count(k => s.KabinCihaz.GetValueOrDefault(k)?.Any(c => c.Aktif) == true), kabinVmli = s.KabinVm.Count,
            cihaz = aktifCihaz.Count, sunucu, sunucuU, kabinDisiSunucu = s.KabinDisi.Count(c => c.Aktif && c.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase)),
            esxHost = hostlar.Count, esxYerlesen = hostYerlesen, esxEkEslesme = s.TurFarkli.Count, esxEksik = s.Eksik.Count(e => e.Kapsam == null || e.Kapsam == AnaKapsam),
            vm = vm.Count, vmKabinli, vmOran = vm.Count == 0 ? 0 : Math.Round(vmKabinli * 100.0 / vm.Count, 1),
            uygulama = uyg.Count, uygulamaYerlesen = uyg.Count(u => u.Kabinli > 0), uygulamaTamami = uyg.Count(u => u.Kabinli == u.Toplam),
            uygulamaTekKabin = uyg.Count(u => u.Kabinli == u.Toplam && u.Kabin == 1), uygulamaHicKabin = uyg.Count(u => u.Kabinli == 0),
            vmUygulamasiz = vm.Count(v => v.Uygulamalar.Count == 0),
            // kabin değer dağılımı: boyut başına "değer → kabin sayısı" (kabin değeri = kabindeki uygulamaların en yükseği)
            degerAdlari = DegerAdlari, degerVar = s.AppDeger.Count > 0,
            kabinDeger = Enumerable.Range(0, 5).Select(i => s.KabinVm.Values
                .Select(vs => vs.SelectMany(v => v.Uygulamalar).Select(a => s.AppDeger.GetValueOrDefault(a.Trim())?[i]).Max())
                .GroupBy(x => x?.ToString() ?? "yok").OrderByDescending(g => g.Key).ToDictionary(g => g.Key, g => g.Count())).ToList(),
            tumVm = s.Vmler.Count, tumVmKabinli = s.Vmler.Count(v => v.Kabin != null),
            kapsamlar = s.Vmler.GroupBy(v => v.Kapsam ?? "(boş)").Select(g => new { kapsam = g.Key, vm = g.Count(), kabinli = g.Count(v => v.Kabin != null), ana = g.Key == AnaKapsam })
                .OrderByDescending(x => x.ana).ThenByDescending(x => x.vm).ToList(),
            ekKapsamdan = s.TurFarkli.Where(t => t.EskiKapsam != null && t.EskiKapsam != AnaKapsam).GroupBy(t => t.EskiKapsam!).Select(g => new { kapsam = g.Key, host = g.Count(), vm = g.Sum(t => t.VmSayisi) }).ToList(),
        };
    }

    public object Oda(FizikselSnapshot s, string ad)
    {
        var kabinler = s.TumKabinler.Where(k => (Parcala(k).Oda ?? "Diğer").Equals(ad, StringComparison.OrdinalIgnoreCase)).Select(k =>
        {
            var p = Parcala(k);
            var vs = s.KabinVm.GetValueOrDefault(k) ?? [];
            var cs = (s.KabinCihaz.GetValueOrDefault(k) ?? []).Where(c => c.Aktif).ToList();
            int vmHost = vs.Select(v => v.Host ?? v.HostFqdn).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            return new
            {
                ad = k, sira = p.Sira, no = p.No, vm = vs.Count, cihaz = cs.Count, vmHost, host = Math.Max(cs.Count, vmHost), turlar = TurSayim(cs),
                uygulama = vs.SelectMany(v => v.Uygulamalar).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                deger = Deger(s, vs.SelectMany(v => v.Uygulamalar)),
                bosU = s.Kabinler.GetValueOrDefault(k)?.BosU ?? vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), konum = KabinKonum(s, k),
            };
        }).OrderBy(k => k.sira).ThenBy(k => k.no).ThenByDescending(k => k.vm).ThenByDescending(k => k.cihaz).ThenBy(k => k.ad.StartsWith("ZB.", StringComparison.OrdinalIgnoreCase)).ThenBy(k => k.ad).ToList();
        // Aynı (sıra, no) konumuna düşen birden çok kayıt (ör. DR3-A1 ve ZB.DR3.A1_A2.45_46U): VM / cihazı en çok olan ızgarada, diğerleri ona "ek"
        var asil = new Dictionary<string, string>();
        var sonuc = kabinler.Select(k =>
        {
            string? ek = null;
            if (k.sira != null && k.no != null) { string key = k.sira + "|" + k.no; if (!asil.TryAdd(key, k.ad)) ek = asil[key]; }
            return new { k.ad, k.sira, k.no, k.vm, k.cihaz, k.vmHost, k.host, k.turlar, k.uygulama, k.deger, k.bosU, k.konum, ek };
        }).ToList();
        return new { ad, kabinler = sonuc };
    }

    // Kabinin konumu: KabinHostVm.Konum, yoksa nlyte UniqueAssetName / KabinUniqueAd içindeki "Room=…"
    static string? KabinKonum(FizikselSnapshot s, string kabin)
    {
        if (s.KabinVm.GetValueOrDefault(kabin)?.Select(v => v.Konum).FirstOrDefault(x => x != null) is string k) return k;
        foreach (var t in (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Select(c => c.UniqueAssetName).Prepend(s.Kabinler.GetValueOrDefault(kabin)?.UniqueAd))
            if (t != null && Regex.Match(t, @"Room=(.+?)\)?\s*$") is { Success: true } m) return m.Groups[1].Value.Trim();   // "ad (Room=…)" → dıştaki tek ')' atılır
        return null;
    }

    // Host / cihaz adı anahtarı: kısa ad, küçük harf. IP (ya da "10.6.30.99.msy.zb" gibi IP'li FQDN) olduğu gibi IP kalır —
    // eskiden ilk noktaya kadar kırpılıp "10" oluyor, tüm IP host'lar aynı anahtara düşüp yanlış kabine bağlanabiliyordu
    static readonly Regex IpBas = new(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}", RegexOptions.Compiled);
    static string Norm(string x) { x = x.Trim().ToLowerInvariant(); var m = IpBas.Match(x); return m.Success ? m.Value : x.Split('.')[0]; }
    // host anahtarı: IP olduğu gibi (Norm "10.6.30.21"i "10"a indirirdi), ad kısa + küçük harf
    static string HostAnahtar(string x) => Norm(x);

    // Tür sayımı: [{tur, adet, vmli}] — en kalabalık önce
    static List<object> TurSayim(IEnumerable<FizCihaz> cs, ISet<FizCihaz>? vmli = null) =>
        cs.GroupBy(c => c.Tur, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => (object)new { tur = g.Key, adet = g.Count(), vmli = vmli == null ? 0 : g.Count(vmli.Contains) }).ToList();

    // Kabindeki VM'li host grupları ↔ nlyte cihazları: NlyteHostEslesme AssetID, seri, MSY serisi, ad
    static (List<IGrouping<string, FizVm>> Gruplar, Dictionary<FizCihaz, IGrouping<string, FizVm>> CihazGrup, List<IGrouping<string, FizVm>> Eslesmeyen)
        Eslestir(FizikselSnapshot s, List<FizVm> vs, List<FizCihaz> cihazlar)
    {
        var gruplar = vs.GroupBy(v => v.Host ?? v.HostFqdn ?? "?", StringComparer.OrdinalIgnoreCase).ToList();
        var cihazGrup = new Dictionary<FizCihaz, IGrouping<string, FizVm>>();
        var eslesmeyen = new List<IGrouping<string, FizVm>>();
        foreach (var g in gruplar)
        {
            var h = g.First();
            string? aid = new[] { g.Key, h.HostFqdn, h.Host }.OfType<string>().Select(x => s.HostAsset.GetValueOrDefault(Norm(x))).FirstOrDefault(x => x != null);
            var c = (aid != null ? cihazlar.FirstOrDefault(c => c.AssetId != null && c.AssetId.Equals(aid, StringComparison.OrdinalIgnoreCase) && !cihazGrup.ContainsKey(c)) : null)
                ?? cihazlar.FirstOrDefault(c => h.HostSeri != null && c.Seri != null && c.Seri.Equals(h.HostSeri, StringComparison.OrdinalIgnoreCase) && !cihazGrup.ContainsKey(c))
                ?? (new[] { g.Key, h.HostFqdn }.OfType<string>().Select(x => s.MsyAd.GetValueOrDefault(Norm(x))?.Seri).FirstOrDefault(x => x != null) is string mseri
                    ? cihazlar.FirstOrDefault(c => c.Seri != null && c.Seri.Trim().Equals(mseri.Trim(), StringComparison.OrdinalIgnoreCase) && !cihazGrup.ContainsKey(c)) : null)
                ?? cihazlar.FirstOrDefault(c => !cihazGrup.ContainsKey(c) && c.Adlar.Any(a => Norm(a) == Norm(g.Key) || (h.HostFqdn != null && Norm(a) == Norm(h.HostFqdn))));
            if (c != null) cihazGrup[c] = g; else eslesmeyen.Add(g);
        }
        return (gruplar, cihazGrup, eslesmeyen);
    }

    // ---- kablo izi ----
    static readonly Regex PortNo = new(@"(\d+)\D*$", RegexOptions.Compiled);
    static bool AyniPort(string? a, string? b)
    {
        if (a == null || b == null) return false;
        if (a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        var ma = PortNo.Match(a); var mb = PortNo.Match(b);
        return ma.Success && mb.Success && int.Parse(ma.Groups[1].Value) == int.Parse(mb.Groups[1].Value);
    }
    static readonly HashSet<string> GucTurleri = new(["PDU", "UPS", "StaticSwitch"], StringComparer.OrdinalIgnoreCase);
    // kablo sınıfı: güç (PDU / UPS / statik switch ucu), fiber (tür / ad: fiber, F-, LC, SC, MPO, OM, OS), veri (bakır)
    static string Sinif(FizikselSnapshot s, FizBaglanti c)
    {
        if (GucTurleri.Contains(TurOf(s, c.KaynakId, c.KaynakTur)) || GucTurleri.Contains(TurOf(s, c.HedefId, c.HedefTur))) return "güç";
        string t = ((c.KabloTuru ?? "") + " " + (c.KabloAdi ?? "")).Trim();
        return Regex.IsMatch(t, @"fib|^F-|\b(LC|SC|MPO|MTP|OM\d|OS\d)\b", RegexOptions.IgnoreCase) ? "fiber" : "veri";
    }
    static string TurOf(FizikselSnapshot s, string? id, string? tur) => tur ?? (id != null ? s.CihazId.GetValueOrDefault(id)?.Tur : null) ?? "Other";

    // Bir cihazın kablolarını patch panel'lerin üzerinden izler: panelde aynı port numarasıyla devam eder (ön ↔ arka / panel ↔ panel).
    // Port bilinmiyorsa ya da devamı yoksa iz panelde "kesik" biter. En çok 6 panel.
    public static List<FizIz> Iz(FizikselSnapshot s, string assetId)
    {
        var sonuc = new List<FizIz>();
        foreach (var c in s.BagIdx.GetValueOrDefault(assetId) ?? [])
        {
            bool kaynakBen = string.Equals(c.KaynakId, assetId, StringComparison.OrdinalIgnoreCase);
            if (kaynakBen && string.Equals(c.HedefId, assetId, StringComparison.OrdinalIgnoreCase)) continue;
            string? yerel = kaynakBen ? c.KaynakPort : c.HedefPort;
            Takip(c, kaynakBen, yerel, [], new HashSet<string> { c.CableId }, 0, Sinif(s, c));
        }
        return sonuc;

        void Takip(FizBaglanti c, bool kaynakBen, string? yerel, List<string> paneller, HashSet<string> gorulen, int derinlik, string sinif)
        {
            string? id = kaynakBen ? c.HedefId : c.KaynakId, ad = (kaynakBen ? c.HedefAdi : c.KaynakAdi) ?? id ?? "?";
            string? port = kaynakBen ? c.HedefPort : c.KaynakPort, kabin = kaynakBen ? c.HedefKabin : c.KaynakKabin;
            string tur = TurOf(s, id, kaynakBen ? c.HedefTur : c.KaynakTur);
            string? kablo = c.KabloTuru ?? c.KabloAdi;
            if (tur.Equals("PatchPanel", StringComparison.OrdinalIgnoreCase) && id != null && derinlik < 6)
            {
                var adaylar = (s.BagIdx.GetValueOrDefault(id) ?? []).Where(d => !gorulen.Contains(d.CableId))
                    .Select(d => (d, panelKaynak: string.Equals(d.KaynakId, id, StringComparison.OrdinalIgnoreCase))).ToList();
                string? PanelPort((FizBaglanti d, bool panelKaynak) x) => x.panelKaynak ? x.d.KaynakPort : x.d.HedefPort;
                // önce birebir aynı port adı (1A1 ↔ 1A1); yoksa sondaki numara (5 ↔ Port 5) — 1A1 / 2A1 gibi adlar karışmasın
                var devam = port == null ? [] : adaylar.Where(x => PanelPort(x)?.Trim().Equals(port.Trim(), StringComparison.OrdinalIgnoreCase) == true).ToList();
                if (devam.Count == 0 && port != null && !Regex.IsMatch(port, @"\d+[A-Za-z]+\d+")) devam = adaylar.Where(x => AyniPort(PanelPort(x), port)).ToList();
                if (devam.Count > 0)
                {
                    foreach (var (d, pk) in devam)
                        Takip(d, pk, yerel, [.. paneller, port != null ? $"{ad}:{port}" : ad], [.. gorulen, d.CableId], derinlik + 1, sinif == "veri" ? Sinif(s, d) : sinif);
                    return;
                }
                sonuc.Add(new FizIz(yerel, id, ad, tur, port, kabin, paneller, kablo, true, sinif));   // panelde devamı yok / port bilinmiyor
                return;
            }
            sonuc.Add(new FizIz(yerel, id, ad, tur, port, kabin, paneller, kablo, false, sinif));
        }
    }

    static object IzSatir(FizIz z) => new { yerelPort = z.YerelPort, uzakId = z.UzakId, uzakAd = z.UzakAd, uzakTur = z.UzakTur, uzakPort = z.UzakPort,
        uzakKabin = z.UzakKabin, paneller = z.Paneller, kablo = z.Kablo, kesik = z.Kesik, sinif = z.Sinif };

    // Cihaz detayı: bağlantılar (uçtan uca) + bu cihaz kapanırsa etkilenen sunucular / VM'ler / uygulamalar
    public object? Cihaz(FizikselSnapshot s, string id)
    {
        if (!s.CihazId.TryGetValue(id, out var c)) return null;
        var izler = Iz(s, id);
        // blade'in kendi kablosu yoksa chassis'inin kabloları
        bool chassisten = false;
        if (izler.Count == 0 && c.ChassisId != null) { izler = Iz(s, c.ChassisId); chassisten = izler.Count > 0; }
        var avm = AssetVm(s);
        // etkilenen sunucular: doğrudan (panel üzerinden) bağlı sunucular / chassis'lerin blade'leri; storage ise bağlı switch'lerin sunucuları da (SAN, dolaylı)
        var sunucu = new Dictionary<string, (FizCihaz C, string Yol)>(StringComparer.OrdinalIgnoreCase);
        void Ekle(string? sid, string yol)
        {
            if (sid == null || sid.Equals(id, StringComparison.OrdinalIgnoreCase) || !s.CihazId.TryGetValue(sid, out var t)) return;
            if (t.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase) || avm.ContainsKey(sid)) sunucu.TryAdd(sid, (t, yol));   // VM'li cihaz türü yanlış olsa da sunucu
            else if (t.Tur.Equals("Chassis", StringComparison.OrdinalIgnoreCase))
                foreach (var b in s.CihazId.Values.Where(b => b.Aktif && string.Equals(b.ChassisId, sid, StringComparison.OrdinalIgnoreCase)))
                    if (b.AssetId != null) sunucu.TryAdd(b.AssetId, (b, $"{yol} · chassis {t.Ad}"));
        }
        bool sunucuMu = c.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase);
        if (!sunucuMu)
        {
            foreach (var z in izler.Where(z => !z.Kesik)) Ekle(z.UzakId, "doğrudan");
            if (c.Tur.Equals("Chassis", StringComparison.OrdinalIgnoreCase)) Ekle(id, "chassis");   // chassis'in kendi blade'leri
            // UPS / statik switch: bağlı PDU'ların beslediği sunucular (dolaylı)
            if (c.Tur is "UPS" or "StaticSwitch")
                foreach (var pdu in izler.Where(z => !z.Kesik && z.UzakId != null && GucTurleri.Contains(z.UzakTur)).Select(z => z.UzakId!).Distinct())
                    foreach (var z2 in Iz(s, pdu).Where(z => !z.Kesik)) Ekle(z2.UzakId, $"PDU {s.CihazId.GetValueOrDefault(pdu)?.Ad ?? pdu} üzerinden (güç)");
            if (c.Tur.Equals("Storage", StringComparison.OrdinalIgnoreCase))
                foreach (var sw in izler.Where(z => !z.Kesik && z.UzakId != null && z.UzakTur is "Network" or "StaticSwitch").Select(z => z.UzakId!).Distinct())
                    foreach (var z2 in Iz(s, sw).Where(z => !z.Kesik)) Ekle(z2.UzakId, $"switch {s.CihazId.GetValueOrDefault(sw)?.Ad ?? sw} üzerinden (dolaylı)");
        }
        var sunucular = sunucu.Values.Select(x =>
        {
            var vs = x.C.AssetId != null ? avm.GetValueOrDefault(x.C.AssetId) ?? [] : [];
            var kabin = s.KabinCihaz.FirstOrDefault(kv => kv.Value.Contains(x.C)).Key;
            return new { ad = x.C.Ad, assetId = x.C.AssetId, kabin, yol = x.Yol, vm = vs.Count,
                uygulamalar = vs.SelectMany(v => v.Uygulamalar).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), deger = Deger(s, vs.SelectMany(v => v.Uygulamalar)) };
        }).OrderByDescending(x => x.vm).ThenBy(x => x.ad).ToList();
        var tumVm = sunucu.Values.SelectMany(x => x.C.AssetId != null ? avm.GetValueOrDefault(x.C.AssetId) ?? [] : []).ToList();
        var uygulamalar = tumVm.SelectMany(v => v.Uygulamalar.Select(a => (a, v))).GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { ad = g.Key, vm = g.Select(x => x.v.Vm ?? x.v.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count(), deger = Deger(s, [g.Key]) })
            .OrderByDescending(x => x.vm).ThenBy(x => x.ad).ToList();
        return new
        {
            ad = c.Ad, assetId = c.AssetId, tur = c.Tur, model = c.Model, kablo = s.Baglantilar.Count > 0, chassisten, guc = GucTurleri.Contains(c.Tur),
            baglantilar = izler.OrderBy(z => z.YerelPort).ThenBy(z => z.UzakAd).Select(IzSatir).ToList(),
            etki = sunucuMu ? null : new { sunucu = sunucular.Count, vm = tumVm.Count, sunucular, uygulamalar, deger = Deger(s, tumVm.SelectMany(v => v.Uygulamalar)) },
        };
    }

    // nlyte AssetID → o cihazdaki (host'taki) VM'ler, tüm kabinler için (bağlantı etkisinde kullanılır)
    static Dictionary<string, List<FizVm>> AssetVm(FizikselSnapshot s)
    {
        if (s.AssetVmOnbellek != null) return s.AssetVmOnbellek;
        var d = new Dictionary<string, List<FizVm>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kabin, vs) in s.KabinVm)
        {
            var (_, cg, _) = Eslestir(s, vs, (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Where(c => c.Aktif).ToList());
            foreach (var (c, g) in cg) if (c.AssetId != null) d[c.AssetId] = g.ToList();
        }
        return s.AssetVmOnbellek = d;
    }

    public object? Kabin(FizikselSnapshot s, string ad)
    {
        var vs = s.KabinVm.GetValueOrDefault(ad) ?? [];
        var kb = s.Kabinler.GetValueOrDefault(ad);
        var tumCihaz = s.KabinCihaz.GetValueOrDefault(ad) ?? [];
        if (kb == null && vs.Count == 0) return null;
        var cihazlar = tumCihaz.Where(c => c.Aktif).ToList();
        var ilk = vs.FirstOrDefault();

        var (gruplar, cihazGrup, eslesmeyen) = Eslestir(s, vs, cihazlar);
        FizMsy? MsyBul(FizCihaz? c, FizVm? h) =>
            (c?.Seri != null ? s.MsySeri.GetValueOrDefault(c.Seri.Trim()) : null)
            ?? new[] { h?.HostFqdn, h?.Host }.Concat(c?.Adlar ?? []).OfType<string>().Select(x => s.MsyAd.GetValueOrDefault(Norm(x))).FirstOrDefault(x => x != null);
        object Satir(FizCihaz? c, IGrouping<string, FizVm>? g)
        {
            var h = g?.First();
            // blade: UAlt'ı NULL, chassis'in U / yüzü kullanılır
            var ch = c?.ChassisId != null ? s.Chassisler.GetValueOrDefault(c.ChassisId) : null;
            bool blade = c != null && c.UAlt == null && ch != null;
            if (blade) c = c! with { UAlt = ch!.UAlt, UYukseklik = ch.UYukseklik, Yuz = c.Yuz ?? ch.Yuz, Chassis = c.Chassis ?? ch.Ad ?? ch.Id };
            return new
            {
                ad = c?.Ad ?? g!.Key, fqdn = h?.HostFqdn, seri = c?.Seri ?? h?.HostSeri, assetId = c?.AssetId, cluster = h?.Cluster, model = c?.Model ?? h?.HostModel,
                tip = c?.MalzemeTip, durum = c?.Operational, uAlt = c?.UAlt ?? h?.HostUAlt, uYukseklik = c?.UYukseklik ?? h?.HostUYukseklik, yuz = c?.Yuz ?? h?.HostYuz,
                chassis = c?.Chassis ?? h?.ChassisAdi, slot = c?.Slot ?? h?.BladeSlot, eslesme = h?.Eslesme, kaynak = h?.Kaynak,
                vm = g?.Count(v => v.Vm != null) ?? 0, uygulama = g?.SelectMany(v => v.Uygulamalar).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0,
                deger = g != null ? Deger(s, g.SelectMany(v => v.Uygulamalar)) : null,
                nlyte = c != null, vmBilgisi = g != null, tur = c?.Tur ?? "Server", chassisId = blade ? c!.ChassisId : null, blade,
                kaynakTablo = c?.Kaynak, bag = c?.AssetId != null ? s.BagIdx.GetValueOrDefault(c.AssetId)?.Count ?? 0 : 0,
                msy = MsyBul(c, h) is { } m ? new { m.DnsName, m.DnsIp, m.IloIp, m.Vendor, m.Model, m.PowerState, m.OsName, m.OsVersion, m.LastReboot } : null,
                turUyari = g != null && c != null && !c.Tur.Equals("Server", StringComparison.OrdinalIgnoreCase) ? $"nlyte'ta {c.Tur} olarak kayıtlı (VM'li ESX host'u — türü düzeltilmeli)"
                    : h?.Eslesme?.Contains("nlyte adı") == true ? $"nlyte'ta farklı adla kayıtlı ({h.Eslesme}) — VM envanterindeki adı {g!.Key}" : null,
            };
        }
        var hostlar = cihazlar.Select(c => Satir(c, cihazGrup.GetValueOrDefault(c))).Concat(eslesmeyen.Select(g => Satir(null, g))).ToList();
        dynamic[] dyn = hostlar.Cast<dynamic>().ToArray();
        int enUst = dyn.Where(h => h.uAlt != null).Select(h => (int)h.uAlt + Math.Max(1, (int?)h.uYukseklik ?? 1) - 1).DefaultIfEmpty(0).Max();
        int uKap = Math.Max(kb?.UYukseklik ?? ilk?.KabinU ?? vs.Select(v => v.KabinU).FirstOrDefault(x => x != null) ?? 42, enUst);
        string? usira = kb?.USirasi ?? ilk?.KabinUSirasi;
        var vmler = vs.Where(v => v.Vm != null || v.Ip != null).Select(v => new { host = v.Host ?? v.HostFqdn, v.Vm, v.Ip, dns = v.DnsName, v.Os, uygulamalar = v.Uygulamalar, muhafizlar = v.Muhafizlar, v.Kaynak })
            .OrderBy(v => v.host).ThenBy(v => v.Vm).ToList();
        // VM'li host'ların adı nlyte cihaz adıyla değiştiyse VM tablosu da o adla eşlenebilsin
        var hostAd = cihazGrup.ToDictionary(kv => kv.Value.Key, kv => kv.Key.Ad, StringComparer.OrdinalIgnoreCase);
        var pasif = tumCihaz.Where(c => !c.Aktif).Select(c => new { ad = c.Ad, tur = c.Tur, durum = c.Record, operasyon = c.Operational, model = c.Model, uAlt = c.UAlt }).ToList();
        return new
        {
            ad, oda = Parcala(ad).Oda ?? "Diğer", konum = KabinKonum(s, ad), cabinetAssetId = kb?.CabinetAssetId ?? ilk?.CabinetAssetId, uKapasite = uKap, uSirasi = usira, u1Ustte = U1Ustte(usira),
            bosU = kb?.BosU ?? vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), bitisikBosU = kb?.BitisikBosU,
            cihazSayisi = cihazlar.Count, vmHostSayisi = gruplar.Count, turlar = TurSayim(cihazlar, cihazGrup.Keys.ToHashSet()), nlyteEslesmeyen = eslesmeyen.Select(g => g.Key).ToList(),
            deger = Deger(s, vs.SelectMany(v => v.Uygulamalar)), degerAdlari = DegerAdlari, kabloVar = s.Baglantilar.Count > 0, kabloDurum = s.BaglantiDurum,
            disBaglanti = tumCihaz.Where(c => c.AssetId != null).SelectMany(c => s.BagIdx.GetValueOrDefault(c.AssetId!) ?? []).DistinctBy(b => b.CableId)
                .Select(b => tumCihaz.Any(c => string.Equals(c.AssetId, b.KaynakId, StringComparison.OrdinalIgnoreCase)) ? b.HedefKabin : b.KaynakKabin)
                .Where(k => k != null && !k.Equals(ad, StringComparison.OrdinalIgnoreCase)).GroupBy(k => k!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Select(g => new { kabin = g.Key, kablo = g.Count() }).ToList(),
            hostlar, vmler = vmler.Select(v => new { host = v.host != null && hostAd.TryGetValue(v.host, out var n) ? n : v.host, v.Vm, v.Ip, v.dns, v.Os, v.uygulamalar, v.muhafizlar, v.Kaynak,
                deger = Deger(s, v.uygulamalar) }).ToList(),
            uygulamalar = Uygulamalar(s, vs, ad, hostAd), pasif,
        };
    }

    // Kabinin uygulamaları + "kabin kapanırsa" etkisi: uygulamanın bu kabindeki VM'leri / tüm (ana DC) VM'leri → tamamı bu kabindeyse kritik
    object Uygulamalar(FizikselSnapshot s, List<FizVm> vs, string kabin, Dictionary<string, string>? hostAd = null)
    {
        var tumu = s.Vmler.Where(v => v.Kapsam == null || v.Kapsam == AnaKapsam).SelectMany(v => v.Uygulamalar.Select(a => (a, v)))
            .GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(x => x.v).ToList(), StringComparer.OrdinalIgnoreCase);
        return vs.SelectMany(v => v.Uygulamalar.Select(a => (a, v))).GroupBy(x => x.a, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var hepsi = tumu.GetValueOrDefault(g.Key) ?? [];
            int buKabin = g.Select(x => x.v.Vm ?? x.v.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int toplam = hepsi.Select(v => v.Vm ?? v.Ip).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var digerKabin = hepsi.Where(v => v.Kabin != null && !v.Kabin.Equals(kabin, StringComparison.OrdinalIgnoreCase)).Select(v => v.Kabin!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int kabinsiz = hepsi.Count(v => v.Kabin == null);
            string etki = digerKabin.Count == 0 && kabinsiz == 0 ? "tamamı bu kabinde" : digerKabin.Count == 0 ? "başka kabin yok (kabini bilinmeyen VM var)" : "başka kabinlerde de var";
            return new
            {
                ad = g.Key, deger = Deger(s, [g.Key]), vm = buKabin, toplamVm = Math.Max(toplam, buKabin), etki, digerKabinler = digerKabin.Take(10).ToList(), digerKabinSayisi = digerKabin.Count, kabinsizVm = kabinsiz,
                hostlar = g.Select(x => x.v.Host ?? x.v.HostFqdn).OfType<string>().Select(h => hostAd?.GetValueOrDefault(h) ?? h).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                muhafizlar = g.SelectMany(x => x.v.Muhafizlar).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }).OrderBy(x => x.etki == "tamamı bu kabinde" ? 0 : x.etki.StartsWith("başka kabin yok") ? 1 : 2).ThenByDescending(x => x.vm).ThenBy(x => x.ad).ToList();
    }

    public object Ara(FizikselSnapshot s, string q, int max = 50)
    {
        q = q.Trim();
        if (q.Length < 2) return new { q, sonuclar = Array.Empty<object>() };
        bool Has(string? x) => x != null && x.Contains(q, StringComparison.OrdinalIgnoreCase);
        var list = new List<(int Sira, string Tur, string Deger, FizVm V)>();
        foreach (var kad in s.TumKabinler.Where(k => Has(k) && !s.KabinVm.ContainsKey(k)))
            list.Add((4, "Kabin", kad, new FizVm(kad, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, [], [], null, AnaKapsam, null, null)));
        foreach (var v in s.Vmler)
        {
            if (v.Ip != null && (v.Ip == q || Has(v.Ip))) list.Add((v.Ip == q ? 0 : 1, "IP", v.Ip, v));
            else if (Has(v.Vm) || Has(v.DnsName)) list.Add((2, "VM", v.Vm ?? v.DnsName!, v));
            else if (Has(v.Host) || Has(v.HostFqdn)) list.Add((3, "Host", v.Host ?? v.HostFqdn!, v));
            else if (Has(v.Kabin)) list.Add((4, "Kabin", v.Kabin!, v));
            else if (v.Uygulamalar.FirstOrDefault(Has) is string a) list.Add((5, "Uygulama", a, v));
            else if (Has(v.Cluster)) list.Add((6, "Cluster", v.Cluster!, v));
        }
        // nlyte cihazları (VM'si olmayanlar da): ad / seri numarası
        foreach (var (kabin, cs) in s.KabinCihaz)
            foreach (var c in cs)
                if (c.Adlar.Any(Has) || Has(c.Seri))
                    list.Add((3, "Cihaz", c.Ad, new FizVm(kabin, null, null, null, c.UAlt, c.UYukseklik, c.Yuz, c.Model, c.Chassis, c.Slot, null, c.Ad, c.Seri, c.CabinetAssetId,
                        null, null, null, null, null, null, [], [], c.Tur, AnaKapsam, null, null)));
        // Host / kabin / uygulama / cluster eşleşmeleri tekilleşir (VM'leri tek tek listelenmez)
        var sonuc = list.GroupBy(x => x.Tur is "IP" or "VM" ? $"{x.Tur}|{x.Deger}|{x.V.Vm}" : $"{x.Tur}|{x.Deger}|{x.V.Kabin}", StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First(), N: g.Count())).OrderBy(x => x.Item1.Sira).ThenBy(x => x.Item1.Deger)
            .Take(max).Select(x => new
            {
                tur = x.Item1.Tur, deger = x.Item1.Deger, kabin = x.Item1.V.Kabin, oda = x.Item1.V.Kabin != null ? Parcala(x.Item1.V.Kabin).Oda ?? "Diğer" : null,
                host = x.Item1.V.Host ?? x.Item1.V.HostFqdn, vm = x.Item1.V.Vm, ip = x.Item1.V.Ip, kapsam = x.Item1.V.Kapsam, adet = x.N,
                cihazTuru = x.Item1.Tur == "Cihaz" ? x.Item1.V.Kaynak : null,
            }).ToList();
        var kabinler = sonuc.Select(x => x.kabin).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new { q, sonuclar = sonuc, kabinler, kesik = list.Count > max };
    }

    public object? Ip(FizikselSnapshot s, string ip)
    {
        if (!s.IpVm.TryGetValue(ip, out var vs)) return null;
        var v = vs.OrderByDescending(x => x.Kabin != null).First();
        return new
        {
            ip, kabin = v.Kabin, oda = v.Kabin != null ? Parcala(v.Kabin).Oda : null, konum = v.Konum, odaAdi = OdaAdi(v.Konum), host = v.Host ?? v.HostFqdn,
            cluster = v.Cluster, vm = v.Vm, kapsam = v.Kapsam, anaDc = v.Kapsam == null || v.Kapsam == AnaKapsam, uAlt = v.HostUAlt, uYukseklik = v.HostUYukseklik,
            yuz = v.HostYuz, chassis = v.ChassisAdi, eslesme = v.Eslesme,
            kabinDeger = v.Kabin != null ? Deger(s, (s.KabinVm.GetValueOrDefault(v.Kabin) ?? []).SelectMany(x => x.Uygulamalar)) : null,
        };
    }

    // Kabine bağlı olmayan aktif cihazlar (klima vb.; kabin kaydı bulunamayanlar)
    public object KabinDisi(FizikselSnapshot s, int max = 3000)
    {
        var l = s.KabinDisi.Where(c => c.Aktif).OrderBy(c => c.Tur).ThenBy(c => c.Ad).ToList();
        return new
        {
            toplam = l.Count, turlar = TurSayim(l), kesik = l.Count > max,
            kabinsiz = l.Count(c => c.CabinetAssetId == null && c.ChassisId == null),
            bilinmeyenKabinCihaz = l.Count(c => c.CabinetAssetId != null),
            // CabinetAssetID dolu ama NlyteKabin'de yok → kabin kaydı çekilmemiş olabilir
            kabinGruplari = l.Where(c => c.CabinetAssetId != null).GroupBy(c => c.CabinetAssetId!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Take(300).Select(g => new
                {
                    cabinetAssetId = g.Key, adet = g.Count(), turlar = TurSayim(g),
                    konum = g.Select(c => c.UniqueAssetName).OfType<string>().Select(u => Regex.Match(u, @"Room=(.+?)\)?\s*$")).Where(m => m.Success)
                        .Select(m => m.Groups[1].Value.Trim()).FirstOrDefault(),
                    ornek = g.Select(c => c.Ad).Take(4).ToList(),
                }).ToList(),
            cihazlar = l.Take(max).Select(c => new
            {
                ad = c.Ad, tur = c.Tur, model = c.Model, seri = c.Seri, assetId = c.AssetId, durum = c.Operational, cabinetAssetId = c.CabinetAssetId,
                konum = c.UniqueAssetName != null && Regex.Match(c.UniqueAssetName, @"Room=(.+?)\)?\s*$") is { Success: true } m ? m.Groups[1].Value.Trim() : null,
            }).ToList(),
        };
    }

    // Eşleşmeyen MSY / ÇSY host'ları: tüm ek eşleştirmelerden sonra hâlâ kabini bilinmeyen VM'lerin host'ları, neden + nlyte adayları
    public List<FizEslesmeyen> Eslesmeyen(FizikselSnapshot s) => s.EslesmeyenOnbellek ??= EslesmeyenHesapla(s);
    List<FizEslesmeyen> EslesmeyenHesapla(FizikselSnapshot s)
    {
        var tum = s.KabinCihaz.SelectMany(kv => kv.Value.Select(c => (C: c, Kabin: (string?)kv.Key))).Concat(s.KabinDisi.Select(c => (C: c, Kabin: (string?)null))).ToList();
        var adIdx = new Dictionary<string, List<(FizCihaz C, string? Kabin)>>(StringComparer.OrdinalIgnoreCase);
        var seriIdx = new Dictionary<string, List<(FizCihaz C, string? Kabin)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in tum)
        {
            foreach (var a in x.C.Adlar.Select(Norm).Distinct()) (adIdx.TryGetValue(a, out var l) ? l : adIdx[a] = []).Add(x);
            if (x.C.Seri != null) (seriIdx.TryGetValue(x.C.Seri.Trim(), out var l2) ? l2 : seriIdx[x.C.Seri.Trim()] = []).Add(x);
        }
        string Aday((FizCihaz C, string? Kabin) x) => $"{x.C.Ad} ({x.C.Tur}{(x.Kabin != null ? ", " + x.Kabin : ", kabin yok")}{(x.C.Aktif ? "" : ", " + x.C.Record)})";
        return s.Vmler.Where(v => v.Kabin == null).GroupBy(v => v.HostFqdn ?? v.Host ?? "", StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var v = g.First();
            string key = g.Key;
            var hlar = new[] { v.Host, v.HostFqdn }.OfType<string>().Select(Norm).Distinct().ToList();
            var msy = hlar.Select(h => s.MsyAd.GetValueOrDefault(h) ?? s.MsyIp.GetValueOrDefault(h)).FirstOrDefault(x => x != null);
            string? seri = v.HostSeri ?? msy?.Seri;
            var adAday = hlar.SelectMany(h => adIdx.GetValueOrDefault(h) ?? []).Distinct().ToList();
            var seriAday = seri != null ? seriIdx.GetValueOrDefault(seri.Trim()) ?? [] : [];
            // ad parça eşleşmesi (≥5 harf): nlyte adı host adını içeriyor ya da tersi — yalnız ipucu
            var kisa = hlar.FirstOrDefault(h => h.Length >= 5 && !IpBas.IsMatch(h));
            var parca = adAday.Count == 0 && kisa != null
                ? tum.Where(x => x.C.Adlar.Any(a => { var n = Norm(a); return n.Length >= 5 && (n.Contains(kisa) || kisa.Contains(n)); })).Take(5).ToList() : [];
            bool anaDisi = v.Kapsam != null && v.Kapsam != AnaKapsam && !v.Kapsam.StartsWith("Host ad", StringComparison.OrdinalIgnoreCase);
            string neden =
                key == "" ? "VM kaydında host yok"
                : anaDisi ? $"nlyte kapsamı dışı ({v.Kapsam})"
                : seriAday.Count > 0 && seriAday.All(x => x.Kabin == null) ? "nlyte'ta var (seri) ama kabine bağlı değil"
                : adAday.Count > 0 && adAday.All(x => x.Kabin == null) ? "nlyte'ta var (ad) ama kabine bağlı değil"
                : adAday.Count > 0 && adAday.Any(x => !x.C.Aktif) && !adAday.Any(x => x.C.Aktif && x.Kabin != null) ? "nlyte kaydı aktif değil"
                : adAday.Count > 1 || seriAday.Count > 1 ? "nlyte'ta birden çok aday"
                : IpBas.IsMatch(key) && msy == null ? "host alanında IP, MSY fiziksel envanterinde yok"
                : seri != null && seriAday.Count == 0 ? $"seri {seri} nlyte'ta yok"
                : parca.Count > 0 ? "ad tam tutmuyor, benzer ad var"
                : "nlyte'ta ad / seri bulunamadı";
            var adaylar = adAday.Concat(seriAday).Concat(parca).Distinct().Take(5).Select(Aday).ToList();
            int n = g.Count();
            return g.Select(x => new FizEslesmeyen(x.Vm, x.Ip, x.DnsName, x.Os, x.Uygulamalar, x.Kaynak, x.Kapsam, v.Host ?? key, v.HostFqdn, x.Cluster ?? v.Cluster,
                msy?.Model ?? v.HostModel, seri, msy?.IloIp, n, neden, adaylar));
        }).SelectMany(x => x).OrderByDescending(x => x.HostVmSayisi).ThenBy(x => x.Host).ThenBy(x => x.Vm).ToList();
    }

    public static string EslesmeyenCsv(IEnumerable<FizEslesmeyen> l)
    {
        static string Q(string? v) => CsvAlan(v);
        var sb = new StringBuilder("\uFEFFVM;IP;DNS;OS;UYGULAMALAR;KAYNAK;KAPSAM;HOST;HOST FQDN;CLUSTER;HOST MODEL;HOST SERİ;HOST ILO;HOST VM SAYISI;NEDEN;NLYTE ADAYLARI\r\n");
        foreach (var e in l)
            sb.Append(string.Join(";", Q(e.Vm), Q(e.Ip), Q(e.Dns), Q(e.Os), Q(string.Join(" | ", e.Uygulamalar)), Q(e.Kaynak), Q(e.Kapsam), Q(e.Host), Q(e.Fqdn), Q(e.Cluster),
                Q(e.HostModel), Q(e.Seri), Q(e.Ilo), e.HostVmSayisi, Q(e.Neden), Q(string.Join(" | ", e.Adaylar)))).Append("\r\n");
        return sb.ToString();
    }

    public IEnumerable<FizEksik> Eksik(FizikselSnapshot s) => s.Eksik.Where(e => e.Kapsam == null || e.Kapsam == AnaKapsam);

    // CSV alanı: satır sonu / sekme boşluğa (Excel'de satır ya da kolon kaymasın), her alan tırnak içinde
    static string CsvAlan(string? v) =>
        "\"" + Regex.Replace(v ?? "", @"[\r\n\t]+", " ").Trim().Replace("\"", "\"\"") + "\"";

    public static string EksikCsv(IEnumerable<FizEksik> l, IEnumerable<FizTurFarkli>? turFarkli = null)
    {
        static string Q(string? v) => CsvAlan(v);
        var sb = new StringBuilder("﻿HOST;FQDN;VM SAYISI;CLUSTER;KAPSAM;ADAY SAYISI;NOT\r\n");
        foreach (var e in l) sb.Append(string.Join(";", Q(e.Host), Q(e.Fqdn), e.VmSayisi, Q(e.Cluster), Q(e.Kapsam), e.AdaySayisi?.ToString() ?? "", Q(e.Not))).Append("\r\n");
        foreach (var t in turFarkli ?? [])
            sb.Append(string.Join(";", Q(t.Host), Q(t.Fqdn), t.VmSayisi, Q(t.Cluster), "", "",
                Q(t.Yontem == "ad"
                    ? $"nlyte'ta '{t.CihazAd}' adıyla {t.Tur} türünde kayıtlı (kabin {t.Kabin}{(t.UAlt != null ? ", U" + t.UAlt : "")}, AssetID {t.AssetId}) — türü Server olmalı"
                    : $"nlyte'ta farklı adla kayıtlı: '{t.CihazAd}' ({t.Tur}, kabin {t.Kabin}{(t.UAlt != null ? ", U" + t.UAlt : "")}, AssetID {t.AssetId}; {t.Yontem} ile bulundu) — nlyte adı düzeltilmeli"))).Append("\r\n");
        return sb.ToString();
    }
}
