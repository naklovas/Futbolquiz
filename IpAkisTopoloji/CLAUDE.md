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
- .NET 9 minimal API (`net9.0`), paketler: `Microsoft.Data.SqlClient`, `Microsoft.AspNetCore.Authentication.Negotiate`, `Oracle.ManagedDataAccess.Core` (yalnız UCMDB).
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
| `Segment.cs` | Segment tarama: segmentteki tüm IP'lerin Carbon Black gelen portları + exe'leri, ERT karşılaştırması (`wwwroot/segment.html`) |
| `Benzer.cs` | Benzer sunucular: segment / etiket taramasında exe + port + trafik + Nessus parmak izi, aynı / yakın amaçlı / alakasız |
| `ExeAgi.cs` | Exe ilişki ağı: segment / etiket taramasının exe → exe bağları, küme topolojisi çiziminde (topoloji.html `?exeag=1`) |
| `TlsAkis.cs` | TLS bağlantıları: AppResponse tls_handshakes, IP'nin gelen / giden el sıkışmalarında kullanılan TLS sürümleri |
| `FizikselEnvanter.cs` | Fiziksel envanter: nlyte kabin → host → VM → uygulama (doku DB'deki KabinHostVm vb., yalnız okur; `wwwroot/fiziksel.js`) |
| `FizikselDeger.cs` | Fiziksel cihaz değerleri (V / İ / E / G / B): tür / model / kabin kuralları, miras, ERT ad eşleşmesi (`partial FizikselService`) |
| `FizikselKayit.cs` | Fiziksel değer denetim kaydı (DF_FIZIKSEL_DEGER_*), elle değer, çok sayfalı Excel (`Xlsx` yazıcı, dış kütüphanesiz) |
| `Imza.cs` | Servis imzası: tüm ERT IP + port → Carbon Black exe + IIS uygulamaları, boş satırlara aynı servisin ERT adı (`wwwroot/imza.html`, DF_SERVIS_IMZA) |
| `HttpSys.cs` | HTTP.sys (ntoskrnl) port → asıl exe: Carbon Black zaman korelasyonu (`HttpSysEslesme`), sonucun kaydı (`HttpSysKayitService`, DF_HTTPSYS_KORELASYON) |
| `araclar/HttpSysTopla.ps1` | İSTEĞE BAĞLI: sunucularda elle `netsh http show servicestate` → DF_HTTPSYS_URL (DeltaFlow bağlanmaz) |
| `Zafiyet.cs` | Açıklık taraması: kurala (CVE) ya da anlık exe listesine uyan süreçler tüm sunucularda, Carbon Black toplu (`wwwroot/zafiyet.html`) |
| `Nessus.cs` | Nessus / Tenable.sc: repository'deki tarama sonuçlarından teknoloji, sertifika, OS, kurulu yazılım |
| `Ucmdb.cs` | UCMDB (Oracle, MFUCMDB.CDM_CONFIGURATION_DOCUMENT_1) web.config belgeleri: sunucu eşleşmesi, içerik (zip / gzip / düz) maskeli görüntüleme |
| `WebConfigTarama.cs` | config taraması (`wwwroot/webconfig.html`, `partial UcmdbService`): web.config / applicationHost / machine / *.config; IP / segment / Nessus DMZ / tüm ağ; zafiyet göstergeleri (idunno, anticsrf, csrfsettings), http/https adresleri, exe, metin / regex araması, tanı, Excel |
| `Yetki.cs` | İşlem + sayfa yetkileri (yazma, topoloji, dışa aktarma, toplu tahmin, TLS, fiziksel, açıklık taraması, erişim, işlev, etiket, eksik, imza, segment) — DokuPanel site kayıtları |
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
  ESNEK OKUMA + TANI (kullanıcı: "tcp_11880: policyResultData boş", "cihaz adı gelmedi"): yanıt alanları büyük / küçük harf ve _ farkı gözetmeden, iç içe
  (data / result sarmalayıcı) aranır (`Get`, `FindArr`, `Yollar`); adımda cihaz adı varsa (device_name / hostname / device{name}) o, yoksa /devices.
  Yol yoksa `Opinnate:VirtualAreaIdler` ve /devices'ta görülen virtual area id'leri (`MaxAlanDenemesi`=4) sırayla denenir; bulunursa "VirtualAreaId = X yapın" mesajı.
  Boş yanıtta mesajda Opinnate'in kendi mesajı, yanıt alanları, ilk 200 karakter. /devices: `CihazOku` (id + ad alanlı ilk dizi, iç içe; ya da id → ad sözlüğü),
  400 / 404 / 405 / 422'de sıradaki sorgu, `Opinnate:CihazYolu` (/devices); ad bulunamazsa ilk kaydın alan adları mesajda.
  `GET /api/opinnate/tani?src=&dst=&port=` düz metin: giriş, rule-checker gövdesi + ham yanıt (her alan için), /devices sorguları + ham yanıt.
- **SQL (doku DB)** — tablo adları `Envanter:*` ayarında:
  - `dbo.KonsolideSegmentler4` (SEGMENT=CIDR, VLAN, Tenant, ApplicationProfile, EPGName, BD, GW, Description, Domain):
    IP → en uzun prefix eşleşmesi. Segment adı = EPGName > ApplicationProfile > Description > CIDR.
    **Aynı adlı segmentler tek segment sayılır** (CIDR değil ad bazında gruplanır). `#N/A`/`NULL` boş sayılır.
  - `dbo.ERT_HOSTIPADDRESS` (IPADDRESS, PORT, VIP_IP, VIP_PORT, APPNAME, WEBSITE, UNIXNAME, SERVICENAME, VARLIKMUHAFIZI,
    SUNUCUISLETENBIRIM, UYGULAMAVARLIKSAHIBI): uygulama eşleşmesi sırası IP+PORT → VIP_IP+VIP_PORT → IP → VIP_IP.
    APPNAME "Sistem Portudur" → "Sistem Portudur: RDP" gibi (SystemPorts; `Envanter:SistemPortlari` ile ek/override).
  - `dbo.ERT_APPLICATIONDETAIL` (`Envanter:UygulamaDetayTablosu`; uygulama ANA KAYDI, Konfigürasyon ve Değişiklik Yönetimi'nden: APPNAME, VARLIKMUHAFIZI,
    SUNUCUISLETENBIRIM, UYGULAMAVARLIKSAHIBI, ACIKLAMA, VARLIKDEGERI, GUVENLIKDEGERI, ISDEGERI, ASUERISILEBILIRLIKDEGERI, GIZLILIKDEGERI, BUTUNLUKDEGERI;
    kullanıcı: "sunucuya atanmamış uygulamalar ERT_HOSTIPADDRESS'te yok"): `EnvanterService.LoadDetayAsync` (SELECT *, kolon adla; yoksa atlanır) →
    `EnvanterSnapshot.Detay`, `KatalogAdlari` (= atanmış ∪ ana kayıt), `Atanmamis(ad)`. ERT eksikleri / segment AI'ı: katalog eşleşmesi (`Snap`,
    `_catTok`, `EkipAdi`) KatalogAdlari'yla; aynı muhafızın atanmamış uygulamaları `_byMuhafiz` adayına girer; kanıta "SUNUCUYA ATANMAMIŞ ADAYLAR"
    satırı (muhafız + ACIKLAMA ilk 160); öneri atanmamış uygulamaya eşlenirse gerekçeye "ERT'de hiçbir sunucuya atanmamış uygulama — sunucu ataması
    eksik" (`CatalogMatch` sarmalayıcı, asıl iş `CatalogMatch0`). Fiziksel envanter `DetayDegerleriAsync` (kullanıcı: "uygulamadan gelen ile sunucudan gelen değerlerin ortalaması, oradan kabin"):
    boyut başına ana kayıt ile ERT_HOSTIPADDRESS MAX'ı ikisi de doluysa ORTALAMA, yukarı yuvarlanır (4,5 → 5); biri doluysa o; İş 0 = girilmemiş ortalamaya girmez
    (Erişilebilirlik = ASUERISILEBILIRLIKDEGERI), sonra `DegerTamamla`; Excel Yöntem sayfası da böyle yazar. GUVENLIKDEGERI okunuyor (`Guvenlik`) ama kullanılmıyor.
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
    HATA DÜZELTİLDİ (kullanıcı: "IRAK'ı kümelememe rağmen Sorgula deyince KOS topolojisi geliyor"): runTopo adres çubuğundaki `ciz` / `ver`'i etiket
    bakmadan kullanıyordu → KOS'un kayıtlı versiyonu (?topo=KOS&ciz=1&ver=7) IRAK seçilince de açılıyordu. Artık yalnız `topo` = seçilen etiketse geçerli;
    `ivVerData` başka etiketin versiyonunu reddeder (ozet.etiket kontrolü).
    Sunucu çekmecesindeki "Kümeyi düzelt" (kullanıcı: "küme değiştirde yalnız mevcut küme geliyor"): datalist'li kutu mevcut küme adıyla dolu açıldığı için
    tarayıcı listeyi o ada göre süzüyordu → `#ivFixSel` seçici (tüm kümeler, mevcut "(şu an)", en altta "+ Yeni küme…" → `#ivFix` metin kutusu).
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

## AI uygulama tahmini — sunucusu olmayan uygulamalar (ERT eksikleri / segment AI)
ERT_APPLICATIONDETAIL canlıda dolu. Kullanıcı: "sunucusu olmayan uygulamalar da sorunun bir parçası, envanterdeki eksik anlamında" → yapıldı:
aday listesine sunucuya atanmamış uygulamalar ADI KANITTA GEÇMESE DE girer: ad + ACIKLAMA'nın ayırt edici kelimeleri (`_atanTok`, ≥4 harf kök, GenelKelime
değil, ana kayıtların > max(3, %2)'sinde geçen kelime atılır) kanıtta en az 2 tane geçerse (en çok 5, aday listesinin ilk 25'inden sonra yer ayrılır).
`EksikSatir.AtanmamisUyg` (öneri — " Veritabanı" eki atılarak — `env.Atanmamis`), `EksikListe.Atanmamis / KatalogDisi / KatalogDisiIp`; `/api/eksik?durum=atanmamis |
katalogdisi` (katalogdisi = durum AI + Yeni + "Altyapı:" değil); CSV "SUNUCUYA ATANMAMIŞ". "Katalogda bulunamayanları yeniden sor" düğmesi:
`/calistir {katalogDisi:true}` → `KatalogDisiPortlarAsync` IP → port (Portlar yolu, yeniden:true; toplu yetkisi yoksa ilk 50 sunucu). Arayüzde mor
"sunucuya atanmamış" rozeti, istatistik kartı. GUVENLIKDEGERI'nin anlamı hâlâ sorulacak.

## Özellikler (hepsi canlıda istenip yapıldı)
- **IP görünümü**: 5 sütun `[segment] → [gelen sunucu] → [SORGULANAN IP] → [gidilen sunucu] → [segment]`.
  2. seviye (`/api/hop2`) sunucuların kendi trafiği tek sorguda. Sunucu/segment sayısı seçilebilir, hover vurgusu,
  detay kartı, "Bu IP'nin topolojisini aç", tablolar, CSV, SVG/PNG, segment özeti.
- **Gelen portlar ve çalışan süreçler**: IP kartında "Bu makinedeki uygulamalar" (ERT) tablosunun ALTINDA ERT'den bağımsız tablo
  (kullanıcı: önce ERT tablosuna exe kolonu eklendi, eşleşme çok az çıktı → "bağımsız gelsin"): Port | exe | PID | gelen bağlantı |
  karşı IP | ERT'deki uygulama (varsa). Kaynak Carbon Black (`/api/surec`, IP sorgusundan sonra Splunk seçiliyse arka planda;
  `ProcNode.Portlar` = port başına tüm bağlantılar, Inbound'un 40 karşı IP sınırından bağımsız). Altyapı portları soluk "altyapı".
  SÜREÇ PENCERESİ (kullanıcı: "processler az geliyor, bir önceki günün 09:00–12:00'ını getir"): /api/surec topolojinin aralığını DEĞİL
  sabit `PROC_PENCERE` (topoloji.html; dün 09:00–12:00, `procRange()`) kullanır; başlıkta yazar. Sunucu içi akış çizimi de aynı veriyi kullanır.
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
  ZAMAN KORELASYONU (kullanıcı: "ntoskrnl olayı: exe gerçekte hangi portta bilinmiyor, çok sıkıntı" → 3 yol önerildi: netsh servicestate toplama,
  IIS logları, korelasyon; kullanıcı korelasyonu seçti): `HttpSys.cs` `HttpSysEslesme.HesaplaAsync(ip, aralık)` — tek SPL: çekirdeğin (ntoskrnl / System)
  kabul ettiği gelen bağlantılar port başına, iş exe'lerinin giden bağlantıları süreç başına, `t=floor(_time/60)` dakika; port × exe Pearson (eksik dakika 0),
  `HttpSys:MinDakika`=5, `MinKorelasyon`=0,3, genel süreçler (`SurecAi.GenelExe`) hariç; aday başına r + eşlik (% dakika). KESİN DEĞİL (dışarı çıkmayan
  uygulama yakalanmaz). Kullanım: (1) `GET /api/surec/httpsys?ip=&start=&end=` → IP kartı Gelen portlar tablosunda ntoskrnl satırı "↔ zaman korelasyonu:
  muhtemelen X (r · % eşlik)" (`loadHttpSys` / `httpSysTahmin`, runProc'tan sonra yalnız ntoskrnl portu varsa, PROC_PENCERE aralığı; 404 → sessiz);
  (2) ERT eksikleri BuildAsync: boş port çekirdekteyse (`HttpSys:Eksikte`=true) kanıta "HTTP.SYS PORT → EXE TAHMİNİ" + prompt'ta orta güçte kanıt. Sahte Splunk'la test.
  KESİN KAYIT (kullanıcı: "gerçek portları bir yere yazsak, önce oraya baksak"): `araclar/HttpSysTopla.ps1` (kullanıcı çalıştırır, zamanlanmış görev; WinRM +
  yerel yönetici) sunucularda `netsh http show servicestate view=requestq verbose=yes` → girintisiz satır = blok (kuyruk adı; 16 haneli onaltılık + süreçsiz =
  sunucu oturumu, atlanır), `^\s*ID: n` süreç (Controller process ID sayılmaz), `https?://host:port/…` URL; + / * / host adı → sunucunun tüm IPv4'leri; PID →
  Win32_Process ExecutablePath / CommandLine; `-SadeceGoster` ekrana, yoksa SQL'e (Windows kimliği, sunucu başına DELETE + INSERT, tek transaction). Tablo
  `sql/httpsys_kayit.sql` dbo.DF_HTTPSYS_URL (IP, HOSTNAME, PORT, PROTOKOL, URL, KUYRUK, PID, EXE_YOL, KOMUT_SATIRI, TOPLAMA_TARIHI, TOPLAYAN) — DeltaFlow YALNIZ
  OKUR (`HttpSysKayitService`, `HttpSys:Tablo`, `HttpSys:Baglanti` yoksa ConnectionStrings:Envanter, IP başına `KayitDakika`=10 önbellek, 208 → `Durum`).
  `/api/surec/httpsys` artık `{portlar (korelasyon), korelasyonHata, kayit, kayitDurum}`; IP kartında kayıt varsa yeşil "✔ HTTP.sys kaydı: exe · kuyruk / IIS havuzu ·
  URL", yoksa korelasyon. ERT eksikleri: boş portun kaydı varsa kanıta "HTTP.SYS KAYDI (… KESİN)" + prompt'ta en güçlü kanıt, korelasyon yalnız kayıtsız portlara;
  `ProgramKontrol` kayıttaki exe yolundan da bilinen ürünü bulur. Ayrıştırma Python'a çevrilmiş aynı kurallarla örnek netsh çıktısında denendi (ortamda PowerShell yok).
  SUNUCUYA BAĞLANMA YOK (kullanıcı: "sunucuya bağlanma falan yok, zaman farkıyla dedin" — bir ara DeltaFlow'dan WinRM ile canlı netsh toplama yapıldı,
  KULLANICI İSTEMEDİ, kaldırıldı; geri ekleme). "Bulunan gerçek portları bir yere yazalım, önce oraya bakalım" = korelasyon sonucunu KAYDETMEK:
  `HttpSysKayitService.KorelasyonAsync(ip, aralık, http, ct, portlar, yenile)` → önce `dbo.DF_HTTPSYS_KORELASYON` (IP, PORT, SIRA, EXE, EXE_YOL, R, ESLIK,
  ORTAK_DAKIKA, DAKIKA, BAGLANTI, TARIH; SIRA 0 / EXE NULL = aday yok, PORT '0' = ntoskrnl portu yok; `HttpSys:KorelasyonTablosu`), `HttpSys:YenilemeSaat`=24'ten
  taze ve istenen portlar kayıtlıysa "kayıt"; değilse `HttpSysEslesme.HesaplaAsync` + IP×port satırları silinip yazılır ("şimdi hesaplandı"); tablo / bağlantı
  yoksa bellekte. IP kartı (`/api/surec/httpsys?yenile=`; ntoskrnl satırında "kayıtlı sonuç · tarih · ↻ yeniden hesapla" / "şimdi hesaplandı ve kaydedildi") ve
  ERT eksikleri (AI'dan önce, yalnız boş ve çekirdekteki portlar) aynı kaydı kullanır. DF_HTTPSYS_URL yalnız İSTEĞE BAĞLI (ps1 elle çalıştırılırsa) okunur.
  Sahte Splunk'la: 1. istek hesapladı, 2. kayıttan, yenile yeniden hesapladı.
  PENCERE + ZAYIF ADAY (kullanıcı: "aday yok (2 dk trafik)", "5 dk de çok az"): korelasyon IP kartının 3 saatlik süreç penceresini DEĞİL son
  `HttpSys:Saat`=72 saati kullanır (`HttpSysEslesme.Aralik`; IP kartı ve ERT eksikleri). `MinDakika`'dan az trafikte de aday aranır (ortak ≥ min(2, dakika)),
  `HttpSysPort.Zayif` → arayüzde "· zayıf (N dk trafik)", AI metninde "AZ TRAFİK — ZAYIF İPUCU"; kayıttan okurken Zayif = DAKIKA < MinDakika. Kullanıcı: "yine hiçbir şey bulamadı" → eşiği geçen yoksa aynı dakikalarda dışarı bağlanan en yakın 3 exe
  `HttpSysAday.EsikAlti` ile ("↔ güçlü aday yok · aynı dakikalarda dışarı bağlanan: …", AI'a "ÇOK ZAYIF İPUCU"); tanı: `HttpSysSonuc.Giden` (dışarı bağlanan iş exe'leri,
  dakika) + `GenelAtilan` (genel diye ayıklananlar) yanıtta / ipucunda; hiç iş exe'si yoksa "bu sunucuda dışarı bağlanan iş exe'si hiç yok". Kayıttan okununca tanı yok. TANI VERİSİ (kullanıcı: "kontrol verisi basarsan gönderebilirim"): `GET /api/surec/httpsys/tani?ip=` düz metin
  (`HttpSysEslesme.TaniAsync`, filtresiz SPL: satır sayısı, ilk satır alanları, yön dağılımı, gelen port × süreç, giden exe × durum (iş / GENEL / çekirdek),
  çekirdek portu başına dakikalar ve exe başına ortak dakika, ±1 dk, r); IP kartında ntoskrnl satırında "tanı verisi ↗".
  CANLI TANI SONUCU (2.71.x sunucusu): iş exe'leri neredeyse hiç dışarı bağlanmıyor (giden korelasyonu boş), ama Carbon Black çekirdeğin aldığı bağlantıyı
  asıl exe'ye de "gelen, local_port 0" yazıyor (w3wp, bsissuerkys, qrtaskman, bsqrmanager, estint…); CB olayları ~1/dk (tekilleştirilmiş). Ayrıca w3wp.exe
  \windows\system32\ yüzünden GENEL sayılıp atılıyordu → `HttpSysEslesme.Genel` w3wp'yi saymaz. YENİ EŞLEŞME "AYNI İSTEMCİ": SPL'de gelen port 0 olayları ve
  `rip` (remote_ip) da; çekirdek portunun (istemci, dakika) olaylarından kaçını exe'nin port 0 olayı aynı istemciden ±1 dk karşılıyor → `HttpSysAday.Istemci`
  (Eslik = yüzde); adaylarda önce bunlar, sonra giden korelasyonu. Kayıt: DF_HTTPSYS_KORELASYON.ISTEMCI (sonradan eklenen kolon, sql betiğinde ALTER; okuma
  SELECT * ad bazında, kolon yoksa yazmada atlanır). Arayüz "↔ aynı istemci eşleşmesi: muhtemelen X (N olay · %E)", AI metninde "güçlü". Tanıda port başına
  "AYNI İSTEMCİ" bölümü, gelen satırlarında istemci sayısı. DÜZELTME (kullanıcının Splunk ekranı): "gelen port 0" olayları Protocol=1 = ICMP (ping) — HTTP değil → SPL'de yalnız
  Protocol 6 (TCP) port 0 olayları; tanıda port 0 satırları "0/<protokol>". CB netconn alanları (canlı): Protocol, direction, local_ip/port, remote_ip/port,
  domain (karşı tarafın DNS adı, ör. …vipws.zb), pid, process_guid, process_path, md5/sha256, link_process (CB konsolunda süreç sayfası), computer_name,
  sensor_id, timestamp, type=ingress.event.netconn. cmdline / parent YOK (netconn'da). IIS HAVUZLARI (yapıldı; Splunk'ta ingress.event.procstart da var): `HttpSysEslesme.SureclerAsync` — (1) netconn'dan process_guid başına pid, yol,
  values(domain), link_process (genel / çekirdek hariç, w3wp dahil); (2) bu guid'lerin procstart olayı (40'ar guid, son `HttpSys:SurecGun`=7 gün):
  coalesce(cmdline, command_line, process_cmdline), parent_guid / parent_path / parent_pid → `HavuzAdi` (w3wp `-ap "X"`), üstü w3wp olan alt sürece
  `UstHavuz`. `GET /api/surec/havuz?ip=&start=&end=` (IP kartı süreç penceresi); Gelen portlar tablosunda exe altında "havuz X / w3wp havuzu X başlatmış ·
  bağlandığı: … · CB ↗" (`loadHavuz` / `havuzNot`, anahtar exe|pid). ERT eksiklerinde w3wp / \inetpub\ varsa `SurecMetin` kanıta ("IIS UYGULAMA HAVUZLARI",
  `HttpSys:HavuzEksikte`=true). procstart alanları (canlıda görüldü): command_line (w3wp -ap "BankSoftDebitCardWS" … -h "C:\inetpub\temp\apppools\X\X.config"),
  username ("IIS APPPOOL\X" → `HavuzKullanici`, havuz kimliğiyle çalışan her süreçte), parent_path, parent_pid, parent_guid SAYISAL (guid değil!) → üst guid
  link_parent'tan (…/#analyze/<guid>/<segment>); üstü w3wp olup ağ olayı olmayan w3wp'nin procstart'ı ayrıca okunur. TÜM HAVUZLAR (kullanıcı: "havuz isimlerini arattığımız yerde getir"):
  `HavuzlarAsync` — IP'nin computer_name'i (netconn, son 24 saat / 7 gün) → o bilgisayarın w3wp procstart'ları (son SurecGun gün) → havuz başına en son başlayan
  (`HttpSysHavuz` Ad, Pid, Bit32 (syswow64), Baslangic, Guid, Link, Baslatma = kaç kez başladı). `/api/surec/havuz` → `{surecler, havuzlar, hata}`; ERT eksikleri
  `SurecMetin`'de "Sunucudaki havuzlar" satırı. Arayüz: Gelen portlar tablosunun üstünde "IIS uygulama havuzları" kutusu (`havuzOzet`: havuz, PID, 32 bit, son
  başlangıç, ad eşleşmesi, CB ↗) ve ntoskrnl satırında "→ IIS havuzu (ad eşleşmesi): X" (`havuzEsleri`: havuz adı ↔ VIP havuz adı (memberOf.pool) / ERT
  APPNAME / WEBSITE / SERVICENAME; `hvKelime` camelCase böler + bitişik ikililer (Bank Soft ~ Banksoft), ≥4 harf, genel kelimeler hariç; puan = ortak kelime
  (tam ad içerme 10), yalnız en yüksek puanlılar). Örnek: BankSoftDebitCardWS ↔ /Common/BS_DebitCard_Grp → port 80. Tabloda "IIS havuzu" kolonu (`havuzHucre`); kutu boşken nedeni (bilgi: bilgisayar,
  w3wp başlangıç sayısı / eski DLL 404 / hata). Kullanıcı: Splunk'ta IP'li alt sorgu (head 1 computer_name) 3 satır, hostname'li tam liste getirdi + hostname
  araması yavaş → `BilgisayarlarAsync` IP'nin olaylarında `stats count by computer_name` (en sık ad; diğerleri bilgide), procstart aramasında `TERM(pc)`
  (indeks; computer_name alan araması yavaş). KÜNYE: IP kartında "Sunucu adı: <ERT UNIXNAME> (ERT) · <CB computer_name> (Carbon Black)" (`hostAdiHtml` /
  `hostAdiIc`, `/api/surec/havuz` → `bilgisayarlar`; ERT ile aynıysa "✓ Carbon Black aynı"). HIZ (kullanıcı: "çok uzun sürdü"): SurecGun varsayılanı 7 (havuzlar ~29 saatte bir
  yeniden başlar), guid aramasında `TERM(guid)`, havuz aramasında `TERM(pc) procstart w3wp` anahtar kelimeleri; bellekte önbellek: IP → bilgisayar 12 saat,
  bilgisayar → havuzlar `HttpSys:HavuzOnbellekSaat`=6 saat (bilgide "önbellekten").
  IIS ERİŞİM LOGLARI (kullanıcı: Splunk'ta `index=iis_msy` (…\inetpub\logs\AdvancedLogs\W3SVC4\…) ve `index=iis_csy` (sourcetype ms:iis:default,
  …\LogFiles\W3SVC1\…), W3C; ilk 9 alan sabit: date time s-ip cs-method cs-uri-stem cs-uri-query s-port cs-username c-ip): `HttpSysEslesme.IisAsync`
  (`HttpSys:IisIndexleri` varsayılan [iis_msy, iis_csy], `IisSaat`=1 (kullanıcı: "sonsuz döngü", 24 saatte "IIS sorgusu tamamlanamadı"; "1 saat bile yeter"), IP başına `IisOnbellekDakika`=60) — TERM(ip) + rex + where s-ip = IP, source'tan W3SVC<n>,
  URI'nin ilk parçası (küçük harf) uygulama → `HttpSysIis` (port, siteler, hostlar, istek, istemci (port başına dc), ilk 8 uygulama). `/api/surec/httpsys` →
  `iis`, `iisHata`; ntoskrnl satırında en üstte yeşil "✔ IIS logu: site W3SVC4 · /vbmservice.svc … · N istek, M istemci · havuz X (ad eşleşmesi)" (havuz
  adı ↔ URI uygulama adı `hvKelime`). ERT eksiklerinde boş portlar için "IIS ERİŞİM LOGU (KESİN …)" kanıtı (`IisMetin`, `HttpSys:IisEksikte`=true) + prompt'ta
  en güçlü kanıt. IIS logu olmayan çekirdek portları = IIS dışı HTTP.sys (self-host) → korelasyon / havuz. KOLON (kullanıcı: "griddeki havuz kolonunda yazarsa daha rahat görürüm"):
  `havuzHucre(r, t)` sırayla: süreç PID'i → havuz (SureclerAsync), w3wp PID'i → havuz listesi (`HttpSysHavuz.Pidler` = son 7 gündeki tüm PID'ler; tablo dünün
  PID'leriyle gelir, havuzlar her gün yeniden başlar), ntoskrnl portu → `iisHavuz` (IIS URI ↔ havuz adı) ya da `havuzEsleri` (VIP / ERT adı); kalın 13px,
  ipucunda nasıl bulunduğu, ad eşleşmesinde "(ad)". Canlı: havuz araması 0 döndü → eklediğim `procstart w3wp` anahtar kelimeleri kaldırıldı
  (yalnız TERM(pc)); `iisHavuz` URI uygulaması BAŞINA en iyi havuz (bir sitede birden çok sanal dizin / havuz: /bskampws, /ldapauthenticationservice…);
  boşsa "havuz listesi boş" (ipucunda bilgi). Kullanıcı: "PID alanına gerek yok" → tablodan PID kolonu kaldırıldı (exe ipucunda). YAVAŞLIK: TERM(pc) o sunucunun TÜM CB olaylarını (milyonlarca netconn) getiriyor →
  procstart aramalarına `TERM(ingress.event.procstart)` (ham JSON'da tırnak içinde tek parça; indeksten süzülür). Kullanıcı: "hiç bitmiyor, 1 saat
  falan yap" → pencere `HttpSys:SurecSaat` (saat), varsayılan 1 (SurecGun kaldırıldı). Kullanıcı: "hâlâ dönüyor" → SÜRE SINIRI: `SplunkService.ExportAsync(..., sure)`
  (süre dolunca okuma bırakılır, o ana kadarki satırlar + "WARN … sn sınırında kesildi (sonuç kısmi)"; bağlantı kapanınca Splunk aramayı iptal eder);
  havuz uçlarındaki tüm Splunk çağrıları `HttpSys:HavuzSureSaniye`=20; procstart sorguları stats YERİNE `table` (akış halinde, en yeni önce; guid başına
  ilk satır C#'ta); kısmi sonuç önbelleğe girmez; pencere yeniden `SurecSaat`=30. Sahte yavaş Splunk'la: 4 sn sınırda 4 havuzla döndü. SONUÇ: kullanıcı "havuz bilgisi olayı fiyasko" → IP kartında CB havuz araması
  KALDIRILDI (loadHavuz / havuzOzet / havuzNot / havuzHucre çağrılmıyor, kod duruyor; `HttpSys:HavuzEksikte` varsayılanı false). Yerine (kullanıcı: "IIS'ten getir,
  o IP portun karşısında ne yazıyorsa"): `GET /api/surec/iis?ip=` (`IisAsync`, `IisSureSaniye`=60 süre sınırı, süre dolarsa önbelleğe girmez) → `loadIis`
  (proc yüklenince) → tabloda "IIS (site · uygulama)" kolonu (`iisHucre`: W3SVC n · /uygulamalar, istek / istemci, ipucunda uygulama başına istek);
  künyede sunucu adı ERT + IIS log host'u. TARAYICI GÜRÜLTÜSÜ (kullanıcı: "saçma havuz isimleri var": her sitede /cgi-bin, /cgi-sys, /s, /.%2e):
  `IisTarama` bilinen tarama yolları (cgi-bin, cgi-sys, s, scripts, _vti_*, .env/.git, wp-*, phpmyadmin, actuator, *.php/cgi/pl, % içerenler…) + sunucunun portlarının
  yarısından çoğunda görünen ve en çok 2 istemciden (SPL `dc(iis_cip) as aist`) gelen adlar uygulama sayılmaz; başka uygulama varsa "/" gösterilmez; `HttpSysIis.Gurultu`
  (ayıklanan istek) kolonda "N tarama isteği ayıklandı". IIS logu olan ntoskrnl satırında korelasyon / IIS satırı artık yazılmaz (bilgi kolonda). Canlıda kalan gürültü (OGNL "${#w=#context.get(…)}.action",
  rastgele "/ixou7arxei1w") → `IisUyg`: geçersiz karakterli adlar (`_iisAd` = harf/rakam . _ ~ -) ve portta başka uygulama varken tek istemcili, az istekli (≤ max(20, %3)) adlar elenir. Canlı: IIS logu Splunk'a gelmeyen bir sunucuda
  (Splunk'ta host'u yok; IP yalnız başka sunucuların logunda c-ip olarak geçiyor) kolon boş kalır — doğru davranış (forwarder / inputs.conf eksik).
  ÇAĞIRDIĞI SERVİSLER (kullanıcı: "onu da ekleyelim"): aynı IIS sorgusu `where iis_sip=IP OR iis_cip=IP`, `yon` in / out; out satırları `HttpSysIisCagri`
  (hedef IP, port, hostlar, siteler, istek, uygulamalar — tarama yolları hariç) → `_iisCagri` (IisCagri(ip)); `/api/surec/iis` → `cagri` (hedef IP:port'un ERT
  uygulamasıyla, envanter okunamazsa boş); IP kartında tablonun altında katlanır "IIS üzerinden çağırdığı servisler" (`iisCagriHtml`). IIS logunda olup CB süreç penceresinde (dün 09–12)
  görülmeyen portlar da tabloya satır olarak eklenir (`iisYalniz`, "IIS (Carbon Black'te bu aralıkta görülmedi)"; kullanıcı: "IIS site neden yok?"). Canlı örnek: 9000–9003
  zb.app.wall.exe IIS logunda VAR (ASP.NET Core IIS arkasında) ama kolon boştu → HATA: site no `source`'taki W3SVC<n>'den; yol W3SVC içermeyince iis_site null,
  `stats … by iis_site` satırı atabilirdi → `coalesce(iis_site, "?")` (ve host), "?" site listesine yazılmaz (o sunucuda yol W3SVC içeriyordu; asıl neden
  büyük ihtimalle süre sınırı → `IisSureSaniye`=60, hata kolonda "IIS sorgusu tamamlanamadı"). UYGULAMA ADI (canlı: /api/v1/kurumsal-krediler/…, ilk parça hep "api"):
  `iis_seg` = URI parçaları, `mvfilter` ile boş / api / apis / vN / rest / service(s) / ws / odata / public / internal atılır, ilk kalan = uygulama; sağlık kontrolleri
  (healthcheck, health, ping, monitor…) gürültü listesinde; uygulama başına en sık `ornek` = "METOT /tam/yol" (`HttpSysIisUyg.Ornek`, kolon ipucunda). HIZ (kullanıcı: "yine çok uzun sürüyor"):
  /api/surec/httpsys artık IIS sorgusu YAPMAZ (çift sorgu Splunk'ın kullanıcı başına eşzamanlı arama sınırını dolduruyordu); `IisAsync` aynı IP için süren aramayı paylaşır
  (`_iisSuren`, tarayıcı iptal etse de biter ve önbelleğe girer); SPL başında `| fields host source _raw`. Gelen portlar tablosunda "VIP havuzu" kolonu (`vipHucre`:
  memberOf port = satır portu → havuz adı ("/Common/" atılır) + VIP adresleri, tıklanır); "Bu sunucu bir VIP havuzunda" kutusu sunucu portuna, sonra VIP portuna göre sıralı. SON 72 SAATİN PORTLARI (kullanıcı: "tanıda exesini yakalamış
  [hsms64.exe 10000], ekranda yok, saçma"): `GET /api/surec/gelenportlar?ip=` (`GelenPortlarAsync`: HttpSys:Saat penceresi, gelen, port ≠ 0, stats port × yol, 30 sn sınır,
  30 dk önbellek) → `loadGelenPortlar`; süreç penceresinde OLMAYAN portlar tabloya eklenir ("· son 72 saatte", ipucunda son görülme). Port 0 satırlarını gizleme
  denendi, kullanıcı GERİ ALDIRDI ("hsms.exe en azından 0 olsa da görünüyordu") — port 0 satırları yine gösterilir. Kullanıcı: "zb.app.wall.exe tanıda var ekranda yok" → `GelenPortlarAsync` artık tüm
  yönler (`stats by yon lp yol`; giden lp "-"); arayüzde tabloda HİÇ olmayan exe'ler de eklenir (gelen port 0 varsa port 0 satırı, yoksa "yalnız giden"), "· son 72 saatte". Kullanıcı: "detaylarda yazıyor ama altta port olarak görünmüyor" → ntoskrnl / IIS-yalnız port satırında `httpSysApp`
  TEK exe bulursa satır o exe'ye yazılır ("HTTP.sys üzerinden (CB'de ntoskrnl.exe) · ERT "X" ile eşleşti"), exe'nin "yalnız giden" satırı kalkar (giden sayısı ilk port satırına);
  72 saatte görülen exe'ler de eşleşme adayı. GRUPLAMA (kullanıcı: "klasörlere göre düzgün grupla"): SPL'e `iis_mod` (URI'nin 2. anlamlı parçası; sayı / GUID / geçersiz
  karakterli parça boş) eklendi, `HttpSysIisUyg.Moduller` (uygulama başına en çok 15, boş = "(doğrudan)"), port başına en çok 25 uygulama; hücrede açılır
  "N uygulama · modüllere göre" listesi (uygulama → modül, istek, yüzde çubuğu, üstüne gelince örnek). Pilot klasörü ↔ port ayrımı ağ verisinde yok
  (Splunk Stream `index=wall` da yalnız Wall'da; port + URL var, klasör yok) — kullanıcı bıraktı. DÖNEN SATIRLAR (kullanıcı: "en üstteki 2 satır hep dönüyor"): ntoskrnl satırlarındaki
  zaman korelasyonu (72 saat, dakika bazlı) süre sınırsızdı → `HttpSys:KorelasyonSureSaniye`=90 (`KorSure`); süre dolarsa kısmi sonuç kullanılmaz / kaydedilmez,
  satırda kırmızı "zaman korelasyonu tamamlanamadı (süre sınırı) ↻ yeniden dene". IIS penceresi `iisSure` ile saatin altındaysa "30 dk" yazılır. GÖSTERİM HATASI (kullanıcı: "IIS verisi ancak yeniden hesapla'ya basınca geliyor"): `loadIis` / `loadGelenPortlar` bitince
  yalnız `renderProc` (akış penceresi) çağrılıyordu, IP kartı tablosu (`updateTargetApps`) yenilenmiyordu → ikisi de artık `updateTargetApps` çağırır. Ayrıca canlıda görülen çelişki: ERT'de 10000 "Kredi Kartı Başvuru", ama süreç
  hsms64.exe ve VIP havuzu HSM_Provizyon_10000_GRP (ERT uyuşmazlık uyarısı önerildi, yapılmadı).
- **Servis imzası** (`Imza.cs`, `wwwroot/imza.html`, sekme çubuğunda "Servis imzası ↗"; kullanıcı: "Entropi'deki envanteri alalım, IP port karşılığında
  bulduklarımızı bir tabloya basalım; boş olanlarda aynı servis çalışıyorsa tespit ettiğimiz uygulamayı basalım"; "exe – IIS uygulaması – ERT adı, bir yol bulmamız lazım";
  sonra AI ile yorumlatılacak): TÜM ERT arka planda (`ServisImzaService`, tek iş): IP'ler `Imza:ParcaIp`=40'lık gruplar, grup başına Carbon Black (son `Imza:CbSaat`=72 saat;
  `stats count dc(remote_ip) by local_ip yon lp yol`) + IIS logu (son `Imza:IisDakika`=30 dk; port → uygulama, gürültü `HttpSysEslesme.IisUyg` / `IisTarama` ile, IP kartıyla
  aynı kural), sorgu başına `Imza:SureSaniye`=120 sınır. Ham tarama (`ImzaTarama`: sunucu başına gelen port × exe, giden exe'ler, IIS port × uygulamalar, Taranan) gzip JSON
  `Imza:Tablo` dbo.DF_SERVIS_IMZA (`sql/servis_imza.sql`, tek satır AD='TUM', TAMAM) — 10 grupta bir ara kayıt, durdurulan / yarım iş "Kaldığı yerden devam" (mod devam).
  Eşleşme her açılışta ham veriden (`Hesapla`, tarama + envanter sürümüne göre önbellek): satır = ERT IP + port (dinamik ≥49152 hariç) ∪ CB / IIS'te görülüp ERT'de olmayan
  portlar ("ertyok"). Exe: o portu kabul eden iş exe'si; yalnız ntoskrnl ise ERT WEBSITE / APPNAME / SERVICENAME adı sunucudaki TEK iş exe'sinin yolunda geçiyorsa o exe
  (`HttpSysExe`, `HttpSys=true`). İmza anahtarı exe|port (genel / altyapı / çözülemeyen ntoskrnl hariç). SÖZLÜK: ERT'de tek, sistem dışı adı olan satırlar → exe|port → adlar
  (sunucu sayısı), IIS uygulamaları, çelişki. Öneri (boş / ertyok): aynı exe|port başka sunucularda + IIS kümeleri Jaccard ≥ 0,5 → güçlü; yalnız exe|port → orta; exe yoksa
  IIS kümesi ≥ 0,6 → zayıf; en çok sunucudaki ad, %60'ın altında çoğunluk → bir kademe düşük + "net çoğunluk yok"; adaylar ipucunda. ERT dolu ama aynı servis ≥2 sunucuda
  başka adla (açık çoğunluk) → "uyumsuz" (ör. hsms64.exe:10000 "Kredi Kartı Başvuru" ↔ "HSM Provizyon"). Uçlar: GET `/api/imza` (satırlar, sözlük, kayıt, iş), `/api/imza/durum`,
  POST `/api/imza/tara {mod, ipler}` / `/api/imza/durdur` (Yetki:Topoloji). KAPSAM (kullanıcı: "tekil seçim yapamıyorum; önce boş olanları yapacağım, ~12K IP port"):
  mod yeni (tüm ERT, öncekini siler) | bos (adı boş portu olan sunucular) | ip (seçili / süzülen / tek IP) | kalan (hiç taranmamışlar) | devam (`ImzaTarama.Bekleyen`);
  yeni dışındakiler mevcut taramaya EKLENİR (aynı IP'nin verisi yenilenir), `Kapsam` bilgide. Satırda `Durum` yalnız ERT tarafı (kayitli | bos | uyumsuz | ertyok),
  tarama tarafı ayrı `Veri` (var | yok | taranmadi) — tarama yokken "boş" süzgeci boş geliyordu. Öneri için referans (ERT'de adı dolu, aynı exe'li) sunucuların da
  taranmış olması gerekir; yalnız boşlar taranırsa sözlük o sunuculardaki dolu portlardan çıkar → sonra "Taranmamışları tara". GENEL SÜREÇLER (kullanıcı: "dsasql.exe … bir sürü svchost geldi"; dsasql.exe SQL Server
  değil — sqlservr.exe; büyük ihtimalle DB izleme / denetim ajanı): `ImzaSatir.Genel` (SurecAi.GenelExe ya da `Imza:GenelExeler` listesi); ERT'de kaydı olmayan
  genel satırlar varsayılan gizli ("Genel süreçler" kutucuğu), exe yanında "genel" notu; zaten sözlüğe / öneriye girmiyordu. w3wp.exe genel sayılmaz (`HttpSysEslesme.Genel`). GEREKSİZ EXE LİSTESİ (kullanıcı: "listeyi sen hazırla, gereksizleri almayalım, exe olarak da
  koymasın"): `ServisImzaService.Gereksiz(yol)` = HttpSysEslesme.Genel + `GereksizAd` (Windows araçları, Carbon Black, Trend, Symantec / McAfee / Trellix / Sophos /
  CrowdStrike / SentinelOne, Splunk / Beats, SCOM / SCCM / Zabbix / NSClient / SiteScope / SolarWinds / Dynatrace / AppDynamics / Datadog, Veeam / Commvault / NetBackup /
  TSM / Networker, Tanium / Qualys / Nessus / BigFix, CyberArk / BeyondTrust / Kron / TeamViewer / AnyDesk / VNC, dsasql / sqldsimservice / DLP / Guardium, VMware Tools…)
  + `GereksizYol` (üretici klasörleri) + `Imza:GenelExeler`; ntoskrnl / System ve w3wp ASLA gereksiz değil. Taramada gereksizler `ImzaSunucu.Genel`'e ayrılır (giden
  listesine hiç girmez), hesapta eski kayıtlar da ayrılır: port yalnız gereksiz süreçteyse exe BOŞ, `GenelExe` (ad) + `Genel=true` → hücrede "yalnız genel süreç",
  ERT'de kaydı yoksa varsayılan gizli; sözlüğe / öneriye / HTTP.sys yol eşleşmesine girmez, "son biten" listesine yazılmaz. VIP HAVUZU (kullanıcı: "vip havuzunu da getirir misin"): `ImzaSatir.Vip` = env.MemberOf(ip) içinde
  üye portu = satır portu → "havuz (/Common/ atılır) · VIP ip:port"; tabloda "VIP havuzu" kolonu, aramada ve CSV'de (VIP HAVUZU). Öneride de kullanılıyor (aşağıda VIP HAVUZU ÖNERİSİ). TEST SUNUCULARI (kullanıcı: "domainden ayrıştırabiliyoruz, testlere bakmasın boşuna"):
  `Test(env, ip)` = segment Domain'inde `Eksik:TestIfadeleri` (varsayılan "test") — `Ipler(env)` tüm kapsamlarda (ip modu dahil) taramadan, satırlardan ve sözlükten
  çıkarır; kayıtta `testSunucu` sayısı, bantta "N test sunucusu dışarıda". KURUM (kullanıcı: "zb banka, zk katılım, zi iştirak"; "sunucu adına göre değil, yine domain"):
  `Kurum(env, ip)` = segment Domain'inin öneki (`Imza:Kurumlar`, varsayılan Ziraat Bankası zb / Ziraat Katılım zk / İştirak zi; eşleşmeyen "Diğer"); `ImzaSatir.Kurum`, kayıtta
  `kurumlar` [{ad, sunucu, bos}]; sayfada "Kurum" seçicisi tabloyu süzer ve tarama düğmelerine uygulanır (`ImzaIstek.Kurum`; bos / kalan / yeni yalnız o kurum;
  kurumlu "yeni" önceki taramayı silmez, ekler), CSV'de KURUM. HTTP.SYS / NTOSKRNL (kullanıcı: "sözlükte ntoskrnl olanlar gelmiyor, tahminlerimiz gelse"; "IIS
  uygulamaları için de sözlük"): HTTP.sys portunda exe sırası: DF_HTTPSYS_URL kesin kaydı → ERT adı exe yolunda (`HttpSysExe`) → DF_HTTPSYS_KORELASYON SIRA 1
  (`HttpSysKayitService.TumTahminlerAsync`, tüm IP'ler tek sorgu, 10 dk önbellek; tablo yoksa bellekteki korelasyonlar); `ImzaSatir.ExeKaynak` (Carbon Black / ERT adı
  exe yolunda / kesin kayıt / zaman korelasyonu / "HTTP.sys · exe bulunamadı"), korelasyonla gelen exe'de güven bir kademe düşük. Çözülemeyen ntoskrnl: IIS varsa
  anahtar "http.sys|port" (eşleşmede IIS Jaccard ≥ 0,5 şart, orta; sözlükte "HTTP.sys (exe bilinmiyor, IIS'le)"); ayrıca sunucuda çalışan TEK bir exe aynı portla
  sözlükteyse (ör. boş satırda zb.app.wall.exe var, başka sunucularda zb.app.wall.exe:9000 "Wall") → orta, IIS de ortaksa güçlü. IIS SÖZLÜĞÜ sekmesi (`ImzaIisSozluk`:
  IIS uygulaması → ERT adları, portlar, çelişki). Hesap önbelleği 10 dk (tahminler değişebilir). VIP HAVUZU ÖNERİSİ (kullanıcı: "aynı şekilde vip havuzu da kapsanacak mı"):
  önerilerden ÖNCE ve TARAMADAN BAĞIMSIZ (envanter): satırın IP:port'u bir havuzun üyesiyse (anahtar VIP ip | VIP port | havuz adı) diğer üyelerin aynı üye portundaki
  ERT adları (sistem portu hariç, üye başına ilk ad) → boş / ERT'de yok satıra öneri "güçlü" (%60 altı çoğunlukta "orta"), not "aynı VIP havuzu (havuz · VIP) → …";
  ERT dolu ama havuzun açık çoğunluğu başka adla → "uyumsuz" (ad listesi " | " ile bölünüp karşılaştırılır). Sahte veriyle: HSM havuzu, boş üye "HSM Provizyon güçlü",
  "Kredi Kartı Başvuru" kayıtlı üye uyumsuz — tarama yokken. SİSTEM PORTLARI (kullanıcı: "sistem portudur ifadesi olan portlar listeye geliyor"):
  `Altyapi` = Uygulama:AltyapiPortlari ∪ satırın ERT adlarının hepsi "Sistem Portudur…" ∪ ERT'de ≥ 3 sunucuda ve dolu kayıtların yarısından çoğunda "Sistem Portudur"
  olan portlar (`sistemPort`; adı boş sunucularda da altyapı) → varsayılan gizli, sözlüğe / öneriye girmez.
  İLERLEME (kullanıcı: "progress bar üste, hangi sunucu hangi port, yüzde kaç"): `ImzaIs` ToplamSunucu / BitenSunucu / Adim (cb | iis) / Yuzde (grup içinde CB = yarım) /
  Grup ("ip sunucuadı (ERT portları)") / Son (son biten 12 sunucu: "ip → port exe (IIS: …)", ntoskrnl "HTTP.sys") / Kalan ("4 dk", "1 sa 5 dk"); sayfada bant
  `position: sticky` üstte, yüzdeli çubuk, şu an sorgulanan sunucu çipleri, son bitenler listesi. Sayfa: tarama bandı (ilerleme, tahmini kalan, durdur), istatistik kartları (tıkla süz), "IP + port
  satırları" | "Sözlük" sekmeleri, arama / durum / güven / altyapı süzgeçleri, ilk 500 + "daha fazla", CSV (Yetki:Disari). Sahte envanter (DF_FAKE_ENV yalnız scratch kopyada) +
  sahte Splunk ile uçtan uca test edildi (Wall güçlü, HSM uyumsuz, foo ertyok).
  PLATFORM SÜREÇLERİ (kullanıcı: "sqlservr.exe:1433'te 26 sunucu 24 farklı ad, 2 sunucuda geçen yapıştırılmaz, AI'a bırakalım"): exe|port ≥ `Imza:PlatformAd`=4
  farklı adla ve en sık ad < %50 ise `Platform` (satır + sözlük) → exe'den öneri yok; aynı sunucuda tek başka uygulama varsa "zayıf · AI ile doğrulayın", yoksa not "AI'a bırakın".
  EKSİK ↔ İMZA BİRLEŞİMİ (kullanıcı: "Eksik sayfasıyla ikisinin güçlerini birleştirelim"): imza.html "AI önerisi" kolonu `/api/eksik/oneriler` (aynı DF_ERT_UYGULAMA_ONERI kaydı),
  "🤖 AI'a sor" seçili / süzülen boş satırları `/api/eksik/calistir {portlar}` ile sorar, `/api/eksik/durum` izlenir; süzgeçler "platform", "AI önerisi yok". Ters yön:
  `ServisImzaService.KanitMetniAsync(ip, portlar)` (kayıtlı taramadan, yeni sorgu yok) → ERT eksikleri BuildAsync'te IIS logundan sonra "SERVİS İMZASI" bölümü (port başına exe +
  kaynağı, IIS uygulamaları, VIP havuzu, aynı exe:port'un başka sunuculardaki ERT adları ya da "PLATFORM SÜRECİ — exe uygulamayı belirlemez", İMZA ÖNERİSİ + güven); prompt'ta
  güçlü / orta / zayıf ağırlığı ve platform kuralı. `Eksik:ImzaKaniti`=true ile kapatılabilir. EksikService'e isteğe bağlı `ServisImzaService? imza` parametresi.
  Karar (kullanıcı: "imza olsun, burada bulduklarımıza ek ne iletiyorsak AI'a"; "sen karar ver"): TEK AI motoru (Eksik) — ayrı imza AI'ı yok, günlük iş imza sayfasından.
  İmza bölümü kanıtta SM'den ÖNCE (kısa; MaxKarakter kesmesin). Eksik'in kendi Carbon Black sorgusu KALDI (istemci uygulamaları, giden hedefler ve
  "- gelen port X" satırları DbHints / PortExeHas / ProgramKontrol için gerekli; imza taraması bunları tutmuyor). imza.html "AI'a sor": seçilenlerde taranmamış
  sunucu varsa sorar → önce `tara("ip", taranmamış)`, `st.aiBekle`'deki portlar tarama "bitti" olunca kendiliğinden `aiGonder` (durdurulursa gönderilmez).
  KURUM ERT EKSİKLERİNDE DE (kullanıcı: "eksik kısmına da test olsun olmasın, iştirak banka katılım seçenekleri"): kurum kuralı ortak `KurumBilgi` (Imza.cs, static;
  `Imza:Kurumlar`); `EksikSatir.Kurum`, `/api/eksik?kurum=`, `EksikListe.Kurumlar` [{ad, satir, sunucu}] (test / altyapı süzgecinden sonra sayılır), `EksikCalistir.Kurum`
  (tüm boşları öner yalnız o kurum). eksik.html "Kurum" seçicisi ("Test segmentleri" kutucuğu zaten vardı).
- **DB teknoloji adı öneri değil** (kullanıcı: "SQL Server önermesin, isim olarak X Uygulaması Veritabanı olabilir"): ERT eksikleri son kontrolü `DbTeknolojiKontrol`
  (CokSistemKontrol'den sonra): öneri yalnız teknoloji adıysa (`DbTeknoloji`: SQL Server / MSSQL / Oracle [Database 19c] / PostgreSQL / MySQL / DB2 / MongoDB / Sybase / Teradata /
  HANA / Redis…, "Veritabanı" ekli de; Oracle Data Integrator, SSRS gibi ürünler DEĞİL) sahibi aranır (`DbSahibi`): servis imzası önerisi (güçlü 70 / 55) → bu porta bağlanan TEK
  istemci uygulaması (CB, 55) → sunucuda ERT'de kayıtlı tek iş uygulaması (50) → SM (boş portu anan / tek sistem, 50) → "<sahip> Veritabanı" (katalog eşlemesiyle), gerekçede teknoloji;
  bulunamazsa öneri null + "Yok sayıldı … sahibi belirsiz". Aynı sunucuda ERT'de birebir bu ad kayıtlıysa (ör. "Redis") dokunulmaz. Prompt'a da kural.
- **Kurum öneki** (kullanıcı: "başında ZK olanlar Ziraat Katılım, DIB olanlar Ziraat Dinamik; öneksizler banka — bakılmadığı için banka uygulamalarını ZK / DIB
  sunucularıyla eşleştiriyor"): `Eksik:UygulamaOnekleri` (varsayılan {"ZK": ["zk"], "DIB": ["dib"]}: önek → sunucunun segment Domain'i ya da (≥3 harfli önekte) sunucu adı
  bununla başlar). `UygOnek`: önek HER ZAMAN köşeli parantezli (kullanıcı: "hepsi [ZK], [DIB]…") — "[ZK] X" → ZK, parantezsiz = "" banka (ZB.Wall, SAS Scoring
  banka sayılır); tanımsız parantezli önek de banka değil. `SunucuGrup` (önek grubu / Domain zb… = banka "" / belirsiz null),
  `KurumUyar`: banka sunucusuna yalnız parantezsiz, ZK / DIB sunucusuna yalnız o önekli; belirsiz sunucuda (İştirak / Diğer) süzme yok; aynı sunucuda ERT'de kayıtlı ad hep uyar.
  Uygulandığı yerler: `Candidates` (aday listesi), `CatalogMatch0` katalog havuzu, `CatalogMatch` sarmalayıcısı (eşleşen ad kuruma uymuyorsa aynı adın kurum kaydı
  ([ZK] Kredi ↔ Kredi), yoksa önek atılmış serbest ad + not); prompt kuralı. `Eksik:KurumOnekKurali`=true.
- **DB teknoloji kontrolü düzeltmesi** (kullanıcı ekranı, "saçmalık": 8765 sqldsimservice.exe = Karmasis Dataskope SQL Agent, AI "Microsoft SQL Server" dedi, DbSahibi
  istemci "Dataskope"yu sahip sayıp "Dataskope Veritabanı" yazdı): `DbTeknolojiKontrol` önce portu kabul eden exe yollarına bakar — ajan ürünüyse (AjanOf, DB süreci değil,
  ürünün kendi sunucusu değil) "Altyapı: <ürün>"; port DB portu / DB süreci değilse öneri null ("veritabanı portu değil"); yalnız DB portunda sahip aranır. `DbSahibi` artık
  instance, ajan adlarını sahip saymaz. `AdTemizle` (katalogda birebir olmayan AI adlarında): "(null)" / sondaki "-null" ve sondaki "sunucu(su)* / sunucuları / server /
  production / prod / canlı / database / db" kelimeleri atılır (ör. "Kredi Kartı production database sunucususu-null(null)" → "Kredi Kartı" → + " Veritabanı").
- **AI tarama tarihi** (kullanıcı: "AI tarama bitince tarih saat dakika saniye bir kolona yazılsın"): `EksikOneri.AiTarih` = AI'ın portu en son taradığı an;
  DF_ERT_UYGULAMA_ONERI.AI_TARIH (sql/ert_oneri.sql'de ALTER + mevcut AI satırlarına TARIH'ten bir kerelik UPDATE). Yalnız AI yazımı günceller (onay / ret / elle korur;
  TARIH ise her değişiklikte). Kolon yoksa (207) `_aiTarihKolon=false` → eski SQL, AI satırlarında AiTarih = TARIH. Arayüz: eksik.html ve imza.html "AI tarama" kolonu
  (`tamTarih`: gg.aa.yyyy ss:dd:sn), CSV'lerde "AI TARAMA TARİHİ".
  SIRALAMA + ARAMA (kullanıcı: "tarih alanına göre sıralama olmalı, aramada tarih girebilmeliyim"): `/api/eksik?sirala=tarih` (taranmamışlar sonda; ilk tık en yeni üstte),
  arama metni AiTarih'in "gg.aa.yyyy ss:dd:sn" / "yyyy-aa-gg …" biçiminde de aranır; "gg.aa.yyyy[ ss:dd[:sn]] - gg.aa.yyyy[…]" aralıktır (`TarihAraligi`; yalnız gün yazılan
  bitiş günün sonuna kadar). imza.html'de aynısı tarayıcıda (`tarihAraligi`, "AI tarama" başlığı tıklanınca ▼ / ▲ / kapalı, `st.tarihSira`).
- **Kesin / tahmini exe ayrımı** (kullanıcı ekranı: 8031 HTTP.sys portunda korelasyon "güçlü aday yok · chrome.exe r 0,03 7/36 dk" eşik altıydı ama imza onu exe
  saydı, AI "Chrome %85 yeni ad" yazdı; "process verisi net ise başka, tahmini ise başka yaklaşım olmalı"): `TumTahminlerAsync` DF_HTTPSYS_KORELASYON'dan yalnız
  R ≥ `HttpSys:MinKorelasyon` ya da ISTEMCI > 0 olan SIRA 1'i okur (eşik altı yok; bellekte EsikAlti hariç), kaynak "zaman korelasyonu (tahmin, kesin değil)".
  Tarayıcılar / güncelleyiciler `SurecAi.GenelAd` / `GenelYol`'a eklendi (chrome, msedge, iexplore, firefox, edgewebview, google/edge update; ayrıca \opsware\ = HP
  Server Automation ajanı, python.exe) → korelasyon adayı, imza exe'si, sözlük olmazlar. Imza kanıt metninde korelasyon exe'si "TAHMİNİ exe (… KESİN DEĞİL, uygulama adı
  olarak kullanma)", imza.html exe hücresinde "~ tahmin · zaman korelasyonu". Eksik prompt'u: kesin (CB doğrudan, HTTP.SYS KAYDI, IIS logu) ↔ tahmin (korelasyon) ayrımı,
  tahminde exe adı uygulama yapılmaz / güven ≤ 50; tarayıcı / genel süreç asla uygulama. Kod: AI'ın adı genel süreç / tarayıcı / w3wp / ntoskrnl adıysa "Yok sayıldı".
  ADDAN AİLE SEÇİMİ (kullanıcı: "uygulama isim benzerlikleri saçma": "Opsware Agent" → "Agent Ziraat Yatırım Tradesoft GTP entegrasyonu", "BanksoftSharePoint" →
  "Banksoft Kredi Kartları"): CatalogMatch0 "aynı ürün ailesi" artık `AileUyar` ister — AI adının ayırt edici kelimeleri (`AyirtKelimeler`: camelCase / _ / rakamla
  bölünür, `AileGenel` agent / service / server / grp / pool / vip / web… ve GenelKelime atılır) en az YARISI katalog adında tam kelime (KelimeEs) olmalı
  (SAS ALM → [ZK] SAS IFRS TFRS9 ve ReeskService → Merkez Bankası Reeskont Kredisi kalır). Ajan listesine Opsware / Server Automation. VIP HAVUZ ADI: AI'ın serbest
  adı sunucunun VIP havuz adından (Grp / pool eki atılmış) türetilmişse güven ≤ 40 + not; prompt'ta "havuz adı uygulama adı değildir".
- **İşlev haritasına çalışan yazılımlar** (kullanıcı: "işlev belirlerken bu exe ve servis detayları işe yaramaz mı" → "evet yap"): IslevService'e isteğe bağlı
  `ServisImzaService? imza`, `NessusService? nessus` (DI döngüsü yok: ikisi de Islev'e bağlı değil). `YazilimEkleAsync` yalnız işlev AI'ına giden profile (RunAsync ve
  `/api/islev/profil` → ProfileAsync) başlık satırından hemen sonra ekler — ProfilesAsync'in kendisi değişmedi (ERT eksikleri kendi CB / imza / Nessus bölümlerini yazıyor).
  `ServisImzaService.SunucuOzetiAsync(ip)`: kayıtlı taramadan (yeni CB sorgusu yok) "ÇALIŞAN YAZILIMLAR": dinlediği portlar → exe (`= <BilinenUrun>` etiketi, korelasyon exe'si
  "[TAHMİN, kesin değil]", çözülemeyen ntoskrnl "HTTP.sys (exe bilinmiyor)") + IIS uygulamaları; yalnız dışarı bağlanan iş exe'leri; genel / ajan süreçleri yok; taranmamışsa
  bölüm yok. + "NESSUS" (`NessusService.Metin`, `Islev:NessusMaxKarakter`=1200). Prompt: çalışan yazılım işlevi en doğrudan gösterir (sas = analitik, pmserver / odi / SSIS =
  ETL, sqlservr / oracle = DB, w3wp + IIS = web, MQ, Control-M…), tahmin yalnız ipucu, envanterle çelişirse yazılıma güven. `Islev:YazilimKaniti`=true ile kapatılır.
  ERT eksiklerinin onaylı önerileri EKLENMEDİ (EksikService IslevService'e bağlı → döngü).
- **T-1 / aynı sistemin yazımları + talep formundaki uygulama** (kullanıcı ekranı: zb1pwkkrapord01 1433 / 1434 — açıklamalarda "Yeni T-1", "Yeni T-1 Ortamı",
  "Yeni T-1 Rapor Ortamı", "KrediKartları t-1", sunucu adı → BİRDEN ÇOK SİSTEM sayıldı; formda 6 talepte ZT_HEDEFUYGULAMA "Üye İşyeri Yönetimi" vardı; AI "SQL Server"
  dedi, DbSahibi "sahibi belirsiz" diye yok saydı; 35058'e "Internal Service"): `SistemKumeleri` — dolgu kelimeler (yeni, ortam, erişim, sunucu…) ve sunucu adı atılır,
  kelime kümesi diğerinin alt kümesi olanlar aynı sistem (alt küme yalnız GenelKelime'den oluşuyorsa birleştirmez: "Ziraat" POS ile Bansoft'u birleştirmez);
  `HedefUygBas` "TALEPLERDEKİ HEDEF UYGULAMA (BU SUNUCU): X (n) | …" satırı (hedef talepler, ajan hariç). `DbSahibi` yeni sıra: imza → bu portun talebindeki
  hedef uygulama (65) → istemci → sunucuda tek ERT uygulaması → formdaki hedef uygulama (tek ya da ikincinin 2 katı, 60) → SM. Yalnız genel kelimelerden oluşan
  ad (`AyirtKelimeler` boş: Internal Service, Web Service, Application Server…, katalogda birebir yoksa) "Yok sayıldı". Prompt: yazım varyantları tek sistem, "T-1 (rapor)
  ortamı" = canlı sistemin gün gecikmeli rapor kopyası → uygulama canlı sistem, DB'de "<sistem> Veritabanı"; formdaki hedef uygulama; sunucu adı kısaltmaları (kk = kredi kartları).
- **Ad standardıyla DB sunucusu + bilinen sistem portları** (kullanıcı ekranı: zb1pwkmpnsqld01 5101 / 5378'e AI "SQL Server Veritabanı" dedi, kod "veritabanı
  portu değil" diye yok saydı — "sunucu isminde db olduğunu standarta göre anlamalıydı"; 3343'e "Üye İşyeri Kampanya" / "[KKARSIV]"): `DbAdi(host)` = DbAd (sql / db /
  ora…) ya da sondaki numaradan önce rol harfi 'd' (`DbRolHarfi`, zb1pwkkrapord01 / zb1pwswarsivd02; `Eksik:DbRolHarfi`=true; a = uygulama varsayımı YOK). DB adlı
  sunucuda `AdDbPort`: sistem / altyapı / web (80, 443, 8080…) portu değil ve CB'de portu DB dışı süreç kabul etmiyorsa port DB portu sayılır (adlandırılmış instance)
  → `DbPort` true (DbHints'teki `DbAdIsareti` metninden), DbSahibi çalışır ("<uygulama> Veritabanı"). `Eksik:BilinenSistemPortlari` (varsayılan 3343 Windows Failover
  Cluster, 47001 WinRM, 9389 AD Web Services, 5723 / 5724 SCOM, 2179 Hyper-V): kanıtta "BİLİNEN SİSTEM PORTLARI" satırı, son kontrolde AI ne derse desin
  "Altyapı: <ad>" (güven 90). Talepte sunucu hedefken "kaynak uygulama" artık "(KARŞI TARAF — … BU SUNUCUNUN uygulaması DEĞİL)" etiketli; prompt kuralları.
- **ÇSY Annotation** (kullanıcı: "ÇSY'deki sunucular için Annotation kolonu var, baya açıklayıcı"): ÇSY sorgusu `Islev:CsyNotKolonu` (varsayılan Annotation) kolonunu
  da okur (yoksa 207 → notsuz). ERT eksikleri kanıtında imza / SM'den ÖNCE "SANAL SUNUCU NOTU" (MSY + ÇSY Annotation) + prompt: SM açıklamasıyla aynı güçte kanıt.
- **Sunucu adı uygulama değil**: `SunucuAdiMi` / `SunucuAdiRx` (AI "ZB1PWKMPNSQLD02.core.ziraat.bank" önerdi) — "BU SUNUCUYU ANLATAN" listesine girmez, öneri "Yok sayıldı".
- **Çok seviyeli etiket** (kullanıcı: "IRAK altında yazdığım tüm başlıkları hiyerarşileriyle görebilmem lazım"; 2 seviye yetmediği için IRAK/ACH RTGS + ACH RTGS/DB +
  DB/TEST zinciri kurmuştu → ACH RTGS, DB, APP… ayrı ana etiket oldu): TAG içinde en çok 4 seviye `A/B/C/D` (`EtiketService.TagRx`, `MaxSeviye`); EnvanterSnapshot sunucuyu
  her ön eke sayar (A, A/B, A/B/C); `Matches` her seviyede alt başlıkları kapsar; `TagCatalog` seviye seviye sıralı (`YolKarsilastir`). Islev `AltOf` = seçilen etiketin
  bir alt seviyesi (IRAK → ACH RTGS; IRAK/ACH RTGS → DB) → işlev haritası "Alt başlıklar" + "← üst", topoloji "Kutu içi grup: Alt etiket" de bir alt seviye. etiket.js:
  `tagLabel` "A › B › C", hızlı filtrede ana etiket seçilince altındaki TÜM başlıklar ağaç (2. seviye başına satır, altı "DB › TEST"), filtre / Topoloji seçicisinde
  derinliğe göre girinti, açıklama panelinde girinti, "Alt başlık" kutusunda / ile seviye (Etiket IRAK + ACH RTGS/DB/TEST). Zincirleme eski etiketler kendiliğinden birleşmez.
- **Zincirleme etiket birleştirme** (kullanıcı: "topolojide en üst IRAK etiketi yok, hep ayrık şekilde alt etiketler var"): `EtiketZincir` (Etiket.cs) — aynı sunucuda
  X/…/Y ve Y/Z → X/…/Y/Z (zincir başı önce, tur tur, en çok MaxSeviye; A/B + B/A gibi döngü birleşmez). GET `/api/etiket/zincir` önizleme (envanterden), POST
  `{ips?}` (Yetki:Yazma) DB'den okuyup yazar (ERT TAG; ERT dışı DF_ETIKET_EK), envanter yeniden yüklenir. etiket.js `egZincirYukle` / `egZincirCiz`: uyarı bandı
  (kaç sunucu, ayrı görünen kökler), "Önizle" (şu an → birleşince tablo), "Hepsini tek etikete birleştir" (onaylı).
- **Etiket düzenleme + ağaçtan seç + topolojide etiketler** (kullanıcı: "mevcut etiketleri düzenleyebilmemiz lazım, ayrıca topolojide etiketleri de göreyim"):
  `EtiketDuzen` (Etiket.cs) — `POST /api/etiket/duzenle {eski, yeni, uygula}` (Yetki:Yazma): eski etiket ve ALTINDAKİ tüm seviyeler tüm sunucularda yeni yola
  (yeniden adlandırma ya da başka başlığın altına taşıma; kendi altına taşıma / 4 seviyeyi aşma hata); uygula=false önizleme (sunucu sayısı, eski → yeni listesi);
  yazma ortak `EtiketZincir.DonusturAsync` (ERT TAG / DF_ETIKET_EK), ayrıca DF_ETIKET_ACIKLAMA (çakışmayan) ve DF_TOPOLOJI_VERSIYON.ETIKET taşınır. DİKKAT: kök
  etiket taşınırsa (ALTYAPI → IRAK/ALTYAPI) o kökü taşıyan TÜM sunucular taşınır. etiket.js: "Etiket açıklamaları" düğmesi → "Etiketleri düzenle", her satırda ✎
  (`egDuzenle`: prompt → önizleme confirm → uygula). Hızlı filtre / ağaç hapına tıklayınca seçim çubuğu o yolla dolar (Etiket = kök, Alt başlık = "kalan/").
  `IslevSunucu.Etiketler` (sunucunun tüm etiketleri) → işlev haritası kartında seçili etiketin altındaki başlıklar ("ALTYAPI › MICROSOFT ATA", `ivEtiketler`; arama
  da bulur); `KtUye.Etiketler` → küme topolojisi sunucu penceresinde "Etiketler" rozetleri, karo ipucunda etiketler.
- **Etiketi tabloda değiştirme + sade görünüm** (kullanıcı: "alt etiketleri alt alta listeliyorsun, her şey aşağı kayıyor"; "etiket düzenle değil, etiket girdiğim yerde
  düzenleyebilmem lazım"; "fazla etiket olduğu için biçimsiz"): hızlı filtrede kök seçilince alt başlıklar TEK satırda grup grup (`.eg-grp`, 2. seviye + altları).
  Tablo hücresi `tagGrup`: aynı kökün etiketleri TEK rozet ("IRAK › ALTYAPI · › BAGDAT › ACTIVE DIRECTORY"), her başlık `data-etag` (tıkla → `egPop` açılır pencere:
  yeni yol (datalist), "Bu sunucuda" / "Seçili N sunucuda" (POST /api/etiket {ekle: yeni, kaldir: eski}) / "Tüm sunucularda (N)" (egDuzenle → /api/etiket/duzenle
  önizlemeli), Enter / Esc), × ile kaldır. "Etiketleri düzenle" paneli yeniden "Etiket açıklamaları" (✎ kaldırıldı). Seçili hap okunur (`.eg-pill.on .eg-tag`).
- **Segment getir'de MSY / ÇSY** (kullanıcı: "bir segment ERT'de hiç yok, sunucu gelmiyor; ÇSY envanterinde var, ERT'de olmayabilir, MSY ÇSY'den de gelmesi lazım"):
  `SanalEnvanter` (Etiket.cs; işlev haritasıyla aynı tablolar `Islev:Tablolar:Msy` / `Csy`, SELECT * kolon adla, IP alanı virgül / boşlukla bölünür, `Etiket:SanalOnbellekDakika`=30),
  GET `/api/etiket/sanal?cidr=a,b` → CIDR'a düşen VM'ler {ip, ad (VM / DNS), kaynak MSY | ÇSY, not (Annotation + klasör)}. etiket.js `egSegGetir`: ağ taraması hata verirse
  (yetki / yok) CIDR'lar `egSegCidr` (segment listesi ya da yazılan CIDR) ile ERT + MSY / ÇSY yine gelir, bilgi satırında kırmızı uyarı; satırda sunucu adının yanında
  kaynak ("ÇSY", "MSY", "ağda görüldü"), Uygulamalar hücresinde (ERT'de değilse) VM notu; bilgi satırında ÇSY / MSY sayıları. Etiketleri DF_ETIKET_EK'e (ERT dışı) yazılır.
- **Segment getir'de karşı taraf izi** (kullanıcı: "küçük şubemiz, mutlaka hareket vardır"; "Carbon Black ajanı olmayabilir"): segment taraması yalnız ajanın kendi
  olaylarına (local_ip) bakar → ajansız sunucu görünmez. `KarsiTaraf` (Etiket.cs): GET `/api/etiket/karsi?cidr=&saat=72` (en geniş /16) — Carbon Black'te remote_ip'si
  segment CIDR'ına düşen olaylar (ham önek terimi `a.b.c.*` + remote_ip alanı + cidrmatch; `SegmentTaramaService.Desenler` internal), `stats by remote_ip yon port`
  → IP başına bağlanılan portlar (giden olay = karşının dinlediği), bağlandığı portlar (gelen olay), karşı bilgisayarlar (computer_name, ilk 8), son görülme;
  süre `Etiket:KarsiSureSaniye`=60. etiket.js `egSegGetir` 4. adım: yeni IP'ler "karşı tarafta görüldü" kaynağıyla, notta portlar / karşı sunucular; bilgi satırında sayı.
- **Etiketleme segment süzgecinde CIDR** (kullanıcı: "segmenti CIDR olarak filtreleyemiyoruz"): `EtiketService.SegmentUyar` — "a.b.c.d/n" (virgül / boşlukla birden çok)
  → IP aralıkta mı (segment kaydından bağımsız), "10.71.51." gibi IP öneki → IP / segment CIDR'ı bununla başlıyor mu, diğer metin → segment adı ya da CIDR'ında geçiyor mu.
  `EtiketSunucu.Cidr` (segment CIDR'ı), etiket.js segment hücresinde altta küçük CIDR, placeholder güncellendi.
  **Segment getir** (etiket.js `egs` / `egSegGetir` / `egSegYerel`; kullanıcı: "segment getir kısmı olsun oradan segment bulayım ama tüm ipleri gelsin",
  "Carbon Black'ten tara demeyelim de öyle gelsin"; ilk sürümdeki ayrı tablo + port listesi "çok saçma oldu" → kaldırıldı, port GÖSTERİLMEZ):
  segment adı / CIDR (datalist `/api/segment/liste`) → "Getir" = `POST /api/segment/tarama` (mod boş: kayıtlı tarama varsa ondan; "Yenile" = "yeniden"),
  `/api/segment/tarama/{id}` yoklanır; arayüzde "Carbon Black" yazmaz. ERT'dekiler `/api/etiket/sunucular?segment=<CIDR'ler>` (500'lük sayfalar).
  ANA TABLO bu listeye geçer (filtre / sıralama / sayfa tarayıcıda, `egSegYerel`); taramada görülüp ERT'de olmayan IP'ler aynı tabloda "ERT'de yok"
  rozetiyle; sayaçlar "Segmentte / ERT'de yok", hızlı filtre ve etiket seçiminde "ERT'de yok" (`!ert`). "Kapat" tüm envantere döner.
  ERT DIŞI ETİKET (kullanıcı: "ERT'de olmayan satırlara veri giremiyorum tamam ama en azından etiket ekleyelim"): `EtiketEk` (Etiket.cs) →
  `Etiket:EkTablosu` dbo.DF_ETIKET_EK (`sql/etiket_ek.sql`: IPADDRESS PK, TAG aynı biçim, HOSTNAME, GUNCELLEYEN, TARIH; ERT'ye satır EKLENMEZ).
  `POST /api/etiket` IP'leri ayırır: ERT'deki → TAG kolonu, olmayan → DF_ETIKET_EK (`adlar` ip → sunucu adı, segment taramasından). Envanter yüklenirken
  (`EnvanterService`, `EtiketEk.OkuAsync`) ERT'de olmayan IP'lerin etiketleri envanterin etiket listesine katılır (TagIps / TagCatalog / TaggedIps →
  topoloji, etiket taraması, işlev haritası da görür); `EnvanterSnapshot.EkSunucular`, `ErtteVar`. Etiketleme listesinde etiketli ERT dışı IP'ler
  her zaman satır (`EtiketSunucu.ErtYok`, rozet + etiketler). IP sonradan ERT'ye girerse ERT TAG'i geçerli (ek kayıt okunmaz). Tablo yoksa okuma
  sessiz, yazma betik adıyla hata. Etiket derinliği: 2 seviye (ANA/ALT; `TagRx`).
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
  Hedef + boş port (YALNIZ ZT_HEDEFPORT, kullanıcı) önce, sonra hedef, sonra kaynak; en yeni 10. AD EŞLEŞMESİ HATASI (kullanıcı: "alakasız talepler geldi,
  aranan IP talepte yok"): UNIXNAME IP biçimliyse ilk nokta öncesi "10" ile her "10.…" hostname'i eşleşiyordu → `Kisa`: IP biçimli ad ad sayılmaz, kısa ad ≥4
  ve yalnız rakam değil; talep satırında "eşleşme: ZT_HEDEF'te IP / ZT_HEDEFHOSTNAME 'ad'" yazılır. ASIL SORUN (kullanıcının ekranları): SM formunda hostname Entropi'den doluyor; aynı ad farklı IP'li
  taleplerde (KKB karşılığı sunucular, başka IP) de yazılı → AD EŞLEŞMESİ YALNIZ O TARAFTA IP YAZILMAMIŞSA (`AnyIp`; Eksik SmFieldsAsync ve Islev profil
  iş istekleri, çoklu erişim formunda da). Talepte doluysa
  ZT_HEDEFUYGULAMA / ZT_KAYNAKUYGULAMA ek satır. Tek sorgu / sunucu, SUBMIT_DATE (`Eksik:SmTarihKolonu`; canlıda OPEN_TIME YOK) ≥ SmYil,
  TOP 300. Bölüm başında her boş port için ayrı özet (kullanıcı): "PORT 5000'E ERİŞİM TALEBİ VAR: N talep (son tarih; talep no'ları;
  talepteki en sık hedef uygulama)" ya da "… YOK" — tüm eşleşen talepler üzerinden. Bulunamazsa / okunamazsa açık satır. (Ara sürümlerde açıklama kolonlarında LIKE araması denendi; kullanıcı düzeltti.)
  AJAN / ARAÇ ÜRÜNLERİ (kullanıcı: kart sunucusuna Dataskope ajanı kurulum talebi var diye AI "Dataskope %85" diyordu; genel bilgiler
  dosyasındaki uyarıya rağmen): `Eksik:AjanUygulamalari` (boşsa DefaultAjanlar: Dataskope, Carbon Black, Splunk, SiteScope, Trend, Veeam…).
  Bu adları anan SM talepleri "[AJAN / ARAÇ TALEBİ: X — sunucunun uygulaması DEĞİL]" işaretli, listede sona; port özetinde ayrı satır,
  "iş uygulaması için talep" sayılmaz. ADAY UYGULAMALAR'dan çıkarılır. Prompt'ta kural + kanıtların SONUNDA hatırlatma (küçük model baştaki
  kuralı unutuyor). AI yine ajan adı önerirse: boş portu kabul eden exe (CB "- gelen port X" satırı) o ürüne aitse "Altyapı: X", değilse
  öneri null, güven 0, gerekçe "Yok sayıldı: …" (`PortExeHas`).
  İSTİSNA (kullanıcı örneği: WEBSENSE segmentinde, ERT'de "Forcepoint Data Discovery" portları olan sunucuda 5432 için AI doğru olarak
  Forcepoint dedi, filtre yok saydı): `AjanSunucusu` — aynı sunucunun ERT uygulamalarında, sunucu adında ya da segment adında ürün ya da
  eski adı (`AjanTakma`: Forcepoint↔Websense, Trellix↔McAfee/FireEye, Carbon Black↔Bit9…) geçiyorsa ürün bu sunucunun uygulamasıdır:
  SM talepleri ajan işaretlenmez, adaylardan çıkarılmaz, öneri yok sayılmaz (DB portunda "Forcepoint Data Discovery Veritabanı").
  Ayrıca BU SUNUCUYU anlatan SM açıklaması ürünün sunucusu olduğunu söylüyorsa (ürün adı + sunucu/server/yönetim/manager/konsol/veritabanı,
  ajan/agent/kurulum YOK; `UrunSunucusuMetni`) — kullanıcı örneği: hedef açıklaması "Symantec Endpoint Protection ürünü sunucusu", AI
  "bu port için kanıt yok" deyip null vermişti. Prompt: açıklama sunucunun ne olduğunu söylüyorsa SUNUCU DÜZEYİNDE kesin kanıt, portta ayrı
  talep / exe olmasa da öneri o sistem (güven 70+), null yazma. Symantec takma adları: endpointprotection, sepm, sepmanager.
  `AjanSunucusu` ayrıca: MSY / ÇSY sanal sunucu notu ürünü anıyor (ajan kelimesi yok), sunucu kurulum talebinin uygulama alanı ürün, gelen
  bağlantı kabul eden exe yolu ürün + server/manager/konsol (ör. …\symantec endpoint protection manager\apache\bin\httpd.exe). Ürün
  sunucusunda "Altyapı:" öneki atılır, AI'ın tam adı kalır; başka sunucudaki ajan portunda "Altyapı: <AI'ın tam adı>" (eskiden kısa ad:
  kullanıcı "neden sadece Symantec").
  SEGMENT ADI ÜRÜN KANITI DEĞİL (kullanıcı: SEPM sunucusu WEBSENSE_11 segmentinde; AI segmentten genelleyip 1433'e "Forcepoint Web Security
  Veritabanı" dedi, "o segment yalnız Forcepoint sunucularından oluşmuyor"): `UrunKaniti` segmenti saymaz; segment komşu satırları "zayıf ipucu"
  etiketli; prompt "segmentten ürün türetme". Ürün reddedilince başka ürünün güçlü kanıtı varsa (`BaskaUrun`) gerekçede yazılır; DB portunda ve
  o ürün aynı sunucuda ERT'de kayıtlıysa "<o ad> Veritabanı" (güven 60). Hedef olunan talebin "Kısa açıklama"sı da ürün kanıtı;
  `AjanKelime` "sunucu kurulumu"nu ajan saymaz ("sunucuya X kurulumu" ajan).
  SM EN KRİTİK (kullanıcı: "kısa açıklamayı dikkate almıyor; erişim açıklaması, kısa açıklama, açıklama alanlarına dikkat etmeli"): asıl hata —
  Symantec kurulum talebi ajan diye işaretlenip "sunucunun uygulaması DEĞİL" olarak gidiyordu (ajan kontrolü yalnız ZT_*ACIKLAMA'ya bakıyordu)
  → artık hedef talepte BRIEF_DESCRIPTION / DESCRIPTION da. SM bölümüne "BU SUNUCUNUN HEDEF OLDUĞU TALEPLERİN KISA AÇIKLAMALARI" ve
  `SmAdayBas` "SM AÇIKLAMALARINDA ADI GEÇEN SİSTEMLER" satırı (`SmAdaylari`: ERT katalog adları — çok kelimeli: tüm anahtar kelimeler, tek
  kelimeli: adın kendisi metinde; ürün sunucusu metninde ürün adı + büyük harfli devamı, `UrunAdi` sondaki "ürünü/sunucusu"yu atar; boş portu
  anan talep önce). Son kontrol: AI boş bıraktıysa ya da yalnız Kahin'e dayandıysa ilk SM sistemi yazılır (güven 65, DB portunda "… Veritabanı",
  gerekçe "SM talep açıklamalarında adı geçen sistem: … (AI: …)"). Sahte envanterle test: Symantec iki talepten tek aday, "KÖS" (KÖS Raporlama değil).
  EKİP ADI UYGULAMA DEĞİL + PROGRAMLAR (kullanıcı: AI işleten birim "KATILIM VERİ AMBARI"nı uygulama diye önerdi, sunucuda sas.exe çalışıyordu
  bakmadı): `EkipAdi` — öneri ERT'deki SUNUCUISLETENBIRIM / VARLIKMUHAFIZI / UYGULAMAVARLIKSAHIBI değerlerinden biriyse (aynı adlı katalog
  uygulaması yoksa) "Yok sayıldı: … ekip / birim adı". CB bölümünün başına "SUNUCUDA ÇALIŞAN İŞ PROGRAMLARI" (genel süreçler hariç,
  `SurecAi.GenelExe`) + bilinen ürün etiketi (`BilinenUrunler`: sas.exe / sashome = SAS, odi = ODI, pmserver = Informatica, SSIS/SSAS/SSRS,
  MicroStrategy, Cognos, OBIEE, Qlik, Tableau, DataStage, Control-M, IBM MQ, SAP, Tomcat, WebLogic…). Son kontrol sırası: SM sistemi, sonra
  `ProgramKontrol` (hâlâ boşsa / Kahin'se: portu kabul eden exe bilinen ürünse güven 55; sunucuda tek bilinen ürün varsa güven 45).
  BENZER KATALOG ADLARI ARASINDA SEÇİM (kullanıcı: zkprodsasalm01 için AI "SAS" dedi, katalogda ZT SAS Enterprise Miner | SAS Scoring | [ZK] SAS
  Enterprise Miner | [ZK] SAS IFRS TFRS9 → "serbest" kaldı; doğrusu [ZK] SAS IFRS TFRS9): `Secim` — kurum öneki ([ZK] / ZT ↔ sunucu adının
  ilk 2 harfi), aynı sunucunun diğer portlarında kayıtlı (+5), adı kanıtta (benzer adlı sunucular / SM / VIP; ADAY ve segment satırları hariç)
  (+3), ayırt edici kelime kanıtta / sunucu adında (+1); tek en yüksek → katalog adı, gerekçede neden. Prompt: genel ürün adı yazma.
  Katalogda hiç karşılığı yoksa (kullanıcı: AI "SAS ALM" dedi → serbest kaldı) AYNI ÜRÜN AİLESİ (AI'ın anahtar kelimesi adında geçen kayıtlar)
  arasından da `Secim`. Ek puanlar doğrudan envanterden: aynı ad kökündeki sunucu (zkprodsasalm01 ↔ zkprodsasalm02, sondaki sayı atılır) +3,
  aynı önekle başlayan (son 3 harf atılmış kök, zkprodsas…) +1. Sahte envanterle: "SAS ALM" ve "SAS" → [ZK] SAS IFRS TFRS9.
  ERİŞEN TARAF / PAM (kullanıcı: "Kron Single-Connect Sunucularının Pinblock Sunucularına Erişmesi" talebinden AI Kron önerdi; Kron yetki yönetimi
  için sunuculara bağlanır): ajan listesine Kron, Single Connect, BeyondTrust, Delinea, Thycotic, Wallix (takma: singleconnect, kronpam).
  `HedefKismi`: "eriş" geçen açıklamada ilk "…sunucularının / sunucusunun / sistemlerinin / cihazlarının / kullanıcılarının" öncesi (erişen
  taraf) atılır — `SmAdaylari` ve `UrunSunucusuMetni` bunu kullanır; ajan / PAM adı aday olmaz (ürün sunucusu metni değilse). Prompt kuralı +
  örnek. SM son kontrolü artık yalnız tek SM adayı varsa ya da aday boş portu anan talepten geliyorsa uygular (çok uygulamalı sunucu:
  Ziraat POS / Bansoft / Pinblock / 3DSec aynı sunucuda → ilk ad her porta yazılmaz). Kullanıcı (canlı): yine de AI port kanıtı olmayan her porta ilk sistemi
  yazdı, açıklamada "<IP>:8031 … 8031 portundan" geçen port için "kanıt yok" dedi → (1) `pa`: açıklama metinlerinde (BRIEF / DESCRIPTION / ZT_*ACIKLAMA) bu
  sunucunun IP'si varsa "IP:port" ya da "N portu/portundan" → port özetinde "PORT N: TALEP AÇIKLAMASINDA BU SUNUCUNUN IP'SİYLE BİRLİKTE GEÇİYOR … o taleplerde bu
  sunucunun açıklaması: X", talep satırında "AÇIKLAMADA BOŞ PORT"; (2) "BU SUNUCUYU ANLATAN" ≥2 farklı sistem → `CokSistemBas` satırı + prompt kuralı (sunucu
  düzeyinde kesin kanıt kuralı yalnız tek sistemde); `CokSistemKontrol`: port kanıtı (ZT_HEDEFPORT, açıklamada port, CB gelen port satırı) yoksa güven ≤ 40 + not. Kullanıcı (yeni DLL'den sonra): AI port kanıtı
  olmayan portlarda null verdi — "tahmin yok, o zaman bu işin anlamı kalmaz" → prompt: port kanıtı yoksa da EN OLASI sistemi öner (VIP uygulaması, aynı VIP
  portları), güven ≤ 40, null yazma; AI yine boş bırakırsa `CokSistemKontrol` en sık anılan sistemi (UrunAdi, katalog eşlemesi) güven 30 ile yazar, diğerleri gerekçede. YARIM CEVAP (kullanıcı: "AI cevabı JSON değil: [ {…" kesik): `ExtractJson`
  kesik dizide tamamlanmış nesnelere kadar alıp kapatır (hiç yoksa boş dizi); `Ai:MaxTokens` verilirse max_tokens gönderilir; gerekçe ≤ 200 karakter istenir. Sahte veriyle: Kron elendi, Pinblock / Ziraat POS / Bansoft.
  HEDEF talebinde "A karşılığı … B'nin DB'lerine erişim" → bu sunucu B (kullanıcı örneği: "KKB Karşılığı / Ziraat Katılım prod segmentinin
  KÖS Db'lerine erişim talebi" + HEDEF → "KÖS"; AI null diyordu). Prompt kuralı: DB portu olsa da sahibi açıklamadaki sistem, null yazma.
  VERİTABANI SUNUCUSU (kullanıcı: "DB sunucusunda uygulamanın yanına 'X Veritabanı' yazsın"): `DbHints` kanıta "VERİTABANI SUNUCUSU
  İPUÇLARI" satırı ekler — bilinen DB portları (`Eksik:VeritabaniPortlari`, varsayılan 1433/1434 SQL Server, 1521–1529 Oracle, 5432, 3306,
  50000, 27017…; "5000 Sybase?" gibi soru işaretliler yalnız süreçle), sunucu adında sql/db/ora/orcl/exa/dwh (`DbAd`, ör. zbkossql01),
  portu kabul eden DB süreci (sqlservr, oracle, tnslsnr, postgres, mysqld, db2sysc…). AI JSON'a `veritabani` alanı; kod: AI işaretlediyse,
  port DB portu / DB süreci ise ya da ad "… DB/Veritabanı" ile bitiyorsa çekirdek ad katalogla eşlenir + " Veritabanı" (ör. "KÖS Veritabanı").
  KATALOG KONTROLÜ (kullanıcı: "içerideki uygulama isimleri kontrol edilsin, yoksa serbest atsın"; `CatalogMatch`): AI'ın adı ERT
  APPNAME kataloğuyla karşılaştırılır — birebir / harf-rakam eşit → o; değilse Tokens ile sırayla: aynı kelime kümesi, AI'ın kelimelerinin
  hepsi tek bir katalog adında, tek bir katalog adının kelimeleri AI'ın adında (anahtar kelimelerin en az yarısı), harf-rakam içerme
  (ZB.Wall.P5 ↔ zbwall; yalnız uzunluklar yakınsa, kısa/uzun ≥ %60). TAM KELİME DOĞRULAMASI (kullanıcı: AI "Ziraat pos" dedi, kod 5 harf kökü "ziraa"
  yüzünden 'Ziraatliyorum'a eşledi — "çok saçma"): esit / ust / alt eşleşmeleri `Kelimeler` + `KelimeEs` ile de doğrulanır (ortak başlangıç ≥ max(4, kısa−1),
  uzunda kalan ≤ 4 harf); kurum adları (ziraat, katılım, banka, zb/zk/zt) GenelKelime'de — tek başına eşleşme sebebi olmaz. YAZIM HATASI (kullanıcı: SM'deki "Bansoft Sunucusu"ndan AI "Bansoft" dedi, serbest kaldı — "saçma"):
  `Duzelt` — katalogda olmayan ≥5 harfli kelime, katalog kelimelerinden TEK birine bir harf farkla (ilk harf aynı; `BirFark`) uzaksa düzeltilip eşleme tekrarlanır
  (Bansoft → Banksoft ailesi → Secim), gerekçede "yazım farkı düzeltildi". ADAY LİSTESİ (kullanıcı: "Ziraatliyorum'u tüm sunucuya önerdi" — kök "ziraa" yüzünden
  ADAY oluyor, AI listeden seçiyordu): `Candidates` kanıt-kelime eşleşmesinde en az bir GenelKelime olmayan kelime + kelimelerin yarısı kanıtta TAM KELİME (`KelimeEs`). Tek açık eşleşme → katalog yazımı (yeni=false); birden çok → serbest + gerekçede "birden çok benzer ad: …";
  hiç yok → serbest (yeni=true) + "ERT kataloğunda yok" (varsa "benzer: …"). Not gerekçenin sonuna [ … ] olarak eklenir.
  KANIT SIRASI (kullanıcı: "kaynak / hedef açıklamasını önemsemiyor"): metin `Eksik:MaxKarakter`=6000'de kesildiği ve uzun profil başta
  olduğu için SM / CB kesiliyordu → sıra: başlık, boş portlar, SM, CB, DB ipuçları, adaylar, komşular, SUNUCU PROFİLİ (sonda). SM bölümünde
  önce "BU SUNUCUYU ANLATAN TALEP AÇIKLAMALARI" (hedefse ZT_HEDEFACIKLAMA, kaynaksa ZT_KAYNAKACIKLAMA; tüm talepler, tekil, sayı + son tarih,
  boş portu ananlar önce) ve "Bu sunucuya erişen taraflar" (hedef taleplerin ZT_KAYNAKACIKLAMA'sı, istemci); prompt'ta en güçlü SM kanıtı.
  DB portları genişletildi (SQL Server/SSAS/AlwaysOn, Oracle 1521–1530/2483/2484/ONS, PostgreSQL/EDB/PgBouncer, MySQL, DB2, Informix,
  Sybase?, Teradata?, HANA, MongoDB, Cassandra, Redis, Elasticsearch?, Couchbase, Neo4j, ClickHouse?, Hive?, Impala, HBase…); "?" = yalnız süreçle.
  ENVANTER KAHİNİ SON ÇARE (kullanıcı: "çok dikkate alıyor, en son baksın; o düzgün çalışsa burada tekrar yapmazdık"): Kahin satırları profilden
  çıkarılıp kanıtın en sonuna `KahinBas` bölümü olarak konur (kayıtlı KANIT'ta görünür). AskAsync 1. tur Kahin'siz; uygulaması bulunamayan
  portlar (Kahin verisi varsa) 2. turda Kahin'le yeniden sorulur: yalnız diğer kanıtlarla çelişmiyorsa, güven ≤ 50 (kodda da sınır),
  gerekçe "Kahin tahmini (son çare)". 2. tur hata verirse 1. tur sonucu kalır.
- **Segment tarama** (`Segment.cs`, `wwwroot/segment.html` — ayrı sayfa, sekme çubuğunda "Segment tarama ↗"; kullanıcı: "verdiğim segmentteki IP
  ve portlar full Carbon Black'ten gelsin, portlarda çalışan exe'ler, ayrı tab"): girdi segment adı (aynı adlı segmentlerin tüm CIDR'ları;
  seçici `/api/segment/liste`, ERT sunucu sayısıyla) ya da CIDR. Arka plan işi (IIS zaman aşımı yüzünden): POST `/api/segment/tarama
  {segment, saat}` (Yetki:Topoloji) → id; GET `/api/segment/tarama/{id}` (2 sn'de bir), `/durdur`. SPL: `local_ip="a.b.c.*"` desenleri (/24'e
  bölünür; /16–/23'te 16'dan çok /24 varsa a.b.*) + `where cidrmatch(...)`, `stats count dc(remote_ip) values(remote_ip) max(_time) by local_ip yon
  port yol` (gidende port boş). `Segment:MaxAdres`=65536, `MaxSaat`=168, `ParcaDesen`=16 desen/sorgu, `AyniAnda`=2, `SonucDakika`=60 (bellekte).
  Satır: IP, sunucu (ERT UNIXNAME), port, exe + yol + ürün etiketi (`EksikService.BilinenUrun` ya da teknoloji: SQL Server, Oracle, IIS…),
  bağlantı, istemci (uygulama adıyla), ERT durumu (kayıtlı / adı boş / ERT'de yok), son görülme. Filtreler: arama, ERT durumu, altyapı / dinamik /
  genel süreç (varsayılan gizli), sıralama; "yalnız giden" programlar ve "ERT'de var ama CB verisi yok" sunucular katlanır bölümlerde; CSV
  tarayıcıda (Yetki:Disari). Fake Splunk + sahte envanterle backend, mock'la arayüz test edildi.
  ETİKETLE TARAMA (kullanıcı: "sadece segment değil kayıtlı etiketler üzerinden de"): sayfada "Segment | Etiket" seçicisi (etiket listesi
  `/api/etiketler`), istek `{etiket, saat}`; IP'ler `env.TagIps` (ana etiket alt etiketleri de kapsar), `Segment:EtiketMaxIp`=2000,
  `Segment:ParcaIp`=40 IP'lik sorgular `(TERM(ip) OR …) local_ip IN (…)`. Sonuçta `Tur`, satır / sunucuda `Segment` (etikette sunucu adının
  altında gösterilir, CSV'de SEGMENT kolonu); `?etiket=` derin bağlantı. Sayfa adı "Segment / etiket tarama".
  PORT 0: önce varsayılan gizlendi; kullanıcı geri aldı ("port 0'lar kalsın, bir sürü iş uygulaması da var") → varsayılan GÖRÜNÜR, özet
  sayılara girer, "Port 0 (N)" kutucuğu (işaretli) ile gizlenebilir, satırda "port yok" notu.
  AI İŞ UYGULAMASI TAHMİNİ (kullanıcı: "sunucu sunucu AI'a gönderelim, exe'lerden çalışan iş uygulamalarını tahmin etsin, bir kolona"):
  "🤖 AI ile iş uygulamalarını tahmin et" (Yetki:Topoloji; 50+ sunucuda onay) → POST `/api/segment/tarama/{id}/ai {ips?, yeniden}`, GET `…/ai`
  (2,5 sn), `…/ai/durdur`. Tarama sonucundaki exe'ler sunucu başına `SurecAiExe`'ye çevrilir (gelen: port / bağlantı / istemci, giden: hedefler)
  ve IP kartındaki `SurecAi.RunAsync` (aynı prompt, genel süreçler ayıklanır, genel bilgiler dosyası) çağrılır; yalnız genel süreçli sunucu
  gitmez; `Segment:AiParalel`=3; sonuç bellekte (`_ai`, tarama işiyle). Arayüz: "AI tahmini" kolonu (exe adıyla eşleşme: uygulama, güven
  çubuğu, ERT karşılığı, rol; AI iş uygulaması saymadıysa "—", hata "AI hatası"), IP'nin ilk satırında sunucu özeti, sıralama / arama / CSV
  (AI TAHMİNİ, AI ERT KARŞILIĞI, AI GÜVEN, AI SUNUCU ÖZETİ), "yalnız giden" tablosunda da kolon. Mock'la arayüz test edildi.
  OTOMATİK + KAYITLI (kullanıcı: "düğme olmasın, segmenti seçeyim, satır satır tahmin etsin, kendi kaydetsin; tekrar tekrar tahmin etmeyeyim"):
  düğme kaldırıldı; tarama bitince sunucuda `AiBaslatAsync` kendiliğinden çalışır, sayfa `…/ai`yi izler, kolon sunucu sunucu dolar. Kayıt
  `Segment:AiTablosu` dbo.DF_SEGMENT_AI_TAHMIN (`sql/segment_ai.sql`: IP PK, SONUC JSON, EXELER (| ile iş exe yolları), TARIH, KAYDEDEN; yoksa
  bellekte + uyarı). Kayıtlı tahmini olan ve şimdiki iş exe'leri kayıttakilerin içinde kalan sunucu AI'a GİTMEZ (kayıt gösterilir, tarih ipucunda);
  yeni sunucu / yeni iş exe'si → sorulur ve MERGE ile yazılır. Durumda `Kayitli`, `Tarihler`, `Uyari`. POST `/ai {yeniden:true}` ucu duruyor
  (arayüzde düğme yok). Sahte veriyle: kayıtlı sunucu yeniden sorulmadı, yeni exe'li sunucu soruldu.
  "AI tahmini full boş" (canlı) üzerine sağlamlaştırma: AI işi kayıtlar okunmadan önce `_ai`ye yazılır (GET 404 vermesin), başlatma hatası
  `Uyari`ya düşer, iş programı yoksa açık uyarı; arayüz 404'te 80 sn bekler (eskiden 7,5 sn sonra vazgeçip kolonu boş bırakıyordu); exe eşleşmesi
  dosya adına indirgenir (AI tam yol, büyük harf ya da ".exe"siz yazsa da eşleşir).
  AI ÖNERİSİ = ERT EKSİKLERİ MOTORU (kullanıcı: "buradaki AI tahmini eksik.cs'deki olmalı, aynı yapı: silme, tekrar AI'a sorma"; aşağıdaki
  SurecAi tabanlı otomatik tahmin ARTIK ÇAĞRILMIYOR, uçları / DF_SEGMENT_AI_TAHMIN duruyor): kolon (IP, port) başına `EksikService` önerisi, aynı
  kayıt (DF_ERT_UYGULAMA_ONERI), aynı kanıt (SM, CB, Nessus, Kahin, katalog), durum AI / Onaylandı / Reddedildi / Elle. Sorulan satırlar: iş süreci
  (genel / altyapı / dinamik değil), port ≠ 0, ERT'de adı boş ya da hiç kaydı yok (`aiGerek`); kayıtlı olan (ERT adı var) "ERT'de kayıtlı", port 0 /
  yalnız giden "port yok". Tarama açılınca `POST /api/eksik/oneriler {ips}` ile kayıtlı öneriler okunur, önerisi OLMAYAN portlar kendiliğinden
  `POST /api/eksik/calistir {portlar: {ip: [port]}, yeniden:false, saat}` (EksikCalistir.Portlar → RunAsync Missing yerine verilen portlar; onaylı /
  elle hiç, AI önerisi yalnız yeniden'de sorulur; ortak `RunTodoAsync`; BuildAsync ERT'de satırı olmayan portu "ERT'de bu port için kayıt YOK" diye
  yazar) ile sorulur; yazma yetkisi gerekir, toplu yetkisi yoksa en çok 50 sunucu; başka eksik işi çalışıyorsa (409) bitince yeniden denenir.
  `/api/eksik/durum` 3 sn'de bir izlenir (ilerleme çubuğu: port), öneriler her turda yenilenir. Satırda ✓ onay / ✗ ret / ✎ elle / ⟲ sil
  (`/api/eksik/karar`), ↻ tekrar sor (o port, yeniden:true), "kanıtlar" (`/api/eksik/kanit`, sağ panel). CSV: AI ÖNERİSİ, GÜVEN, DURUM, ADI ERT'DE,
  GEREKÇE. Mock'la test edildi (yalnız önerisiz port soruldu, onay ve tekrar sor istekleri doğru).
  Kullanıcı: "Aç kayıtlı'ya basınca doğrudan AI devreye girdi, satırda tekrar sor / sil yok" → kayıttan açılınca AI KENDİLİĞİNDEN SORULMAZ (yalnız
  yeni tarama / ekle sonrası); araç çubuğunda "🤖 Önerisi olmayan N portu AI'a sor" ve çalışırken "AI'ı durdur" (`/api/eksik/durdur`). Satır
  düğmeleri her zaman görünür (yetkisizse pasif + ipucu): önerisizde "↻ AI'a sor" + ✎; öneride "✓ onay ✗ ret ✎ elle 🗑 sil ↻". `/api/surum`
  `eksikPortlar=true`; yoksa (eski DLL "portlar"ı tanımayıp TÜM eksikleri çalıştırırdı) sayfa AI'ı göndermez, "DLL eski" uyarısı verir.
  KAYITLI TARAMA (kullanıcı: "her seferinde tekrar sorgulamayayım, kaydetsin; istersem yeniden çek, ya da yeni sunucuları ekle"): tarama bitince
  sonuç `Segment:TaramaTablosu` dbo.DF_SEGMENT_TARAMA'ya (`sql/segment_tarama.sql`; PK TUR + AD, SONUC VARBINARY gzip JSON, SAAT, SUNUCU, SATIR,
  TARIH, KAYDEDEN; tablo yoksa bellekte + `KayitUyari`) MERGE. POST `/api/segment/tarama {…, mod}`: mod boş + kayıt var → Carbon Black sorgulanmaz,
  iş "Kayıttan açıldı" olarak hemen biter (AI tahmini yine kendiliğinden, kayıtlı tahminleri kullanır); `mod:"yeniden"` → baştan tarar, üzerine yazar;
  `mod:"ekle"` → kayıtta olmayan ya da CbVar=false olan sunucular (segmentte ERT IP'leri CIDR içinde, etikette TagIps) `IpSorgulari` (TERM) ile taranır,
  `Birlestir` o IP'lerin eski satırlarını yenileriyle değiştirir (Nessus da), aralık kayıttaki saat; eklenecek yoksa kayıt açılır + not. `SegmentIs.Kayit`
  (tarih, kaydeden, saat, sunucu, satır) ve `Not`. `GET /api/segment/kayitlar` (gövdesiz liste). Arayüz: seçili segment / etiketin kaydı varsa düğme
  "Aç (kayıtlı)" ve bilgi satırında tarih; sonuçta kayıt bandı ("📁 Kayıtlı tarama açıldı" / "💾 kaydedildi") + "➕ Yeni sunucuları ekle" ve
  "🔄 Yeniden çek" (onaylı; Yetki:Topoloji). gzip / birleştirme sahte veriyle, bant ve istekler mock'la test edildi.
  KATALOGDAN SEÇİM (kullanıcı: "hep yeni uygulama öneriyor, envanterden seçsin"): `SurecAi.RunAsync` prompt'una ERT eksiklerinin aday listesi
  (`EksikService.Adaylar` = Candidates, ajanlar hariç) "ERT KATALOĞUNDAN ADAY UYGULAMALAR" olarak girer, kural: önce listeden harfi harfine, yalnız
  uymuyorsa yeni=true. Sonra `SurecAi.Katalogla`: her uygulama `EksikService.Katalog` (= CatalogMatch: birebir / kelime / Secim) ile ERT
  APPNAME'e eşlenir → eşleşirse Uygulama = ErtKarsiligi = katalog adı, `AiAdi` AI'ın adı, `KatalogNot`; değilse `Yeni=true`. Kayıtlı (eski)
  tahminlere de okurken uygulanır. Arayüzde "ERT kataloğu" / "yeni · ERT'de yok" rozeti, ipucunda AI'ın adı ve katalog notu, CSV "AI ADI ERT'DE".
- **Benzer sunucular** (`Benzer.cs`, segment sayfasında "Portlar ve exe'ler | 🧬 Benzer sunucular" sekmesi; kullanıcı: "exe ve trafik parmak izini
  çıkarıp aynı amaçlı, yakın amaçlı, alakasızları anlayalım"): AI YOK. Parmak izi tarama sonucundan: exe (`exe:` ad, `klasör:` kökü; sürüm /
  sayı `Sade` ile atılır, genel süreçler hariç), port (altyapı / dinamik / 0 hariç), gelen (istemcinin ERT uygulaması, yoksa segmenti; LB GW
  hariç), giden (hedefin uygulaması / segmenti), nessus (servis, ürün, sertifika, DB instance). Benzerlik: bileşen başına IDF ağırlıklı Jaccard,
  iki tarafta dolu bileşenlerin ağırlıklı ortalaması (`Segment:Benzer:*Agirlik` exe .45 port .15 gelen .15 giden .15 nessus .10), exe yoksa 0;
  aday çiftler ters dizinle (sunucuların %60'ından çoğunda olan özellik aday üretmez). ≥ `Segment:Benzer:Ayni` (0,6) union-find → aynı amaçlı
  grup (ortak özellikler ≥%60 üyede, ERT dağılımı, ERT tutarsız = kayıtlı üyelerin ortak uygulaması yok, öneri = ERT'siz üyeye kayıtlıların
  yarısından fazlasındaki uygulama); `Yakin` (0,35)–Ayni → yakın amaçlı (grup çifti başına en yüksek, ERT'siz sunucuya eşin tek uygulaması
  "yakın" öneri); altı → alakasız; iş exe'si yok → zayıf. ERT benzerlikte kullanılmaz. Uçlar `GET /api/segment/tarama/{id}/benzer?ayni=&yakin=`,
  `…/benzer/karsilastir?a=&b=` (bileşen başına ortak / yalnız A / yalnız B). Arayüz: eşik seçicileri, istatistik, grup kartları, yakın tablo,
  alakasız / zayıf katlanır listeler, 2 sunucu seçip karşılaştırma paneli, CSV. Sahte envanterle test edildi (ODEME 4'lü grup, KREDI'li üye
  tutarsız, boş üyeye ODEME önerisi, SAS ikilisi yakın %56, foo alakasız, yalnız svchost zayıf).
- **Exe ilişki ağı** (`ExeAgi.cs`, kullanıcı: "exelerin ilişki ağını çıkaralım; topolojide etiketlileri çizdiğimiz gibi, aynı yapı"): segment
  taraması SPL'i artık gidende `port=remote_port` ve karşı IP'leri ilk 50 (`ornek`) tutar; TaraAsync her satırdan ham bağ üretir
  (`SegmentSonuc.Baglar`, `SegmentBag(Kaynak, KaynakYol, Hedef, Port, HedefYol, Baglanti)`: giden = exe → hedef IP:port, gelen = istemci IP → exe:port;
  bağlantı sayısı örnek IP'lere eşit bölünür; `Segment:ExeAgMaxBag`=60000; Birlestir yeni IP'lerin bağlarını değiştirir). `GET /api/segment/exeag?tur=&ad=`
  (kayıtlı tarama) ya da `?id=` (bellekteki iş), `&genel=true` → `ExeAgi.Build` KtSonuc: kutu = exe (aynı exe'yi çalıştıran sunucular; ntoskrnl →
  "HTTP.sys (ntoskrnl.exe)"), eleman `x:ip|exe` (Alt = exe yolu, Uygulama = ERT), bağ X@A → Y@B (B taranmış ve P'yi kabul eden exe biliniyorsa), aksi
  halde hedef `d:ip:port` "Dış · <ERT uygulaması / segment>" (taranmış ama exe yoksa "Exe görülmedi · …"); taranmayan istemciden gelen `c:ip`
  "Dış istemci · …" (taranmış ama giden olayı yoksa "İstemci, exe görülmedi · …"); genel / ajan süreçleri hariç (`genel` ile dahil); bağsız iş
  exe'leri de karo (trafiksiz). Eski kayıtta Baglar yok → 409 "Yeniden çek". Arayüz: topoloji.html `?exeag=1&tur=&ad=` → `ktExeRun` (kumetopo.js,
  `kt.exe`): aynı çizim (kutu / eleman bağları, kutu içi grup ERT uygulaması, Kutular Exe | Segment, sütun, yakınlaştırma), başlık / istatistik /
  düğmeler exe kipine göre (versiyon, yeniden sorgula yok; "← Segment / etiket tarama", "Genel süreçler"), karoya tıkla → `ktExeOpen` (exe yolu,
  ERT, bağlananlar / bağlandıkları, karşı karoya geçiş, IP topolojisi). Segment sayfasında sekmelerin yanında "🕸 Exe ilişki ağı ↗" (kayıt yoksa
  "Exe ağı için yeniden çekin"). `ktRun` / `ktShowSaved` `kt.exe`'yi sıfırlar. Sahte veriyle backend ve Playwright'la çizim test edildi.
  SADE GÖRÜNÜM (kullanıcı: "çok karmaşık olmuş; her exe tek kutu, dış istemci tek gözüksün, bağlantıya tıklayınca sağda panel açılsın, orada
  bağlantılar yazsın"): segment sayfasında üçüncü sekme "🕸 Exe ilişki ağı" (`st.view="exeag"`, `loadAg` → aynı `/api/segment/exeag`, `agHazirla`):
  KtSonuc kutu başına TEK düğüm (karo yok), tüm `c:` istemcileri tek "Dış istemciler" düğümü, `d:` hedefleri "Dış · <uygulama>" (turuncu); bağlar
  düğüm çiftine toplanır (satırlar sunucu sunucu saklanır). Yerleşim `agYerlesim`: istemciler 0. sütun, exe'ler en uzun yol derinliği (≤8),
  dış hedefler en sağ, sütun içi sıra önceki sütundaki bağlıların ortalaması; komşu sütun S eğrisi, sütun atlayan / geri ok kutuların altındaki
  kendi şeridinden; uçlar kenar boyunca dağıtılır; kalınlık log(bağlantı). Oka tıkla → sağ panel (`agPanel`): kaynak sunucu, hedef sunucu
  (uygulama), port, bağlantı; kutuya tıkla → sunucular, uygulamalar, bağlananlar / bağlandıkları (satıra tıklayınca o bağ). "Altyapı portları",
  "Genel süreçler" kutucukları; bağsız exe'ler katlanır listede; eski kayıtta "🔄 Yeniden çek". topoloji.html `?exeag=1` çizimi duruyor (sayfada bağlantı yok).
  KURUM + EXE BİLGİSİ (kullanıcı: "buraya da domain seçme ekleyelim, sağ panelde exe bilgisi de verelim"): `KtEleman.Kurum` (segment Domain,
  ExeAgi doldurur). Araç çubuğunda "Kurum: Tümü ▾" çoklu seçim (`st.agKurum`, boş / hepsi = tümü; kurum başına sunucu sayısı): yalnız seçili
  kurumların `x:` karoları kalır, dış hedef / istemciler bağı kaldıkça (`agHazirla` yeniden, sorgu yok). Sağ panelde `agExeBilgi`: exe adı +
  bilinen ürün + yollar, sunucu başına exe yolu, dinlediği portlar (ya da "yalnız giden"), ERT uygulaması ya da AI önerisi (durumuyla), Nessus
  teknolojisi, bağlantı, son görülme (tarama satırları + `st.on`); bağ panelinde kaynak ve hedef exe'nin bilgisi + satırlarda exe yolları.
  KUTU GİZLEME (kullanıcı: "bazı exe'leri çıkartabileyim: SiteScope, dış istemci ya da herhangi bir kutu"): kutunun üstüne gelince sağ üstte ×,
  ayrıca kutu panelinde "🙈 Bu kutuyu gizle" → kutu bağlarıyla çizimden çıkar (`agGizle`, `agYerlesim` gizli uçlu bağları ve bağsız listeyi atar).
  Gizlenenler localStorage `df.agGizli` (kutu adı küçük harf; "__ist" = Dış istemciler) → başka segment / etiket taramasında da gizli. Çizimin
  altında "🙈 Gizlenen kutular" şeridi (tıkla → geri göster, "+N başka taramalardan", "Tümünü göster").
- **Nessus / Tenable.sc** (`Nessus.cs`, kullanıcı: "teknolojisi ASP mi PHP mi"; diğer projedeki guide): CANLI TARAMA YOK — repository'deki
  birikmiş sonuçlar (`POST /rest/analysis`, sourceType cumulative, tool vulndetails, filtreler ip / pluginID / repository / lastSeen 0:MaxGun),
  kimlik `x-apikey: accesskey=…; secretkey=…;` (anahtarlar yalnız appsettings.Production.json). Kullanıcının reposu **121** (`Nessus:Repolar`).
  Bilgi plugin'leri (`Nessus:Pluginler`, boşsa varsayılan): 19506 kimlikli mi, 11936 OS, 45590 CPE, 10107 web sunucusu, 24260 HTTP başlıkları
  (X-Powered-By, X-AspNet-Version, PHPSESSID / JSESSIONID / ASP.NET_SessionId çerezleri → teknoloji), 22964 servis, 10863 SSL sertifika CN + SAN,
  34252 / 25221 portu dinleyen program, 20811 / 22869 kurulu yazılım (gürültü / Linux'ta yalnız teknoloji paketleri), 11219 / 10335 / 34220 /
  14272 açık port. pluginText `<plugin_output>` çözülür → `NessusSunucu` (OS, kimlikli, portlar: servis, web sunucusu, teknoloji, sertifika,
  dinleyen; yazılım; CPE). `ParcaIp`=100 IP / sorgu, `SayfaBoyutu`=2000, IP başına `CacheDakika`=60 önbellek, `IgnoreSslErrors` yalnız "nessus"
  HttpClient'ı. Segment tarama Carbon Black'ten sonra Nessus'u sorar (`NessusEkleAsync`; hata taramayı bozmaz → `NessusDurum`), sonuçta
  `Nessus` sözlüğü; arayüzde "Teknoloji (Nessus)" kolonu, sunucu altında OS, "Nessus'un açık gördüğü ama CB'de gelen bağlantısı olmayan
  portlar" bölümü, CSV kolonları. AI tahminine `NessusService.Metin` kanıt olarak eklenir (`SurecAi.RunAsync(..., ekKanit)`). Kurulum testi:
  `/api/nessus/repolar`, `/api/nessus/test?ip=&tum=true` (ham kayıt + plugin listesi + özet). Sahte Tenable sunucusuyla test edildi.
  UYGULAMAYI BULMAK İÇİN (kullanıcı: "1. madde komple"): 10144 / 10674 SQL Server instance (InstanceName / Version / tcp → port teknolojisi
  "SQL Server instance X" + `Veritabani`), 10658 Oracle TNS (sürüm, SID / SERVICE_NAME), 44401 Windows servisleri (yalnız Active; \Windows\
  ve `SurecAi.GenelExe` ayıklanır → `Servisler` ad / görünen ad / yol / hesap), 70329 çalışan süreçler (genel ayıklanır), 10395 SMB paylaşımları
  ($'lılar hariç). Ürün tespiti: ayrı hafif `listvuln` sorgusu (severity=0 bilgi bulguları, `Nessus:UrunTespiti`=true) — "… Detection /
  Installed / Version" plugin adlarından ürün adı (`UrunAdi`, genel / OS / .NET / ajanlar hariç), portluysa port teknolojisine. HTTP.sys:
  Server `Microsoft-HTTPAPI` → "HTTP.sys (IIS değil)"; `Metin` ntoskrnl / HTTP.sys portları için "IIS sitesi (w3wp, WEBSITE)" ya da "IIS DEĞİL —
  servislerden biri" + sertifika adı satırı. Nessus kanıtı artık ERT eksiklerinde de (`EksikService.BuildAsync`, DB ipuçlarından sonra,
  `Eksik:NessusMaxKarakter`=2500) ve IP kartı `/api/surec/ai`'de. Segment sayfasında sunucu altında "🛡 Nessus ayrıntısı" sağ paneli
  (portlar, DB instance, ürünler, servisler, süreçler, paylaşımlar, yazılım, CPE) ve 🗄 DB instance satırı.
  **TLS sürümü** (kullanıcı: "uygulamanın çalıştığı TLS versiyonu, IP aramadaki portlar / süreçler tablosunda"): `NessusPort.Tls` = portun
  KABUL ETTİĞİ SSL / TLS sürümleri (Nessus el sıkışması; istemcinin fiilen kullandığı sürüm DEĞİL — o AppResponse / PaloAlto ile ayrı adım).
  Plugin'ler: 56984 desteklenen sürümler, 20007 SSLv2/v3 (yalnız SSL eşleşmeleri), 104743 TLS 1.0, 121010 / 157288 TLS 1.1, 136318 TLS 1.2,
  138330 TLS 1.3 (sürüme özel plugin'de sürüm plugin ID'sinden — çıktıdaki "TLS 1.2'ye geçin" sayılmaz), 22964 "A TLSv1.2 server answered".
  `TlsSurumleri` → "TLS 1.0" / "SSL 3.0". `GET /api/nessus/ip?ip=` → `{acik, sunucu | hata}`; topoloji.html IP kartı "Gelen portlar ve
  çalışan süreçler" tablosunda "TLS (Nessus)" kolonu (SSL / TLS 1.0 / 1.1 kırmızı; "TLS yok" = Nessus portu gördü el sıkışması yok; "—" =
  port Nessus'ta yok; uç 404 ise kolon gizli). Segment sayfası Nessus hücresi / paneli / CSV de TLS gösterir.
- **TLS bağlantıları** (`TlsAkis.cs`, `GET /api/tls?ip=&saat=&port=`; kullanıcı: "IP arama ekranının üstünde gelen ve giden TLS bağlantıları,
  yeni bir tab"): AppResponse `tls_handshakes` kaynağı (canlıda `ArTlsTest/` deneme aracıyla bulundu; kolon listesi o araçla). Tüm kutular paralel,
  VifgIds destekli. Sırayla: group_by (istemci, sunucu, port, `tls.version`) + `sum_tls.session_attempted` → metriksiz gruplu → ham satırlar
  (`tls.version_name`; cihaz başına 100.000'de kesilir, `Kesik` uyarısı). Canlıda `tls.version_name` ile group_by "invalid group by" verdi;
  `tls.version` değeri 769/770/771/772 → TLS 1.0–1.3 (`SurumAd`). Filtre `(srv == ip) or (cli == ip)`; IP karşılaştırması biçimden bağımsız
  (`::ffff:`). Gelen = IP sunucu (port × sürüm; istemciler + ERT uygulaması / segment, ilk 50), giden = IP istemci (hedef IP × port × sürüm).
  Ayarlar `Tls:*` (kolon adları, `Saat`=24, `MaxSaat`=24, `BeklemeSaniye`=300, `CacheDakika`=30, `Sni` boş). Arayüz: topoloji.html IP sonucunun
  EN ÜSTÜNDE (durum satırının altı) "🔒 TLS bağlantıları" paneli (`tlsHtml` / `loadTls`; AppResponse kaynağı seçiliyse sorgudan sonra arka planda,
  değilse "Getir"): gelen / giden sürüm dağılımı (yüzde), "▼ Gelen TLS | ▲ Giden TLS" sekmeleri, SSL / TLS 1.0 / 1.1 kırmızı, kutu durumları,
  aralık seçici 1 / 6 / 24 saat. Uç 404 → "DLL güncel değil". Sahte AppResponse ile backend, mock'la arayüz test edildi.
  TOPLU (kullanıcı: "Excel'den bir kolon IP girip karşılığı olan port – uygulama – TLS versiyonu"): sekme çubuğunda "TLS toplu sorgu"
  (`wwwroot/tlstoplu.js`, `data-ui="tls"`, `?tls=1`; tarih / kaynak / Sorgula gizli). IP'ler metinden regex ile (satır / sekme / virgül; başlık ve
  port sütunu yok sayılır) ya da dosyadan: .xlsx / .xlsm (kullanıcı: Excel seçince kutuya ikili veri düşüp "IP bulunamadı" oldu → `ttXlsxMetni`:
  zip merkezi dizini elle okunur, xl/sharedStrings.xml + xl/worksheets/*.xml `DecompressionStream("deflate-raw")` ile açılır, etiketler atılır;
  dış kütüphane yok), .xls (OLE2: latin1 + UTF-16 olarak taranır), CSV / TXT (UTF-8, BOM'lu UTF-16); kutuya yalnız bulunan IP'ler yazılır. Arka plan işi (`TlsToplu`, IIS zaman aşımı): POST `/api/tls/toplu {ips, saat}` → id,
  GET `/api/tls/toplu/{id}` (3 sn), POST `…/durdur`; `Tls:TopluParca`=20 IP'lik gruplar sırayla, `Tls:TopluMaxIp`=500, iş 1 saat bellekte.
  Ortak çekirdek `TlsAkis.CekAsync(ips, …, gelenYalniz)` (tek IP paneli de kullanır); toplu yalnız GELEN (srv IN grup). Satır = IP × port:
  ERT portları ∪ TLS görülen portlar; uygulama ERT IP + port (port ERT'de yoksa IP'nin uygulaması "(IP)"); sürüm yüzdeleri, el sıkışması, istemci,
  not ("TLS el sıkışması görülmedi", "ERT'de bu port yok", "ERT'de kayıt yok…", grup hatası). Arama, "Yalnız eski sürüm", "Yalnız TLS görülen",
  Excel (CSV, `data-csv`, Yetki:Disari), IP bağlantısı yeni sekmede IP sorgusu. Mock'la arayüz, sahte AppResponse ile çekirdek test edildi.
  OLASI ŞİFRESİZ (kullanıcı: "şifreleme olmadan çalışıyorsa araya giren dinleyebilir" → "evet"; sonra "zaten trafiği çekiyorsun, ek veri
  çekmeyeceğiz aslında" → toplu sorguda trafik YOKTU, isteğe bağlı yapıldı): "TCP trafiğini de al" kutucuğu (kullanıcı: "baya yavaşlattı, seçeyim" →
  varsayılan KAPALI, seçim localStorage `df.ttSifresiz`; istek `sifresiz`, sunucu varsayılanı false; açıkken trafik sorgusu TLS sorgusuyla
  aynı anda başlar) → grup başına `TlsAkis.AkisAsync` (flow_tcp, srv IN grup, group_by srv ip + port + `Tls:AkisMetrikleri`
  [sum_tcp.new_connections, sum_traffic.total_bytes] → metriksiz gruplu → ham sayım). Satır `Sifreleme`: tls | sifresiz (trafik var, TLS yok) |
  protokol (trafik var, şifreleme protokolün içinde: 5985/47001 WinRM mesaj düzeyinde, 1433 TDS, 3389 CredSSP, 389 StartTLS, 445 SMB3, 1521 Oracle
  NNE, 22 SSH…; `TlsToplu.Protokol`, `Ipucu`) | trafikyok | bilinmiyor; `Trafik`. Trafikte görülen portlar da satır olur (≥ `Tls:DinamikPortBaslangic`
  =49152 hariç). Arayüzde "Şifreleme" + "TCP trafiği" kolonları (kutucuk kapalıysa gizli), "N portta olası şifresiz", "Yalnız olası şifresiz",
  olası şifresiz satır kırmızı; CSV ŞİFRELEME / ŞİFRELEME NOTU / TCP TRAFİĞİ. Tek IP panelinde bu ayrım henüz yok (IP sorgusunun flow_tcp verisi
  orada zaten var, ek sorgusuz eklenebilir).
  FQDN / ENDPOINT URL (kullanıcı: mailde "ilgili servislerin FQDN / endpoint URL bilgilerinin paylaşılması" istendi; elde IP + port, tek uygulama, ~1000 sunucu →
  "toplu TLS içine ekleyebiliriz"): "FQDN / endpoint URL bilgisini de al" kutucuğu (localStorage `df.ttFqdn`, istek `fqdn`) → grup başına TLS sorgusuyla aynı anda
  `TlsToplu.UcNoktaAsync`: (1) Carbon Black `(TERM(ip)…) remote_ip IN (…) direction=outbound domain=*` → `stats by remote_ip remote_port domain` (istemcilerin bağlanırken
  çözdüğü DNS adı; sunucunun üyesi olduğu VIP ip'leri de sorguya girer, VIP ip:port'taki ad üye portuna "DNS · VIP x" kaynağıyla yazılır; IP biçimli ad atılır), `Tls:UcCbSaat`=24;
  (2) IIS logu (HttpSys:IisIndexleri) `iis_sip IN (…)`, yol küçük harf + GUID → {guid}, sayı parçası → {n}, `coalesce(cs_host, cs_Host)` Host başlığı (alan çıkarılıyorsa),
  stats ip × port × yol; tarama / sağlık yolları (`HttpSysEslesme.IisTarama`, ilk parça) atılır; `Tls:UcIisSaat` (boşsa HttpSys:IisSaat=1); (3) Nessus `SunucularAsync`
  (port sertifika adları, TLS → https, sunucu DNS adı; `Tls:UcNessus`=true). Sorgu başına `Tls:UcSureSaniye`=180. `TlsTopluSatir` ek alanları: Fqdn [TlsFqdn(Ad, Kaynaklar, Sayi,
  Istemci)] (sıra: CB DNS > IIS Host > sertifika > sunucu DNS; en çok 12), Uclar [TlsUc(Yol, Istek, Istemci, Metot, Site)] (ilk 25), Url (ilk joker olmayan ad + http/https
  (TLS / sertifika / 443-8443-9443) + port; 80/443'te port yazılmaz), Website (ERT WEBSITE, IP + port), Vip (üye portu = satır portu → LbIp:LbPort (havuz)), Istenen.
  CB / IIS'te görülen portlar da satır olur. AppResponse hatasında FQDN açıksa satırlar yine üretilir (TLS kolonu "alınamadı"). IP + PORT GİRİŞİ: `ttCiftler` satırdaki IP'den
  sonraki ilk 1–65535 sayı (sekme / : / ; ayraçlı); Excel (`ttXlsxMetni`) artık satır yapısını korur (sharedStrings dizinle çözülür, <row> → satır, hücre → sekme);
  "Yalnız verilen portlar" kutucuğu (varsayılan açık) → istek `portlar {ip: [port]}` → yalnız o portlar (veri olmasa da satır). `Tls:TopluMaxIp` varsayılanı 2000.
  Arayüz kolonları: FQDN (kaynak rozetleri DNS / VIP DNS / IIS / sertifika / sunucu DNS, ilk 3 + açılır), Önerilen URL, Endpoint (IIS) (ilk 3 + açılır), VIP, IIS website (ERT);
  özet "N / M portta FQDN bulundu"; CSV IIS WEBSITE, VIP, ÖNERİLEN URL, FQDN, FQDN KAYNAKLARI, ENDPOINT URL'LERİ (URL + yol (istek)). Sahte envanter + sahte Splunk ile uçtan
  uca, Playwright'la Excel'den IP + port okuma denendi. IIS dışı (Java / nginx…) servislerde yol ağ verisinde yok (yalnız FQDN + port).
  GİDEN YÖN (kullanıcı: "TLS'e gelen / giden diye bakıyor muyuz" → toplu yalnız gelendi; "tek IP yazarsam full giden gelen ne varsa baksın", "portları belirt, gelen mi
  giden mi"): "Giden bağlantıları da al" kutucuğu (listede TEK IP varsa kendiliğinden işaretlenir, elle değiştirilirse dokunulmaz; istek `giden`) → `CekAsync(..., gelenYalniz: false)`
  (filtre srv OR cli), `GidenSatirlar`: IP'nin istemci olduğu el sıkışmaları hedef ip:port başına; `TlsTopluSatir.Yon` "gelen" | "giden", `Hedef`, `HedefSunucu`, Uygulama =
  hedefin ERT uygulaması; FQDN açıksa CB `local_ip IN (grup) direction=outbound domain=*` → hedef ip:port'a bağlanırken çözülen ad (`UcVeri.GidenDns`) ve TLS görülmeyen
  ama CB'de adıyla bağlanılan hedefler de satır ("TLS el sıkışması görülmedi"). Önerilen URL HTTP olmayan bilinen portlarda (DB, SSH, RDP, LDAP, MQ… `_httpDegil`) üretilmez.
  Arayüz: "Yön" (▼ Gelen / ▲ Giden) ve "Hedef (giden)" kolonları, Port altında "kendi portu" / "hedef portu", "Yön" süzgeci, özette gelen / giden sayıları; CSV YÖN, HEDEF IP,
  HEDEF SUNUCU. Şifresiz kontrolü (flow_tcp) yalnız gelen. Kolon sırası (kullanıcı: "TLS versiyonu kolonu gitti" — FQDN kolonları araya girince ekrandan taştı): IP, Sunucu,
  [Yön, Hedef], Port, Uygulama, [Şifreleme], TLS sürümü, El sıkışması, [TCP], İstemci, Not, sonra FQDN grubu (FQDN, Önerilen URL, Endpoint, VIP, IIS website).
  DIŞ KURUM / VIP DIŞ BACAĞI (kullanıcı: dış kurum tahsilatlarında gidenlerde URL yok, "muhtemelen VIP üzerinden gidiyor"; "yap bişeyler, yakalayalım"): (1) SNI —
  `TlsAkis.CekAsync(..., sniKol)`: FQDN / giden kipinde `Tls:TopluSni` (varsayılan `tls.sni`, boş = kapalı) ile önce "gruplu + SNI" denemeleri, olmazsa SNI'siz; hiçbir kutuda
  SNI'li deneme tutmazsa `_sniYok` (süreç boyunca denenmez). Gelen satırda SNI adları FQDN'e "SNI (AppResponse)" kaynağıyla (öncelik DNS ile aynı), giden satırda da.
  (2) Giden hedef VIP ise (`env.VipMembers(hedef, port)`): `Vip` "VIP · havuz X", `Uyeler` [TlsUye(Ip, Port, Dis (özel olmayan IP), Surumler, Sni, Adlar)] — `DisBacakAsync`
  grup başına: üye IP'lerinin TLS el sıkışmaları (`CekAsync` sunucu = üye; istemci F5 olduğundan sunucuya bağlanamayan bacak, `Tls:DisBacakMaxIp`=200) → üyenin sürümü + SNI
  ("SNI · dış bacak"); (3) Palo Alto (`Firewall:Index`, `Alanlar:DestIp/DestPort`): üyeler + doğrudan dış hedefler için `coalesce(Tls:FwAdAlanlari = url, dest_hostname,
  dest_name, server_name, sni, hostname)` → host "Firewall (Palo Alto)" kaynağıyla, URL yolu giden satırın endpoint'i (`TlsUc` Site "Palo Alto"); `Tls:DisBacakFirewall`=true.
  `DisUrl` = dış bacaktaki ilk ad + üye portu (TLS görüldüyse https). Arayüz: Hedef hücresinde "VIP · havuz", "→ üye ip:port [dış|iç] TLS rozeti + ad", "Dış adres: …";
  kaynak rozetleri SNI / SNI dış / FW; CSV DIŞ BACAK (VIP HAVUZ ÜYELERİ), DIŞ KURUM URL. Sahte envanter (çıkış VIP'i + dış üyeler) + sahte Splunk (CB giden DNS, PAN url)
  ile uçtan uca denendi; AppResponse SNI kolonu ve PAN alan adları canlıda teyit edilmedi.
  İLERLEME (kullanıcı: "%0'da bayadır"): yüzde = biten grup / grup; tek IP tek grup olduğundan iş bitene kadar %0 kalıyordu (TLS + CB / IIS / Nessus + dış bacak sırayla).
  `TlsTopluIs.Adimlar` (`TlsAdim` ad, durum, başlangıç, bitiş; grup başında sıfırlanır, `GrupBas`) — `CekAsync(..., ilerleme)` kutu başına "bağlanıyor / n. deneme: X (en çok N sn) /
  tamam · yöntem · satır / alınamadı", `Izle(j, ad, task)` CB DNS (gelen / giden), IIS, Nessus, flow_tcp, Palo Alto; dış bacak TLS ayrı etiketle. GET `/api/tls/toplu/{id}` →
  `gecen`, `grupGecen`, `adimlar` [{ad, durum, bitti, sure}]; arayüzde ilerleme çubuğunun altında adım listesi (`ttAdimHtml`, süren önce) ve "x dk geçti".
  SNI denemeleri `Tls:SniBeklemeSaniye`=120 (BeklemeSaniye'den büyük olamaz); SNI'li deneme zaman aşımına düşerse o kutuda kalan SNI denemeleri atlanır ve `_sniYok` işaretlenmez
  (yavaşlık kolon yokluğu sayılmaz).
  WEB.CONFIG (UCMDB) (kullanıcı: "boş geldi; web.config'ten çıkabilir dediler — MFUCMDB.CDM_CONFIGURATION_DOCUMENT_1, A_DOCUMENT_CONTEN(T) blob, Oracle"; sonra "önce sadece
  web.config görüntüle, bir kolonda olsun — ilk hedef bu"): `Ucmdb.cs` `UcmdbService` — Oracle'a DOĞRUDAN, yalnız okuma (`Oracle.ManagedDataAccess.Core`; bağlantı cümlesi
  `Ucmdb:Baglanti` yalnız appsettings.Production.json'da; isteğe bağlı `WalletKlasoru` / `TnsAdmin`). Şema ALL_TAB_COLUMNS'tan (CDM% tabloları): kimlik CMDB_ID, ad A_NAME,
  içerik ilk A_DOCUMENT_CONTEN* BLOB, üst A_ROOT_CONTAINER (yoksa / boşsa END1_ID–END2_ID'li COMPOSITION / CONTAINMENT tabloları), yol A_*PATH|LOCATION; hepsi `Ucmdb:*Kolonu`
  ile değişir; RAW kimlik HEXTORAW. DİZİN (arka planda, `OnbellekSaat`=12): LOWER(ad) LIKE `DosyaDesenleri` (varsayılan web.config), `MaxBelge`=50000; kapsayıcı zinciri seviye
  seviye (`MaxSeviye`=6) — bilinmeyen kimlikler CMDB_ID + (A_NAME | üst | A_PRIMARY_DNS_NAME) kolonlu dolu CDM tablolarında aranır (önce `DugumTablolari` CDM_NODE_1 / NT / UNIX…,
  sonra isabet alanlar; ad + üst bulununca durur), düğüme (düğüm tablosu ya da A_PRIMARY_DNS_NAME) varınca sunucu adı. IP → ERT UNIXNAME / IP → tam ve kısa ad eşleşmesi.
  İÇERİK: zip (PK, girdi adı eşleşen ya da ilk) / gzip / zlib / düz; BOM'lu UTF-8 / UTF-16, yoksa UTF-8, olmazsa 1254; `MaxBoyut`=5 MB. `Maskele`: bağlantı cümlesi Password / Pwd /
  AccountKey, key adında pass / secret / token / apikey / credential / sifre geçen appSettings değerleri (iki öznitelik sırası), password / validationKey / decryptionKey / clientSecret…
  öznitelikleri, gizli adlı setting değerleri, URL kullanıcı:şifre@ ve gizli sorgu parametreleri → *** (sayısı gösterilir). Uçlar (sayfa yetkisi Tls): POST `/api/ucmdb/belgeler {ips}`
  → {acik, hazir, hata, belgeler: ip → [{id, ad, sunucu}]} (dizin yoksa arka planda başlatır, hazir=false), GET `/api/ucmdb/belge?id=` (yalnız dizindeki kimlik; metin maskeli, biçim,
  boyut, zincir), GET `/api/ucmdb/tani?ip=` düz metin (bağlantı + Oracle sürümü, kolonlar ve seçilenler, 5 örnek belgenin boyut / ilk bayt / biçim / bulunan adres sayısı — içerik
  basılmaz, dizin istatistiği + zincir örnekleri, IP'nin belgeleri ve ayrıştırılmış adresleri). Arayüz: TLS toplu tablosunda Sunucu'dan sonra "web.config" kolonu (`ttWc`, `ttWcYukle`:
  satır IP'leri bir kez, hazırlanıyorsa 5 sn sonra; 403 / 404 / UCMDB tanımsızsa kolon gizli) → "📄 görüntüle (N)" / "yok" / "hazırlanıyor…"; pencere `#ttWcModal` (belge seçici,
  Kopyala, Esc / dışına tıkla kapatır, bilgi satırında maskelenen sayısı ve UCMDB zinciri). `Ayristir` / `Db` (WCF client = giden, service / baseAddress = gelen, appSettings /
  setting / rewrite URL'leri = giden, connectionStrings → yalnız sunucu / port / DB) YAZILDI ama şimdilik yalnız tanıda kullanılıyor — FQDN / giden satırlarına bağlamak sonraki adım
  (kullanıcı onayıyla). Sahte XML ile çözme (düz / zip / gzip / UTF-16), maskeleme (sızıntı yok) ve ayrıştırma; mock'la arayüz denendi; Oracle tarafı canlıda denenmedi.
  "HAZIRLANIYOR'DA KALDI" (canlı): zincir taraması her seviyede yüzlerce CDM tablosunu alfabetik sırayla geziyordu → tablo sırası: önce isabet alanlar, sonra `DugumTablolari`,
  sonra adı `_oncelik` (NODE / NT / UNIX / HOST / IIS / WEB / APP_POOL / VIRTUAL_DIR / J2EE / APPLICATION_SERVER / RUNNING_SOFTWARE / SOFTWARE…) olanlar, en son diğerleri; öncelikli
  olmayan ve isabetsiz tablolara kimliklerin `YeterOran`=0,98'i bulunduysa geçilmez; seviye başına `SeviyeSureSaniye`=120 sınırı (not düşülür); IN listesi 1000. İlerleme `_asama`
  ("seviye 2/6: 41/812 tablo · 3120/3400 kapsayıcı bulundu"), `/api/ucmdb/belgeler` → `asama`, `gecen`; arayüzde tablonun üstünde bant + hücrede süre; hata olursa bant kırmızı
  (tanı bağlantısıyla) ve dizin 5 dk geçmeden yeniden başlatılmaz (`_hataZaman`). Tanı artık tam dizini KURMAZ: tam listenin durumu + 5 örnek belgeyle zincir (seviye başına 20 sn),
  seviye başına hangi tablodan kaç kimlik geldiği (`seviye N: CDM_X → k kimlik`), bağ tabloları; IP belgeleri yalnız tam liste hazırsa.
  PORT BAZLI AYRIM (kullanıcı: "bir sunucudan çok sayıda web.config çıkıyor, port bazlı ayırabilir miyiz"; "UCMDB'de port bazlı ayrım var mı"): `PortAta(ip, belgeler, env, iis?)`
  → `UcmdbPortlu(Belge, Portlar, Neden)` sırayla: (1) UCMDB binding — zincir taramasında düğüm olmayan tablolarda `PortKolonu` (A_…BINDING(S) / PORT(S) / PORT_NUMBER…, tür /
  protokol / durum / ad değil) değerleri `PortCoz` ("http/*:80:", "*:443:host", "8443", URL) ile, en yakın kapsayıcının portları (`UcmdbBelge.Portlar`); (2) ERT WEBSITE (IP + port) ↔
  `UcmdbBelge.Adlar` (zincirdeki site / dizin / havuz adları + `Klasorler`: belge yolundaki inetpub / wwwroot / sites… dışındaki klasörler) `AdEs` (harf-rakam eşit ya da ≥5 harfte
  içerme); (3) IIS logu (`HttpSysEslesme.IisAsync`, port → uygulama /app) ↔ Adlar; (4) önbellekteki içerikten WCF gelen baseAddress portu. `/api/ucmdb/belgeler` (1)–(2)(+4) ile
  `portlar`, `neden`; yeni GET `/api/ucmdb/ip?ip=` pencere açılınca IIS logu + `IcerikleriYukleAsync` (tek bağlantı, `PencereMaxBelge`=200) ile inceltir. Arayüz: hücre satırın
  portuna düşen sayı ("📄 görüntüle (n)" + "+k port belirsiz"; yoksa "port belirsiz (k)" / "diğer portlarda (n)", giden satırda tümü); pencerede seçici optgroup'lu (satır portu
  önce, diğer portlar, "Port belirlenemeyen"), bilgi satırında "Port 443 (neden)", `#ttWcPortNot` inceltme durumu; incelme sonrası `ttWc.belgeler[ip]` güncellenir, tablo yeniden çizilir.
  TEST DÖKÜMÜ (kullanıcı: "o tablonun alanlarını çekip bir yere yazabilir miyiz test olarak"): GET `/api/ucmdb/dokum?tablo=CDM_X&ornek=5` (yalnız şemanın CDM% tabloları; kolon +
  tip + LOB / port kolonu işareti + örnek satırlar, belge tablosunda yalnız web.config satırları) ya da tablo verilmeden zincir dökümü `?ip=` (o sunucunun belgeleri, yoksa örnek;
  `ornek`=3): belge satırı, seviye seviye kapsayıcının bulunduğu tablolar ve dolu kolonları (`DokumSeviyeSaniye`=40, kimlik başına ilk 3 tablo), her kaydın TÜM bağ tablolarındaki
  (END1_ID / END2_ID) bağları ve bağlı ENDPOINT / PORT / BINDING / URL tablolarındaki kayıtlar; `&indir=1` → .txt. LOB okunmaz, PASS / PWD / SECRET / TOKEN / CREDENTIAL / PRIVATE
  adlı kolonlar ***; UCMDB'ye yazılmaz. Sahte verilerle PortCoz / Klasorler / PortAta ve mock'la arayüz denendi.
  IP UCMDB'DEN + YOL KOLONU (kullanıcı: "bu tabloda IP alanı yok, nasıl getirdin" — cevap: zincirle sunucu ADINA çıkılıyor, IP ERT UNIXNAME'den; "en sağlam yolu deneyelim,
  UCMDB'den alabiliyorsan onu al; web.config'in path'i alan olarak var, onu da tabloya getir"; kullanıcı Oracle'a sorgu atamıyor): `DugumIpAsync` — zincirin vardığı düğüm
  kimlikleri için (1) düğüm satırlarındaki IP kolonları (A_…IP / IP_ADDRESS / ADDR; tür / maske / gateway… değil; zincir taramasında okunur, `Ci.Ip`), (2) adında IP_ADDRESS
  geçen CDM tablolarında üst kolonu düğüm olan kayıtların A_NAME'i, (3) COMPOSITION / CONTAIN bağ tabloları ↔ IP tablosu JOIN (iki yön), `IpSureSaniye`=180; `IpGecerli`
  (127. / 0. / yayın değil). `UcmdbBelge.Ipler`, `Dizin.IpBelge` (IP → belgeler). `Belgeler(ip)`: önce UCMDB IP'si bu IP olanlar (`Eslesme` "UCMDB IP"), sonra yalnız UCMDB'de IP'si
  OLMAYAN sunucuların ERT ad eşleşmesi ("ERT sunucu adı"; aynı adlı başka IP'li sunucu karışmasın). YOL: `Ucmdb:YolKolonu` ya da A_PATH / A_DOCUMENT_PATH / A_FILE_PATH / A_FULL_PATH /
  A_RESOURCE_PATH / A_DOCUMENT_LOCATION / A_LOCATION / A_FILE_LOCATION, yoksa metin tipli A_…PATH|LOCATION|DIRECTORY|FOLDER; `UcmdbBelge.Yol` ayrı (Ad artık yalnız A_NAME), Klasorler
  yoldan. Tanıda "UCMDB IP'si bulunan belge N / M · yol kolonu X", IP tabloları, kaynak başına satır sayısı. Uçlar `yol`, `ipler`, `eslesme` döner. Arayüz: hücrede düğmenin
  altında yollar (`ttWcKisa` "…\KrediWS\api", en çok 3 + "N daha", tıkla → o belge açılır `data-wcid`), seçicide tam yol, bilgi satırında tam yol + "IP eşleşmesi: UCMDB IP /
  ERT sunucu adı" (ipucunda UCMDB IP'leri); CSV'de "WEB.CONFIG (UCMDB)" (satır portundakiler + "(port belirsiz)"). Mock'la denendi; Oracle'da denenmedi.
  CANLI KOLONLAR (kullanıcının dökümü, MFUCMDB.CDM_CONFIGURATION_DOCUMENT_1, 65 kolon): CMDB_ID RAW, A_ROOT_CONTAINER VARCHAR2 (RAW değil — metin kimlik), A_NAME,
  A_DOCUMENT_CONTENT BLOB, A_DOCUMENT_CONTENT_TYPE, A_DOCUMENT_SIZE, A_DOCUMENT_CHECK_SUM, A_DOCUMENT_LAST_UPDATE_TIME NUMBER, A_DOCUMENT_EXTENSION, A_CODEPAGE,
  A_RESOURCE_PATH, A_CONTENT_LOCATION, A_DISPLAY_LABEL, A_USER_LABEL, A_PORT_APPLICATION_TYPE (port numarası değil), A_KRITIKLIK_SEVIYESI… — IP / port kolonu YOK.
  Yol artık birden çok kolon (`Sema0.Yol` liste; varsayılan sıra A_RESOURCE_PATH, A_CONTENT_LOCATION, …; `Ucmdb:YolKolonu` virgüllü verilebilir), satırda ilk dolu olan;
  tanıda "yolu dolu belge". IP kaydının üst kolonu metinse karşılaştırma UPPER(...) ile (RAW onaltılık büyük harf).
  SEGMENT / VLAN (kullanıcı: "TLS sorgusunda sütun olarak segment VLAN bilgisi de ekleyelim mi"): `TlsTopluSatir.Segment / Vlan / Cidr / HedefSegment / HedefVlan` —
  `TlsToplu.SegmentEkle` (env.FindSegment: ad = EPG > ApplicationProfile > açıklama > CIDR), grup sonunda tüm satırlara (hata satırları dahil). Arayüz: Sunucu'dan sonra
  "Segment / VLAN" kolonu (`ttSegHtml`: ad, altında "VLAN x · CIDR"), giden satırda Hedef hücresinde hedefin segmenti + VLAN; aramada segment / VLAN / CIDR; CSV SEGMENT, VLAN,
  CIDR (+ giden: HEDEF SEGMENT, HEDEF VLAN).
  FİLTRE UZUNLUĞU + 2000 IP (canlı: "Dış bacak: [Finart] Rapor hata verdi … steelfilter parsing_error … got ` o` at position 3919" — dış bacakta 200 üye IP'si tek filtredeydi,
  AppResponse ~3.900 karakterde kesiyor): `TlsAkis.CekAsync` artık sarmalayıcı — filtre `Tls:FiltreMaxKarakter`=3000'i aşacaksa IP'ler açgözlü parçalara bölünür, her parça
  `CekTekAsync` (eski gövde) ile ayrı rapor, satırlar birleşir, kutu durumu birleşir, mesaj "N IP K parçada soruldu"; ilerleme "parça i/K (n IP) · …". Sahte HTTPS AppResponse
  (3.900 üstünü reddeden) ile: 200 IP → 2970 + 2518 karakter, 200 satır; 20 IP gelen+giden tek rapor (1066). 2000 IP İÇİN: iş temizliği yalnız BİTMİŞ işleri bitişten 1 saat sonra
  siler (`BittiZaman`; eskiden başlangıçtan 1 saat sonra süren iş de silinirdi → "iş bulunamadı"); GET `/api/tls/toplu/{id}?den=N` yalnız N'den sonraki satırlar (`den`, `satirToplam`;
  satırlar yalnız eklenir), arayüz birleştirir (eski DLL'de den yok sayılır, tümü gelir); tablo ilk `TT_MAX_SATIR`=1000 satırı çizer (not satırı; arama / süzgeç / CSV tüm satırlarda).
  Mock'la: den 0 / 1200 / 2400, 1000 satır çizildi, arama 3600. satırı buldu. Süre: grup (Tls:TopluParca=20) sırayla; 2000 IP = 100 grup, saatler sürebilir.
- **web.config taraması** (`WebConfigTarama.cs`, `wwwroot/webconfig.html`, sekme çubuğunda "web.config taraması ↗"; kullanıcı: "verdiğim IP'lerde ya da segmentlerde ya da tüm
  ağda web.config taraması; text arayacağız; içinde http / https geçen adresleri ve exe geçen yerleri arasam da aramasam da listelesin — çok önemli"; "Nessus'un DMZ kategorisini de
  ekleyelim"; "sonuçları Excel'e de çıkalım"): `UcmdbService` artık `partial`. KAPSAM (`WcKapsam`, birleşebilir): IP'ler (`Belgeler(ips)`: UCMDB IP'si / ERT adı), segment adları
  ya da CIDR'lar (belgenin sunucu IP'lerinden biri — UCMDB IP'leri, yoksa ERT UNIXNAME → IP — segment Label / Cidr eşit ya da CIDR içinde), Nessus DMZ (`Asset` boşsa
  `Zafiyet:DmzAsset`; `NessusService.AssetBulAsync` + `AssetIpleriAsync` → `Icerir(ip)`, iş başlamadan çözülür, hata hemen döner), "tüm ağ" (dizindeki bütün belgeler, sunucusuz
  olanlar dahil; diğerlerini yok sayar). İŞ (`WcIs`, `_wcIsler`, bitmişler 2 saat sonra silinir, aynı anda `Ucmdb:WebConfigAyniAnda`=2): dizin hazır değilse beklenir; içerik
  `IcerikAsync` ile `WebConfigParca`=100'erli, Oracle koparsa en çok 3 kez yeniden bağlanıp kaldığı belgeden sürer; her belge `Coz` → `Maskele` → `WcCikar` (satır satır: http /
  https adresleri — HTML karşılıkları çözülür, `;` ayırır, `Temizle`, host / port / yol, satırın anahtarı key > name > contract > id; ".exe" geçen yerler — geriye ayraca kadar,
  boşluklu yollar dahil, satır metni; belge başına en çok 500 adres / 300 exe) + verilmişse `WcAra` (satır içinde terim, büyük / küçük harf seçmeli, belge başına 200) → `WcBelge`
  (IP, sunucu, segment, UCMDB IP'leri, eşleşme yolu, yol, boyut, biçim, maske, sayılar, hata), `WcUrl`, `WcExe`, `WcEslesme`. MASKELİ metin gzip'li bellekte (`Icerik`,
  `WebConfigMaxBellekMB`=300; dolarsa `IcerikKesik`) → YENİ ARAMA Oracle'a gitmeden (`WcYenidenAra`, `WebConfigMaxEslesme`=20000); şifre aranırsa bulunmaz (maskeli).
  Tarama içerikleri servis önbelleğine (`_icerik`) YAZMAZ (tüm ağda bellek). Uçlar (sayfa yetkisi Tls): GET `/api/ucmdb/segmentler` (segment listesi + dmzAsset adı; /api/segment/liste
  Segment yetkisi istediği için ayrı), POST `/api/ucmdb/tara {ips, segmentler, dmz, asset, tum, aramalar}` → id, GET `/api/ucmdb/tara/{id}?db=&du=&de=&da=` (artımlı listeler),
  POST `…/durdur`, POST `…/ara {aramalar, buyukKucuk}`, GET `…/excel?ara=&ara=&bk=` (Yetki:Disari; `WcExcel` → `Xlsx.Yaz` 7 sayfa: Özet, web.config, Adresler (benzersiz / tüm
  satırlar), EXE (benzersiz / tüm satırlar), Arama; hücre 32.000 karakterde kesilir). Sayfa: kapsam kutuları (IP metinden regex, segment datalist + çip, Nessus DMZ + başka asset
  adı, tüm ağ onaylı), aranacak metinler (satır başına bir, "Ara" taramadan sonra bellekte arar), ilerleme bandı (okunan / toplam, aşama, kapsam, bellekteki MB), kartlar (tıkla →
  sekme), sekmeler Arama sonuçları | HTTP / HTTPS adresleri | EXE geçen yerler | Belgeler, "Benzersiz | Tüm satırlar", http / https süzgeci, metin süzgeci, ilk 1000 + "daha fazla",
  satıra tıkla → web.config penceresi (satır numaralı, terimler işaretli, ilgili satır vurgulu ve ortada, Kopyala, Esc), "Bu sekme (CSV)" ve "📊 Excel (tüm sonuçlar)".
  `?ip=a,b` derin bağlantı. Test: WcCikar / WcAra / WcExcel (openpyxl ile 7 sayfa) / WcYenidenAra küçük test projesinde; sayfa mock'la Playwright'ta; Oracle ve Nessus canlıda denenmedi.
  ZAFİYET GÖSTERGELERİ + TÜM *.config (kullanıcının e-postası, "açıklık için istemiştim"; "sana bırakıyorum, net anlayabileceğimiz indikatörler koy"): kurallar
  `Ucmdb:ZafiyetKurallari` [{Ad, Desen, Regex, BuyukKucuk, Derece kesin | olasi | kontrol, Aciklama}], boşsa `VarsayilanKurallar`: "idunno" kesin zafiyetli (düzeltilmiş sürüm yok),
  regex `<add[^>]*anticsrf` (harf duyarsız) zafiyetli olabilir, "csrfsettings" ek kontrol. Her taramada (arama olsun olmasın) TÜM metinde aranır (`WcKuralAra`; çok satırlı
  `<add …>` da yakalanır, satır = eşleşmenin başladığı satır, metin tek satıra indirilir; kural başına belge başına 50, regex zaman aşımı 2 sn) → `WcBulgu` (Belge, Kural, Derece,
  Satir, Metin), `WcBelge.Bulgu`. Serbest arama da aynı motor (`WcDesen` düz metin → Regex.Escape; `WcAra(id, metin, desenler, max)`), "Düzenli ifade (regex)" seçeneği
  (`WcTaramaIstek.Regex / BuyukKucuk`, `WcAramaIstek.Regex`, excel `rx`); geçersiz regex 400. DOSYALAR: dizin artık `DizinDesenleri` = DosyaDesenleri ∪ `Ucmdb:TaramaDesenleri`
  (varsayılan [".config"]) — applicationHost.config, machine.config, app / exe .config'ler de dizine girer (dizin büyür; `MaxBelge` sınırına dikkat); `Belgeler(ips, env, hepsi=false)`
  ve dolayısıyla TLS sayfası / port eşlemesi yalnız `WebConfigMi` (DosyaDesenleri) olanları görür, tarama `hepsi=true`. `DosyaTuru(A_NAME)` web.config | applicationHost.config |
  machine.config | diğer; `WcBelge.Tur`; istek `Turler` (boş = hepsi) süzer, `WcIs.KapsamTur` süzgeçten önceki sayılar. IP kapsamında UCMDB'de hiç config'i olmayan IP'ler
  `WcIs.BelgesizIpler` (mesaj + Excel özet). TANI: GET `/api/ucmdb/dosyaturleri?yenile=` (`DosyaTurleriAsync`, 1 saat önbellek, yalnız sayım): tabloda LOWER(A_NAME) LIKE
  '%config%' ada göre ilk 150 + adında / yol kolonlarında applicationhost / machine.config geçen satır sayısı + dizindeki tür başına belge / sunucusu bulunan / sunucu / UCMDB IP'li.
  GET `/api/ucmdb/kurallar`. Tarama durumu `?dz=` (bulgular artımlı), `kurallar`, `belgesizIpler`, `kapsamTur`. Excel sayfaları: Özet (+ dosya türü ve kural başına satır / dosya /
  sunucu, UCMDB'de config'i olmayan IP'ler, "DLL içine gömülü modül config taramasıyla bulunamaz" notu), Zafiyet (derece, kural, açıklama, sunucu, IP, segment, dosya türü, yol,
  satır, eşleşen satır), Sunucu özeti (web.config sayısı, applicationHost / machine.config var | "UCMDB'de yok", diğer, kesin / olası / kontrol, en yüksek derece), Dosyalar, adresler,
  exe, arama. Sayfa (başlık "config taraması", sekme çubuğunda "config taraması ↗"): "Dosya türleri" kutucukları, "Zafiyet göstergeleri" kutusu (kurallar rozetli), "🩺 UCMDB'de hangi
  config'ler var?" (tanı tablosu: tür × UCMDB'de / adı-yolu geçen / dizinde / sunucusu bulunan / UCMDB IP'li + en sık adlar) ve "📋 Tanı özeti" (pencerede seçili metin: yalnız sayılar
  ve ayarlar, IP'ler x.x.x.x — kullanıcı bunu iletir), sekmeler ⚠ Zafiyet göstergeleri (varsayılan) | Sunucular | Arama | HTTP / HTTPS | EXE | Dosyalar, derece süzgeci, kartlar
  (zafiyet: derece başına sunucu sayısı; sunucu: applicationHost / machine.config'i olan; dosya: tür sayıları), pencerede kural eşleşmeleri kırmızı (`mark.z`), arama sarı;
  vurgulama ham metinde regex ile (`vurgula(metin, desenler)`). Test: uct'de çok satırlı <add, Idunno, CSRFSettings, DosyaTuru, regex arama, geçersiz regex, Excel 9 sayfa; mock'la sayfa.
  WEB FORMS İŞARETİ (kullanıcı: "Web Forms varsa işaretle, arama yaparken"; Idunno.AntiCsrf yalnız Web Forms'ta anlamlı; Carbon Black'te DLL izi yok — Splunk'a moduleload / filemod
  gelmiyor, binaryinfo yalnız md5): `WcWebForms(metin)` yalnız tür web.config olan ve .NET'in kök config'i olmayan (`FrameworkConfig`: yolda Microsoft.NET\Framework) dosyalarda;
  göstergeler (http(s) adreslerindeki ".aspx" atılarak, satır satır): .aspx sayfası (defaultDocument, loginUrl…), tagPrefix (Web Forms denetimleri), ViewState ayarları (enableViewState(Mac),
  viewStateEncryptionMode, enableEventValidation, maxPageStateFieldLength), PageHandlerFactory, ASP.NET AJAX (System.Web.Handlers.ScriptModule, AjaxControlToolkit); ASP.NET Core
  (aspNetCore) ve MVC (System.Web.Mvc, yalnız <pages namespaces>) göstergesizse işaretlenmez. `WcBelge.WebForms / WfNeden` ("ViewState ayarı (satır 3); …"). Sayfa: "Web Forms" rozeti
  (zafiyet / dosyalar; ipucunda gösterge), Sunucular'da "Web Forms" kolonu, "Yalnız Web Forms" süzgeci (zafiyet, sunucular, dosyalar), sunucu kartında sayı, tanı özetinde satır;
  Excel: Dosyalar WEB FORMS + GÖSTERGESİ, Sunucu özeti WEB FORMS, Zafiyet WEB FORMS (DOSYA), Özet sayısı.
- **Fiziksel envanter** (`FizikselEnvanter.cs`, `wwwroot/fiziksel.js`, sekme "Fiziksel envanter" `data-ui="fiziksel"`, `?fiziksel=1&kabin=&host=`;
  claude.ai sohbetindeki görev tanımı: "Bu kabin kapanırsa hangi uygulamalar etkilenir?"): veri doku DB'de HAZIR — NlyteCollector (nlyte OData →
  dbo.NlyteRaw) + `EXEC dbo.sp_KabinZincirYenile` doldurur; DeltaFlow YALNIZ OKUR (yazmaz, prosedürü çağırmaz). Betik kullanıcıda
  (`sql/05_kabin_zincir_tablolar.sql`, repoya henüz eklenmedi). `dbo.KabinHostVm` (VM başına satır: Kabin, KabinUYukseklik, KabinUSirasi, KabinBosU,
  HostUAlt, HostUYukseklik, HostYuz Front/Rear, HostModel, ChassisAdi, BladeSlot, Konum, FizikselHost, HostSeri, CabinetAssetID, Cluster, HostFqdn,
  Vm, Ip, DnsName, Os, Uygulamalar / Muhafizlar ' | ' ile, Kaynak MSY/ÇSY, Kapsam, EslesmeTuru, YenilemeZamani) + `dbo.NlyteHostEslesme`
  (EslesmeTuru='Yok' → "nlyte'ta eksik host'lar"; tablo yoksa KabinHostVm'den türetilir). `SELECT *`, kolonlar ADLA okunur (tip / sıra farkına
  dayanıklı; sayılar float'tan da). `FizikselService` EnvanterService kalıbı: bellekte, `Fiziksel:CacheMinutes`=30, `POST /api/fiziksel/yenile`;
  tablo adları `Fiziksel:Tablolar:*`, bağlantı ConnectionStrings:Envanter (`Fiziksel:Baglanti` ile değişir); tablo yoksa (208) "sp_KabinZincirYenile
  çalıştırılmamış". `Olustur(vmler, eksik, tablodan)` anlık görüntü (sahte veriyle test için de). Uçlar: `/api/fiziksel/ozet` (kapsam sayıları,
  son yenileme, odalar), `/oda?ad=`, `/kabin?ad=`, `/ara?q=` (en çok 50; IP > VM > host > kabin > uygulama > cluster), `/eksik` (+`?csv=1`, Yetki:Disari),
  `/ip?ip=`. Kabin adı → oda / sıra / no (`Parcala`, aşağıda KABİN ADI → ODA) → oda planı; uymayanlar oda "Diğer". Kapsam: varsayılan `Fiziksel:AnaKapsam`="Ana veri
  merkezi" (nlyte yalnız Esenyurt); diğerleri yalnız sayı. Kabin U kapasitesi = KabinUYukseklik (NULL → 42) ya da en üst dolu U. U1 yönü
  `KabinUSirasi`: değerler kesin bilinmiyor (kullanıcıya soruldu) → `Fiziksel:UstSirasiIfadeleri` doluysa o değerler "U1 üstte", boşsa "top…/yukar…/
  TopToBottom/descending" ile başlayan. "Kabin kapanırsa" etkisi: uygulamanın bu kabindeki VM'i / ana DC'deki toplamı, başka kabinler; sıra "tamamı bu
  kabinde" > "başka kabin yok (kabini bilinmeyen VM var)" > "başka kabinlerde de var". Arayüz üç bölme: oda planı (satır = sıra harfi, sütun = no;
  renk VM sayısı, tek ton; "Diğer kabinler"), ölçekli kabin çizimi (13 px/U, U etiketleri 1/5'in katları, Ön | Arka seçici — yeni kabin Ön ile açılır,
  chassis kutusu içinde yuva sırasıyla blade'ler, "U bilgisi yok" listesi), sağ detay (host: cluster, FQDN, model, seri, U, chassis, eşleşme; VM
  tablosu, IP → IP sorgusu, uygulama → `?app=` yeni sekmede; "Bu kabinin uygulamaları" / "Kabin kapanırsa" tabloları). Arama: sonuç düğmeleri, eşleşen
  kabinler planda turuncu halka. Altta katlanır "nlyte'ta eksik host'lar" + CSV. IP kartında `#fizKonum` satırı (`loadFizIp`, sorgudan sonra; ana DC
  dışıysa "… — nlyte kapsamı dışı", kabin yoksa "kabin bilinmiyor"; kabin bağlantısı derin bağlantıyla açar). Sahte veriyle backend (fiztest
  düzeneği, scratch) ve Playwright'la arayüz test edildi. Sonraki: "Kabin kapanırsa" için AI etki analizi; KabinBosU ile "taşınacak host'lara yer var mı?".
  TÜM KABİNLER + CİHAZLAR (kullanıcı: "eksik kabinler var, bilgisi boş da olsa tümü gelsin; bazı kabinlerde 2 host var, mümkün değil"; canlıda
  NlyteKabin 640 / KabinHostVm'de 59 kabin; DR4-B5'te nlyte'ta 7 sunucu, DF'de 2): KabinHostVm VM'DEN başladığı için yalnız VM'li + eşleşen ESX'leri
  bilir. Artık `dbo.NlyteKabin` (CabinetAssetID, KabinAdi, KabinUniqueAd, KabinUYukseklik decimal, KabinUSirasi, BosU, BitisikBosU) ve `dbo.NlyteSunucu`
  (Rid, AssetID, AssetName, Ad, AdKisa, AdOneksiz, AdTiresiz, UniqueAssetName "ad (Room=SISTEM ODASI-4 - 3 - Area (…))", SerialNumber, CabinetAssetID,
  RecordStatus, OperationalStatus, UAlt, UNumara, UEtiket, Yuz, UYukseklik, Model, MalzemeAdi, MalzemeTip…) da okunur (`Fiziksel:Tablolar:NlyteKabin /
  NlyteSunucu`; yoksa eski davranış, özet `nlyte=false`). Plan = NlyteKabin ∪ KabinHostVm kabinleri; karo: VM'li (mavi ton), cihaz var VM yok (gri
  çizgili `vmsiz`), cihaz yok (kesikli `bosk`) + lejant. Kabin çizimi = aktif (RecordStatus Active / NULL) nlyte cihazları; VM'li host grubu cihaza
  ÖNCE SerialNumber = HostSeri, sonra ad (AssetName/Ad/AdKisa/AdOneksiz/AdTiresiz ↔ FizikselHost / HostFqdn kısa adı) ile eşlenir; eşleşmeyen VM'li
  host'lar yine çizilir ("nlyte cihaz listesinde eşleşmeyen" notu); VM'siz cihaz gri çizgili "VM yok", detayda tür / durum / AssetID ve açıklama;
  aktif olmayan kayıtlar katlanır listede. Başlık "N cihaz · M VM'li host · K VM". Konum: KabinHostVm.Konum ya da UniqueAssetName / KabinUniqueAd
  içindeki "Room=…" (dıştaki tek ')' atılır). Arama nlyte cihaz adı / serisi ("Cihaz") ve VM'siz kabin adlarını da bulur. KabinUSirasi canlıda yalnız
  `Ascending` / NULL → U1 altta (mevcut kural doğru).
  TÜM CİHAZ TÜRLERİ (kullanıcı: "sadece sunucuları çekiyoruz, ne varsa her şey"; veri tarafı NlyteCollector'da genişletildi: Servers, Chassis,
  Networks, Peripherals, PowerStrips / PDUs, UPSs, KVMSwitches, StaticSwitches, Auxiliaries, AirConditionings): cihazlar önce `dbo.NlyteCihaz`
  (`Fiziksel:Tablolar:NlyteCihaz`; NlyteSunucu ile aynı kolonlar + **CihazTuru** Server / Firewall / LoadBalancer / Network / PatchPanel / Storage / PDU /
  Chassis / KVM / UPS / Peripheral / Cooling / StaticSwitch / Auxiliary / Other, `MalzemeTipi` (MalzemeTip değil), Kaynak, ChassisAssetID), yoksa
  NlyteSunucu (hepsi Server; bilgi satırında "yalnız sunucular" uyarısı). CihazTuru boşsa `TurBul` malzeme tipi / entity adından. `dbo.NlyteChassis`
  (`Fiziksel:Tablolar:NlyteChassis`; yoksa Chassis türündeki cihazlar): BLADE = UAlt NULL + ChassisAssetID → U / yüz chassis'ten, kabin de chassis'ten
  (blade'in CabinetAssetID'si boşsa); çizimde chassis cihazı kutu (adı tıklanır), blade'ler içinde. VM'li host ↔ cihaz eşleşmesi önce
  `NlyteHostEslesme.NlyteAssetID` (HostKey / HostFqdn / NlyteAdi → AssetID), sonra seri, sonra ad; uygulama tablosundaki host adları da nlyte adına
  çevrilir. Türe göre renk / lejant (`FZ_TUR`: sunucu mavi — VM'li dolu, VM'siz gri çizgili; ağ yeşil, firewall kırmızı, PDU turuncu, storage mor…),
  kabin başlığı ve oda karosu ipucunda "15 cihaz: 10 sunucu (VM'li: 1), 1 firewall, …" (`turlar`); 1U kutular küçük yazı. UAlt NULL ve blade değil →
  "0U / U bilgisi yok" listesi (tür etiketli). Kabine bağlı olmayan / kabin kaydı bulunamayan aktif cihazlar (klimalar) `/api/fiziksel/kabindisi` →
  katlanır "Kabin dışı cihazlar" bölümü. CANLI GERİ BİLDİRİM (DR1-E13 / DR2-A9: "kabinde sunucu yok ama uygulama var"): dikey 0U PDU'lar (AP88xx) nlyte'ta U1'den
  kabin boyu kayıtlı → tüm cihazları örtüyordu → PDU (≥ max(8, %40 U)) ya da sunucu / storage / chassis dışı çok uzun cihazlar kabinin kenarında dikey
  şerit (`fz-serit`); aynı yüzde U aralığı çakışan kutular yan yana (küme başına şerit) + "⚠ N cihaz çakışıyor" notu. Kabin dışı bölümü: CabinetAssetID
  boş / dolu ama NlyteKabin'de yok ayrımı ve kabin kimliği başına grup (canlıda 1.579 kabin dışı cihaz, 595'i sunucu — Cabinets çekimi eksik olabilir).
  Kimlik kolonları "123.00" → "123" (`Id`, tip farkı eşleşmeyi bozmasın). Kabin dışı listede konuma göre (Location oda / depo adı) gruplama +
  tıkla-süz (canlıda nlyte arayüzünde teyit: kabinsiz bir sunucu "SARIYER DEPO" lokasyonunda, Cabinet Name boş — yani veri doğru, depo stoğu). YANLIŞ TÜRDE KAYITLI ESX (canlı: esxzbdwh04 nlyte'ta
  Network / Networks, kabin 451 U7; sp_KabinZincirYenile yalnız sunucularla eşleştirdiği için NlyteHostEslesme "Yok", 19 VM kabinsiz): `Olustur` yedek
  eşleşme — kabinsiz VM'in host adı (Norm) kabindeki TEK aktif cihazın adlarından biriyse VM bellekte o kabine bağlanır (Eslesme "Ad (DeltaFlow; nlyte'ta
  Network)", U / yüz cihazdan), host eksik listesinden çıkar, `TurFarkli` listesine girer (`/api/fiziksel/eksik` turFarkli, CSV'de not satırı, eksik
  bölümünde ayrı tablo + kabin bağlantısı); kabin çiziminde VM'li olduğu için sunucu gibi çizilir, `turUyari` ("türü düzeltilmeli"). Kalıcı çözüm:
  prosedür NlyteCihaz'la eşleştirsin + nlyte'ta tür Server yapılsın. MSY FİZİKSEL ENVANTER (kullanıcı buldu: `dbo.MsyFizikselEnvanter` Id,
  SerialNumber, Mac, Vendor, Model, PowerState, DnsName, DnsIp, IloIp, OsName, OsVersion, OnlineHbaWwnn, LastReboot, InsertedAt; `Fiziksel:Tablolar:MsyFiziksel`,
  yoksa atlanır): yedek eşleşmede adla bulunamayan host için önce KabinHostVm.HostSeri, sonra MSY'deki DnsName → SerialNumber → kabindeki TEK nlyte cihazının
  SerialNumber'ı ("seri (MSY fiziksel envanter)", nlyte'ta farklı adla kayıtlı); Kabin() VM grubu ↔ cihaz eşleşmesinde de MSY serisi; host satırında `msy`
  (iLO, yönetim IP, OS, güç, son reboot) detayda. Eksik bölümündeki tablo "Ek eşleştirmeyle kabine bağlanan ESX host'ları" + "Nasıl" (ad · tür yanlış / seri · ad farklı), CSV notu. ALT İSTATİSTİK (kullanıcı: "toplam yerleşen
  sunucu ve uygulama sayısı önemli"): `/api/fiziksel/ozet` → `istatistik` (`Istatistik`, ana kapsam): kabine yerleşen sunucu (aktif Server cihazları,
  U'su bilinen, kabin dışı sunucu), ESX host yerleşimi (kabini bilinen / toplam, ek eşleşme, nlyte'ta yok), VM yerleşimi (%), kabine yerleşen uygulama
  (en az bir VM'i kabinde / toplam, tümü yerleşen, hiç yerleşmeyen), tek kabine bağlı uygulama (kabin kapanırsa tamamen durur), kabin sayıları → sayfanın
  altında kart ızgarası (`fzIstCiz`, `#fzIst`). GAP (kullanıcı: "MSY + ÇSY 10.000 VM, 6.374 yerleşti"): canlı kapsamlar Ana 6.701 / ODM 2.236 /
  KKB 803 / Host adı yok (boş / IP) 813 / OpenShift 195 — ODM / KKB nlyte'ta yok. Host alanında IP yazan VM'ler: MSY `DnsIp` / `IloIp` → kaydın DnsName'i
  nlyte'ta (ad) ya da SerialNumber (`MsyIp`; "IP → ad / IP → seri"); `HostAnahtar` IP'yi Norm'la kırpmaz. Yedek eşleşme artık Eslesme'ye bakmadan tüm kabinsiz
  VM'lere; kabine bağlanan VM'in Kapsam'ı ana kapsama geçer (`FizTurFarkli.EskiKapsam`). İstatistikte "Tüm VM'ler (MSY + ÇSY)" kartı (kapsam dağılımı,
  "X kapsamından N VM ana DC'ye yerleşti"). Fiziksel host envanterleri birden çok tablo: `Fiziksel:Tablolar:FizikselEnvanterler`
  (liste; boşsa eski `MsyFiziksel`, o da yoksa dbo.MsyFizikselEnvanter + dbo.CsyFizikselEnvanter, olmayan atlanır); kolonlar birkaç olası adla (SerialNumber /
  SeriNo, DnsName / HostName / Fqdn, DnsIp / Ip / YonetimIp, IloIp / IdracIp …). ÇSY VM'leri KabinHostVm'de zaten var; ÇSY'nin fiziksel tablosu YOK (kullanıcı). HATA DÜZELTİLDİ: `Norm` IP'li
  FQDN'i ("10.6.30.99.msy.zb") ilk noktaya kadar kırpıp "10" yapıyordu → tüm IP host'lar aynı anahtar, yanlış kabine bağlanabiliyordu; artık IP başı
  olduğu gibi kalır (`IpBas`). Ters DNS denendi, kullanıcı vazgeçti (yapılmadı). KABİNE YERLEŞMEYEN VM'LER (kullanıcı: "eşleşmeyen MSY / ÇSY
  sunucularını basalım; host değil VM'leri, VM'in host bilgisiyle"): `/api/fiziksel/eslesmeyen` (+`?csv=1`, Yetki:Disari) → `FizEslesmeyen` VM başına
  (VM, IP, DNS, OS, uygulamalar, kaynak, kapsam, host, FQDN, cluster, host modeli / serisi / iLO, host'taki VM sayısı, neden, nlyte adayları); neden host
  düzeyinde: VM kaydında host yok / nlyte kapsamı dışı / nlyte'ta var ama kabine bağlı değil / kayıt aktif değil / birden çok aday / IP, MSY'de yok /
  seri nlyte'ta yok / benzer ad var / bulunamadı; adaylar ad, seri ve ≥5 harf parça eşleşmesinden (kabin dışı dahil). Snapshot başına önbellek.
  Arayüz katlanır "Kabine yerleşmeyen MSY / ÇSY VM'leri" (kaynak / kapsam / neden seçicileri, arama, ilk 2000 satır, CSV). UYGULAMA DEĞERLERİ (kullanıcı:
  "ERT'de VARLIKDEGERI / ISDEGERI / ERISILEBILIRLIKDEGERI / GIZLILIKDEGERI / BUTUNLUKDEGERI (0–3) var; uygulama çoklanmış, bazı satırlar NULL → dolu
  olanları bul; VM'deki uygulamaların maksimumu ESX'in ve kabinin değeri"): `DegerleriYukleAsync` ERT'den APPNAME başına MAX(TRY_CONVERT) (`Fiziksel:
  DegerKolonlari` 5 kolon, sıra Varlık, İş, Erişilebilirlik, Gizlilik, Bütünlük; kolon yoksa 207 → gösterilmez) → `AppDeger`; `Deger(s, uygulamalar)`
  boyut başına en yüksek + kaynağı uygulama ({v,i,e,g,b,kaynak}). Uygulama / VM / host / kabin satırlarında `deger`, IP kartı `kabinDeger`, istatistikte
  `kabinDeger` dağılımı. Arayüz: `fzDeger` rozetleri (0 gri, 1 yeşil, 2 turuncu, 3 kırmızı; ipucunda en yüksek değeri veren uygulama), oda planında
  "Renk: VM sayısı | <boyut> değeri" seçicisi (localStorage `df.fzRenk`), kabin başlığında "Kabin değeri", host detayı, VM ve uygulama tablolarında "Değer" kolonu,
  "Kabin değerleri" istatistik kartı. KABİN ADI → ODA (kullanıcı: "Diğer'de DR1 / DR2 yazan net isimler var, yerleştir; EF odası
  da oluşsun; Asset_ ile başlayanların içi boş"): `Parcala` üç kalıp ("Eski " / "ZB." öneki atılır, oda kodu büyük harf 2–3 harf + numara, CO-1 → CO1):
  oda+sıra+no (DR4-D3, ZB.DR1.B10, ZB.DR2.A12-1, ZB.DR3.A1_A2.45_46U → A1, ZB.DR4.F8.46U, EF1-A1, ZB.TR1.B2, YZ-A1) → ızgara; oda+sıra+kelime+no (kullanıcı: DR1-E-EMC-12 → E12,
  DR1-E-ORACLE-1(FAH11) → E1, DR1-B-EMC-1 → B1, çok kelimeli ara ad DR1-B-HUAWEI POC-2 / DR1-B-HUAWEI-POC-1 → B2 / B1; `KalipAra`, U aralığı "…PP.45_46U" no sayılmaz) → ızgara; oda+sıra (ZB.DR3.C.NS6.45_46U) → odada "Sıra X" listesi; yalnız oda (CO2-ORACLE-1) → odanın diğer kabinleri; kalanlar "Diğer" (Asset_… katlanır
  "Adsız kabin kayıtları"). Odalar: DR'ler numara sırasıyla, sonra diğerleri, en sonda Diğer. Aynı (sıra, no)'ya düşen birden çok kayıt: VM / cihazı
  en çok (ZB. öneksiz önce) ızgarada, diğerleri `ek` (karoda "+N", katlanır "Aynı konumda ek kabin kayıtları"). `/api/fiziksel/kabin` `oda` döner;
  `fzKabinAc` kabin başka odadaysa o odayı açar (derin bağlantı / eksik listesi regex'i kaldırıldı). KABLO BAĞLANTILARI (veri tarafı: nlyte `Connections`
  → `dbo.NlyteBaglanti`, kablo başına satır: CableID, KabloTuru / KabloAdi, Kaynak/Hedef AssetID, Adi, CihazTuru, Port, Kabin…; `Fiziksel:Tablolar:NlyteBaglanti`,
  yoksa bağlantı gösterilmez): `BagIdx` (AssetID → kablolar). `Iz(s, id)` uçtan uca: karşı uç PatchPanel ise panelde AYNI PORT NUMARASIYLA (`AyniPort`, sondaki
  sayı) devam eden kablodan sürer (≤6 panel), devamı yoksa / port yoksa `Kesik`. `/api/fiziksel/cihaz?id=` (FizY) → cihaz + bağlantılar (yerel port, karşı cihaz /
  tür / port / kabin, paneller) + `etki` (sunucu değilse): doğrudan bağlı sunucular + VM'li cihazlar (`AssetVm`: kabin başına `Eslestir` = Kabin()'in VM grubu
  ↔ cihaz eşleşmesi, tek fonksiyona alındı), chassis → blade'ler, storage ise bağlı switch'lerin sunucuları (dolaylı); VM / uygulama / değer. Blade'in kendi kablosu
  yoksa chassis'in kabloları. Kabin(): satırda `bag` (kablo sayısı), başlıkta `disBaglanti` (başka kabinlere giden kablolar). Arayüz: cihaz detayında "🔌 Bağlantılar"
  tablosu (karşı cihaza tıkla → onun kabini ve detayı), "Bu cihaz kapanırsa" (sunucular, uygulamalar, değer); kabin başlığında "Kabin dışı kablolar". Sahte veriyle (switch, firewall, patch panel, 0U PDU, chassis + 3 blade, AssetID ile farklı adlı host, klima) test edildi. Panel devamında önce port adı BİREBİR (1A25 ↔ 1A25), yoksa ve ad "1A1" tarzı değilse sondaki sayı. Kablo SINIFI (`Sinif`;
  canlıda sunucuya PDU güç kabloları BLUE-/GREEN- ve panel kabloları C-/F- geldi): ucu PDU / UPS / StaticSwitch → "güç", kablo türü / adında fiber /
  F- / LC / SC / MPO / OM / OS → "fiber", kalan "veri"; Bağlantılar tablosunda "Tür" kolonu (⚡ güç, 🔆 fiber). PDU'da başlık "Bu cihazın gücü kesilirse";
  UPS / statik switch → bağlı PDU'ların beslediği sunucular (dolaylı, "PDU X üzerinden (güç)"). YEDEK YOL (canlı örnek: Cisco MDS 9710 → 9 ESX):
  etkideki doğrudan bağlı sunucunun bu cihaz dışında aynı sınıftan (güç ↔ güç; veri / fiber ↔ panel / sunucu olmayan uç) başka cihaza izi varsa `yedek`,
  yoksa "tek yol"; özet "N sunucu (M VM) yalnız bu cihaza bağlı". Değerler canlıda 0–5 (renk ölçeği 5 koyu kırmızı … 0 gri, lejant veriden). KABLO
  KAPSAMASI (kullanıcı: "çok sayıda bağlantısı hiç olmayan kabin var"): Oda() satırında `kablo` (kablolu aktif cihaz), planda "Renk: Kablo kaydı";
  istatistikte "Kablo kaydı" kartı (kabinKablolu, sunucuKablolu / sunucuVeri, hostVmli / hostVmliVeri). Canlı sorgular: sunucuların ~%12'sinde kablo var
  (ESX 53 / 330, diğer 90 / 903), 595 panelin hepsi panel ↔ panel trunk'lı, yalnız 70 panelde switch kablosu → port düzeyinde host → switch izi kurulamıyor
  (kullanıcı: "bize bir şey vermiyor"); kesin host → switch için APIC (ACI endpoint) / MDS flogi / vCenter CDP önerildi.
  DEĞER KURALLARI (`FizikselDeger.cs`; kullanıcı: "FW, veri merkezi switch'leri, LB full 5 gibi genel kurallar; Exadata (model Server X9-2 / X9-2L) 5"):
  `Degerler(s)` (snapshot başına önbellek `DegerOnbellek`): cihaz değeri = hesaplanan (VM'lerin uygulamaları; VM'siz fiziksel sunucuda cihaz adı ↔ ERT
  UNIXNAME, MSY seri → DnsName / DnsIp ↔ ERT — `ErtAd` / `ErtIp`, DegerleriYukleAsync'te `SELECT DISTINCT UNIXNAME, IPADDRESS, APPNAME`; chassis →
  blade'leri) ile İLK UYAN kuralın boyut başına en yükseği. VM'i olan cihaza türe bağlı kural uygulanmaz (yanlış türde ESX). Varsayılan kurallar
  (`VarsayilanKurallar`; appsettings `Fiziksel:DegerKurallari` [{Ad, Turler, Model, Isim, Kabin (regex), Degerler [5], Miras kabin|oda, Boyutlar "E"/"GB", Aciklama}]
  doluysa onlar): Exadata modeli (`exadata|zdlra|exalogic|\bx\d+m?-\d+[a-z]*\b`) 5, kabin adında EXA 5, SAN switch (MDS / Brocade) 5, Firewall / LoadBalancer /
  Network / Storage 5, KVM → kabinin G / B, PDU / PatchPanel → kabinin E, UPS / StaticSwitch / Cooling → odanın E (Diğer odada kabinin); mirasta İş de gelir,
  Varlık = en yüksek(G, B, E). Kabin değeri = kabindeki VM uygulamaları + cihaz değerleri (kaynak "cihaz (kural: …)"). Kabin dışı (depo) değerlendirme dışı.
  ERT analizi (canlı): Varlık hiçbir uygulamada en yüksek(G, B, E)'yi geçmiyor (%71 eşit, kalanı 1–3 düşük), İş ya E ya 0 → `DegerTamamla`: İş 0 / boş →
  E, Varlık boş → en yüksek(G, B, E) (`AppTuretilen`). Uçlar `/api/fiziksel/kurallar` (KuralOzet: kural başına cihaz, değersiz cihazlar türe göre) ve `?csv=1`
  (tüm kabin cihazları değer / yöntem / kaynak, Yetki:Disari). Kabin() host satırı, Oda(), Istatistik.kabinDeger (tüm kabinler), Ip().kabinDeger, Cihaz()
  `deger` artık bundan; değer nesnesinde `yontem`. Arayüz: host detayında "nasıl: …", katlanır "Değer kuralları" bölümü (tablo, değersizler, CSV).
  DENETİM KAYDI + ELLE DEĞER + EXCEL (`FizikselKayit.cs`, betik `sql/fiziksel_deger.sql`; kullanıcı: "denetçi rapor isterse; DB'ye alalım, gerekirse elle
  değiştirelim, Excel de olsun, yetki DokuPanel'den"): tablolar `Fiziksel:DegerTablolari` (Kayit dbo.DF_FIZIKSEL_DEGER_KAYIT başlık: tarih, kaydeden, açıklama,
  sayılar, VERI_TARIHI, KURALLAR JSON; Cihaz dbo.DF_FIZIKSEL_DEGER; Kabin dbo.DF_FIZIKSEL_DEGER_KABIN; Elle dbo.DF_FIZIKSEL_DEGER_ELLE AssetID PK, gerekçe zorunlu).
  DeltaFlow yalnız bu DF_ tablolarına yazar. ELLE: snapshot yüklenirken okunur (`s.Elle`, tablo yoksa `ElleDurum`); `ElleUygula` dolu boyutları hesaplananın
  YERİNE koyar (düşürebilir), girilmeyen Varlık = en yüksek(G, B, E), İş = E; kabin değerine de yansır; yazınca önbellek (`DegerOnbellek`) sıfırlanır.
  Uçlar: GET `/api/fiziksel/deger/kayitlar`, POST `/deger/kaydet {aciklama}` (transaction + SqlBulkCopy), GET/POST `/deger/elle {assetId, v,i,e,g,b, gerekce, sil}`,
  GET `/deger/excel?kayit=` (kayıt yoksa güncel hesap; sayfalar Cihazlar, Kabinler, Kurallar, Elle değerler, Yöntem; `Xlsx.Yaz` inlineStr, ilk satır kalın +
  dondurulmuş + filtre). YETKİ: yeni `Yetki:FizikselDeger` (kaydet + elle; Fiziksel de gerekir; boşsa Yazma), Excel = Fiziksel + Disari. Kullanıcı: "yetkisi
  olmayan görüntüleme de yapamasın, combobox / hiçbir şey aktif olmasın" → sekme artık GİZLENMİYOR; Yetki:Fiziksel yoksa `fzYetkiYok` sayfa kabuğunu veri
  yüklemeden çizer, tüm input / select / button pasif, bölümler açılmaz, kilit mesajı (`#fzShell.fz-kapali`). Arayüz: cihaz detayında "✎ Elle değer" formu
  (boyut başına — / 0–5, gerekçe, kaldır; yetkisizse pasif), "elle" rozeti; "Değer kuralları"nda "💾 Değerleri kaydet", "📊 Excel (güncel)", CSV, denetim
  kayıtları tablosu (kayıt başına Excel), elle değerler listesi. Sahte veriyle Excel (openpyxl ile açıldı) ve Playwright'la yetkisiz / yetkili akış test edildi.
- **Açıklık taraması** (`Zafiyet.cs`, `wwwroot/zafiyet.html`, sekme çubuğunda "Açıklık taraması ↗"; kullanıcı: CVE-2026-114938 "Java4Shell" acil — "tek tek sunucu
  işimizi görmez, CB'den toplu almamız lazım"; "sadece Java için değil herhangi bir process için"): KURAL tabanlı (`ZfKural`: Id, Ad, Cve, Aciklama, Exeler (ad, * joker,
  .exe isteğe bağlı), SurumDesenleri, AnaMin, Araliklar [{Ana, Min, Duzeltilmis}], Onlem (komut satırı regex'leri), OnlemNot, Java, DestekDisiEtkilenir, Anahtar).
  Hazır kurallar `Zafiyet:Kurallar` (boşsa varsayılan Java4Shell: java / javaw / tomcat* / prunsrv; AnaMin 17; 17: 17.0.6 → 17.0.21.1, 21 → 21.0.13.1, 25 → 25.0.5.1,
  27 → 27.0.1.1; aralıkta olmayan ≥17 ana sürüm (18–20, 22…) destek dışı = etkilenen; önlem -XX:-UseBASE64Intrinsics / -XX:UseAVX=0..2). Sayfadan anlık kural:
  exe adları (+ isteğe bağlı en düşük etkilenen / düzeltilmiş sürüm; verilmezse durum "bilgi", yalnız maruziyet). Arka plan işi (tek; bellekte son tarama):
  POST `/api/zafiyet/tara {saat, kural | exeler, min, duzeltilmis, ad}` (Yetki:Topoloji), GET `/api/zafiyet` (iş, satırlar, mesajlar, kurallar), POST `/durdur`.
  (1) CB ağ olayları TOPLU (tüm sunucular, `Zafiyet:Saat`=24, en çok `MaxSaat`=168; anahtar kelimeler + `where match(lower(yol), …)`; local_ip × yön × lp × yol,
  örnek istemciler, computer_name), (2) CB `ingress.event.procstart` (`SurecSaat`=168) bilgisayar × yol → son komut satırı (ilk 1500 karakter), önlem bayrağı,
  -jar, jdk yolu; (3) servis imzası kaydı (`ServisImzaService.TaramaAsync`) CB'de görünmeyen IP'ler için; ağ olayı olmayan procstart'lar ERT UNIXNAME → IP.
  Sorgu başına `Zafiyet:SureSaniye`=900. Sürüm: yol → komut satırı (Java kalıpları jdk-17.0.10 / jdk1.8.0_202 / java-17-openjdk-17.0.9… / zulu / corretto; genel kural
  ilk a.b[.c]). Gömülü JRE (…\jre\bin\java.exe) ve tomcat / prunsrv sarmalayıcısında not. Öncelik: etkilenen / olası + VIP üyesi ya da özel olmayan istemci → kritik,
  + port → yüksek, yalnız giden → orta; sürüm bilinmiyor + port → incele; önlem varsa en çok orta. Dinamik port (≥49152, ≤1 istemci) atılır. AVX-512 bilgisi yok.
  Arayüz: kural / "Süreç adıyla ara" modu, ilerleme bandı (3 sn), uyarılar, öncelik + durum kartları (tıkla süz), arama / durum / kurum / yalnız dinleyen / test,
  tablo (sunucu, ERT uygulaması + muhafız, süreç + ürün + yol, sürüm + durum, portlar (🌐 dış, ⇄VIP), giden, önlem, son görülme, kaynak), satıra tıkla → sağ panel
  (komut satırı dahil), CSV (Yetki:Disari). Sürüm / kural mantığı yansıma testiyle, arayüz Playwright + mock'la denendi; gerçek Splunk'ta denenmedi.
  AI İLE DOLDURMA + KAYITLI KURAL (kullanıcı: "CVE'nin bir bölümünü yapıştırsak hangi process'lerin aranacağını önerse"): "Süreç adıyla ara" formunda CVE metni →
  POST `/api/zafiyet/ai {metin}` (`AiDoldurAsync`, `IslevService.ChatAsync`, metin `Zafiyet:AiMaxKarakter`=8000) → {urun, cve, ozet, kutuphane, exeler [{ad, neden,
  kaynak AI | sözlük | envanter, sunucu, ornek}], araliklar, onlem, digerEtkilenir, uyarilar}. AI yalnız FORMU doldurur, tarama başlatmaz. Kodda doğrulama: sürüm ve
  önlem metinde birebir geçmiyorsa atılır (`MetindeVar`), CVE metinde yoksa metindeki ilk CVE; exe adları servis imzası kaydındaki exe'lerle sayılır (`ExeIndexAsync`,
  kaç sunucuda görüldü, örnek yol), görülmeyen AI adı uyarı; ek öneriler: `UrunExe` ürün → süreç sözlüğü (Tomcat, log4j, OpenSSL, nginx, httpd, SQL Server, Exchange…)
  ve ürün adının ayırt edici kelimesi envanterdeki exe adında / yolunda geçen süreçler (genel / ajan hariç, `ServisImzaService.Gereksiz` public yapıldı). Tek aralıkta
  Ana boşaltılır, çoklu aralıkta Ana sürümden türetilir. Arayüzde öneri çipleri (işaretli = AI ve görülen sözlük; envanter önerisi işaretsiz), düzenlenebilir aralık
  tablosu (ana / etkilenen ilk / düzeltilmiş, + aralık), önlem (düz metin, regex'e kaçışlanır), "aralıkta olmayan ana sürümler" seçimi. İstek artık
  `ZafiyetIstek {saat, kural | ozel: ZfOzel}`. KAYIT: POST `/api/zafiyet/kural` (ZfOzel; Id = CVE / addan slug), DELETE `/api/zafiyet/kural/{id}` (Yetki:Topoloji);
  tablo `Zafiyet:KuralTablosu` dbo.DF_ZAFIYET_KURAL (`sql/zafiyet_kural.sql`: ID, KURAL JSON, TARIH, KAYDEDEN; yoksa / yazılamazsa bellekte + uyarı), 5 dk önbellek;
  kural listesi = kayıtlı + ayar / yerleşik (aynı Id'de kayıtlı geçerli). "✎ Kopyala / düzenle" hazır kuralı forma alır, "🗑 Sil" yalnız kayıtlıda. Sahte AI ile backend
  (uydurma sürüm / önlem atıldı), mock'la arayüz denendi.
  CANLI DÜZELTMELER (kullanıcının yapıştırdığı Oracle CPU metni, Java SE "component: Compiler"): (1) AI tomcat'i eklemedi → Java çalışma ortamı sözlük kalıbı
  (openjdk / java se / jdk / jre / jvm / java N / java.util…) artık java, javaw, tomcat*, prunsrv, elasticsearch-service-* önerir (JVM'i kendi içinde çalıştıran
  Windows servisleri: CB'de java.exe görünmez) + prompt kuralı; sözlük önerisi envanter yoksa da işaretli. (2) Oracle "Supported versions that are affected are X" =
  X ve ÖNCESİ: AI bunu min'e yazdı (her şey "etkilenmez" çıkacaktı) → `ZfAralik.Son` (son etkilenen, dahil): sürüm ≤ Son → etkilenen, > Son → yamalı; AI cevabında
  yalnız min dolu ve metinde "versions that are affected are" varsa min → son'a taşınır; prompt'ta açıklama; arayüzde "Son etkilenen (dahil)" kolonu.
  (3) Java 8 biçimi `8u511b33-perf` → `NormSurum` 1.8.0.511 (metinde "8u511" geçmesi yeterli; elle girişte de). (4) AI ile doldurma Ad / CVE'yi her zaman yazar
  (önceki kuraldan kalan değer kalmaz; metinde CVE yoksa boş). (5) AI "javac" önerdi ("Compiler" = HotSpot JIT, javac değil) → `JdkAraclari` (javac, jar, keytool,
  jshell…) kaynak "AI · geliştirme aracı", işaretsiz, notlu; prompt kuralı.
  VLAN + DOMAIN (kullanıcı: "VLAN bilgilerini de çeksene domain bilgisiyle beraber"): `ZfSatir.Vlan / Domain / Cidr` (segment kaydından); tabloda sunucu hücresinde,
  kullanıcı: "gride ayrı kolon, filtreleyebileyim, panele gerek yok" → tabloda ayrı VLAN ve Domain kolonları (CIDR VLAN hücresinin ipucunda), araç çubuğunda
  "Domain" / "VLAN" seçicileri (satır sayısıyla, boşlar "(yok)"); sunucu hücresindeki ve sağ paneldeki VLAN satırı kaldırıldı; arama ve CSV'de duruyor.
  DAĞITIM (kullanıcı: "dağıtımları bulabiliyor muyuz"; Temurin / Red Hat'in yaması henüz yok, Oracle / Corretto / Zulu'nun var): `Dagitim(yol)` Java kuralında exe yolundan,
  yoksa komut satırındaki JVM yolundan (Eclipse Temurin, Amazon Corretto, Azul Zulu, Microsoft OpenJDK, IBM Semeru, Liberica, SapMachine, GraalVM, Red Hat (…el8),
  Debian / Ubuntu, "Oracle (muhtemel)" = program files\java\jdk…, "ürünle gelen (gömülü)"); `ZfSatir.Dagitim`, tabloda kolon + "Dağıtım" süzgeci, CSV, sağ panel.
  HATA DÜZELTİLDİ: Temurin / Microsoft klasörü "jdk-17.0.21.9-hotspot"ta son parça YAPI numarası (17.0.21+9) — 17.0.21.9 okunup 17.0.21.1'den büyük, "yamalı"
  sayılıyordu → "-hotspot" / "-openj9" ile biten 4+ parçalı sürümde son parça atılır. Oracle dışı dağıtımda "yamalı" notuna "dağıtıcının kendi düzeltilmiş sürümüyle karşılaştırın".
  NESSUS (kullanıcı: "Nessus şart, API gateway gibi kutularda Carbon Black yok"): taramanın 3. adımı (`Zafiyet:Nessus`=true; Nessus:BaseUrl yoksa uyarı).
  `NessusService.CveAsync(cve)` (tüm repo, filtre cveID) ve `UrunAsync(kelime)` (pluginName "~=" kelime + severity 0 = "… Detection" bilgi bulguları), sayfa sınırı
  `Zafiyet:NessusMaxSayfa`=25 × SayfaBoyutu; `NessusBulgu` (ip, dns, netbios, plugin, ciddiyet, port, son görülme, `Kurulumlar` = çıktıdaki "Path / Binary Location" +
  "Version / Installed version" (+ "Fixed version") çiftleri, `KurulumOku`). Kural alanı `NessusAnahtarlar` (Java4Shell: Java, JDK, JRE, Corretto, Temurin, Zulu, GraalVM;
  anlık kuralda exe adları + ad kelimeleri). `NessusIsle`: (a) kurulum yolu aynı IP'deki CB satırının exe yolunun başıysa ve CB'de sürüm yoksa sürüm Nessus'tan
  (`NessusSurum`: "17.0.10+7" → 17.0.10, 8u202); eşleşmeyen kurulum yeni satır (kaynak "Nessus (kurulu yazılım)", exe "java (Nessus)"); (b) CVE bulgusu olan IP'nin
  CB satırlarına `ZfSatir.Nessus` notu + durum etkilenen (etkilenmez / yamalı değilse); satırı olmayan IP yeni satır "Nessus (CVE bulgusu)". Java kuralında
  javascript / jquery plugin'leri elenir. Nessus satırında port bilinmez: VIP üyeliği "VIP" sahte port (`ZfPort`) olarak maruziyet; `Oncelikle` önceliği yeniden
  hesaplar (CVE doğrulanmış → en az yüksek, + VIP → kritik). Arayüz: "Kaynak" süzgeci (Carbon Black / + Nessus CVE doğruladı / Nessus CVE bulgusu / Nessus kurulu /
  servis imzası), kaynak hücresinde 🛡 Nessus notu, panelde ve CSV'de NESSUS. Tenable çıktı ayrıştırması örnek çıktılarla denendi, gerçek Tenable.sc'de denenmedi.
  CANLI (kullanıcı: "Nessus: CVE için bulgu yok" — kontrol henüz yok; kutu (apigee) gelmedi; "tarama ekseninde gidelim"): kurulu yazılım araması HİÇ çalışmamıştı (eski
  kayıtlı kuralda `NessusAnahtarlar` null) → `NessusKelimeleri(k)`: boşsa Java kuralında JavaNessus, diğerlerinde exe adları. `UrunAsync` artık TÜM ciddiyetler
  ("Oracle Java SE … CPU" açıklık bulguları da kurulu sürümü yazar; `yalnizBilgi` ile eski davranış). Sürüm yazmayan ürün bulgusu olan ve başka satırı olmayan IP →
  "Nessus (ürün bulgusu)" satırı, durum bilinmiyor, öncelik İncele (Nessus kaynaklı / VIP'li bilinmiyor satırları İncele). Kelime başına mesaj "X: N bulgu / M IP /
  K sürümlü". Formda "Nessus'ta aranacak ürün adları" (`ZfOzel.NessusAnahtarlar`, virgülle; kopyala / düzenle doldurur), kural açıklamasında aranan kelimeler.
  Sahte envanter + bulgularla yansıma testi (eski kural, CPU bulgusu → etkilenen, sürümsüz kutu → İncele, JavaScript elendi).
  CANLI HATA (kullanıcının mesajı: her kelime "50000 bulgu / 663 IP / 3527 sürümlü", 4354 yeni satır): Tenable.sc `pluginName "~="` süzgecini YOK SAYDI → repodaki ilk
  50.000 bulgu geldi, alakasız "Version" satırları Java sürümü sanıldı. Düzeltme: `NessusService.PluginOzetiAsync` (tool `sumid`, plugin başına id + ad + host, 30 dk
  önbellek, `Zafiyet:NessusPluginSayfa`=40 × 5000) → plugin adı KODDA eşlenir (kelime solunda harf yok; Java kuralında javascript / jquery / json hariç) →
  `UrunlerAsync` bulguları pluginID ile (150'lik parçalar) ister ve bulgu adını yine kodda doğrular (süzgeç yok sayılsa da alakasız bulgu girmez). Mesajda kelime başına
  eşleşen plugin sayısı. JavaNessus'a OpenJDK eklendi. Ad süzgecini yok sayan sahte Tenable.sc ile denendi (JavaScript ve Windows yazılım listesi elendi).
  TEKİL IP TARAMASI + LİSTEYE EKLE (kullanıcı: "tekil IP taraması olsa, gelen IP'den listeye ekleyebileyim"): `ZafiyetIstek.Ipler` (virgül / boşluk / satır, IPv4,
  en çok `Zafiyet:MaxIp`=50; `IpListesi`), `Ekle` (sonuç mevcut listeye; o IP'lerin eski satırları yenilenir, diğerleri kalır, mesajlar birleşir), `SonKural` (son
  taramanın kuralı). IP kipinde CB ağ sorgusu `(TERM(ip) OR …)` + `where local_ip=…`; süreç başlangıcı ağ olaylarındaki computer_name + ERT UNIXNAME ile
  `(TERM(pc) OR …)` (ad yoksa atlanır + mesaj); Nessus CVE / ürün sorgularına `ip` süzgeci (sonuç kodda da IP'ye süzülür); servis imzası yalnız o IP'ler;
  sonuçta bulgu çıkmayan IP'ler mesajda. `ZfIs.Kapsam` ("tüm sunucular" / "N IP: …"). Arayüz: "IP'ler (isteğe bağlı)" kutusu + "Mevcut listeye ekle" (varsayılan
  işaretli; liste boşsa yok sayılır), ilerleme bandında kapsam, satır panelinde "↻ Bu IP'yi yeniden tara" (sonKural + ekle). Mock'la istekler denendi.
  NGINX (kullanıcı: "CVE-2026-123633 ve CVE-2026-123007 de çıktı, ekleyelim"): yerleşik kural `NginxHttp2` ("nginx-http2-2026"; nginx, openresty; SurumDesenleri
  nginx-1.24.0 / openresty-1.25.3.1 / nginx/1.x; aralıklar: dal 1.30 → 1.30.7, dal 1.31 → 1.31.9, diğer 1.9.5 … son 1.31.8; Nessus kelimeleri nginx, OpenResty; önlem =
  HTTP/2'yi kapatmak, yapılandırmada → ölçülmez; NGINX Plus R37 sürümü yoldan çıkmaz). `ZfAralik.Dal` (sürüm bu önekle başlıyorsa; "1.30" tek başına → olası); aralık
  seçimi dal > ana > genel. Yerleşik kurallar (`Yerlesik` = Java4Shell, NginxHttp2) artık HER ZAMAN listede (ayarda aynı Id varsa o). Kural birden çok CVE taşır
  (Cve "A, B"; Nessus'ta her biri ayrı). AI doldurmada metindeki tüm CVE'ler; çoklu aralıkta ana hep aynıysa (NGINX) düzeltilmiş sürümlü aralıklar dala bağlanır, genel
  aralık dalsız kalır. Arayüzde aralık tablosunda "Ana sürüm / dal" (17 ya da 1.30). Nessus satırının süreç adı kuralın ilk exe'si ("nginx (Nessus)"). Sürüm mantığı yansıma testiyle.
  KAYITLI TARAMALAR (kullanıcı: "son taramalar kayıtlı olsun, kayıtlıdan getir / yeni tara seçeneği olsun"): biten tarama "bitti"den ÖNCE ("Sonuç kaydediliyor")
  `Zafiyet:TaramaTablosu` dbo.DF_ZAFIYET_TARAMA'ya (`sql/zafiyet_tarama.sql`: ID IDENTITY, KURAL_ID, KURAL_AD, CVE, KAPSAM, SAAT, SATIR, OZET (öncelik → sayı JSON),
  SONUC VARBINARY gzip JSON `ZfKayitVeri` {is, satirlar, mesajlar}, TARIH, KAYDEDEN) yazılır; kural başına son `Zafiyet:KayitSayisi`=5 (eskiler silinir). Anahtar = kural Id,
  anlık kuralda "anlik:" + Slug(cve ?? ad). Yalnız tüm sunucu taraması ve mevcut listeye EKLENEN tekil tarama (birleşik liste) kaydedilir; listesiz IP taraması
  kaydedilmez ("Kayıtlıdan getir" tam listeyi açsın). Tablo yoksa / yazılamazsa bellekte (eksi Id) + `_taramaUyari` mesajda. `ZfIs.KayitId / KayitTarihi / Kayittan`.
  Uçlar: GET `/api/zafiyet/kayitlar?kural=` (gövdesiz, en çok 200), POST `/api/zafiyet/kayit/{id}/ac` (sorgusuz; iş "bitti" + Kayittan, satırlar / mesajlar servise yüklenir →
  süzgeçler, CSV, "Bu IP'yi yeniden tara" (sonKural + ekle) aynen çalışır; tarama çalışırken açılmaz), DELETE `/api/zafiyet/kayit/{id}` (Yetki:Topoloji). Arayüz: kural
  satırında "📂 Kayıtlıdan getir (tarih)" (seçili kuralın en son kaydı), "Taramayı başlat" → "🔄 Yeni tara"; katlanır "Son taramalar" (tarih, kural, kapsam, ağ olayı
  aralığı, satır, kritik / yüksek / orta, kaydeden, Aç / 🗑; açık olan vurgulu); bantta "📁 Kayıttan açıldı: tarih" ya da "💾 kaydedildi". Bellek yedeği (son 5, aç, sil)
  scratch test ucuyla, arayüz mock'la denendi; SQL yolu ortamda SQL Server olmadığı için denenmedi.
  DMZ + DERİN DIŞ (kullanıcı: "DMZ çok kritik, ayrıca tarasak; dışa açık / dış IP'lerle iletişim kuranları cidden kritik ele almalıyız"; Nessus ekibinin DMZ asset seti
  "DMZ - (ZB(Access&Hosting) ve ZI(Access&Hosting) ) [671]"): `Zafiyet:DmzAsset` = Tenable.sc asset adı ya da id'si (sondaki "[n]" olabilir; önce adla, sonra id ile eşlenir,
  `NessusService.AssetBulAsync`; bulunamazsa "DMZ geçenler" listesiyle hata). `AssetlerAsync` (GET /rest/asset, usable + manageable, 30 dk), `AssetIpleriAsync` (GET /rest/asset/{id}
  viewableIPs / typeFields.definedIPs → `IpAc` aralık / CIDR açar, `Nessus:AssetMaxIp`=100000; boşsa asset süzgeçli sumip; `Nessus:AssetDakika`=60). Asset süzgeci
  `{"filterName":"asset","value":{"id":…}}`; asset süzgeçli sorguda repository süzgeci KONMAZ (DMZ başka repoda taranmış olabilir; `Nessus:AssetteRepoSuzgeci`=true ile konur).
  `CveAsync` / `UrunlerAsync` / `PluginOzetiAsync` `assetId` alır. Taramada: tanımlıysa normal Nessus sorgularına EK olarak DMZ asset sorguları (ip + plugin + port tekilleştirilir;
  önceki "DMZ diye ilettikleri gelmiyor" sorunu); `ZafiyetIstek.Dmz` (sayfada "Yalnız DMZ asset'leri") → yalnız asset sorguları + CB sonucu DMZ IP'lerine süzülür (CB yine toplu),
  asset çözülemezse tarama hata verir. `ZfSatir.Dmz` (asset IP'si ya da DMZ sorgusundan gelen bulgu), `ZfIs.Dmz` (listeye eklenen tekil IP taraması listenin DMZ'sini korur), kayıt
  anahtarı "<kural>:dmz" (Kayıtlıdan getir DMZ kutusuna göre). DERİN DIŞ: CB ağ SPL'inde giden olayların RFC1918 / loopback / link-local / multicast dışı hedefleri `dis` (ip:port,
  ilk 300) → C#'ta `Ic(env, ip)` (Yolculuk'taki tanım: uygulama / LB GW / ≥/8 segment kaydı olan genel bloklar iç) → `ZfSatir.DisGiden` (ilk 50) / `DisGidenSay`; gelen
  dış istemci listesi de artık `Ic` ile. `DisOncelik`: etkilenen / olası / bilgi + (DMZ | derin dış | dış istemci) → kritik; sürümü bilinmeyen ve düşük → incele; önlem varsa dokunmaz.
  Uçlar `GET /api/zafiyet/dmz` (asset, IP sayısı, hata), `GET /api/nessus/assetler?q=` (ayarı doğru yazmak için). Arayüz: "Yalnız DMZ asset'leri" kutusu + yanında asset adı / IP sayısı
  (tanımsız / okunamazsa pasif), "Dışa açıklık" süzgeci (DMZ / Derin dış / Dış istemci-VIP / herhangi), "DMZ asset'i" ve "derin dış" kartları, sunucuda kırmızı DMZ rozeti, Giden
  hücresinde "🌐 N dış hedef" (ipucunda liste), sağ panelde hedefler, aramada dış hedef, CSV'de DMZ / DIŞ HEDEF SAYISI / DIŞ HEDEFLER. Sahte envanter + sahte Splunk + asset'li sahte
  Tenable.sc ile uçtan uca ve Playwright'la denendi.
  EKSİK IP (kullanıcının Excel'i: Nessus ekibinin 55 IP'si, DeltaFlow'da 41): HATA DÜZELTİLDİ — `NessusIsle` (a2) yalnız `Kurulumlar.Count == 0` bulgulara "sürüm bilinmiyor"
  satırı açıyordu; kurulum satırı olup sürümü okunamayan ("unknown") bulgular hiç satır üretmiyordu → artık (a)'dan sonra satırı olmayan her ürün bulgusu IP'si satır olur (notta
  Nessus'un yazdığı ham sürüm). `NessusSurum` başta değilse metindeki ilk sürümü de alır ("nginx/1.24.0"). TANI: `GET /api/zafiyet/tani?ip=a,b&kural=` (düz metin, Yetki:Topoloji) —
  `NessusService.HamBulgularAsync` (yalnız ip süzgeci; repository / lastSeen YOK) ile IP başına: DMZ asset'inde mi, son taramada satır var mı, ilgili (kelime / CVE) bulgular +
  kurulum / okunan sürüm + "TARAMADA ALINIR / ALINMAZ: repository X Nessus:Repolar'da yok / son görülme N günden eski (Nessus:MaxGun) …"; ilgili bulgu yoksa IP'deki ürün plugin
  adları; sonda özet. Sahte Tenable.sc ile denendi.
  CANLI TANI SONUCU: eksik 8 IP'nin bulguları repo 119 / 121 / 46'da, son görülme 06.10.2026 (DMZ 30 günden seyrek taranıyor) → açıklık taramasının Nessus sorguları
  (`GenelSorgu`) artık `Zafiyet:NessusGun`=90 (`NessusService.AcikGun`; diğer sayfalar Nessus:MaxGun=30). Asset IP listesi 100.000 sınırında KESİLİYORDU (asset geniş aralıklarla
  tanımlı) → `NessusAssetIp.Araliklar` (repository başına uint aralık, açılmaz; `Icerir(ip, repoId, izinli)`, `Repolari(ip)`, `RepoOzet`; `Nessus:AssetMaxIp` kaldırıldı).
  Fazla 5 IP (192.168.0.x) Londra (72) / Bulgaristan (52) repolarından DMZ asset sorgusuyla geldi — kullanıcı: "fazla olanlar olsun, repo adını ekle" → ayıklanmaz;
  `NessusBulgu.RepoId / RepoAd`, `ZfSatir.Repo` ("Ad (id)"; Nessus satırı, Nessus'tan sürüm alan ya da CVE ile işaretlenen CB satırı), sayfada "Repository" süzgeci, Kaynak
  hücresinde 📁, sağ panel, CSV "NESSUS REPOSITORY". İsteğe bağlı `Zafiyet:DmzRepolar` (id listesi) DMZ asset sorgusunu o repolara indirir (boş = hepsi). Tanıda asset'in repo
  başına aralık / adres sayısı, IP'nin hangi repo aralığında olduğu.
  İŞLETEN BİRİM + UYGULAMA / MUHAFIZ TAHMİNİ (kullanıcı: "SUNUCUISLETENBIRIM'i de ekle; ERT'de yoksa AI tahmini, ERT eksikleri yapısı"; "uygulama ve VARLIKMUHAFIZI için de
  tahmin varsa getir"; "tekrar tahmin de yaptıralım, IP ile ilgili"): `Birim.cs` (`BirimService`; Eksik / Islev'e bağlı, döngü yok). Dizin (envanter anlık görüntüsü başına):
  IP → ERT birimi (satırlarında en sık), birim kataloğu (sunucu sayısı + ERT_APPLICATIONDETAIL birimleri), uygulama / muhafız → birim dağılımı, ad kökü (`AdKoku`: ilk etiket,
  sondaki numara atılır, ≥5 harf) → IP'ler. Kanıt + puan: uygulama ana kaydının birimi (4), aynı uygulamanın diğer sunucuları (3), adı benzeyen sunucular (3), ERT eksiklerinin
  uygulama önerisi (onaylı 4 / AI güvenle), aynı VIP havuzu (2), muhafız (1,5), segment (1, zayıf). En yüksek puan ≥ `Birim:KuralEsik`=4 ve ikincinin ≥3 katı → AI'sız "Kural";
  değilse AI (`IslevService.ChatAsync`; Eksik genel bilgi dosyası + birim listesi (`Birim:KatalogMax`=250) + kanıt + işlev profili `ProfileAsync`, `Birim:MaxKarakter`=6000) —
  yalnız ERT'de VAR OLAN birim adı (kodda doğrulanır, yoksa "Yok sayıldı"). Kayıt `Birim:OneriTablosu` dbo.DF_BIRIM_ONERI (`sql/birim_oneri.sql`: IP PK, BIRIM, GUVEN, GEREKCE,
  KAYNAK AI | Kural | Elle, DURUM AI | Onaylandı | Reddedildi | Elle; yoksa bellekte), ERT'ye yazılmaz. İş tek (`Birim:MaxIp`=300, `Birim:Paralel`=3): ERT'de birimi olan,
  onaylı / elle ve (yeniden değilse) önerisi olan IP'ler atlanır. Uçlar: POST `/api/birim/satirlar {ips}` (IP başına ERT birimi / uygulamaları / muhafızı, birim önerisi, ERT
  eksiklerinin uygulama önerileri (reddedilen hariç, onaylı önce), ERT'de boşsa önerilen uygulamanın ana kaydından muhafız / birim tahmini + kaynağı), GET `/api/birim/katalog`,
  `/durum`, `/kanit?ip=` (düz metin), POST `/calistir {ips, yeniden}`, `/durdur`, `/karar {ip, karar: onay|red|elle|sil, birim}` (Yetki:Yazma). zafiyet.html: "İşleten birim" kolonu
  (ERT düz; öneri 🤖 mor + güven + durum + ✓ ✗ ✎ 🗑 ↻; uygulama önerisinden gelen "(uygulama önerisinden)"), Uygulama hücresinde ERT boşsa 🤖 uygulama önerisi (satırın portları
  önce) ve 🤖 muhafız tahmini, "Birim" süzgeci, "🤖 Birimi boş N sunucuyu tahmin et" + ilerleme / durdur, sağ panelde birim + gerekçe + kanıt ↗, CSV'de İŞLETEN BİRİM / BİRİM KAYNAĞI /
  UYGULAMA ÖNERİSİ / MUHAFIZ TAHMİNİ. "↻ Bu IP için yeniden tahmin" (`ipTahmin`): ERT'de uygulama boşsa önce `/api/eksik/calistir {portlar: {ip: satırın portları}, yeniden}` →
  `/api/eksik/durum` bitene kadar, sonra `/api/birim/calistir {ips:[ip], yeniden}`. Sahte envanter + sahte AI ile backend (benzer ad → birim, uydurma birim reddi, onaylıya dokunmama)
  ve Playwright'la arayüz denendi.
  UYGULAMA TAHMİNİ DE (kullanıcı: "sadece birim değil uygulama tahmini de yapması lazım, aynı ERT eksikleri gibi"): toplu düğme "🤖 Eksikleri tahmin et (uygulama N · birim M)"
  (`birimAi` → `tahminEt(ips, yeniden, not)`): (1) ERT'de uygulaması olmayan ve (yeniden değilse) önerisi olmayan IP'ler (`uygGerekenler`) ERT eksikleri motoruna — portlu olanlar
  `/api/eksik/calistir {portlar: {ip: satirPortlari(ip)}}` (CB portları ∪ `ZfSatir.NessusPortlar` (NessusIsle sonunda IP'nin Nessus bulgu portları; port hücresinde "Nessus: …") ∪
  `BirimSatir.BosPortlar` (ERT'de uygulaması boş portlar)), portsuz ama ERT'de kaydı olanlar (`BirimSatir.ErtKayit`) `{ips}`; 50 sunuculuk parçalar, 409'da bekleyip bir kez
  daha, her parça `/api/eksik/durum` bitene kadar izlenir; (2) satırlar yenilenir, birimi hâlâ boş (öneri yok, uygulama önerisinden de gelmeyen) IP'ler `/api/birim/calistir`.
  "↻ Bu IP için yeniden tahmin" aynı akış (`yeniden:true`). Durdur iki işi de durdurur (`st.tahminIptal`). Mock'la denendi.
  SEÇİM (kullanıcı: "sadece seçilenleri de aratabileyim"): tabloda ilk kolon onay kutusu (`data-sec`, IP bazında `st.secili`; başlıktaki `#secHepsi` süzülen satırların IP'leri),
  "🤖 Seçilenleri tahmin et (N sunucu)" (`secTahmin` → `tahminEt(seçili, yeniden)`) + "önerisi olanları da yeniden sor" (`#secYeniden`) + "✕ seçim"; toplu düğme "Tümünü tahmin et".
  Yeni tarama / kayıt açılınca seçim temizlenir. Mock'la denendi.
  AI İLE UYGULAMA ARA (kullanıcı: "tek tek AI'a gönder, uygulama tahmini gelmedi, sadece ekip"; "atlama yok, AI ile uygulama ara nerede"): ayrı düğme `#uygAi` (seçim varsa
  seçililer `yeniden:true`, yoksa ERT'de uygulaması boş ve önerisi olmayan tümü) + Uygulama hücresinde "🤖 AI ile ara" (`data-bk="uyg"`, tek IP yeniden). `uygAra`: HİÇBİR sunucu
  atlanmaz — port: CB ∪ Nessus bulgu ∪ ERT boş portları; yoksa `POST /api/birim/portlar` (Nessus `SunucularAsync` portları, yoksa ERT portları; < dinamik), o da yoksa "0"
  (sunucu düzeyi). Önceden sessiz kalan durumlar: AI'ın "bulunamadı" cevabı (Uygulama null) gösterilmiyordu → `BirimUygOneri.Uygulama` nullable, hücrede "🤖 bulunamadı" +
  gerekçe ipucu; motor hatası ("sorulacak port yok", AI hatası) → `eksikBekle` son durumu döner, `asama Hata / mesaj / hatalar` özette. Özet `st.sonMesaj`: "N soruldu → X bulundu,
  Y bulunamadı, Z cevapsız · HATA: …". `tahminEt` = `uygAra` + birim. Mock'la denendi.
  TANIMLI BİRİM KURALI (kullanıcı: "ZP ile başlayan ya da Ziraat PAY ile ilgili tüm sunucular Ziraat PAY Uygulamaları ekibinin olmalı"): `Birim:Kurallar` [{Desen (regex,
  büyük / küçük harf duyarsız, İ/ı katlanır), Birim, Alan ("ad" yalnız sunucu adı | "uygulama" | boş = ikisi)}]; boşsa varsayılan `^py` (Alan ad) ve
  `ziraat\s*-?\s*pay` → "Ziraat PAY Uygulamaları" (ERT kataloğundaki yazımıyla, `KatalogYazimi`). DÜZELTME (kullanıcı: "ZP Ziraat Portföy, PY genelde Ziraat Pay"): ilk sürümdeki
  `^zp` kaldırıldı; o kuralla yazılmış öneriler (`BayatKural`: Kaynak Kural, durum AI, gerekçe "tanımlı kural …", şimdiki kural uymuyor) gösterilmez ve yeniden sorulur.
  `KuralBul`: ERT sunucu adları + sayfadan gelen ad (`BirimIstek.Adlar` IP → CB bilgisayar / sunucu adı; ERT'de kaydı olmayan kutular için) + ERT uygulamaları + uygulama önerileri.
  `TahminAsync`'te AI'dan ÖNCE (Kaynak "Kural", güven 95); `BirimSatir.KuralBirim / KuralNeden` → sayfada ERT boşsa "📏 X (kural)" (onaylı / elle öneri öncelikli, AI önerisinden
  önce), ERT doluysa ve farklıysa "⚠ kurala göre X olmalı"; CSV BİRİM KAYNAĞI'nda. genelbilgiler.txt (Eksik:BilgiDosyasi) hem uygulama hem birim AI prompt'unun başında.
  VARLIK MUHAFIZI KOLONU (kullanıcı: "VARLIKMUHAFIZI kolonu kayboldu?" — Uygulama hücresinin altında küçük yazıydı, yeni düğmelerle görünmez oldu): ayrı "Varlık muhafızı" kolonu
  (`muhafizHtml` / `muhafizBilgi`: satırdaki ERT, yoksa `BirimSatir.Muhafiz` (eski kayıtlı taramalar), yoksa 🤖 önerilen uygulamadan tahmin), "Muhafız" süzgeci, aramada; CSV MUHAFIZ.
  KARŞILAŞTIRMA (kullanıcı: "bulgusu giderilenleri nasıl anlayacağız" → "satırda eski ve yeni halini göreyim, eski versiyon yeni versiyon; tarih olarak da"): GET
  `/api/zafiyet/kayit/{id}` (`KayitVeriAsync`, kayıtlı taramanın satırları, servis durumuna dokunmaz; `KayitOkuAsync` KayitAcAsync ile ortak). Araç çubuğunda "Karşılaştır" (aynı
  kuralın kayıtlı taramaları, DMZ eki gözetilmez; varsayılan bu taramadan önceki en yeni kayıt, `st.karsOto`), eşleşme IP (yoksa CB bilgisayarı) + süreç yolu (küçük harf, \ → /).
  `degisim`: giderildi (önce etkilenen / olası / bilinmiyor / bilgi → şimdi yamalı / etkilenmez), önlem alındı (önlem yok → var), değişti, aynı, yeni; önceki taramada olup şimdi
  olmayanlar soluk "hayalet" satır (artık görülmüyor — doğrulanmalı; tıklanmaz, seçilmez). Sürüm hücresinde "önce (tarih): sürüm durum" + rozet, hayalette "şimdi (tarih): görülmedi";
  "Değişim" süzgeci, özet (`#karsNot`: tarih → tarih: sayılar), CSV ÖNCEKİ TARAMA / ÖNCEKİ SÜRÜM / ÖNCEKİ DURUM / DEĞİŞİM (hayaletler dahil). Mock'la denendi.
  "SİSTEM PORTUDUR" DÜZELTMESİ (kullanıcı: "hiç memnun değilim, sistem portudur basıp geçiyor hepsine"): (1) `ZfSatir.Uygulamalar` ERT'deki "Sistem Portudur…" kayıtlarını
  uygulama sayıyordu (yalnız sistem portu kaydı olan sunucuda hücre "Sistem Portudur", AI düğmesi çıkmıyordu) → `env.IsSystemApp` ile ayıklanır; sayfada `ertUyg` eski kayıtlarda da
  ayıklar. (2) Zafiyet'ten ERT eksiklerine giden port listesinde altyapı / sistem portları vardı (özellikle Nessus'un tüm portları) → `EksikService.TahminDisiPortlar()`
  (Uygulama:AltyapiPortlari ∪ Eksik:BilinenSistemPortlari ∪ 23, 111, 593, 1900, 3702, 5353, 5355; web portları GÖNDERİLİR) `/api/birim/portlar`'da ve sayfada (`satirPortlari`, yanıttaki
  `tahminDisi`) süzülür; sıra CB süreç portları → ürünün Nessus portları → ERT boş portları, en çok 10. (3) "Altyapı: / Sistem Portudur" cevapları ve sistem portundaki öneriler
  uygulama önerisi sayılmaz (`isOnerisi`, `BirimService.UygulamaDegil`; birim / muhafız bunlardan türetilmez, "tümü" yeniden sorabilir). Mock'la denendi.
  BAŞTAN (kullanıcı: "tüm eksikleri baştan kontrol et seçeneği yok"): `#secYeniden` "baştan (önerisi olanları da yeniden sor)" her zaman görünür; işaretliyse "Tümünü baştan tahmin et"
  ve "AI ile uygulama ara — baştan" `yeniden:true` (uygulama: onaylı / elle uygulaması olan IP'ler hariç; birim `birimHedef`: ERT'de birimi boş, onaylı / elle birimi olmayanlar).
  Yeniden sorarken önceki iş önerilerinin portları da port listesine girer.
  CSV (kullanıcı: "değişim kolonuna = yazmışsın, Excel formül sanıyor"): DEĞİŞİM sembolsüz (`DEG_DUZ`); `q()` = + @ ya da - ve harfle başlayan hücrenin başına boşluk koyar
  (Excel formül sanmaz; komut satırı / Nessus metniyle formül enjeksiyonu da olmaz). imza.html / topoloji.html CSV'lerinde aynı koruma henüz yok.
  TANIDA CARBON BLACK (kullanıcı: "bir IP gelmiyor, ekip Java var diyor; fazla mı Nessus bazlıyız, CB'den gelmiyor, nasıl test ederiz"): `/api/zafiyet/tani` artık Nessus'tan önce
  `CbTaniAsync` (son `Zafiyet:TaniSaat`=168 saat, sorgu başına `TaniSureSaniye`=180; `Zafiyet:TaniCarbonBlack`=false kapatır): (1) IP'nin ağ olayları süreç başına (local_ip IN) —
  ✔ kurala uyan, ☕ iz var ama kurala uymayan (yolunda kuralın exe adları / Java'da java|jre|jdk|jvm|openjdk|zulu|corretto|temurin|graalvm|semeru|-jar geçen YA DA süreç başlangıcının
  komut satırında iz olan, ör. wrapper.exe … jvm.dll), diğerleri; (2) olay yoksa ERT / CB adıyla arama → CB'de başka IP ile mi görünüyor (çok kartlı / NAT); (3) bilgisayar adlarıyla
  süreç başlangıçlarında iz (komut satırı). IP başına sonuç: başka adlı exe içinde → exe adını kurala ekle; kurala uyan var ama son olay tarama penceresinden eski → pencereyi büyüt;
  yalnız süreç başlangıcı; ajan yok / olay yok; başka IP. Sahte Splunk ile denendi.
  KÖR NOKTA / NESSUS ENVANTERİ (kullanıcı: "ajan yok, Nessus'tan da gelmedi; o sunucuya ait ilgili exe'leri her koşulda Nessus'tan getirelim, kısıt varsa belirt"; "ERT iş görmez,
  manuel bilgi" → ERT'ye dayalı "aynı uygulamanın sunucuları" yapılmadı): `NessusService.EnvanterAsync(ips | assetId)` plugin 70329 süreçler, 44401 servisler (yol), 34252 / 25221
  dinleyen program, 20811 yazılım, 22869 paketler, 19506 kimlikli mi — repository süzgeçsiz, son `Zafiyet:NessusGun`, `Zafiyet:NessusEnvanterSayfa`=30 × 2000. `KorNoktaAsync`
  (Birlestir'den hemen sonra, `Zafiyet:NessusEnvanter`=true): kapsam IP kipi ya da DMZ asset'i (tam taramada da; tüm sunucularda yapılmaz, mesajla); sonuçta HİÇ satırı olmayan
  IP'lerde satır satır eşleşme (kuralın exe adları; yazılım / paket satırında Nessus kelimeleri + Java'da JVM izleri, javascript / jquery / json hariç), yol satırdan, sürüm Java kalıpları
  / "Java 8 Update 202" → 1.8.0.202 / ilk a.b.c; yalnız adı olan süreç kaydı yolu bilinen aynı exe varsa atılır; satır kaynağı "Nessus (süreç listesi | Windows servisleri | portu
  dinleyen program | kurulu yazılım | paket listesi)". Mesajda kayıtlı / kimlikli / kimliksiz sayıları ve kimliksiz IP'ler (Nessus kurulu yazılım / süreç GÖREMEZ). IP kipinde hâlâ
  görünmeyen IP → "Kör nokta (görülmedi)" satırı, öncelik İncele, notta CB (`CbOlayVarAsync`: pencerede olay var mı) + Nessus nedeni (kayıt yok / kimliksiz / kimlikli ama yok /
  19506 yok). `SunucularAsync(…, genis)` da repository süzgeçsiz + NessusGun (ayrı önbellek anahtarı "g:"). Sahte Tenable.sc + Splunk ile denendi.
  DMZ ETİKETİ HATASI (kullanıcı: "Nessus'ta DMZ olan sunucu bizde DMZ etiketi almamış, dün verdiğim grubun içinde"): üyelik yalnız asset'in viewableIPs aralıklarına bakıyordu
  (dinamik asset'te eksik / farklı) ve sumip yalnız aralık boşsa çalışıyordu → `AssetIpleriAsync` HER ZAMAN asset süzgeçli sumip'i (repository süzgeçsiz, NessusGun) de alıp tek IP
  aralığı olarak ekler; kaynak "asset tanımı: N aralık + Tenable asset süzgeci: M sunucu". OpenSSL CVE-2026-121481 (EVP_EncodeBlock, PR #33652 açık, dal 4.0 / 4.1, sürüm yok)
  kullanıcı isteğiyle PARK edildi (sürüm aralıksız "OpenSSL envanter" kuralı önerildi).
  YETKİ (kullanıcı: "DeltaFlow'un bu bulgu kısmını da DokuPanel'e ekleyelim, yetkilendirme olsun"): yeni `Yetki:Zafiyet` (DokuPanel site kaydı; boşsa Topoloji) — Program.cs'te
  `/api/zafiyet*` ve `/api/birim*` için ara katman (okuma dahil, 403 "Açıklık taraması yetkiniz yok"); tarama / kural / kayıt / tanı uçlarındaki Topoloji kontrolleri Zafiyet oldu;
  uygulama / birim tahmini ve kararı ayrıca Yazma, Excel Disari. zafiyet.html önce /api/me: `zafiyet` yetkisi yoksa (`yetkiYok`) veri istenmez, tüm kontroller pasif, kilit mesajı
  (eski DLL'de `zafiyet` alanı yoksa topoloji'ye bakar); GET /api/zafiyet 403 de kilitler. Yetkisiz / yetkili akış Playwright'la denendi. OpenSSL 17.11.2026 duyurusu (kullanıcı yapıştırdı:
  CVE-2026-107493, CVE-2026-124709, ikisi Low; güncel sürümler 4.1.1 / 4.0.4 / 3.5.10 / 3.0.24) — 121481 henüz yok; "OpenSSL güncel mi" kuralı önerildi, onay bekliyor.
  KİŞİYE ÖZEL (kullanıcı: "herkes kendi kayıtlı taramalarını görmeli"): kayıtlar — liste (`KayitlarAsync(…, user)`: KAYDEDEN = kullanıcı, DOMAIN\ öneki fark etmez, sahibi
  boş eski kayıtlar herkese), açma / karşılaştırma / silme (`Sahibi`) yalnız kaydı yapanın; saklama kural + KİŞİ başına son KayitSayisi. Ekrandaki sonuç da kişiye özel: `_is / _satir
  / _mesaj` çalışan işin canlı durumu (`_isSahip`), iş bitince ya da kayıt açılınca `_gor[kullanıcı]`; `SonucAsync(ct, user)` → `Gorunum(user)`; başkasının işi çalışırken
  `ZfSonuc.Mesgul` ("X şu an tarama yapıyor…", sayfada ⏳ bandı, "Yeni tara" pasif, 15 sn'de bir yoklar) — aynı anda tek tarama kalır; kendi kayıtları bu sırada açılabilir.
  "Listeye ekle" ve SonKural kullanıcının görünümünden; durdurma yalnız başlatan (`Durdur(user)`, 403); tanı kullanıcının görünümüyle. Sayfada "Son taramalarım". Scratch test ucuyla
  (iki kullanıcı, bellek kaydı) denendi; SQL'de KAYDEDEN eşleşmesi `= @u OR = kısa ad OR LIKE '%\kısa' OR IS NULL`.
  FİZİKSEL DE (kullanıcı: "fiziksele de yapalım"): `FizikselService.KayitlarAsync(ct, kullanici)` denetim kayıtlarını yalnız kaydedene listeler (aynı KAYDEDEN eşleşmesi, sahibi boş
  eskiler herkese), `ExcelAsync(kayit=id)` başkasının kaydında 403; fiziksel.js "Denetim kayıtlarım". ELLE DEĞERLER ORTAK KALDI (cihazın değeri tek; kişiye özel olursa kabin değeri
  kişiden kişiye değişir) — kullanıcıya soruldu. SQL olmadığı için yalnız derlendi.
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
  Yazma da gerekir; boşsa `Eksik:TopluYetkililer`, varsayılan visikhan), `Yetki:Tls` (kullanıcı: "bu ekrana da DokuPanel yetkisi": TLS toplu
  sorgu — `/api/tls/toplu*` 403, sekme `body.no-tls` ile gizli, `?tls=1` ile gelinirse "yetkiniz yok"; boşsa Topoloji ile aynı; tek IP TLS paneli
  serbest), `Yetki:Fiziksel` (kullanıcı: "fiziksel envanteri de DokuPanel'e kaydedelim": `/api/fiziksel/*` (ozet, oda, kabin, ara, eksik,
  eslesmeyen, kabindisi, yenile; `FizY`) 403, sekme `body.no-fiziksel` ile gizli, `?fiziksel=1` → "yetkiniz yok" (`fzYetkiYok`); IP kartındaki
  konum satırı `/api/fiziksel/ip` serbest, yetkisizde kabin adı bağlantısız; boşsa Topoloji ile aynı). Auth ara katmanı /api isteklerinde yapılandırılmış siteleri
  `AuthHelper.YetkiKontrol(ctx, cfg, siteAdi)` ile sorar (kullanıcı + site başına `Auth:CacheMinutes`; aynı ad tek sorgu) →
  `context.Items["Yetki"]` → `Yetki.Var(cfg, ctx, tür)`. `/api/me` → `yetki {yazma, topoloji, disari, toplu, tls, fiziksel, fizikseldeger}`; topoloji.html body
  `no-disari` / `no-topoloji` sınıfları (yüklenene kadar gizli).
  SAYFA YETKİLERİ (kullanıcı: "IP sorgulama ve Uygulama sorgulama dışındaki tüm DeltaFlow alt sayfalarında yetkilendirme olmalı, TLS eksik"): yeni
  `Yetki:Erisim` (Erişim sorgula), `Yetki:Islev` (Topoloji sekmesi: işlev haritası, küme topolojisi, versiyon, kurum), `Yetki:Etiket` (Sunucu etiketleme),
  `Yetki:Eksik` (ERT eksikleri), `Yetki:Imza` (Servis imzası), `Yetki:Segment` (Segment / etiket tarama) — hepsi boşsa Topoloji ile aynı (`TopolojiyeDuser`).
  Tek ara katman (Program.cs, eski Zafiyet ara katmanının yerine) `Yetki.SayfaYetkisi(yol, metot)` → gereken türlerden BİRİ: /api/zafiyet, /api/birim,
  /api/nessus/assetler → Zafiyet; /api/erisim, /api/opinnate → Erisim; /api/islev, /api/tenantlar → Islev; /api/etiket/aciklama → Etiket|Islev; /api/etiket/*
  → Etiket (POST /api/etiket SERBEST: IP kartındaki "+ Etiket", uçta Yazma); /api/eksik ve /csv → Eksik; /api/eksik/ip|durum|calistir SERBEST (IP kartı AI önerisi;
  calistir uçta Yazma); diğer /api/eksik/* → Eksik|Imza|Segment|Zafiyet (oneriler, kanit, karar, durdur başka sayfalardan da); /api/imza → Imza;
  /api/segment/exeag → Segment|Etiket|Islev; /api/segment → Segment|Etiket (etiketlemedeki "Segment getir"); /api/tls/toplu → Tls; /api/fiziksel (ip hariç)
  → Fiziksel; POST /api/envanter/yenile → Yazma. Serbest kalanlar: IP / uygulama görünümünün uçları (flow, hop2, surec*, tls, nessus/ip, f5uri, oturum,
  yolculuk, ai/etki, app, apps, etiketler, filtreler, appliances, me). Uçlarda: imza tara / durdur → Imza (eskiden Topoloji), segment tarama → Segment|Etiket,
  segment AI → Segment, nessus/assetler → Zafiyet. Arayüz: TÜM sekmeler görünür (TLS'in `body.no-tls` gizlemesi kaldırıldı); topoloji.html `SEKME_YETKI`
  (topo → islev, erisim, etiket) + `sekmeKapali` / `sekmeKilit` (#main'de 🔒 mesaj, setUiMode / submit / runTopo / runAccess / runEtiket / ktExeRun sorgu
  göndermez), TLS `ttYetkiYok`, fiziksel `fzYetkiYok`; ilk derin bağlantı `/api/me` gelmeden çalışmaz (`meHazir`). eksik.html / imza.html / segment.html:
  `sayfaKilit` (veri yüklenmez, main'deki tüm denetimler pasif, 🔒 mesaj); segment tarama / yeniden çek düğmeleri `tarayabilir()` (segment, eski DLL'de topoloji),
  imza tarama düğmeleri imza yetkisine bakar. Auth kapalı + `Etiket__Yazanlar__0=baskasi` ile uçlar, Playwright + mock'la kilitler denendi. DokuPanel'e ulaşılamazsa o işlem kapalı. Görüntüleme: IP / uygulama sorgusu yalnız site yetkisiyle (AppSettings:CurrentSiteName) açık; diğer alt sayfalar yukarıdaki SAYFA YETKİLERİ ile.
- Marka: **DeltaFlow** adı ve logosu.

## Kullanıcının tercihleri / kurallar
- Arayüzde ve metinlerde **"bizim" ifadesi kullanılmaz**: "Sorgulanan IP", "Gelen/Gidilen sunucular", "Karşılayan uygulama", "Aynı segment".
- Ayarların tamamı **yalnızca `appsettings.json`**'dan okunur (eski DeltaFlow `config.json` desteği kaldırıldı).
- **Repodaki `appsettings.json` DEĞİŞTİRİLMEZ / commit'lenmez** (kullanıcı: kendi appsettings'i var, pull çakışıyor — "appsettings'i hiç yollama,
  ekleme varsa buradan ver"). Yeni ayar gerekirse kodda varsayılan değer olur ve kullanıcıya yalnız eklenecek JSON parçası sohbette verilir.
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
