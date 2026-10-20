# DeltaFlow — proje bağlamı (yeni oturumlar için)

Bu dosya önceki geliştirme oturumunun özetidir. Yeni bir oturumda önce bunu oku.
Kullanıcıyla **Türkçe** konuşulur; arayüz metinleri ve commit mesajları Türkçedir.

## Ne yapar
Kurum içi (bankacılık) bir ağ akışı ve bağımlılık aracı. Bir IP ya da uygulama için Splunk/Carbon Black ve
Riverbed AppResponse trafiğini çeker, SQL Server envanteriyle (segment, uygulama, VIP) zenginleştirir,
topoloji çizer ve şirket içi AI'a etki analizi yaptırır. Canlıda IIS'te, Windows Auth ile çalışıyor.

## Konum ve yapı
- Repo `naklovas/Futbolquiz`, dal `claude/kod-analizi-aciklama-1vwqfv`, klasör `IpAkisTopoloji/`
  (repo adı ilgisiz; proje ayrı repoya taşınabilir). `TrafikPaketi/TrafikServisi.cs` başka projeye verilen
  taşınabilir trafik servisi (tek dosya).
- .NET 9 minimal API (`net9.0`), paketler: `Microsoft.Data.SqlClient`, `Microsoft.AspNetCore.Authentication.Negotiate`.
- Arayüz: `wwwroot/topoloji.html` (IP görünümü + ortak yardımcılar, inline script) ve `wwwroot/uygulama.js`
  (uygulama görünümü, topoloji.html'deki global yardımcıları kullanır). Dış CDN/font yok (iç ağ).
  `wwwroot/deltaflow.svg` logo. Kök adres `/` topoloji.html'i açar.

| Dosya | İçerik |
|---|---|
| `Program.cs` | DI, auth middleware, endpoint'ler, SplunkService, AppResponseService (QueryAnyAsync = tüm kutular), LookupQuery |
| `Topoloji.cs` | EnvanterService/EnvanterSnapshot (SQL, bellekte cache), FlowBuilder, Hop2Builder, VipResolver, SystemPorts |
| `Uygulama.cs` | AppTopology (uygulama görünümü), EnvFilter (ODM/TEST) |
| `AiEtki.cs` | AI etki analizi prompt'u ve OpenAI uyumlu çağrı |
| `Firewall.cs` | Palo Alto trafik logu sorgusu (Splunk) |
| `Surec.cs` | Sunucu içi akış: gelen → süreç → giden (Carbon Black) |
| `AkisTesti.cs` | AppResponse ham bağlantı testi (zaman kapsaması uygun mu) |
| `Oturum.cs` | Oturum akışı: aynı oturumda gelen → giden (AppResponse, zaman kapsaması) |
| `Yolculuk.cs` | Uçtan uca yolculuk: dış IP → … → sorgulanan → … → DB |
| `Erisim.cs` | Erişim sorgula: kaynak IP → hedef IP (port) izin/engel, son görülme (Firewall + Carbon Black) |
| `Opinnate.cs` | Opinnate firewall kural kontrolü (rule-checker/topology): tanımlı kurala göre erişim, yol üzerindeki cihazlar |
| `Islev.cs` | İşlev haritası: etiketli sunucuların AI ile işlev kümelemesi (ERT AI_KUME / AI_DETAY), SM / MSY / ÇSY kanıtları |
| `KumeTopoloji.cs` | Küme topolojisi: işlev kümeleri kutu, içinde VIP / VIP'siz sunucu elemanları, aralarındaki trafik |
| `wwwroot/kurum.js` | Kurum (segment Domain) çoklu seçimi: işlev haritası ve küme topolojisi süzme / gruplama |
| `Etiket.cs` | Sunucu etiketleme: ERT TAG kolonu okuma/yazma, alt etiket (ANA/ALT), etiket açıklamaları |
| `Eksik.cs` | ERT eksikleri: APPNAME boş (IP, port) satırlarına AI uygulama önerisi (`wwwroot/eksik.html`, ayrı sayfa) |
| `Versiyon.cs` | İşlev haritası + küme topolojisi versiyonları (etiket başına son 3) |
| `F5Waf.cs` | F5 ASM / Bot Defense güvenlik logları (Splunk index=f5): erişimde engel kararı, IP görünümünde URI listesi |
| `PortBilgisi.cs` | Port → işlev ve kesilirse duracak iş (AI prompt'u için) |
| `Yetki.cs` | İşlem yetkileri (yazma, topoloji, dışa aktarma, toplu tahmin) — DokuPanel site kayıtları |
| `AuthHelper.cs` | Kullanıcının verdiği DokuPanel yetki helper'ı (namespace `AppRel`), anahtar config'den |

## Veri kaynakları
- Uygulama görünümü "Yalnızca segmentler" kutucuğu (`segmentLinks`, uygulama.js): kullananlar / bağımlılıklar karşı IP'lerin
  segmentine göre tek kutu; içinde o segmentteki VIP'ler (`VIP ip:port · uygulama`) ve uygulama adları; hedef bağları hit payıyla dağıtılır.
  Varsayılan açık; ortadaki uygulama kutusunda da VIP'ler kendi segment kutusuna girer (VIP'LER bölümü yok, kutuda adıyla listelenir).
- **Splunk / Carbon Black**: `/services/search/v2/jobs/export`, SPL `index=carbonblack sourcetype="bit9:carbonblack:json" TERM(ip)`
  (çoklu IP: `(TERM(a) OR TERM(b))`), `direction`'a göre istemci/sunucu, `stats` by client_ip server_ip server_port Protocol.
- **AppResponse**: token → `npm.reports` instance (L4 `flow_tcp` + L7 `wtapages`, STEELFILTER `cli_tcp.ip == X or srv_tcp.ip == X`)
  → bekle → veri → sil. `appliance=-1` = tüm kutular paralel (arayüzde varsayılan "Tüm kutular"). VifgIds destekli.
- **Firewall (Palo Alto)**: `Firewall.cs`, aynı Splunk bağlantısı (`SplunkService.ExportAsync`), `index=fw_paloalto TERM(ip)`,
  stats by src→dest:dest_port + action; `fw_denied` (izin değerleri `Firewall:IzinDegerleri`), kural/cihaz/App-ID.
  Alan adları `Firewall:Alanlar`. FlowEdge: FirewallHits/FirewallDenied/FwRules/FwDevices/FwApps; Hits = CB + AR + FW.
  Arayüzde "Firewall" kaynak kutucuğu, tabloda Firewall sütunu (⛔ engel), yalnızca engellenen akış kırmızı kesikli;
  tamamen engellenen bağlantılar AI'a gönderilmez. Sonraki adım (planlandı): "Geriye iz" ve "Oturum izi" (zaman kapsama).
- **Sunucu içi akış** (`Surec.cs`, `/api/surec`, IP görünümünde topolojinin altı): Carbon Black'te sorgulanan sunucunun
  kendi olayları (`local_ip = IP`) süreç + PID bazında: gelen (dinlediği port) → süreç → giden. Süreç bazında bağ
  (istek bazında değil); PID ayrı düğüm (IIS havuzları karışmasın), PID alanı `Splunk:ProcessIdField` (varsayılan process_pid).
  LB GW'den gelen → hangi VIP:port. Altyapı portları varsayılan gizli; süreç tıklanınca yalnız o süreç.
- **Aynı oturumda gelen → giden** (istek bazında) isteniyor. Ağ verisinde ortak istek kimliği yok; AppInternals (APM)
  kurumda var ama erişim zayıf. Yol: AppResponse bağlantı başlangıç/bitiş (ms) ile zaman kapsaması. Önce ölçüm:
  `/api/appresponse/akis-testi?ip=&appliance=` (`AkisTesti.cs`, en fazla 15 dk, tek kutu): çözünürlük, gelen bağlantı
  süreleri (keep-alive?), aynı anda açık sayısı. Kolonlar `AkisTesti:Kolonlar` (varsayılan start_time,end_time,cli_tcp.ip/port,srv_tcp.ip/port).
  `AppResponseService.ConnectAsync` / `RunSingleAsync` ortak yardımcılar.
- **Oturum akışı** (`Oturum.cs`, `/api/oturum`; topoloji araç çubuğundaki "⇄ Oturum akışı" düğmesi tam ekran modal açar,
  yalnızca Kapat ile kapanır (arka plan / Esc kapatmaz); grafik alanında −, Sığdır, +, Genişlik ve Ctrl+tekerlek (`flowZoom`,
  ölçek sekme başına `state.flowZoom`); veri açılınca çekilir; kutular uygulama bazında, IP:port listesi kutunun içinde; uygulaması yoksa segmente göre): AppResponse'tan
  bağlantılar tek tek (başlangıç/bitiş ms) çekilir (en fazla son `Oturum:MaxDakika`=15 dk; tüm kutular seçiliyse en çok
  bağlantı gören kutu). Kısa gelen bağlantının (≤ `Oturum:MaxOturumSaniye`=30) penceresinde başlayan giden bağlantılar o
  oturuma bağlanır; aynı anda k oturum açıksa ağırlık 1/k (k=1 "kesin"). Çıktı: giriş (karşı IP:port) → hedef (ağırlık,
  kesin, oturumların yüzde kaçında). Üstte güven: aynı anda ort. ≤1.5 güvenilir. "Süreçler (Carbon Black)" ikinci sekme.
  Firewall da kaynak: `FirewallService.SessionsAsync` (izinli oturumlar tek tek, s = _time - duration, saniye; alanlar
  SrcPort/Duration). Aynı istemci IP:port→sunucu IP:port AppResponse'ta varsa FW kaydı atlanır; bir giden bağlantı hem
  ms'lik hem saniyelik oturuma düşerse yalnız ms'lik sayılır (kaba pencere yanlış bağ üretmesin).
- **Uçtan uca yolculuk** (`Yolculuk.cs`, `/api/yolculuk`, modalın varsayılan sekmesi): sorgulanan sunucudan geriye
  (dış IP'lere) ve ileriye (DB'ye) durak durak `OturumAkisi.Match`. İleri: önceki duraktan gelen oturumlarda gidilen
  yerler (VIP → üyeler, üyede LB GW'den gelen oturumlar). Geri: sonraki durağa giden oturumların kaynakları (LB GW →
  VIP → VIP'e gelenler). Özel olmayan IP = dış, orada durur. Seviye başına tek veri çekimi (AR tüm kutular birleşik +
  FW, 5-tuple tekil). `Yolculuk:Dakika`=5, `Geri`/`Ileri`=3, `Dallanma`=6. Firewall oturumlarında NAT: sunucu =
  dest_translated_ip (varsa). Arayüzde sütun = durak, kutu = uygulama (VIP ayrı, dış IP'ler tek kutu), üstüne gelince yol.
  Hedef oranı paydası = en az bir yere giden oturumlar (health check / izleme seyreltmesin); sıralama oran > ağırlık.
  VIP `Ileri` durak sayısını tüketmez (Item.Hops). DB'ye havuz bağlantıları zaman eşleşmesine girmediği için ileri
  yönde `Yolculuk:HavuzPortlari` (1521, 1433, 5432…) portlarına giden eşleşmemiş bağlantılar `Havuz=true` bağ olarak
  eklenir (kesik çizgi, uç durak).
  Ortak hizmetler (`Yolculuk:OrtakHizmetler`: SiteScope, Redis, Splunk, Carbon Black — uygulama adında geçerse):
  JNode.Ortak dolu, Dallanma'ya sayılmaz, devam edilmez; arayüzde sütunlar dışında altta hizmet başına tek kutu,
  çizgi çekilmez, üstüne gelince bağlı kutular yanar (vurgu ortak kutudan geçip yayılmaz).
  "Dış" = özel olmayan IP ve envanterde uygulaması / segmenti (/8+) / LB GW kaydı yok (kurum içi genel bloklar iç sayılır).
  Geri yönde de VIP durak saymaz (Hops); seviye başına en çok `Yolculuk:SeviyeMax`=60 düğüm.
  "Dıştan içe" modu (`/api/yolculuk?dis=true`, sekmedeki "Dıştan içe" düğmesi): geri `DisGeri`=6 durak, `DisDallanma`=15,
  dış kaynaklar öne, ortak hizmetler atlanır; sonunda geri tarafta yalnızca bir dış IP'den başlayan yollar bırakılır
  (bulunamazsa açıklayıcı mesaj).
  İleri yönde VIP → üye: önce üyeye LB GW'den gelen oturumlar; yoksa VIP'i çağıranın kendi IP'sinden (LB SNAT yapmıyor),
  yoksa üyenin VIP portuna gelen tüm oturumlar; hiçbiri yoksa VIP → üye bağı `Envanter=true` (gri noktalı, üyeden devam yok).
- **Erişim sorgula** (sekme, `wwwroot/erisim.js`, `/api/erisim?src=&dst=&port=&start=&end=&sources=`): Firewall
  `FirewallService.AccessAsync` (src = kaynak, dest ya da dest_translated_ip = hedef; port + action bazında sayı, ilk/son,
  kural, cihaz, App-ID, `session_end_reason` (`Firewall:Alanlar:Reason`), NAT; ayrıca son 20 olay) + Carbon Black
  (client→server bağlantısı, süreç, son) + envanter (segment, uygulama, VIP üyeliği). Karar: son izin ≥ son engel → "Erişim var";
  izin sonra engel → "Son durumda engelleniyor"; yalnız engel → "Engelleniyor"; yalnız CB → firewall'dan geçmiyor; hiçbiri → "Kayıt yok"
  (aynı segment notu). İki IP aynı segmentte (aynı CIDR) ve firewall kaydı yoksa karar "Aynı VLAN — firewall gerekmez" (mavi);
  firewall kaydı varsa normal karar + aynı segment notu. Aralık en fazla `Erisim:MaxGun`=30 gün; sekmeye geçince aralık 1 günden kısaysa son 7 gün yapılır.
  Derin bağlantı `?src=&dst=&port=`.
- **Dataskope (KALDIRILDI)**: Infraskope DB audit API entegrasyonu ve "DB'ye gelenler" sekmesi kullanıcı isteğiyle tamamen
  kaldırıldı (Dataskope.cs, DbGelen.cs, dbgelen.js, /api/dbgelen, Erişim'deki kutucuk ve DB bölümü, appsettings bölümü). Geri istenirse git geçmişinde.
- **F5 (WAF)** (`F5Waf.cs`, Splunk `index=f5`, sourcetype f5:bigip:syslog): yalnızca güvenlik olayları loglanıyor
  (15 dk'da ~83 bin olay; ~13 bin ASM: request_status illegal(+action alarm = geçti)/challenged/legal/bot_signature;
  ~12 bin Bot Defense/DoS: enforcement_action block/none, durum `req_status`; ~58 bin bilinmeyen türde) ve birkaç internete
  açık VIP. Alanlar: ip_client (yoksa client_ip, XFF), dest_ip/dest_port (VIP), vs_name, uri, method, hostname, support_id,
  attack_type, violations, enforced_by, resp_code. Sonuç sınıfı: engellendi / doğrulama / ihlal (geçti) / bot imzası / geçti.
  Erişim sorgula'da "F5 WAF" kutucuğu: kaynak → hedef VIP olayları; engel varsa karar "… — ama F5 WAF engelliyor" (support ID ile).
  IP görünümünde `/api/f5uri`: sorgulanan IP + üyesi olduğu VIP'ler için URI + metot bazında istek/istemci/sonuç (kayıt yoksa
  tek satır "kayıt yok" bilgisi). F5 aramalarında TERM() yok, `dest_ip="x"` alan filtresi (ham satırda ip:port olabilir). Çok değerli Splunk alanları `|||` ile birleştirilir (URL'deki virgül bozulmasın). AppResponse L7 kapalı kalıyor.
- **Opinnate** (`Opinnate.cs`, Erişim sorgula'da "Opinnate (kural)" kutucuğu): trafikten bağımsız, tanımlı kurala göre erişim.
  Swagger'da anahtar alanı yok, kullanıcı girişiyle (oturum) yetkilendiriyor. Giriş: `Username` doluysa her zaman
  `POST {BaseUrl}/login {username,password}` (Token yalnız Username boşsa); `opinnate` HttpClient'ında UseCookies=false, Set-Cookie
  elle taşınır, csrftoken/XSRF-TOKEN çerezi varsa X-CSRFToken/X-XSRF-TOKEN + Referer eklenir; → yanıtta token/access_token/… (iç içe
  aranır) ya da oturum çerezi; 30 dk önbellek, 401/403'te bir kez yeniden. Başlık: `TokenHeader`/`TokenPrefix` verilmişse yalnız o;
  verilmemişse 401/403'te sırayla Authorization Bearer, x-api-key, düz Authorization, X-Auth-Token, token, apikey denenir, çalışan hatırlanır
  (canlıda ilk deneme `{"message":"Forbidden"}` 403 verdi — API gateway tarzı).
  Port boşken "any" SORULMAZ (hep sondaki CLEANUP-DROP any/any/any deny döner — canlıda görüldü): firewall + CB'de görülen portlar
  (en çok `MaxPort`=5) ayrı ayrı sorgulanır (`CheckManyAsync`), port başına izin/engel; hiç port yoksa "port girin" (`PortYok`).
  any/any/any deny = "varsayılan engel, özel izin kuralı yok". Çelişki uyarısı yalnız logda izinli trafik görülen port engelliyse.
  Sorgu `POST /rule-checker/topology` {source:["ip/32"], destination, service:["tcp_{port}"], virtualAreaId (1),
  user_base:false, users:[]} → policyResultData[].PathResult[] (src_int, dst_int, device_id, existPolicy = eşleşen kural ya da null).
  Cihaz adları `GET /devices` (esnek okunur; canlıda parametresiz 400 verdi → `Opinnate:CihazSorgusu` ya da ?virtualAreaId=, ?page=1&per_page=1000 sırayla denenir). Karar: engel kuralı → "Kural engelliyor"; izin + trafik yok → "Kural açık — bu aralıkta trafik
  görülmedi"; hiç kural yok + trafik yok → "Erişim tanımlı değil"; trafikle çelişirse uyarı. Gövde StringContent (Content-Length'lı).
  Swagger'da yalnızca okuma uçları kullanılır (policy/get, rule-history ileride); görev/kural değiştiren uçlar kullanılmaz.
- **SQL (doku DB)** — tablo adları `Envanter:*` ayarında:
  - `dbo.KonsolideSegmentler4` (SEGMENT=CIDR, VLAN, Tenant, ApplicationProfile, EPGName, BD, GW, Description, Domain):
    IP → en uzun prefix eşleşmesi. Segment adı = EPGName > ApplicationProfile > Description > CIDR.
    **Aynı adlı segmentler tek segment sayılır** (CIDR değil ad bazında gruplanır). `#N/A`/`NULL` boş sayılır.
  - `dbo.ERT_HOSTIPADDRESS` (IPADDRESS, PORT, VIP_IP, VIP_PORT, APPNAME, WEBSITE, UNIXNAME, SERVICENAME, VARLIKMUHAFIZI,
    SUNUCUISLETENBIRIM, UYGULAMAVARLIKSAHIBI): uygulama eşleşmesi sırası IP+PORT → VIP_IP+VIP_PORT → IP → VIP_IP.
    APPNAME "Sistem Portudur" → "Sistem Portudur: RDP" gibi (SystemPorts; `Envanter:SistemPortlari` ile ek/override).
  - **Etiket** (veri ambarı işi için; kullanıcı kararı: etiketler ERT tablosunda, etiket için ayrı tablo YOK — yalnız açıklama ve versiyonlar için küçük DF_ tabloları): `ERT_HOSTIPADDRESS.TAG NVARCHAR(200)`
    (`Envanter:EtiketKolonu`, varsayılan TAG; virgülle birden çok, büyük harfe çevrilir; kolon yoksa sessizce etiketsiz). İleride
    Entropi'den dolacak. `Etiket.cs` (EtiketService): liste (`/api/etiket/sunucular?ip=&sunucu=&uygulama=&muhafiz=&segment=&etiket=&sirala=&yon=&sayfa=&boyut=`; etiket: "" tümü, "-" etiketsiz, "*" etiketli; filtre/sıralama/sayfalama sunucuda, liste envanter anlık görüntüsü başına önbellekli) ve yazma
    (`POST /api/etiket {ips, ekle|kaldir}`: IP'nin mevcut TAG'i DB'den okunur, IP'nin TÜM satırları UPDATE, sonra envanter zorla
    yeniden yüklenir; kolon yoksa ALTER önerisiyle hata). Yetki `Etiket:Yazanlar` (boş = herkes), her değişiklik loglanır.
    Arayüz: **Topoloji** sekmesi (etiket seç → `etiket:<ad>` ile uygulama çizimi, `?topo=`), **Sunucu etiketleme** sekmesi
    (`wwwroot/etiket.js`: istatistik kartları, hızlı filtre hapları, kolon başına filtre satırı (IP, sunucu, uygulama, VARLIKMUHAFIZI, segment, etiket), sıralanabilir başlıklar, sayfalama, seçim çubuğu ile toplu ekle/kaldır, satırda + Etiket, × ile kaldır; `?etiket=`; başka sekmeye geçince `setUiMode` #main'i temizler), IP kartında rozet + "+ Etiket".
  - **İşlev haritası** (`Islev.cs`, `wwwroot/islev.js`; Topoloji sekmesinin varsayılan görünümü, kullanıcı kararı: bağımlılık
    çizimi değil, işleve göre kümeler): küme → segment → VIP → sunucu kartları. Sunucu başına bilgi özeti (doku DB, `Islev:Tablolar`):
    ERT port/uygulama/muhafız, segment, VIP üyelikleri, MSY `dbo.Envanter` (Vm, DnsName, Cluster, Host, Os, Annotation), ÇSY
    `dbo.CsyEnvanter` (Folder, VM, CPU/RAM/disk), Kahin `dbo.SONUCLAR`, SM kurulum (`ZTSUNUCUKURULUMUOPTIONSM1` + `CM3RM1` + `REQUESTM1`,
    SUNUCU_IP / UYGULAMA_BILGISI), SM iş istekleri (`REQUESTM1` + `DEPTM1`; ZT_KAYNAK/ZT_HEDEF IP, hostname, ZT_ERISIM_KAYNAK/HEDEF
    blob'u `ParseHexOrBinary`; son `SmYil` yıl, 25'er IP'lik toplu LIKE sorgusu, eşleşme C#'ta regex ile). Kullanıcı: veri maskelenmez.
    AI 1. aşama `Paket`=4 sunucu/istek, `Paralel`=2: {ip, islev, rol, guven, gerekce}; islev SABİT KATALOGDAN seçilir
    (`DefaultKategoriler`, `Islev:Kategoriler` [{Ad, Aciklama}] ile değişir; uymazsa "Diğer: …"). Canlıda katalogsuz sürüm
    "ETL ve Veri Entegrasyonu / ETL / Veri Entegrasyonu / ETL ve Entegrasyon" gibi ayrı kümeler üretti → kodda yazım-bağımsız
    eşleme (`Tokens`: Türkçe harf sadeleştirme, bağlaç/“sunucu” atma, kısaltma açma db/dwh/sftp, 5 harf kök; `Similar`/`Snap`:
    aynı kök kümesi, %60+ örtüşme, ≥2 kelimelik içerme, tek kelime yalnız tek hedefte geçiyorsa). 2. aşama: katalog + elle
    kümelere eşlenemeyen işlevler AI'a "listeden birine ata ya da yeni küme" diye sorulur, sonunda benzer küme adları yine kodda
    birleşir. "Yeniden kümele" sunuculara yeniden sormadan eski sonuçları da toplar. Kartta "birleştir": kümenin tüm sunucularını
    başka kümeye elle atar.
    Kartta "yeniden adlandır" (`/api/islev/yeniden-adlandir {etiket, eski, yeni}`): AI_KUME değişir, AI'lı sunucular AI'lı kalır;
    IslevDetay.TakmaKaynak (asıl küme) → Takma (yeni ad) tutulur, ClusterAsync bu eşlemeyi uygular (yeniden kümelemede ad korunur).
    "+ Yeni küme / taşı" düğmesi seçim modu açar (sunucuya tıklamak seçer); çubukta küme adı (+ yeni küme için açıklama) →
    `/api/islev/duzelt {ips, kume, aciklama}` (elle atama; KumeAciklama verilmezse aynı adlı kümenin açıklaması alınır). Küme
    ayrı kayıt değil, sunuculardaki AI_KUME değeridir; boş küme olmaz.
    Sonuç ERT'de `AI_KUME NVARCHAR(100)` + `AI_DETAY NVARCHAR(MAX)` (JSON IslevDetay). Elle düzeltme (`/api/islev/duzelt`, Kaynak "Elle: kullanıcı")
    AI'ca ezilmez. Arka plan işi (tek seferde bir tane): `/api/islev/calistir {etiket, hepsi, sadeceKumele}`, `/api/islev/durum`, `/durdur`;
    `/api/islev?etiket=` harita, `/api/islev/profil?ip=` AI'a giden metin + SM kayıtları (link şablonları `Islev:SmTalepLinki` {NUMBER},
    `SmGorevLinki` {CMTASKID}; iç adresler repoya girmez). Yetki `Etiket:Yazanlar`. "Bağımlılık çizimi" düğmesi eski etiket topolojisini açar.
  - **Küme topolojisi** (`KumeTopoloji.cs`, `wwwroot/kumetopo.js`; işlev haritasında "Topoloji çiz", `?topo=X&ciz=1`):
    `/api/islev/topoloji?etiket=&start=&end=&appliance=&sources=`. Eleman = haritadaki VIP (`v:ip:port`) ya da VIP üyesi olmayan
    sunucu (`s:ip`); havuz üyesinin trafiği VIP elemanına yazılır. Sorgu: VIP IP'leri + sunucular (`Islev:TopolojiMaxIp`=400),
    `TopolojiParca`=80'erli parçalarla Splunk/AR/FW sırayla, sonuçlar birleşir. IP düzeyinde akış: hedef sorgulandıysa yalnız
    gelen tarafından sayılır; LB GW → üye ve aynı eleman içi atlanır; etiket dışı trafik eleman başına DisGelen/DisGiden (ilk 8).
    Çıktı: Kumeler/Elemanlar/Uyeler, Baglar (eleman→eleman, portlar, hits, engel, altyapı), Akislar (IP düzeyi, ilk 5000).
    Arayüz (canlıda katmanlı sütun düzeni 355 sunucuda kutuları aşağı uzattı, çizgiler karıştı → yeniden yapıldı): kümeler 3–4
    sütunluk ızgara (net akış sırası, "Sütun" seçici), kutu içinde varsayılan 5 sütunlu karolar ("Kutu içi" seçici 2–6; ızgara sütunundaki
    kutular aynı genişlikte, karo sütunu sütundaki en kalabalık kutuya göre; otomatik ızgara sütunu = ekran genişliğine (%15 küçültme payıyla) sığan en çok kutu; kullanıcı "kutu içi 4–5 sütun, sayfayı doldursun" dedi); "Kutu içi grup" = Uygulama (ERT, varsayılan): kutu içinde karolar ERT APPNAME alt başlıkları
    altında (`KtEleman.Uygulama`: VIP'in uygulaması ya da üyelerinin ilk uygulaması; sunucuda ilk sistem-dışı APPNAME; `ktPlan`), "Yok" = düz ızgara; trafiksiz sunucular "+N trafiksiz" karosunda
    (tıkla → açılır). YERLEŞİM (canlıda satır hizalı ızgara uzun ETL kutusu yüzünden boşluklu/şekilsiz kaldı → değişti): SÜTUN
    YIĞMA — kümeler akış sırasıyla "en kısa sütuna" üst üste dizilir (satır hizası yok); otomatik sütun sayısı en geniş kutuya göre
    ekrana sığan. Oklar kutunun YAN kenarından (başlığın altından itibaren 9px aralıkla, karşı ucun y'sine göre sıralı; eleman
    modunda karonun y'si) çıkar: komşu sütun → aradaki dikey kanal, aynı sütun → sağdaki kanal, uzak sütun → kendi yanındaki
    kanal → en üst ya da en alt yatay kanal → hedefin yanındaki kanal. Her kanalda her oka şerit; kanal genişliği şerit sayısıyla büyür.
    ÖZET görünüm denendi, kullanıcı BEĞENMEDİ ("sunucu görünmüyor, geri al") → kaldırıldı. Bunun yerine: kompakt karo (TW 128 × TH 32),
    en yüksek küme SOL üst sütunda, diğerleri büyükten küçüğe sağdaki sütunlara (sağdakiler soldakini aşarsa sola da), otomatik sütun =
    ekrana (genişlik VE yükseklik) en büyük ölçekle sığan (`stack(C)` denemesi), açılışta `ktFitScale` ile tamamı ekranda; Ctrl+tekerlek
    imleç noktasında yakınlaştırır (`ktSetScale`, yalnız svg boyutu), boş yerde sürükle = kaydır (`ktZoomPan`). Varsayılan "Küme bağları"
    (küme çifti başına tek ok; kenar noktaları karşı uca göre yayılır); "Tüm eleman bağları" eleman → eleman (kenar noktası karonun x'i).
    Elemanın üstüne gelince yalnız onun bağları turuncu üst katmanda; küme adının üstüne gelince kümenin bağları. Kutu içinden çizgi
    geçmediği Playwright'ta yol üzerinden örnekleyerek doğrulandı.
    "Küme bağları" modu kümeler arası toplam. Hover vurgusu, ok üzerinde ipucu. Elemana tıkla → ortada pencere (VIP: üyeler,
    kümelerden gelen/giden, etiket dışı; üyeye tıkla → sunucu: AI rolü, /api/islev/profil gerekçe ve kanıtlar, IP düzeyi akışlar,
    "IP topolojisini aç"), "← geri" yığını, Esc kapatır. Backend `KtTest` benzeri sahte envanterle doğrulandı (scratch, repoda yok).
    "Kutular: İşlev | Segment" seçicisi (kullanıcı: "segment ya da işlev seçebileyim"; localStorage `df.ktBy`): `kt.src` sunucudan gelen
    işlev kümeli veri, `ktView()` segment seçiliyse elemanları `segment`'e göre (çoklu kurumda kurum × segment) yeniden kutular; bağlar
    eleman → eleman olduğundan yeniden sorgu yok. Segment kutusunun açıklaması "İşlevler: …", kutu içi grup "İşlev" (`e.islev`) seçeneği.
  - **Alt etiket + açıklama**: alt etiket ayrı kolon DEĞİL, TAG içinde `ANA/ALT` (ör. `MOBILBANKACILIK/MOBIL APP`); `EnvanterSnapshot`
    sunucuyu hem alt hem ana etikete sayar (`TagIps("ANA")` altları da getirir, `EtiketService.Matches/Parent`). Etiketleme çubuğunda
    "Etiket" + "Alt etiket" kutuları, haplarda ana etiket seçilince altları ikinci satırda, rozet "ANA › ALT". Açıklamalar ayrı tablo
    `Etiket:AciklamaTablosu` (dbo.DF_ETIKET_ACIKLAMA; `EtiketAciklama`, 5 dk önbellek), `POST /api/etiket/aciklama {etiket, aciklama}`;
    "Etiket açıklamaları" paneli ve işlev haritası başlığında "✎ düzenle"; açıklamalar AI sınıflandırma prompt'una eklenir.
    İşlev haritasında alt etiket rozetleri (IslevSunucu.AltEtiket, IslevHarita.AltEtiketler/Aciklama), alt etikete tıklayınca yalnız o alt
    etiket; topolojide "Kutu içi grup: Alt etiket" (KtEleman.AltEtiket). TAG uzunluğu `Etiket:MaxUzunluk` (kolon büyütülürse artırılır).
  - **Versiyonlar** (`Versiyon.cs`, tablo `Islev:VersiyonTablosu` dbo.DF_TOPOLOJI_VERSIYON, betik `sql/etiket_versiyon.sql`): canlı küme
    topolojisi bitince "💾 Versiyon olarak kaydet" → işlev haritası (aynı kurum seçimiyle `api/islev`, ktRun'da paralel alınır) + topoloji
    JSON'u + parametre (aralık, kaynak, kurum) kaydedilir; etiket başına son `Islev:VersiyonSayisi`=3 (kayıtta eskiler silinir).
    `/api/islev/versiyonlar?etiket=`, `/api/islev/versiyon/{id}` (GET/DELETE), `POST /api/islev/versiyon`. Kullanıcı kararı: her seferinde
    topoloji çizilmesin. İlk sürümde sayfa en son versiyonla açılıyordu; kullanıcı "küme adı değişmiyor, sunucu taşınamıyor" dedi
    (versiyonda düzenleme kapalı) → Topoloji sekmesi CANLI haritayla açılır (`iv.ver`, URL `&ver=ID`); araç çubuğunda "Son kayıtlı
    topoloji" düğmesi ve "Görünüm" seçicisi kayıtlı versiyonu sorgusuz açar (`ktShowSaved`); versiyon bandında "Canlıya dön (düzenle)".
    Düzenleme / AI / yeniden çizim yalnız canlıda. Kurum seçimi değişince canlıya geçer.
  - **Kurum seçimi** (`wwwroot/kurum.js`, Topoloji sekmesinde Etiket'in yanında): KonsolideSegmentler4.**Domain** (kullanıcı düzeltti:
    Tenant DEĞİL; kodda adlar hâlâ tenant/TenantOf) — sunucunun segmentinin Domain'i; yoksa "(kurum yok)". `/api/tenantlar?etiket=` (kurum + sunucu sayısı). Çoklu seçimli açılır kutu: arama,
    "Tümü" (boş seçim = tümü; hepsi tek tek seçilirse yine tümü), Uygula; seçim localStorage `df.tenant`. `/api/islev` ve
    `/api/islev/topoloji` `tenant=a,b` alır (`IslevService.MapAsync(..., tenants)` süzer). Kullanıcı kararı: TEK kurum seçiliyse
    gruplama yok; birden çok / tümü (ve veride >1 kurum) → `Coklu`: işlev haritasında segment başlığında kurum rozeti, topolojide kutu =
    kurum × küme (`KtKume.Tenant`, arayüzde anahtar "Kurum · Küme", kutu başlığında yalnız küme), her kurum yeni satırdan başlar ve
    kesik çizgili kurum çerçevesiyle çevrilir (başlık kanalın üst bandında, `hPad`; şeritler bandın altında).
    Plan: Faz 1 AI rol sınıflaması (Exadata, ETL, rapor…; AI kararı ayrı tutulacak), Faz 1.5 aktarım takibi, Faz 2 DB→tablo→kolon.
  - `dbo.vip_envanteri` (hostname, server_ip, server_port, server_type, loadbalancer_ip, loadbalancer_port, loadbalancer_pool)
  - `dbo.vipgw` (GWIP, FW, Segment=CIDR): LB havuz üyelerine kendi segmentindeki GW IP'sinden gider.

## PARK EDİLDİ — "3. faz" (kullanıcı "3. faz" dediğinde buradan devam)
Etiket → işlev haritası → küme topolojisi → versiyonlar çalışması (2. faz) tamamlandı ve park edildi. 3. fazda hedef: veri
elemanlarına erişmek ve ETL'i kontrol etmeye kadar gitmek (DB → tablo → kolon, ETL iş/aktarım takibi: hangi ETL hangi kaynaktan
hangi tabloya yüklüyor; işlev kümeleri ve küme topolojisi bunun üstüne kurulacak). Açık kalan küçük fikir: küme topolojisinde
VIP tarafında veri yokken GW → üye trafiğini VIP penceresinde "LB GW üzerinden (kaynak bilinmiyor)" olarak göstermek (şu an atlanıyor).
Açık soru: ERT Entropi'den truncate ile yeniden yükleniyorsa TAG / AI_KUME / AI_DETAY kaybolur (kullanıcıya soruldu, cevap yok).
3. faz için ipucu: doku.dbo.REQUESTM1'de ZT_DWHORTAM, ZT_DWH_DBNAME, ZT_DWH_SCHEMANAME, ZT_DWH_TABLENAME kolonları var (DWH tablo
erişim talepleri) — DB → şema → tablo ve kim erişiyor bilgisi buradan başlanabilir.

## Özellikler (hepsi canlıda istenip yapıldı)
- **IP görünümü**: 5 sütun `[segment] → [gelen sunucu] → [SORGULANAN IP] → [gidilen sunucu] → [segment]`.
  2. seviye (`/api/hop2`) sunucuların kendi trafiği tek sorguda. Sunucu/segment sayısı seçilebilir, hover vurgusu,
  detay kartı, "Bu IP'nin topolojisini aç", tablolar, CSV, SVG/PNG, segment özeti.
- **Gelen portlar ve çalışan süreçler**: IP kartında "Bu makinedeki uygulamalar" (ERT) tablosunun ALTINDA ERT'den bağımsız tablo
  (kullanıcı: önce ERT tablosuna exe kolonu eklendi, eşleşme çok az çıktı → "bağımsız gelsin"): Port | exe | PID | gelen bağlantı |
  karşı IP | ERT'deki uygulama (varsa). Kaynak Carbon Black (`/api/surec`, IP sorgusundan sonra Splunk seçiliyse arka planda;
  `ProcNode.Portlar` = port başına tüm bağlantılar, Inbound'un 40 karşı IP sınırından bağımsız). Altyapı portları soluk "altyapı".
  Bu sunucuda ağ olayı görülen TÜM exe'ler (kullanıcı: "Splunk'ta daha çok exe var, burada 1 satır" → yalnız gelen bağlantılı süreçler
  gösteriliyordu ve kendine / karşı IP'siz olaylar Surec.Build'de atılıyordu; artık süreç listesi tüm satırlardan, çizim filtreli):
  gelen bağlantısı olmayanlar "— yalnız giden" satırı (giden sayısıyla), yerel portu olmayan "?". Başlıkta kullanılan aralık yazılır.
  HTTP.sys: bir portu ntoskrnl.exe / System (PID 4) "dinliyorsa" bağlantıyı çekirdek kabul ediyor (HTTP.sys URL kaydı); asıl uygulama
  (ör. canlıda 9000 → d:\aspnetcore\zb.wall.p5\core\zb.app.wall.exe, HTTP.sys'li ASP.NET Core) CB'de "yalnız giden" görünür; satırda
  "HTTP.sys (Windows çekirdeği)" notu (`HTTPSYS`), kesin eşleme sunucuda `netsh http show servicestate`. Surec SPL'inde Windows yolu
  kesilmiyordu (`[\\/]` yerine `[\\\\/]` olmalı) → düzeltildi, ayrıca C#'ta `ExeName` ve tam yol `ProcNode.Yol` (ipucunda).
  HTTP.sys portunda asıl exe tahmini (`httpSysApp`): ERT'de o port için WEBSITE (IIS site adı; kullanıcı: "IIS adı WEBSITE kolonunda") /
  APPNAME / SERVICENAME, sunucudaki bir exe'nin tam yolunda (harf-rakam sadeleştirilmiş) geçiyorsa → "asıl uygulama büyük ihtimalle X";
  geçmiyorsa kelimelerin ≥2'si ve ≥2/3'ü yolda. Canlı örnek: 9000 · WEBSITE "ZB.Wall.P5" → d:\aspnetcore\zb.wall.p5\core\zb.app.wall.exe.
- **"Bu sunucuda hangi uygulama çalışıyor?"** (IP kartı, Gelen portlar tablosunun üstündeki düğme; `SurecAi` Surec.cs, `POST /api/surec/ai`):
  arayüzdeki /api/surec sonucu exe başına toplanıp (PID'ler, portlar, giden, bağlanan / bağlandığı uygulamalar) AI'a gider (yeniden
  sorgulanmaz) + ERT kayıtları + segment + genel bilgiler dosyası. Kesin genel süreçler kodda ayıklanır (`GenelAd` / `GenelYol`: svchost,
  splunkd, Trend Micro, Carbon Black, VMware Tools…; ntoskrnl/System tutulur — HTTP.sys). AI: iş uygulamalarına ad (ör. paloma.bulksms.exe,
  odi4.exe), rol (hizmet veren port X / istemci), ERT karşılığı, güven, özet. Kaydedilmez (`state.procAi`, IP değişince sıfırlanır).
- **ERT eksikleri** (`Eksik.cs`, `wwwroot/eksik.html` — ana sayfadan bağımsız sayfa, sekme çubuğunda "ERT eksikleri ↗"): ERT'de
  APPNAME'i boş (IP, port) satırları (`EksikService.Missing`; altyapı portları `Uygulama:AltyapiPortlari` ayrı, AI'a gitmez).
  Kanıt (sunucu başına, `BuildAsync`): IslevService.ProfilesAsync metni (ERT, segment, VIP, MSY, ÇSY, Kahin, SM) + boş satırın
  WEBSITE/SERVICENAME/UNIXNAME/muhafız + KOMŞULAR (benzer adlı sunucular: ad sonundaki sayı atılınca aynı, ör. zb1pwatmvzsra01/02 →
  uygulamaları; aynı segmentte aynı portu kullananlar; segmentin yaygın uygulamaları) + Carbon Black (son `Eksik:Saat`=24 saat,
  20'şer IP tek SPL: port → exe tam yolu, istemci uygulamaları; exe'lerin gittiği yerler) + ADAY UYGULAMALAR (aynı sunucu, VIP ve
  havuz komşuları, benzer ad, segment+port, muhafızın uygulamaları, adı kanıtla kelime örtüşen ERT uygulamaları).
  SM talepleri IP + port (`SmPortAsync`, kullanıcının diğer projesinden gelen sorgu): `Eksik:SmTablolari` (varsayılan dbo.REQUESTM1; SM
  ERT ile aynı doku DB'sinde), DESCRIPTION / BRIEF_DESCRIPTION / ZT_KAYNAKACIKLAMA / ZT_HEDEFACIKLAMA'da IP ya da UNIXNAME VE
  DESCRIPTION / BRIEF_DESCRIPTION'da port. Kullanıcı: tablo büyük → tek sorgu 20 sunuculuk grup için, tarih (`Eksik:SmTarihKolonu`, varsayılan
  SUBMIT_DATE — canlıda OPEN_TIME YOK, "Invalid column name" verdi) ≥ son `Eksik:SmYil`
  (varsayılan Islev:SmYil=3) yıl, NOLOCK, TOP 500; sunucu/port ataması C#'ta (IP tam, port tam sayı), sunucu başına en yeni 10; ZT_ kolonu yoksa (207) yalnız iki kolon. Kullanıcının canlı örneği:
  2.71.51.16:5000 boş, CB'de c:\bbpgm\atm_monitoring_service\terminaldurum.restservice.exe, sunucu adı zb1pwatmvzsra01 ("atm").
  AI (`IslevService.ChatAsync`; kullanıcı: 20'lik toplu kanıt REQUESTM1 ile çok uzadı → SUNUCU SUNUCU: kanıt topla → hemen AI'a sor
  (tek sunucu/istek) → kaydet, aynı anda `Paralel`=3 sunucu; `Paket` kaldırıldı; `MaxIp`=200/çalıştırma): adaydan harfi harfine seçer ya da
  yeni=true; "Altyapı: …"; kanıt yoksa null. Sonuç ERT'ye YAZILMAZ: `Eksik:OneriTablosu` dbo.DF_ERT_UYGULAMA_ONERI (`sql/ert_oneri.sql`;
  yoksa bellekte + uyarı), DURUM AI / Onaylandı / Reddedildi / Elle; onaylanan / elle girilene AI dokunmaz. Uçlar: `/api/eksik`
  (ara, durum yok|ai|onay|red, altyapi, sayfa), `/api/eksik/durum`, `/kanit?ip=`, POST `/calistir {ips?, hepsi, yeniden, saat}`,
  `/durdur`, `/karar {ip, port, karar: onay|red|elle|sil, uygulama}` (calistir'da `max`: sayfadaki "Sunucu" seçicisi 100/200/500/1000/Tümü=0; varsayılan `Eksik:MaxIp`=200 — kullanıcı "kafasına göre sayı seçiyor" dedi: 14 bin satırdan 1438 = 200 sunucunun portları; onay penceresi ve ilerlemede "bu çalıştırmada X / Y sunucu" yazılır), `/csv?hepsi=` (Excel, ; ayraçlı, BOM). Yazma yetkisi
  `Etiket:Yazanlar`. Kullanıcı: alt sayfalara ayrı yetki kontrolü SONRA eklenecek.
  Kullanıcı istekleri: otomatik / dinamik portlar (≥ `Eksik:DinamikPortBaslangic`=49152) HİÇ listelenmez; test segmentleri
  (segment Domain'inde `Eksik:TestIfadeleri`=["test"]) altyapı portları gibi seçmeli ("Test segmentleri" kutucuğu, varsayılan gizli,
  işaretli değilse "AI ile öner" onları göndermez). IP sorgulama kartında ERT tablosuna "AI önerisi" kolonu (yalnız uygulama adı boş
  satır varsa; `/api/eksik/ip?ip=` port → öneri, `aiSuggest`/`loadOneri`; öneri yoksa "öneri yok ↗" eksik sayfasına).
  Kolon başlığında "↻ yenile" (`refreshOneri`: `/calistir {ips:[ip], yeniden}` → durum bitene kadar izlenir → öneriler yeniden okunur).
  YETKİ (kullanıcı): TÜM boşları tahmin ettirme (calistir'da ips yok) yalnız `Eksik:TopluYetkililer` (varsayılan ["visikhan"], DOMAIN\
  öneki fark etmez; `CanBulk`); diğer yazma yetkililer yalnız seçili sunucular (en çok 50) / tek sunucu yenileme. `/api/eksik`
  TopluYetki döner, düğme pasif. Envanter Kahini (dbo.SONUCLAR, kullanıcının önceki Gemini projesinin KAYITLI sonuçları; IP, PORT, TAHMIN, SAHIPLIK_ANALIZI) AI'a
  girdi; prompt: önceki AI tahmini, tek dayanaksa güven ≤60 ve "yalnız Kahin tahmini", çelişirse diğer kanıtlar.
  Genel bilgiler dosyası (`Eksik:BilgiDosyasi`, kullanıcının .txt'si; göreli yol uygulama klasörüne göre; UTF-8 ya da Windows-1254;
  `BilgiMaxKarakter`=15000; tarih değişince yeniden okunur): HER AI isteğinin başına eklenir (model istekler arası hatırlamaz);
  sayfada "AI'a her istekte giden genel bilgi dosyası: … karakter" satırı.
  SM TALEPLERİ (`SmFieldsAsync`, kullanıcının son tarifi): ARAMA sunucu IP'si ZT_HEDEF / ZT_KAYNAK'ta (LIKE + C#'ta tam IP), UNIXNAME
  ZT_HEDEFHOSTNAME / ZT_KAYNAKHOSTNAME'de; AI'A GİDEN bulunan talebin DESCRIPTION (ilk 400), BRIEF_DESCRIPTION, ZT_HEDEFACIKLAMA,
  ZT_KAYNAKACIKLAMA — sunucunun rolüne göre etiketli ("Hedef açıklaması (BU SUNUCU)" / "Kaynak açıklaması (karşı taraf)" ya da tersi).
  Hedef + boş port (YALNIZ ZT_HEDEFPORT, kullanıcı) önce, sonra hedef, sonra kaynak; en yeni 10. Talepte doluysa
  ZT_HEDEFUYGULAMA / ZT_KAYNAKUYGULAMA ek satır. Tek sorgu / sunucu, SUBMIT_DATE (`Eksik:SmTarihKolonu`; canlıda OPEN_TIME YOK) ≥ SmYil,
  TOP 300. Bölüm başında her boş port için ayrı özet (kullanıcı): "PORT 5000'E ERİŞİM TALEBİ VAR: N talep (son tarih; talep no'ları;
  talepteki en sık hedef uygulama)" ya da "… YOK" — tüm eşleşen talepler üzerinden. Bulunamazsa / okunamazsa açık satır. (Ara sürümlerde açıklama kolonlarında LIKE araması denendi; kullanıcı düzeltti.)
  AJAN / ARAÇ ÜRÜNLERİ (kullanıcı: kart sunucusuna Dataskope ajanı kurulum talebi var diye AI "Dataskope %85" diyordu; genel bilgiler
  dosyasındaki uyarıya rağmen): `Eksik:AjanUygulamalari` (boşsa DefaultAjanlar: Dataskope, Carbon Black, Splunk, SiteScope, Trend, Veeam…).
  Bu adları anan SM talepleri "[AJAN / ARAÇ TALEBİ: X — sunucunun uygulaması DEĞİL]" işaretli, listede sona; port özetinde ayrı satır,
  "iş uygulaması için talep" sayılmaz. ADAY UYGULAMALAR'dan çıkarılır. Prompt'ta kural + kanıtların SONUNDA hatırlatma (küçük model baştaki
  kuralı unutuyor). AI yine ajan adı önerirse: boş portu kabul eden exe (CB "- gelen port X" satırı) o ürüne aitse "Altyapı: X", değilse
  öneri null, güven 0, gerekçe "Yok sayıldı: …" (`PortExeHas`).
- **VIP**: sorgulanan IP VIP ise üyeler + GW'ler aynı sorguya eklenir, üyeler "VIP ÜYESİ" (GW→üye trafiği varsa ✓).
  Gidilen IP VIP ise "VIP → n ÜYE"; gelen IP LB GW ise "LB GW"; sorgulanan sunucu havuz üyesiyse VIP kartı.
- **Uygulama görünümü** (`/api/apps`, `/api/app`): `[kullanan uygulamalar] → [UYGULAMA: VIP'ler + segment grupları] → [bağımlı olunanlar]`,
  iç trafik (LB/doğrudan) mor yaylar, altyapı (DNS/AD/RDP/SSH… `Uygulama:AltyapiPortlari`) varsayılan gizli, uygulamadan uygulamaya geçiş.
  Uygulamanın VIP'leri: ERT'de VIP_IP'si ona kayıtlı olanlar + sunucularının üyesi olduğu VIP'lerden ona ait olanlar
  (`AppMemberOf`: VIP ERT'de başka uygulamaya kayıtlıysa alınmaz; değilse üye portu uygulamanın o sunucudaki portu olmalı).
- **ODM / TEST**: karşı IP'nin segment Domain'inde "odm"/"test" geçiyorsa topoloji araç çubuğundaki kutucuk işaretliyken
  gösterilir (varsayılan gizli, tarayıcıda filtre, yeniden sorgu yok). `OrtamFiltreleri` ayarı. Uygulama görünümünde "X · ODM" ayrı grup.
  Karşı IP bir VIP ise ortamı havuz üyelerinden gelir (üyelerin hepsi aynı ortamdaysa; VIP segmenti ortam taşımaz).
  Sorgulanan IP'nin kendi Domain'i bir ortamdaysa (ör. zbtest → TEST) kartta "zbtest · TEST ortamı", araç çubuğunda
  "Sorgulanan IP: TEST" rozeti ve o ortamın kutucuğu o sorgu için otomatik işaretli (sonraki sorguda geri alınır).
  Kutucuktaki sayı karşı IP sayısıdır ("N karşı IP"); VIP havuz üyeleri sayılmaz/filtrelenmez.
  Uygulama görünümünde kutucuklar uygulamanın kendi VIP/sunucularına da uygulanır (`appFiltered`, appState.raw → data).
- **AI etki analizi** (`/api/ai/etki`): OpenAI uyumlu şirket içi servis, model `zt-ga-small-0`, Bearer ApiKey,
  tek user mesajı, temperature 0.1, `<think>` temizlenir. Etki tablosu kodda hesaplanır (servis → kullanan uygulamalar,
  bağımlılıklar; her bağlantı iki ucundaki uygulamayla). **2. seviye AI'a gönderilmez** (flu sonuç veriyordu).
  `PortBilgisi.cs`: her port için işlev + "kesilirse duracak iş" (gelen/giden ayrı) prompt'a eklenir; model port adını
  değil etkilenen işi ve kimin etkileneceğini yazar (ör. 3389 → kaynaklardan uzak masaüstü kurulamaz). Kuruma özel
  portlar `Ai:PortAciklamalari` ({"8443": "Ödeme API'si"}); 49152+ = muhtemelen dinamik RPC.
  Arayüzde tek satırlık "AI'a sor" soru çubuğu sayfanın altına sabit (büyümez); "yorumlat" deyince cevap paneli
  hemen çubuğun üstünde açılır (ekranın ~%32'si, "Büyüt" ile %70, "Kapat"/"Cevabı aç"), kaydırma gerekmez,
  Enter gönderir; ODM/TEST değişince soru/cevap korunur.
- **Auth**: Negotiate (Windows Auth; IIS'te IIS'e bırakır) + her istekte `AuthHelper.YetkiKontrol` (DokuPanel),
  sonuç kullanıcı başına cache; yetkisiz sayfa/403 JSON; `/api/me` başlıkta kullanıcı; `Auth:Enabled=false` yerelde.
  İŞLEM YETKİLERİ (`Yetki.cs`; kullanıcı: "herkes etiketleme yapamamalı", "topoloji, işlev, toplu tahmin hepsi DokuPanel'den, yetkisiz
  Excel'e bile çıktı alamasın"): DokuPanel'de kod değişmeden her yetki için ayrı site kaydı → `Yetki:Yazma` (etiket, açıklama, işlev
  haritası, versiyon, ERT eksikleri; boşsa eski `Etiket:YazmaSiteAdi`, o da boşsa `Etiket:Yazanlar`), `Yetki:Topoloji` (küme topolojisi
  `/api/islev/topoloji` ve `/api/app?name=etiket:`; boşsa Yazma), `Yetki:Disari` (Excel/CSV, SVG, PNG; `/api/eksik/csv` sunucuda,
  tarayıcıdaki CSV/SVG/PNG düğmeleri gizli + fonksiyonda kontrol; boşsa Yazma), `Yetki:Toplu` (ERT eksiklerinde tüm boşlar / 50+ sunucu;
  Yazma da gerekir; boşsa `Eksik:TopluYetkililer`, varsayılan visikhan). Auth ara katmanı /api isteklerinde yapılandırılmış siteleri
  `AuthHelper.YetkiKontrol(ctx, cfg, siteAdi)` ile sorar (kullanıcı + site başına `Auth:CacheMinutes`; aynı ad tek sorgu) →
  `context.Items["Yetki"]` → `Yetki.Var(cfg, ctx, tür)`. `/api/me` → `yetki {yazma, topoloji, disari, toplu}`; topoloji.html body
  `no-disari` / `no-topoloji` sınıfları (yüklenene kadar gizli). DokuPanel'e ulaşılamazsa o işlem kapalı. Görüntüleme (IP / uygulama
  sorgusu, işlev haritası, kayıtlı versiyonlar) yalnız sayfa yetkisiyle açık.
- Marka: **DeltaFlow** adı ve logosu.

## Kullanıcının tercihleri / kurallar
- Arayüzde ve metinlerde **"bizim" ifadesi kullanılmaz**: "Sorgulanan IP", "Gelen/Gidilen sunucular", "Karşılayan uygulama", "Aynı segment".
- Ayarların tamamı **yalnızca `appsettings.json`**'dan okunur (eski DeltaFlow `config.json` desteği kaldırıldı).
- **Sır/şifre repoya girmez**: DokuPanelKey, AppResponse/Splunk şifreleri, AI ApiKey → `appsettings.Production.json`
  (`.gitignore`'da `appsettings.*.json`). AuthHelper'daki sabit anahtar koddan çıkarıldı.
- Görseller sade ve iyi gruplanmış olmalı, karmaşık olmamalı. Önce basit yap, sonra kullanıcı geri bildirimiyle geliştir.
- Kullanıcı ekran görüntüsü / SSMS fotoğrafıyla tablo şeması paylaşır; kod gerçek sistemlere erişmeden sahte veriyle test edilir.

## Geliştirme ortamı notları
- Bu bulut ortamında .NET 9 SDK yok (apt'de yok, Microsoft indirme sunucusu kapalı). Derleme kontrolü için proje
  geçici bir kopyada `net8.0` + Negotiate `8.0.31` ile derlenir (repo dosyası `net9.0` kalır).
- Arayüz Playwright (`/opt/node22/lib/node_modules/playwright`) ile, API'ler route mock'larıyla test edildi;
  AppResponse/Splunk/AI için küçük Python sahte sunucular kullanıldı.
- Commit mesajları Türkçe; push `git push -u origin claude/kod-analizi-aciklama-1vwqfv`.

## Açık fikirler (henüz yapılmadı)
- Uygulama görünümü için AI etki analizi paneli.
- Bağımlılıkta yalnızca sistem portu kaydı olan hedefleri "Sistem Portudur: DNS" gibi etiketlemek.
- Carbon Black'e Splunk yerine doğrudan API ile bağlanmak (kullanıcı API bilgisini verirse).
- Sistem portu listesini tamamen appsettings'e taşımak (kullanıcıya soruldu, cevap bekleniyor).
- Projeyi `Futbolquiz` yerine kendi reposuna taşımak.
