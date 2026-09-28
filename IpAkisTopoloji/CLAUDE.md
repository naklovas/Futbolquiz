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
| `AuthHelper.cs` | Kullanıcının verdiği DokuPanel yetki helper'ı (namespace `AppRel`), anahtar config'den |

## Veri kaynakları
- **Splunk / Carbon Black**: `/services/search/v2/jobs/export`, SPL `index=carbonblack sourcetype="bit9:carbonblack:json" TERM(ip)`
  (çoklu IP: `(TERM(a) OR TERM(b))`), `direction`'a göre istemci/sunucu, `stats` by client_ip server_ip server_port Protocol.
- **AppResponse**: token → `npm.reports` instance (L4 `flow_tcp` + L7 `wtapages`, STEELFILTER `cli_tcp.ip == X or srv_tcp.ip == X`)
  → bekle → veri → sil. `appliance=-1` = tüm kutular paralel (arayüzde varsayılan "Tüm kutular"). VifgIds destekli.
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
- **ODM / TEST**: karşı IP'nin segment Domain'inde "odm"/"test" geçiyorsa topoloji araç çubuğundaki kutucuk işaretliyken
  gösterilir (varsayılan gizli, tarayıcıda filtre, yeniden sorgu yok). `OrtamFiltreleri` ayarı. Uygulama görünümünde "X · ODM" ayrı grup.
- **AI etki analizi** (`/api/ai/etki`): OpenAI uyumlu şirket içi servis, model `zt-ga-small-0`, Bearer ApiKey,
  tek user mesajı, temperature 0.1, `<think>` temizlenir. Etki tablosu kodda hesaplanır (servis → kullanan uygulamalar,
  bağımlılıklar; her bağlantı iki ucundaki uygulamayla). **2. seviye AI'a gönderilmez** (flu sonuç veriyordu).
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
