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
| `PortBilgisi.cs` | Port → işlev ve kesilirse duracak iş (AI prompt'u için) |
| `AuthHelper.cs` | Kullanıcının verdiği DokuPanel yetki helper'ı (namespace `AppRel`), anahtar config'den |

## Veri kaynakları
- Uygulama görünümü "Yalnızca segmentler" kutucuğu (`segmentLinks`, uygulama.js): kullananlar / bağımlılıklar karşı IP'lerin
  segmentine göre tek kutu; içinde o segmentteki VIP'ler (`VIP ip:port · uygulama`) ve uygulama adları; hedef bağları hit payıyla dağıtılır.
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
- **SQL (doku DB)** — tablo adları `Envanter:*` ayarında:
  - `dbo.KonsolideSegmentler4` (SEGMENT=CIDR, VLAN, Tenant, ApplicationProfile, EPGName, BD, GW, Description, Domain):
    IP → en uzun prefix eşleşmesi. Segment adı = EPGName > ApplicationProfile > Description > CIDR.
    **Aynı adlı segmentler tek segment sayılır** (CIDR değil ad bazında gruplanır). `#N/A`/`NULL` boş sayılır.
  - `dbo.ERT_HOSTIPADDRESS` (IPADDRESS, PORT, VIP_IP, VIP_PORT, APPNAME, WEBSITE, UNIXNAME, SERVICENAME, VARLIKMUHAFIZI,
    SUNUCUISLETENBIRIM, UYGULAMAVARLIKSAHIBI): uygulama eşleşmesi sırası IP+PORT → VIP_IP+VIP_PORT → IP → VIP_IP.
    APPNAME "Sistem Portudur" → "Sistem Portudur: RDP" gibi (SystemPorts; `Envanter:SistemPortlari` ile ek/override).
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
