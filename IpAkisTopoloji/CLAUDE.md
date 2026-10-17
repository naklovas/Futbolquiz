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
| `Etiket.cs` | Sunucu etiketleme: ERT TAG kolonu okuma/yazma (Topoloji ve Sunucu etiketleme sekmeleri) |
| `F5Waf.cs` | F5 ASM / Bot Defense güvenlik logları (Splunk index=f5): erişimde engel kararı, IP görünümünde URI listesi |
| `PortBilgisi.cs` | Port → işlev ve kesilirse duracak iş (AI prompt'u için) |
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
  - **Etiket** (veri ambarı işi için; kullanıcı kararı: HER ŞEY ERT tablosunda, ayrı tablo YOK): `ERT_HOSTIPADDRESS.TAG NVARCHAR(200)`
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
    kutular aynı genişlikte, karo sütunu sütundaki en kalabalık kutuya göre; kutu içi ≥4 iken otomatik ızgara 3 sütun); trafiksiz sunucular "+N trafiksiz" karosunda
    (tıkla → açılır). Oklar YALNIZ kutular arası yatay/dikey kanallardan, köşeleri yuvarlatılmış: aşağı satıra = alt kenar → alt kanal
    → (gerekirse hedef sütunun sol kanalında dikey) → hedefin üst kenarı; yukarı tersi; aynı satır alt kenardan alt kanal. Her
    kanalda her oka şerit; kanal genişliği şerit sayısıyla (en çok 170/260) büyür, şerit aralığı kanala sığar. Varsayılan "Küme bağları"
    (küme çifti başına tek ok; kenar noktaları karşı uca göre yayılır); "Tüm eleman bağları" eleman → eleman (kenar noktası karonun x'i).
    Elemanın üstüne gelince yalnız onun bağları turuncu üst katmanda; küme adının üstüne gelince kümenin bağları. Kutu içinden çizgi
    geçmediği Playwright'ta yol üzerinden örnekleyerek doğrulandı.
    "Küme bağları" modu kümeler arası toplam. Hover vurgusu, ok üzerinde ipucu. Elemana tıkla → ortada pencere (VIP: üyeler,
    kümelerden gelen/giden, etiket dışı; üyeye tıkla → sunucu: AI rolü, /api/islev/profil gerekçe ve kanıtlar, IP düzeyi akışlar,
    "IP topolojisini aç"), "← geri" yığını, Esc kapatır. Backend `KtTest` benzeri sahte envanterle doğrulandı (scratch, repoda yok).
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

## Özellikler (hepsi canlıda istenip yapıldı)
- **IP görünümü**: 5 sütun `[segment] → [gelen sunucu] → [SORGULANAN IP] → [gidilen sunucu] → [segment]`.
  2. seviye (`/api/hop2`) sunucuların kendi trafiği tek sorguda. Sunucu/segment sayısı seçilebilir, hover vurgusu,
  detay kartı, "Bu IP'nin topolojisini aç", tablolar, CSV, SVG/PNG, segment özeti.
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
