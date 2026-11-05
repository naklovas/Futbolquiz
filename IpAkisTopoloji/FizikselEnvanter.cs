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
        snap.CihazTablosu = cihazTablo;
        return snap;
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
    public static (string? Oda, string? Sira, int? No) Parcala(string kabin)
    {
        var m = KabinKalip.Match(kabin);
        return m.Success ? (m.Groups[1].Value.ToUpperInvariant(), m.Groups[2].Value.ToUpperInvariant(), int.Parse(m.Groups[3].Value)) : (null, null, null);
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
            .OrderBy(x => x.ad == "Diğer").ThenBy(x => int.TryParse(x.ad.AsSpan(2), out int n) ? n : 999).ToList();
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
                bosU = s.Kabinler.GetValueOrDefault(k)?.BosU ?? vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), konum = KabinKonum(s, k),
            };
        }).OrderBy(k => k.sira).ThenBy(k => k.no).ThenBy(k => k.ad).ToList();
        return new { ad, kabinler };
    }

    // Kabinin konumu: KabinHostVm.Konum, yoksa nlyte UniqueAssetName / KabinUniqueAd içindeki "Room=…"
    static string? KabinKonum(FizikselSnapshot s, string kabin)
    {
        if (s.KabinVm.GetValueOrDefault(kabin)?.Select(v => v.Konum).FirstOrDefault(x => x != null) is string k) return k;
        foreach (var t in (s.KabinCihaz.GetValueOrDefault(kabin) ?? []).Select(c => c.UniqueAssetName).Prepend(s.Kabinler.GetValueOrDefault(kabin)?.UniqueAd))
            if (t != null && Regex.Match(t, @"Room=(.+?)\)?\s*$") is { Success: true } m) return m.Groups[1].Value.Trim();   // "ad (Room=…)" → dıştaki tek ')' atılır
        return null;
    }

    static string Norm(string x) => x.Trim().ToLowerInvariant().Split('.')[0];
    // host anahtarı: IP olduğu gibi (Norm "10.6.30.21"i "10"a indirirdi), ad kısa + küçük harf
    static string HostAnahtar(string x) => System.Net.IPAddress.TryParse(x.Trim(), out _) && x.Contains('.') ? x.Trim() : Norm(x);

    // Tür sayımı: [{tur, adet, vmli}] — en kalabalık önce
    static List<object> TurSayim(IEnumerable<FizCihaz> cs, ISet<FizCihaz>? vmli = null) =>
        cs.GroupBy(c => c.Tur, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
            .Select(g => (object)new { tur = g.Key, adet = g.Count(), vmli = vmli == null ? 0 : g.Count(vmli.Contains) }).ToList();

    public object? Kabin(FizikselSnapshot s, string ad)
    {
        var vs = s.KabinVm.GetValueOrDefault(ad) ?? [];
        var kb = s.Kabinler.GetValueOrDefault(ad);
        var tumCihaz = s.KabinCihaz.GetValueOrDefault(ad) ?? [];
        if (kb == null && vs.Count == 0) return null;
        var cihazlar = tumCihaz.Where(c => c.Aktif).ToList();
        var ilk = vs.FirstOrDefault();

        // VM'li host grupları; nlyte cihazına seri numarasıyla, olmazsa adla eşlenir
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
                nlyte = c != null, vmBilgisi = g != null, tur = c?.Tur ?? "Server", chassisId = blade ? c!.ChassisId : null, blade,
                kaynakTablo = c?.Kaynak,
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
            ad, konum = KabinKonum(s, ad), cabinetAssetId = kb?.CabinetAssetId ?? ilk?.CabinetAssetId, uKapasite = uKap, uSirasi = usira, u1Ustte = U1Ustte(usira),
            bosU = kb?.BosU ?? vs.Select(v => v.KabinBosU).FirstOrDefault(x => x != null), bitisikBosU = kb?.BitisikBosU,
            cihazSayisi = cihazlar.Count, vmHostSayisi = gruplar.Count, turlar = TurSayim(cihazlar, cihazGrup.Keys.ToHashSet()), nlyteEslesmeyen = eslesmeyen.Select(g => g.Key).ToList(),
            hostlar, vmler = vmler.Select(v => new { host = v.host != null && hostAd.TryGetValue(v.host, out var n) ? n : v.host, v.Vm, v.Ip, v.dns, v.Os, v.uygulamalar, v.muhafizlar, v.Kaynak }).ToList(),
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
                ad = g.Key, vm = buKabin, toplamVm = Math.Max(toplam, buKabin), etki, digerKabinler = digerKabin.Take(10).ToList(), digerKabinSayisi = digerKabin.Count, kabinsizVm = kabinsiz,
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

    public IEnumerable<FizEksik> Eksik(FizikselSnapshot s) => s.Eksik.Where(e => e.Kapsam == null || e.Kapsam == AnaKapsam);

    public static string EksikCsv(IEnumerable<FizEksik> l, IEnumerable<FizTurFarkli>? turFarkli = null)
    {
        static string Q(string? v) { v ??= ""; return v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }
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
