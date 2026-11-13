using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

// SERVİS İMZASI (kullanıcı: "Entropi'deki envanteri alalım, IP port karşılığında burada bulduklarımızı getirelim, bir tabloya basalım;
// boş olanlarda aynı servis çalışıyorsa karşısına tespit ettiğimiz uygulamayı basarız"; "exe – IIS uygulaması – ERT adı, bir yol bulmamız lazım").
// Tüm ERT (ERT_HOSTIPADDRESS) arka planda taranır: IP'ler parça parça Carbon Black (son Imza:CbSaat saat, port → exe) + IIS logu
// (son Imza:IisDakika dakika, port → IIS uygulamaları). Ham tarama gzip'li JSON olarak DF_SERVIS_IMZA'ya yazılır (sql/servis_imza.sql);
// eşleşme her açılışta ham veriden hesaplanır (kural değişince yeniden tarama gerekmez). ERT'ye hiçbir şey yazılmaz.
// İmza = exe (HTTP.sys / ntoskrnl portunda ERT WEBSITE / APPNAME adıyla yolu eşleşen asıl exe) + port + IIS uygulama kümesi.
// Sözlük: ERT'de adı dolu satırların imzası → ERT adı. Boş satır: aynı exe + port (+ IIS uygulamaları örtüşüyorsa güçlü), yalnız IIS kümesi (zayıf).

record ImzaPort(string Port, string Yol, long Baglanti, int Istemci);
record ImzaIis(string Port, List<string> Uygulamalar, long Istek);
// Genel: yalnız gereksiz (genel Windows / ajan) süreçlerin kabul ettiği portlar — exe olarak gösterilmez, sözlüğe girmez (kullanıcı: "gereksizleri almayalım, exe olarak da koymasın")
record ImzaSunucu(string Ip, List<ImzaPort> Gelen, List<string> Giden, List<ImzaIis> Iis, List<ImzaPort>? Genel = null);
record ImzaTarama(DateTime Baslangic, DateTime? Bitis, double CbSaat, double IisDakika, List<ImzaSunucu> Sunucular, List<string> Taranan,
    List<string> Mesajlar, int ToplamIp, string? Kaydeden, List<string>? Bekleyen = null, string? Kapsam = null);
// İlerleme (kullanıcı: "progress bar üste, hangi sunucu hangi port yazsın, yüzde kaç"): ToplamSunucu / BitenSunucu, Adim (cb | iis), Yuzde,
// Grup = şu an sorgulanan sunucular ("ip (ERT portları)"), Son = son biten sunucularda bulunan port → exe
record ImzaIs(string Durum, int Toplam, int Biten, string Asama, DateTime Baslangic, DateTime? Bitis, string? Hata, string? Kaydeden, int Sunucu,
    int ToplamSunucu = 0, int BitenSunucu = 0, string? Adim = null, double Yuzde = 0, List<string>? Grup = null, List<string>? Son = null, string? Kalan = null);
record ImzaAday(string Ad, int Sunucu);
// Durum = ERT tarafı (kayitli | bos | uyumsuz | ertyok); Veri = tarama tarafı (var | yok | taranmadi) — kullanıcı: tarama yokken "boş" süzgeci boş geliyordu
record ImzaSatir(string Ip, string Port, string? Sunucu, string? Ert, string? Website, string? Exe, string? ExeYol, bool HttpSys,
    long Baglanti, int Istemci, List<string> Iis, string Durum, string? Oneri, string? Guven, string? Not, List<ImzaAday>? Adaylar, bool Altyapi, string Veri, bool Genel = false,
    string? GenelExe = null, List<string>? Vip = null, string? Kurum = null, string? ExeKaynak = null);   // Vip: "havuz · VIP ip:port" (vip_envanteri, üye portu = satır portu)
record ImzaSozluk(string Exe, string Port, List<ImzaAday> Adlar, List<string> Iis, int Sunucu, bool Celiski);
// IIS sözlüğü (kullanıcı: "IIS uygulamaları için de sözlük olsa"): IIS uygulama adı → ERT adları (sunucu sayısı), görüldüğü portlar
record ImzaIisSozluk(string Uygulama, List<ImzaAday> Adlar, List<string> Portlar, int Sunucu, bool Celiski);
record ImzaSonuc(ImzaIs? Is, object? Kayit, List<ImzaSatir> Satirlar, List<ImzaSozluk> Sozluk, List<string> Mesajlar, string? Uyari, List<ImzaIisSozluk>? IisSozluk = null);

sealed class ServisImzaService(IConfiguration cfg, IHttpClientFactory factory, EnvanterService envanter, HttpSysKayitService httpsys, ILogger<ServisImzaService> log)
{
    static readonly System.Text.Json.JsonSerializerOptions Js = new(System.Text.Json.JsonSerializerDefaults.Web);
    readonly object _gate = new();
    ImzaIs? _is;
    CancellationTokenSource? _cts;
    ImzaTarama? _son;                       // son (bellekteki) tarama
    (ImzaTarama T, DateTimeOffset Env, ImzaSonuc S, DateTime At)? _hesap;   // hesap önbelleği (tarama + envanter sürümü)
    public string? Uyari { get; private set; }

    string Tablo => cfg["Imza:Tablo"] is { Length: > 0 } t && Regex.IsMatch(t, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*){0,2}$") ? t : "dbo.DF_SERVIS_IMZA";
    double CbSaat => Math.Clamp(cfg.GetValue("Imza:CbSaat", 72.0), 1, 24 * 14);
    double IisDakika => Math.Clamp(cfg.GetValue("Imza:IisDakika", 30.0), 5, 24 * 60);
    int Parca => Math.Clamp(cfg.GetValue("Imza:ParcaIp", 40), 1, 200);
    TimeSpan Sure => TimeSpan.FromSeconds(Math.Clamp(cfg.GetValue("Imza:SureSaniye", 120.0), 10, 900));
    int Dyn => cfg.GetValue("Eksik:DinamikPortBaslangic", 49152);

    public ImzaIs? Durum { get { lock (_gate) return _is; } }
    public void Durdur() { lock (_gate) _cts?.Cancel(); }

    // ---------------- Tarama ----------------
    // mod: yeni (tüm ERT, önceki tarama silinir) | devam (bekleyenler) | bos (adı boş portu olan sunucular) | kalan (hiç taranmamışlar) | ip (verilen IP'ler);
    // yeni dışındaki kapsamlar mevcut taramaya EKLENİR (aynı IP yeniden taranırsa verisi yenilenir) — kullanıcı: "tekil seçim yapamıyorum", "önce boş olanları yapacağım"
    public async Task<ImzaIs> BaslatAsync(string mod, List<string>? ipler, string user, CancellationToken ct, string? kurum = null)
    {
        lock (_gate) if (_is?.Durum == "calisiyor") throw new InvalidOperationException("Tarama zaten çalışıyor.");
        var env = await envanter.GetAsync(false, ct);
        var tum = Ipler(env).ToList();
        ImzaTarama? eski; lock (_gate) eski = _son;
        eski ??= await OkuAsync(ct);
        var t = (mod == "yeni" && string.IsNullOrWhiteSpace(kurum)) || eski == null ? new ImzaTarama(DateTime.Now, null, CbSaat, IisDakika, [], [], [], tum.Count, user) : eski with { ToplamIp = tum.Count, Kaydeden = user };
        var yapilan = t.Taranan.ToHashSet();
        List<string> liste = mod switch
        {
            "yeni" => tum,
            "devam" => t.Bekleyen ?? [],
            "kalan" => tum.Where(ip => !yapilan.Contains(ip)).ToList(),
            "bos" => tum.Where(ip => env.AppsOnIp(ip).Any(h => string.IsNullOrWhiteSpace(h.AppName) && !string.IsNullOrWhiteSpace(h.Port)
                && !(int.TryParse(h.Port, out int pn) && pn >= Dyn))).ToList(),
            "ip" => (ipler ?? []).Select(x => x.Trim()).Where(x => EnvanterSnapshot.TryToUInt(x, out _) && !Test(env, x)).Distinct().ToList(),
            _ => throw new InvalidOperationException("Geçersiz kapsam: " + mod)
        };
        // kurum seçiliyse (Banka / Katılım / İştirak / Diğer) yalnız o kurumun sunucuları; "yeni" kurumla verilirse önceki tarama silinmez, eklenir
        if (!string.IsNullOrWhiteSpace(kurum) && mod != "devam" && mod != "ip") liste = liste.Where(ip => Kurum(env, ip) == kurum).ToList();
        if (liste.Count == 0) throw new InvalidOperationException(mod == "devam" ? "Bekleyen sunucu yok." : "Taranacak sunucu yok.");
        liste = liste.OrderBy(ip => EnvanterSnapshot.TryToUInt(ip, out uint u) ? u : 0).ToList();
        string kapsam = mod switch { "yeni" => "tüm ERT", "bos" => "adı boş portu olan sunucular", "kalan" => "taranmamış sunucular", "ip" => $"{liste.Count} seçili sunucu", _ => t.Kapsam ?? "devam" };
        t = t with { Bekleyen = liste, Kapsam = kapsam, Bitis = null };
        var parcalar = liste.Chunk(Parca).ToList();
        var cts = new CancellationTokenSource();
        var is_ = new ImzaIs("calisiyor", parcalar.Count, 0, $"{kapsam}: {liste.Count} sunucu sırada", DateTime.Now, null, null, user, t.Sunucular.Count, liste.Count);
        lock (_gate) { _is = is_; _cts = cts; _son = t; _hesap = null; }
        string Etiket(string ip)
        {
            var ps = env.AppsOnIp(ip).Select(h => h.Port).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => int.TryParse(x, out int n) ? n : 999999).ToList();
            string ad = env.AppsOnIp(ip).Select(h => h.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
            return $"{ip}{(ad.Length > 0 ? " " + ad : "")} ({(ps.Count == 0 ? "ERT portu yok" : string.Join(", ", ps.Take(8)) + (ps.Count > 8 ? $" +{ps.Count - 8}" : ""))})";
        }
        _ = Task.Run(() => CalisAsync(t, parcalar, user, Etiket, cts.Token));
        return is_;
    }

    void Set(Func<ImzaIs, ImzaIs> f) { lock (_gate) if (_is != null) _is = f(_is); }

    async Task CalisAsync(ImzaTarama t, List<string[]> parcalar, string user, Func<string, string> etiket, CancellationToken ct)
    {
        var sunucular = t.Sunucular.ToDictionary(s => s.Ip);
        var taranan = t.Taranan.ToHashSet();
        var bekleyen = (t.Bekleyen ?? []).ToList();
        var mesaj = t.Mesajlar.ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ImzaTarama Anlik(DateTime? bitis) => t with { Sunucular = sunucular.Values.ToList(), Taranan = taranan.ToList(), Bekleyen = bekleyen.ToList(), Mesajlar = mesaj.Distinct().TakeLast(40).ToList(), Bitis = bitis };
        int toplamSunucu = parcalar.Sum(x => x.Length), bitenSunucu = 0;
        var sonlar = new List<string>();
        try
        {
            for (int i = 0; i < parcalar.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = parcalar[i];
                string? kalan = bitenSunucu > 0 ? Sure2(TimeSpan.FromSeconds(sw.Elapsed.TotalSeconds / bitenSunucu * (toplamSunucu - bitenSunucu))) : null;
                var grup = p.Select(etiket).ToList();
                double Yuzde(double adim) => toplamSunucu == 0 ? 100 : Math.Round((bitenSunucu + p.Length * adim) * 100.0 / toplamSunucu, 1);
                Set(x => x with { Biten = i, Asama = $"{i + 1} / {parcalar.Count}. grup · Carbon Black sorgulanıyor", Adim = "cb", Grup = grup, BitenSunucu = bitenSunucu, Yuzde = Yuzde(0), Kalan = kalan });
                var cb = await CbAsync(p, mesaj, ct);
                Set(x => x with { Asama = $"{i + 1} / {parcalar.Count}. grup · IIS logu sorgulanıyor", Adim = "iis", Yuzde = Yuzde(0.5) });
                var iis = await IisAsync(p, mesaj, ct);
                foreach (var ip in p)
                {
                    var gelen = cb.Gelen.GetValueOrDefault(ip) ?? [];
                    var giden = cb.Giden.GetValueOrDefault(ip) ?? [];
                    var ii = iis.GetValueOrDefault(ip) ?? [];
                    var gn = cb.Genel.GetValueOrDefault(ip);
                    if (gelen.Count + giden.Count + ii.Count + (gn?.Count ?? 0) > 0) sunucular[ip] = new ImzaSunucu(ip, gelen, giden, ii, gn);
                    else sunucular.Remove(ip);
                    taranan.Add(ip);
                    // son biten sunucular: port → exe (genel / çekirdek dahil, kısa), IIS portları
                    var pe = gelen.Where(x => x.Port != "0" && !Gereksiz(x.Yol)).GroupBy(x => x.Port).OrderBy(g => int.TryParse(g.Key, out int n) ? n : 999999)
                        .Select(g => { var iu = ii.FirstOrDefault(x => x.Port == g.Key); string e = ExeAd(g.OrderByDescending(x => x.Baglanti).First().Yol);
                            return $"{g.Key} {(HttpSysEslesme.Cekirdek(e) ? "HTTP.sys" : e)}{(iu != null ? $" (IIS: {string.Join(", ", iu.Uygulamalar.Take(3))})" : "")}"; }).ToList();
                    foreach (var x in ii) if (!pe.Any(q => q.StartsWith(x.Port + " "))) pe.Add($"{x.Port} IIS: {string.Join(", ", x.Uygulamalar.Take(3))}");
                    sonlar.Insert(0, $"{ip} → {(pe.Count == 0 ? "veri yok" : string.Join(" · ", pe.Take(6)) + (pe.Count > 6 ? $" +{pe.Count - 6}" : ""))}");
                }
                if (sonlar.Count > 12) sonlar.RemoveRange(12, sonlar.Count - 12);
                bitenSunucu += p.Length;
                var bitti = p.ToHashSet(); bekleyen.RemoveAll(bitti.Contains);
                Set(x => x with { Biten = i + 1, Sunucu = sunucular.Count, BitenSunucu = bitenSunucu, Yuzde = toplamSunucu == 0 ? 100 : Math.Round(bitenSunucu * 100.0 / toplamSunucu, 1), Son = sonlar.ToList() });
                lock (_gate) { _son = Anlik(null); _hesap = null; }
                if ((i + 1) % 10 == 0) await YazAsync(Anlik(null), user);   // uzun işte ara kayıt (uygulama yeniden başlarsa "devam et")
            }
            var son = Anlik(DateTime.Now);
            lock (_gate) { _son = son; _hesap = null; }
            await YazAsync(son, user);
            Set(x => x with { Durum = "bitti", Biten = x.Toplam, Asama = $"Bitti · {sunucular.Count} sunucuda veri", Bitis = DateTime.Now, Yuzde = 100, Grup = null });
        }
        catch (OperationCanceledException)
        {
            await YazAsync(Anlik(null), user);
            Set(x => x with { Durum = "durduruldu", Asama = "Durduruldu (kaldığı yerden devam edilebilir)", Bitis = DateTime.Now });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Servis imzası taraması hata verdi");
            await YazAsync(Anlik(null), user);
            Set(x => x with { Durum = "hata", Hata = ex.Message, Asama = "Hata", Bitis = DateTime.Now });
        }
    }

    // GEREKSİZ EXE'LER (kullanıcı: "gereksiz exe listesini sen hazırla, gereksizleri almayalım, exe olarak da koymasın"):
    // HttpSysEslesme.Genel (svchost, lsass… + \windows\system32\ / syswow64 / ajan klasörleri; w3wp ve ntoskrnl hariç) + aşağıdaki ajan / araç listesi
    // + Imza:GenelExeler. ntoskrnl / System (HTTP.sys) ve w3wp (IIS) asla gereksiz sayılmaz.
    static readonly HashSet<string> GereksizAd = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows
        "system", "registry", "lsaiso.exe", "fontdrvhost.exe", "sihost.exe", "ctfmon.exe", "searchindexer.exe", "searchprotocolhost.exe", "searchfilterhost.exe",
        "taskhost.exe", "taskeng.exe", "dllhost.exe", "msdtc.exe", "vds.exe", "vssvc.exe", "trustedinstaller.exe", "tiworker.exe", "wuauclt.exe", "musnotification.exe",
        "sppsvc.exe", "smartscreen.exe", "securityhealthservice.exe", "rundll32.exe", "regsvr32.exe", "mmc.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wsmprovhost.exe",
        "winrshost.exe", "mstsc.exe", "tasklist.exe", "net.exe", "net1.exe", "ping.exe", "nslookup.exe", "certutil.exe", "w32tm.exe", "dfsrs.exe", "dns.exe", "ismserv.exe",
        "msiexec.exe", "mpcmdrun.exe", "nissrv.exe", "mssense.exe", "sensecncproxy.exe", "senseir.exe", "msmpeng.exe", "mpdefendercoreservice.exe",
        // Carbon Black / VMware Carbon Black
        "cb.exe", "repmgr.exe", "repux.exe", "repwsc.exe", "cbcomms.exe", "cbdefense.exe", "parity.exe",
        // Trend Micro / Apex One / Deep Security
        "ntrtscan.exe", "tmlisten.exe", "pccntmon.exe", "tmbmsrv.exe", "tmccsf.exe", "tmpfw.exe", "coreserviceshell.exe", "coreframeworkhost.exe", "ds_agent.exe",
        "dsa.exe", "amsp_logserver.exe", "tmwscsvc.exe",
        // Symantec / McAfee / Trellix / Sophos / CrowdStrike / SentinelOne
        "ccsvchst.exe", "smc.exe", "smcgui.exe", "sepwscsvc64.exe", "mcshield.exe", "masvc.exe", "macmnsvc.exe", "mfemms.exe", "mfeesp.exe", "xagt.exe",
        "sophosfs.exe", "savservice.exe", "csfalconservice.exe", "sentinelagent.exe",
        // Splunk / Elastic / Fluent
        "splunkd.exe", "splunk-winevtlog.exe", "splunk-perfmon.exe", "splunk-admon.exe", "splunk-netmon.exe", "splunk-powershell.exe", "splunk-regmon.exe",
        "splunk-monitornohandle.exe", "splunk-winhostinfo.exe", "splunk-winprintmon.exe", "btool.exe", "winlogbeat.exe", "filebeat.exe", "metricbeat.exe", "fluent-bit.exe",
        // İzleme: SCOM, SCCM, Zabbix, Nagios / NSClient, SiteScope, SolarWinds, PRTG, Dynatrace, AppDynamics, Datadog, New Relic
        "healthservice.exe", "monitoringhost.exe", "ccmexec.exe", "ccmrepairagent.exe", "cmrcservice.exe", "zabbix_agentd.exe", "zabbix_agent2.exe", "nscp.exe",
        "sitescope.exe", "swiagent.exe", "solarwinds.agent.service.exe", "prtg probe.exe", "oneagentwatchdog.exe", "oneagentos.exe", "oneagenthelper.exe",
        "appdynamics.agent.coordinator.exe", "datadog-agent.exe", "trace-agent.exe", "process-agent.exe", "newrelic-infra.exe",
        // Yedekleme: Veeam, Commvault, NetBackup, TSM / Spectrum Protect, Networker, Data Protector
        "veeam.backup.service.exe", "veeamagent.exe", "veeamtransportsvc.exe", "veeamdeploymentsvc.exe", "cvd.exe", "clmgrs.exe", "evmgrc.exe", "bpinetd.exe", "bpcd.exe",
        "vnetd.exe", "pbx_exchange.exe", "dsmcad.exe", "dsmc.exe", "dsmagent.exe", "nsrexecd.exe", "omnicheck.exe", "omniinet.exe",
        // Envanter / zafiyet / yönetim ajanları: Tanium, Qualys, Nessus, Ivanti, ManageEngine, BigFix, Lansweeper, Rapid7
        "taniumclient.exe", "qualysagent.exe", "nessus-agent-module.exe", "nessusd.exe", "nessus-service.exe", "ivanti.exe", "dcagentservice.exe", "besclient.exe",
        "lsagent.exe", "ir_agent.exe",
        // PAM / erişim / uzak destek: CyberArk, BeyondTrust, Kron, TeamViewer, AnyDesk, VNC
        "cyberark.exe", "pmservice.exe", "bomgar-scc.exe", "kron.exe", "teamviewer_service.exe", "teamviewer.exe", "anydesk.exe", "winvnc.exe", "tvnserver.exe",
        // DLP / veri güvenliği / DB denetim ajanları (kullanıcı: dsasql.exe, sqldsimservice.exe)
        "dsasql.exe", "sqldsimservice.exe", "fppsvc.exe", "wepsvc.exe", "dgagent.exe", "edpa.exe", "dlpagent.exe", "guardium_stap.exe", "stap.exe",
        // Sanallaştırma araçları
        "vmtoolsd.exe", "vm3dservice.exe", "vgauthservice.exe", "vmwaretray.exe", "xenservice.exe", "vmms.exe", "vmcompute.exe",
        // Microsoft ajanları
        "omsagent.exe", "azuremonitoragent.exe", "mmaagent.exe", "wmiapsrv.exe", "wmiadap.exe"
    };
    static readonly string[] GereksizYol = [@"eeam", @"\commvault", @"eritas
etbackup", @"	ivoli	sm", @"\legato
sr", @"\emc networker", @"	anium",
        @"\qualys", @"	enable", @"\dynatrace", @"ppdynamics", @"\datadog", @"
ew relic", @"\solarwinds", @"\system center operations manager",
        @"\microsoft monitoring agent", @"\ccm", @"\symantec", @"\mcafee", @"	rellix", @"\sophos", @"\crowdstrike", @"\sentinelone", @"\cyberark",
        @"eyondtrust", @"	eamviewer", @"nydesk", @"orcepoint", @"\websense", @"\elasticeats", @"ilebeat", @"\winlogbeat", @"
xlog"];
    HashSet<string>? _ek;
    HashSet<string> Ek => _ek ??= cfg.GetSection("Imza:GenelExeler").GetChildren().Select(c => c.Value?.Trim().ToLowerInvariant()).OfType<string>().Where(x => x.Length > 0).ToHashSet();
    bool Gereksiz(string yol)
    {
        if (HttpSysEslesme.Cekirdek(yol)) return false;   // HTTP.sys (ntoskrnl / System) gerekli
        string ad = ExeAd(yol);
        if (ad == "w3wp.exe") return false;               // IIS uygulama havuzu
        return HttpSysEslesme.Genel(ad, yol) || GereksizAd.Contains(ad) || Ek.Contains(ad)
            || GereksizYol.Any(g => yol.Contains(g, StringComparison.OrdinalIgnoreCase));
    }

    // TEST SUNUCULARI (kullanıcı: "domainden ayrıştırabiliyoruz, testlere bakmasın boşuna"): segment Domain'inde Eksik:TestIfadeleri (varsayılan "test")
    // geçen sunucular taranmaz, listelenmez, sözlüğe girmez (ERT eksikleriyle aynı kural)
    bool Test(EnvanterSnapshot env, string ip)
    {
        var words = cfg.GetSection("Eksik:TestIfadeleri").GetChildren().Select(c => c.Value).OfType<string>().Where(w => w.Length > 0).ToList();
        if (words.Count == 0) words = ["test"];
        string? d = env.FindSegment(ip)?.Domain;
        return d != null && words.Any(w => d.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
    IEnumerable<string> Ipler(EnvanterSnapshot env) => env.HostIps.Where(ip => EnvanterSnapshot.TryToUInt(ip, out _) && !Test(env, ip)).Distinct();

    // KURUM (kullanıcı: "zb ile başlayanlar banka, zi ile başlayanlar iştirak, zk ile başlayan katılım bankası"; "sunucu adına göre değil, yine domain bilgisi"):
    // segment Domain'inin (KonsolideSegmentler4.Domain) önekinden;
    // Imza:Kurumlar {"Banka": ["zb"], …} ile değişir; eşleşmeyen "Diğer"
    Dictionary<string, string[]>? _kurumlar;
    Dictionary<string, string[]> Kurumlar => _kurumlar ??= cfg.GetSection("Imza:Kurumlar").GetChildren()
        .Select(c => (c.Key, c.GetChildren().Select(x => x.Value).OfType<string>().Where(x => x.Length > 0).Select(x => x.ToLowerInvariant()).ToArray()))
        .Where(x => x.Item2.Length > 0).ToDictionary(x => x.Key, x => x.Item2) is { Count: > 0 } d ? d
        : new() { ["Ziraat Bankası"] = ["zb"], ["Ziraat Katılım"] = ["zk"], ["İştirak"] = ["zi"] };
    public IEnumerable<string> KurumAdlari => Kurumlar.Keys.Append("Diğer");
    string Kurum(EnvanterSnapshot env, string ip)
    {
        string ad = (env.FindSegment(ip)?.Domain ?? "").Trim().ToLowerInvariant();
        foreach (var (k, onekler) in Kurumlar) if (onekler.Any(o => ad.StartsWith(o))) return k;
        return "Diğer";
    }

    static string Sure2(TimeSpan t) => t.TotalMinutes < 1 ? "1 dk'dan az" : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} dk" : $"{(int)t.TotalHours} sa {t.Minutes} dk";
    static string Terimler(IEnumerable<string> ips) => "(" + string.Join(" OR ", ips.Select(ip => $"TERM({ip})")) + ")";
    static string Liste(IEnumerable<string> ips) => string.Join(",", ips.Select(ip => $"\"{ip}\""));

    async Task<(Dictionary<string, List<ImzaPort>> Gelen, Dictionary<string, List<string>> Giden, Dictionary<string, List<ImzaPort>> Genel)> CbAsync(string[] ips, List<string> mesaj, CancellationToken ct)
    {
        var s = cfg.GetSection("Splunk");
        string index = s["Index"] ?? "carbonblack", sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
        string spl = $"""
            search index={index} sourcetype="{sourcetype}" {Terimler(ips)} local_ip IN ({Liste(ips)})
            | eval yon=if(lower(direction)=="outbound", "out", "in"), yol=coalesce(process_path, process_name, "-"), lp=if(yon=="in", coalesce(local_port, "0"), "-")
            | stats count dc(remote_ip) as ist by local_ip yon lp yol
            """;
        var gelen = new Dictionary<string, List<ImzaPort>>(); var giden = new Dictionary<string, List<string>>(); var genel = new Dictionary<string, List<ImzaPort>>();
        var end = DateTimeOffset.Now;
        try
        {
            var res = await SplunkService.ExportAsync(spl, new LookupQuery(ips[0], end.AddHours(-CbSaat), end), cfg, factory.CreateClient("splunk"), ct, "Splunk", Sure);
            if (res.Messages.Any(m => m.Contains("sınırında") || m.Contains("cevap vermedi"))) mesaj.Add($"Carbon Black {ips[0]} … grubunda süre sınırı (sonuç kısmi)");
            foreach (var r in res.Rows)
            {
                string ip = r.GetValueOrDefault("local_ip") ?? "", yol = r.GetValueOrDefault("yol") ?? "-", lp = r.GetValueOrDefault("lp") ?? "-";
                if (ip.Length == 0 || yol == "-") continue;
                long.TryParse(r.GetValueOrDefault("count"), out long n); int.TryParse(r.GetValueOrDefault("ist"), out int ist);
                bool gr = Gereksiz(yol);
                if (r.GetValueOrDefault("yon") == "out") { if (!gr) (giden.TryGetValue(ip, out var l) ? l : giden[ip] = []).Add(yol); continue; }
                var hedef = gr ? genel : gelen;
                (hedef.TryGetValue(ip, out var g) ? g : hedef[ip] = []).Add(new ImzaPort(lp == "" ? "0" : lp, yol, n, ist));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add($"Carbon Black {ips[0]} … grubu: {ex.Message}"); }
        foreach (var k in giden.Keys.ToList()) giden[k] = giden[k].Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return (gelen, giden, genel);
    }

    async Task<Dictionary<string, List<ImzaIis>>> IisAsync(string[] ips, List<string> mesaj, CancellationToken ct)
    {
        var idx = cfg.GetSection("HttpSys:IisIndexleri").Get<string[]>() is { Length: > 0 } a ? a : ["iis_msy", "iis_csy"];
        if (idx.Any(x => !Regex.IsMatch(x, @"^[A-Za-z0-9_\-\*]+$"))) return [];
        string spl = $"""
            search ({string.Join(" OR ", idx.Select(x => "index=" + x))}) {Terimler(ips)}
            | fields _raw
            | rex "^\S+ \S+ (?<iis_sip>\S+) \S+ (?<iis_uri>\S+) \S+ (?<iis_port>\d+) \S+ (?<iis_cip>\S+)"
            | search iis_sip IN ({Liste(ips)})
            | eval iis_seg=split(lower(iis_uri), "/")
            | eval iis_seg=mvfilter(NOT match(iis_seg, "^(|api|apis|v\d+(\.\d+)*|rest|services?|ws|odata|public|internal)$"))
            | eval iis_app=mvindex(iis_seg, 0), iis_app=if(isnull(iis_app) OR iis_app="", "/", iis_app)
            | stats count dc(iis_cip) as aist by iis_sip iis_port iis_app
            """;
        var sonuc = new Dictionary<string, List<ImzaIis>>();
        var end = DateTimeOffset.Now;
        try
        {
            var res = await SplunkService.ExportAsync(spl, new LookupQuery(ips[0], end.AddMinutes(-IisDakika), end), cfg, factory.CreateClient("splunk"), ct, "Splunk", Sure);
            if (res.Messages.Any(m => m.Contains("sınırında") || m.Contains("cevap vermedi"))) mesaj.Add($"IIS logu {ips[0]} … grubunda süre sınırı (sonuç kısmi)");
            var satir = res.Rows.Select(r => (Ip: r.GetValueOrDefault("iis_sip") ?? "", Port: r.GetValueOrDefault("iis_port") ?? "", App: r.GetValueOrDefault("iis_app") ?? "/",
                N: long.TryParse(r.GetValueOrDefault("count"), out long n) ? n : 0, AIst: int.TryParse(r.GetValueOrDefault("aist"), out int ai) ? ai : 0))
                .Where(x => x.Ip.Length > 0 && x.Port.Length > 0).ToList();
            foreach (var gi in satir.GroupBy(x => x.Ip))
            {
                // gürültü: tarama yolları + sunucunun portlarının yarısından çoğunda görünen, en çok 2 istemcili adlar (IP kartıyla aynı kural)
                int portSay = gi.Select(x => x.Port).Distinct().Count();
                var yaygin = gi.GroupBy(x => x.App).Where(g => portSay >= 3 && g.Select(x => x.Port).Distinct().Count() * 2 > portSay && g.Max(x => x.AIst) <= 2).Select(g => g.Key).ToHashSet();
                sonuc[gi.Key] = gi.GroupBy(x => x.Port).Select(gp =>
                {
                    var u = HttpSysEslesme.IisUyg(gp.Select(x => (x.App, "", x.N, x.AIst, (string?)null)), a => yaygin.Contains(a) || HttpSysEslesme.IisTarama(a), out _);
                    return new ImzaIis(gp.Key, u.Where(x => x.Ad != "/").Select(x => x.Ad).Take(15).ToList(), gp.Sum(x => x.N));
                }).Where(x => x.Uygulamalar.Count > 0).ToList();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { mesaj.Add($"IIS logu {ips[0]} … grubu: {ex.Message}"); }
        return sonuc;
    }

    // ---------------- Kayıt (gzip'li JSON, tek satır) ----------------
    static byte[] Sikistir(ImzaTarama t)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) System.Text.Json.JsonSerializer.Serialize(gz, t, Js);
        return ms.ToArray();
    }
    static ImzaTarama? Ac(byte[] b)
    {
        using var gz = new System.IO.Compression.GZipStream(new MemoryStream(b), System.IO.Compression.CompressionMode.Decompress);
        return System.Text.Json.JsonSerializer.Deserialize<ImzaTarama>(gz, Js);
    }
    async Task YazAsync(ImzaTarama t, string user)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"""
                MERGE {Tablo} AS t USING (SELECT @a AS AD) AS s ON t.AD = s.AD
                WHEN MATCHED THEN UPDATE SET SONUC = @s, SUNUCU = @n, TAMAM = @b, TARIH = SYSDATETIME(), KAYDEDEN = @u
                WHEN NOT MATCHED THEN INSERT (AD, SONUC, SUNUCU, TAMAM, TARIH, KAYDEDEN) VALUES (@a, @s, @n, @b, SYSDATETIME(), @u);
                """, conn);
            cmd.Parameters.AddWithValue("@a", "TUM");
            cmd.Parameters.Add("@s", System.Data.SqlDbType.VarBinary, -1).Value = Sikistir(t);
            cmd.Parameters.AddWithValue("@n", t.Sunucular.Count); cmd.Parameters.AddWithValue("@b", t.Bitis != null); cmd.Parameters.AddWithValue("@u", user);
            await cmd.ExecuteNonQueryAsync();
            Uyari = null;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Uyari = $"Kayıt tablosu ({Tablo}) yok; tarama yalnız bellekte tutuluyor (sql/servis_imza.sql)."; }
        catch (Exception ex) { log.LogWarning(ex, "Servis imzası taraması kaydedilemedi"); Uyari = "Tarama kaydedilemedi: " + ex.Message; }
    }
    async Task<ImzaTarama?> OkuAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cfg.GetConnectionString("Envanter"));
            await conn.OpenAsync(ct);
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand($"SELECT SONUC FROM {Tablo} WHERE AD = 'TUM'", conn);
            if (await cmd.ExecuteScalarAsync(ct) is byte[] b && Ac(b) is { } t) { lock (_gate) _son ??= t; return t; }
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 208) { Uyari = $"Kayıt tablosu ({Tablo}) yok; tarama yalnız bellekte tutuluyor (sql/servis_imza.sql)."; }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Servis imzası kaydı okunamadı"); }
        return null;
    }

    // ---------------- Eşleşme ----------------
    public async Task<ImzaSonuc> SonucAsync(CancellationToken ct)
    {
        ImzaTarama? t; lock (_gate) t = _son;
        t ??= await OkuAsync(ct);
        var env = await envanter.GetAsync(false, ct);
        lock (_gate) if (_hesap is { } h && ReferenceEquals(h.T, t) && h.Env == env.LoadedAt && DateTime.UtcNow - h.At < TimeSpan.FromMinutes(10)) return h.S with { Is = _is, Uyari = Uyari };
        var tahmin = await httpsys.TumTahminlerAsync(ct);
        var s = Hesapla(t, env, tahmin);
        lock (_gate) if (t != null) _hesap = (t, env.LoadedAt, s, DateTime.UtcNow);
        return s with { Is = Durum, Uyari = Uyari };
    }

    static string Norm(string? x) => Regex.Replace((x ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
    static string ExeAd(string yol) => yol.Replace('/', '\\').Split('\\').Last().Trim().ToLowerInvariant();
    static readonly HashSet<string> GenelKelime = ["exe", "dll", "app", "core", "web", "api", "service", "services", "zb", "zk", "zt"];
    static List<string> Kelime(string? x) => Regex.Split((x ?? "").ToLowerInvariant(), "[^a-z0-9]+").Where(w => w.Length >= 2 && !GenelKelime.Contains(w)).Distinct().ToList();
    static double Jaccard(ICollection<string> a, ICollection<string> b) =>
        a.Count == 0 || b.Count == 0 ? 0 : (double)a.Intersect(b).Count() / a.Union(b).Count();

    // HTTP.sys portu: ERT'deki WEBSITE / APPNAME / SERVICENAME adı bu sunucudaki tek bir iş exe'sinin yolunda geçiyorsa asıl exe odur (IP kartı httpSysApp ile aynı)
    static string? HttpSysExe(IEnumerable<string?> adlar, List<string> yollar)
    {
        var nm = adlar.Where(x => Norm(x).Length >= 4).Distinct().ToList();
        foreach (var a in nm)
        {
            var hit = yollar.Where(y => Norm(y).Contains(Norm(a))).Select(ExeAd).Distinct().ToList();
            if (hit.Count == 1) return yollar.First(y => ExeAd(y) == hit[0]);
        }
        foreach (var a in nm)
        {
            var ws = Kelime(a); if (ws.Count < 2) continue;
            var hit = yollar.Where(y => { var ew = Kelime(y).ToHashSet(); int n = ws.Count(ew.Contains); return n >= 2 && n >= ws.Count * 2.0 / 3; }).Select(ExeAd).Distinct().ToList();
            if (hit.Count == 1) return yollar.First(y => ExeAd(y) == hit[0]);
        }
        return null;
    }

    ImzaSonuc Hesapla(ImzaTarama? t, EnvanterSnapshot env, Dictionary<string, (string Yol, string Kaynak)>? tahmin = null)
    {
        var infra = AppTopology.InfraPorts(cfg);
        var veri = (t?.Sunucular ?? []).ToDictionary(s => s.Ip);
        var taranan = (t?.Taranan ?? []).ToHashSet();
        var satirlar = new List<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)>();
        var sunucuExe = new Dictionary<string, HashSet<string>>();   // sunucuda çalışan iş exe'leri (HTTP.sys portunu sözlükteki exe'ye bağlamak için)
        bool Sistem(string? a) => a != null && a.StartsWith("Sistem Portudur", StringComparison.OrdinalIgnoreCase);

        foreach (var ip in Ipler(env))
        {
            var ert = env.AppsOnIp(ip).Where(h => !(int.TryParse(h.Port, out int pn) && pn >= Dyn)).GroupBy(h => h.Port ?? "").ToDictionary(g => g.Key, g => g.ToList());
            veri.TryGetValue(ip, out var v);
            // eski kayıtlarda gereksizler Gelen / Giden içinde olabilir: burada da ayrılır
            var gelenTum = (v?.Gelen ?? []).Concat(v?.Genel ?? []).ToList();
            var gelenIs = gelenTum.Where(x => !Gereksiz(x.Yol)).ToList();
            var gelenGenel = gelenTum.Where(x => Gereksiz(x.Yol)).ToList();
            var yollar = gelenIs.Select(x => x.Yol).Concat((v?.Giden ?? []).Where(y => !Gereksiz(y))).Where(y => !HttpSysEslesme.Cekirdek(y))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            sunucuExe[ip] = yollar.Select(ExeAd).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var portlar = ert.Keys.Where(p => p.Length > 0).ToList();
            foreach (var p in gelenTum.Select(x => x.Port))
                if (p != "0" && !(int.TryParse(p, out int pn) && pn >= Dyn) && !portlar.Contains(p)) portlar.Add(p);
            foreach (var iisP in v?.Iis.Select(x => x.Port) ?? []) if (!portlar.Contains(iisP)) portlar.Add(iisP);
            var uyelik = env.MemberOf(ip);
            List<string>? Vip(string ip0, string port0)
            {
                var l = uyelik.Where(m => m.Port == port0).Select(m => $"{Regex.Replace(m.Pool ?? "", "^/Common/", "", RegexOptions.IgnoreCase)} · {m.LbIp}{(string.IsNullOrEmpty(m.LbPort) ? "" : ":" + m.LbPort)}")
                    .Distinct().Take(6).ToList();
                return l.Count > 0 ? l : null;
            }
            string? sunucuAd = ert.Values.SelectMany(x => x).Select(h => h.UnixName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            string kurum = Kurum(env, ip);
            foreach (var port in portlar)
            {
                var rows = ert.GetValueOrDefault(port) ?? [];
                var adlar = rows.Select(h => h.AppName).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                string? website = rows.Select(h => h.Website).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                var gel = gelenIs.Where(x => x.Port == port).OrderByDescending(x => x.Baglanti).ToList();
                var gen = gelenGenel.Where(x => x.Port == port).OrderByDescending(x => x.Baglanti).ToList();
                var is_ = gel.Where(x => !HttpSysEslesme.Cekirdek(x.Yol)).ToList();
                string? yol = is_.FirstOrDefault()?.Yol; bool hs = false; string? kaynakAd = yol != null ? "Carbon Black" : null;
                if (yol == null && gel.Count > 0)
                {
                    // HTTP.sys portu: sırayla kesin kayıt (netsh) → ERT adı exe yolunda → kayıtlı zaman korelasyonu; hiçbiri yoksa ntoskrnl kalır
                    hs = true;
                    (string Yol, string Kaynak)? tk = tahmin != null && tahmin.TryGetValue(ip + "|" + port, out var tv) && !string.IsNullOrEmpty(tv.Yol) ? tv : null;
                    if (tk is { } t1 && t1.Kaynak.StartsWith("HTTP.sys kesin") && !Gereksiz(t1.Yol)) { yol = t1.Yol; kaynakAd = t1.Kaynak; }
                    if (yol == null && HttpSysExe(rows.SelectMany(h => new[] { h.Website, h.AppName, h.ServiceName }), yollar) is { } y2) { yol = y2; kaynakAd = "ERT adı exe yolunda"; }
                    if (yol == null && tk is { } t2 && !Gereksiz(t2.Yol)) { yol = t2.Yol; kaynakAd = t2.Kaynak; }
                    yol ??= gel[0].Yol;   // çözülemedi: ntoskrnl kalır
                }
                var iis = v?.Iis.FirstOrDefault(x => x.Port == port)?.Uygulamalar ?? [];
                string? exe = yol == null ? null : HttpSysEslesme.Cekirdek(yol) ? "ntoskrnl.exe" : ExeAd(yol);
                bool genel = exe == null && gen.Count > 0;   // portu yalnız gereksiz süreç kabul ediyor
                string? genelExe = gen.Count > 0 ? string.Join(", ", gen.Select(x => ExeAd(x.Yol)).Distinct().Take(3)) : null;
                bool alt = infra.Contains(port);
                // çözülemeyen HTTP.sys portu: IIS uygulamaları varsa "HTTP.sys|port" imzası (eşleşmede IIS kümesi şart)
                string? anahtar = genel || alt || exe == null ? null : exe == "ntoskrnl.exe" ? (iis.Count > 0 ? "http.sys|" + port : null) : exe + "|" + port;
                string durum = rows.Count == 0 ? "ertyok" : adlar.Count > 0 ? "kayitli" : "bos";
                string veriDurum = exe != null || iis.Count > 0 || gen.Count > 0 ? "var" : taranan.Contains(ip) ? "yok" : "taranmadi";
                var s = new ImzaSatir(ip, port, sunucuAd, adlar.Count > 0 ? string.Join(" | ", adlar) : null, website, exe, yol, hs && exe != "ntoskrnl.exe",
                    gel.Sum(x => x.Baglanti), gel.Count > 0 ? gel.Max(x => x.Istemci) : 0, iis, durum, null, null, null, null, alt, veriDurum, genel, genelExe,
                    Vip(ip, port), kurum, exe == "ntoskrnl.exe" ? (iis.Count > 0 ? "HTTP.sys · exe bulunamadı, IIS uygulamalarıyla eşleşir" : "HTTP.sys · exe bulunamadı") : kaynakAd);
                satirlar.Add((s, iis.ToHashSet(StringComparer.OrdinalIgnoreCase), anahtar));
            }
        }

        // SÖZLÜK: ERT'de tek ve sistem dışı adı olan satırlar
        var kaynak = satirlar.Where(x => x.S.Durum == "kayitli" && !x.S.Ert!.Contains(" | ") && !Sistem(x.S.Ert) && !x.S.Altyapi).ToList();
        var exeSoz = kaynak.Where(x => x.Anahtar != null).GroupBy(x => x.Anahtar!).ToDictionary(g => g.Key, g => g.ToList());
        var iisSoz = kaynak.Where(x => x.IisSet.Count > 0).ToList();
        static List<ImzaAday> Say(IEnumerable<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)> l) =>
            l.GroupBy(x => x.S.Ert!, StringComparer.OrdinalIgnoreCase).Select(g => new ImzaAday(g.Key, g.Select(x => x.S.Ip).Distinct().Count()))
                .OrderByDescending(a => a.Sunucu).ToList();

        // VIP HAVUZU (kullanıcı: "aynı şekilde vip havuzu da kapsanacak mı"): aynı LB havuzunun (VIP ip:port + havuz adı) diğer üyelerinin aynı üye portundaki
        // ERT adı — en güçlü kanıt, tarama gerektirmez (envanterden)
        var havuz = new Dictionary<string, List<(string Ip, string Port)>>(StringComparer.OrdinalIgnoreCase);
        string HKey(VipMember m) => $"{m.LbIp}|{m.LbPort}|{m.Pool}";
        foreach (var ip in Ipler(env))
            foreach (var m in env.MemberOf(ip))
                if (!string.IsNullOrEmpty(m.Port)) (havuz.TryGetValue(HKey(m), out var hl) ? hl : havuz[HKey(m)] = []).Add((ip, m.Port!));
        List<string> ErtAdlari(string ip, string port) => env.AppsOnIp(ip).Where(h => h.Port == port).Select(h => h.AppName?.Trim()).OfType<string>()
            .Where(a => a.Length > 0 && !Sistem(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        (List<ImzaAday> Ad, string Havuz)? VipAdlari(string ip, string port)
        {
            foreach (var m in env.MemberOf(ip).Where(m => m.Port == port))
            {
                if (!havuz.TryGetValue(HKey(m), out var uy)) continue;
                var ad = uy.Where(u => u.Ip != ip).SelectMany(u => ErtAdlari(u.Ip, u.Port).Take(1).Select(a => (A: a, u.Ip)))
                    .GroupBy(x => x.A, StringComparer.OrdinalIgnoreCase).Select(g => new ImzaAday(g.Key, g.Select(x => x.Ip).Distinct().Count()))
                    .OrderByDescending(x => x.Sunucu).ToList();
                if (ad.Count > 0) return (ad, $"{Regex.Replace(m.Pool ?? "", "^/Common/", "", RegexOptions.IgnoreCase)} · {m.LbIp}{(string.IsNullOrEmpty(m.LbPort) ? "" : ":" + m.LbPort)}");
            }
            return null;
        }

        var sonuc = new List<ImzaSatir>(satirlar.Count);
        foreach (var (s, iisSet, anahtar) in satirlar)
        {
            if (!s.Altyapi && VipAdlari(s.Ip, s.Port) is { } va)
            {
                var en0 = va.Ad[0]; int top0 = va.Ad.Sum(a => a.Sunucu);
                bool kar0 = va.Ad.Count > 1 && en0.Sunucu < top0 * 0.6;
                string not0 = $"aynı VIP havuzu ({va.Havuz}) → diğer üyelerden {en0.Sunucu} sunucuda '{en0.Ad}'" + (va.Ad.Count > 1 ? $" ({va.Ad.Count} farklı ad{(kar0 ? " — net çoğunluk yok" : "")})" : "");
                if (s.Durum is "bos" or "ertyok")
                {
                    sonuc.Add(s with { Oneri = en0.Ad, Guven = kar0 ? "orta" : "güçlü", Adaylar = va.Ad.Take(8).ToList(), Not = not0 });
                    continue;
                }
                if (s.Durum == "kayitli" && !kar0 && !va.Ad.Any(a => s.Ert!.Split(" | ").Any(e => string.Equals(a.Ad, e, StringComparison.OrdinalIgnoreCase))))
                {
                    sonuc.Add(s with { Durum = "uyumsuz", Oneri = en0.Ad, Guven = "güçlü", Adaylar = va.Ad.Take(8).ToList(), Not = not0 + " — bu sunucuda farklı kayıtlı" });
                    continue;
                }
            }
            if (s.Altyapi || s.Veri != "var") { sonuc.Add(s); continue; }
            List<(ImzaSatir S, HashSet<string> IisSet, string? Anahtar)> c = [];
            string? guven = null, nasil = null;
            if (anahtar != null && exeSoz.TryGetValue(anahtar, out var ec))
            {
                c = ec.Where(x => x.S.Ip != s.Ip).ToList();
                if (anahtar.StartsWith("http.sys|"))
                {
                    c = c.Where(x => Jaccard(iisSet, x.IisSet) >= 0.5).ToList();
                    if (c.Count > 0) { guven = "orta"; nasil = $"HTTP.sys:{s.Port} + IIS uygulamaları ortak ({string.Join(", ", iisSet.Take(3))})"; }
                }
                else if (c.Count > 0 && iisSet.Count > 0)
                {
                    var g = c.Where(x => Jaccard(iisSet, x.IisSet) >= 0.5).ToList();
                    if (g.Count > 0) { c = g; guven = "güçlü"; nasil = $"{s.Exe}:{s.Port} + IIS uygulamaları ortak"; }
                }
                if (c.Count > 0 && guven == null) { guven = "orta"; nasil = $"aynı exe + port ({s.Exe}:{s.Port})"; }
            }
            if (c.Count == 0 && s.Exe == "ntoskrnl.exe" && sunucuExe.TryGetValue(s.Ip, out var se))
            {
                // kullanıcı örneği: boş satırda ERT adı yok → exe çözülmedi, ama sunucuda zb.app.wall.exe çalışıyor ve başka sunucularda zb.app.wall.exe:9000 "Wall"
                var es = se.Where(e => exeSoz.ContainsKey(e + "|" + s.Port)).ToList();
                if (es.Count == 1)
                {
                    c = exeSoz[es[0] + "|" + s.Port].Where(x => x.S.Ip != s.Ip).ToList();
                    if (c.Count > 0)
                    {
                        bool iisOrtak = iisSet.Count > 0 && c.Any(x => Jaccard(iisSet, x.IisSet) >= 0.5);
                        guven = iisOrtak ? "güçlü" : "orta";
                        nasil = $"HTTP.sys:{s.Port}; sunucuda {es[0]} çalışıyor ve başka sunucularda {es[0]}:{s.Port} kayıtlı{(iisOrtak ? " + IIS uygulamaları ortak" : "")}";
                    }
                }
            }
            if (c.Count == 0 && iisSet.Count > 0 && s.Durum != "kayitli")
            {
                c = iisSoz.Where(x => x.S.Ip != s.Ip && Jaccard(iisSet, x.IisSet) >= 0.6).ToList();
                if (c.Count > 0) { guven = "zayıf"; nasil = "yalnız IIS uygulamaları ortak (" + string.Join(", ", iisSet.Take(4)) + ")"; }
            }
            if (c.Count == 0) { sonuc.Add(s); continue; }
            var ad = Say(c);
            int top = ad.Sum(a => a.Sunucu);
            var en = ad[0];
            bool karisik = ad.Count > 1 && en.Sunucu < top * 0.6;
            if (s.Durum == "kayitli")
            {
                // ERT dolu ama aynı servis başka sunucularda farklı adla (≥ 2 sunucu, açık çoğunluk) → uyumsuzluk
                if (!ad.Any(a => string.Equals(a.Ad, s.Ert, StringComparison.OrdinalIgnoreCase)) && en.Sunucu >= 2 && !karisik)
                    sonuc.Add(s with { Durum = "uyumsuz", Oneri = en.Ad, Guven = guven, Not = $"{nasil}: başka {en.Sunucu} sunucuda '{en.Ad}' olarak kayıtlı", Adaylar = ad.Take(8).ToList() });
                else sonuc.Add(s);
                continue;
            }
            if (karisik && guven == "güçlü") guven = "orta"; else if (karisik) guven = "zayıf";
            // exe yalnız kayıtlı zaman korelasyonundan geliyorsa (kesin değil) bir kademe düşük
            if (s.ExeKaynak?.Contains("korelasyon") == true) { guven = guven == "güçlü" ? "orta" : "zayıf"; nasil += " · exe zaman korelasyonu tahmini"; }
            sonuc.Add(s with
            {
                Oneri = en.Ad, Guven = guven, Adaylar = ad.Take(8).ToList(),
                Not = $"{nasil} → {en.Sunucu} sunucuda '{en.Ad}'" + (ad.Count > 1 ? $" (toplam {top} sunucu, {ad.Count} farklı ad{(karisik ? " — net çoğunluk yok" : "")})" : "")
            });
        }

        var sozluk = exeSoz.Select(kv =>
        {
            var ad = Say(kv.Value);
            var iis = kv.Value.SelectMany(x => x.IisSet).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(8).ToList();
            var p = kv.Key.Split('|');
            return new ImzaSozluk(p[0] == "http.sys" ? "HTTP.sys (exe bilinmiyor, IIS'le)" : p[0], p[1], ad, iis, ad.Sum(a => a.Sunucu), ad.Count > 1);
        }).OrderByDescending(x => x.Sunucu).ThenBy(x => x.Exe).Take(5000).ToList();

        // IIS SÖZLÜĞÜ: ERT'de adı dolu satırların IIS uygulamaları → ERT adı
        var iisSozluk = kaynak.SelectMany(x => x.IisSet.Select(u => (U: u, x.S))).GroupBy(x => x.U, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var ad = g.GroupBy(x => x.S.Ert!, StringComparer.OrdinalIgnoreCase).Select(a => new ImzaAday(a.Key, a.Select(x => x.S.Ip).Distinct().Count())).OrderByDescending(a => a.Sunucu).ToList();
            return new ImzaIisSozluk(g.Key, ad, g.Select(x => x.S.Port).Distinct().OrderBy(p => int.TryParse(p, out int n) ? n : 999999).ToList(), g.Select(x => x.S.Ip).Distinct().Count(), ad.Count > 1);
        }).OrderByDescending(x => x.Sunucu).ThenBy(x => x.Uygulama).Take(5000).ToList();
        var kayit = (object)new
        {
            tarama = t != null, baslangic = t?.Baslangic, bitis = t?.Bitis, cbSaat = t?.CbSaat ?? CbSaat, iisDakika = t?.IisDakika ?? IisDakika, taranan = t?.Taranan.Count ?? 0,
            toplamIp = Ipler(env).Count(), kurumlar = KurumAdlari.Select(k => new { ad = k, sunucu = Ipler(env).Count(ip => Kurum(env, ip) == k),
                bos = Ipler(env).Count(ip => Kurum(env, ip) == k && env.AppsOnIp(ip).Any(h => string.IsNullOrWhiteSpace(h.AppName) && !string.IsNullOrWhiteSpace(h.Port) && !(int.TryParse(h.Port, out int pn) && pn >= Dyn))) }).ToList(), testSunucu = env.HostIps.Count(ip => EnvanterSnapshot.TryToUInt(ip, out _) && Test(env, ip)),
            sunucu = t?.Sunucular.Count ?? 0, kaydeden = t?.Kaydeden, bekleyen = t?.Bekleyen?.Count ?? 0, kapsam = t?.Kapsam,
            bosSunucu = Ipler(env).Count(ip => env.AppsOnIp(ip).Any(h => string.IsNullOrWhiteSpace(h.AppName) && !string.IsNullOrWhiteSpace(h.Port) && !(int.TryParse(h.Port, out int pn) && pn >= Dyn)))
        };
        return new ImzaSonuc(null, kayit, sonuc.OrderBy(x => EnvanterSnapshot.TryToUInt(x.Ip, out uint u) ? u : 0)
            .ThenBy(x => int.TryParse(x.Port, out int p) ? p : 999999).ToList(), sozluk, t?.Mesajlar ?? [], null, iisSozluk);
    }
}
