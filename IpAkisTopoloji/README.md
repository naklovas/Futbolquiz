# DeltaFlow

<img src="wwwroot/deltaflow.svg" width="56" alt="DeltaFlow">

Ağ akışı, segment ve uygulama topolojisi ve etki analizi.

Bir IPv4 adresi ve zaman aralığı girersin. Araç Splunk (Carbon Black) ve Riverbed AppResponse'tan
o IP'nin akışlarını çeker, iki SQL Server envanteriyle zenginleştirir ve şunları gösterir:

- **Gelen trafik**: bu makineye hangi IP'ler, hangi segmentlerden, hangi uygulamalardan geliyor;
  sorgulanan IP'de hangi porta, hangi uygulamaya geliyorlar.
- **Giden trafik**: bu makine hangi IP:port'a gidiyor; hedef hangi segmentte ve hangi uygulama.
- **Topoloji** (iki seviyeli zincir):

  ```
  [Segment] ──> [Gelen sunucu] ──> [SORGULANAN IP] ──> [Gidilen sunucu] ──> [Segment]
  ```

  - 1. seviye: sorgulanan IP'ye gelen ve sorgulanan IP'nin gittiği sunucular (Splunk + AppResponse).
  - 2. seviye: bu sunucuların kendi trafiği tek sorguda çekilir (`POST /api/hop2`).
    Solda sunuculara **gelen** segmentler, sağda sunucuların **gittiği** segmentler gösterilir;
    iki taraf da açılır listeden ters çevrilebilir.
  - Taraf başına ilk 15 sunucu çizilir (10/15/25/40 seçilebilir), kalanı "+ n sunucu" kutusunda.
  - Sorgulanan IP ile aynı segment kalın çerçeve ve "AYNI SEGMENT" ile işaretlenir.
  - Üzerine gelince yol vurgulanır. Sunucuya tıklayınca detay (port, uygulama, 2. seviye segmentleri)
    açılır; "Bu IP'nin topolojisini aç" ile o sunucuya geçilir. Segmente tıklayınca bağlı sunucular listelenir.
  - Yakınlaştırma, SVG/PNG indirme var.
- **Sunucuların kendi segmentleri**: 1. seviye sunucuların segment özeti; IP sayısı, port, uygulama, hit.

Sayfa: `/` ya da `/topoloji.html?ip=10.210.10.63` (adres çubuğundaki link paylaşılabilir).

- **AI etki analizi**: sorgu bittikten sonra "AI'a yorumlat" ile şirket içi AI servisine bir etki tablosu gönderilir.
  Tablo kodda hesaplanır:
  - Sorgulanan sunucudaki her servis (uygulama + port) ve onu kullanan uygulamalar (IP, segment, sahip/muhafız).
  - Sorgulanan sunucunun kullandığı servisler (hedef uygulama, IP:port, bağlantıyı açan süreç).
  AI yalnızca bu tabloyu yorumlar, tablo dışına çıkmaması ve her maddede uygulama/IP/port yazması istenir.
  2. seviye (sunucuların kendi trafiği) AI'a gönderilmez. Soru metni değiştirilebilir; gönderilen metin sayfada görülebilir.

## Uygulama görünümü

Başlıktaki **Uygulama sorgula** sekmesinde ERT_HOSTIPADDRESS'teki uygulamalar arama kutusunda listelenir
(`/?app=<ad>` ile de açılır). Seçilen uygulamanın tüm sunucuları ve VIP'leri (ERT `VIP_IP` + `vip_envanteri`)
tek Splunk sorgusu / tek AppResponse raporuyla sorgulanır ve akışlar **uygulama seviyesinde** gruplanır:

```
[Bu uygulamayı kullananlar] ──> [ UYGULAMA: VIP'ler + sunucular (segment bazında) ] ──> [Bağımlı olunanlar]
```

- Kenar kutuları uygulamadır; envanterde olmayan IP'ler segmentine göre gruplanır.
- Ortadaki kutuda VIP'ler ve segment grupları; segment grubuna tıklayınca sunucular açılır.
  Mor yaylar uygulama içi trafik: düz = LB (VIP → üye, GW üzerinden), kesik = sunucudan sunucuya.
- Kullananlar / Bağımlılıklar / İç trafik / Altyapı seçilebilir. Altyapı (DNS, AD, NTP, izleme, RDP/SSH…)
  varsayılan gizli; portlar `Uygulama:AltyapiPortlari` ile değiştirilebilir.
- Kutuya tıklayınca IP'ler ve "Bu uygulamayı aç"; VIP/sunucuya tıklayınca üyeler ve IP topolojisi bağlantısı.
- En fazla `Uygulama:MaxIp` (varsayılan 80) IP sorgulanır; VIP'ler önce gelir.

## Kimlik doğrulama ve yetki

Sayfaya Windows Authentication ile girilir; yetki DokuPanel'den `AuthHelper.YetkiKontrol` ile alınır.
Kontrol bütün isteklere (sayfa, statik dosyalar, API) uygulanır; sonuç kullanıcı başına
`Auth:CacheMinutes` (varsayılan 5 dk) önbellekte tutulur, hata 30 sn tutulur. Yetkisiz kullanıcı
sayfada "Erişim Reddedildi" ekranını, API'de `403` JSON hatasını görür. API çağrıları kullanıcı adıyla loglanır.

```json
"Auth": { "Enabled": true, "CacheMinutes": 5 },
"AppSettings": {
  "CurrentSiteName": "DeltaFlow",
  "DokuPanelApiUrl": "http://.../DokuPanel/api/config/get-groups-by-name?siteName=",
  "DokuPanelKey": "<DokuPanel anahtarı - repoya koymayın>"
}
```

- IIS: sitede **Windows Authentication açık, Anonymous kapalı** olmalı (IIS Manager > Authentication).
- Kestrel (`dotnet run`): Negotiate kullanılır; tarayıcı sunucuyu intranet sitesi olarak tanımalı.
- Yerelde denemek için `"Auth": { "Enabled": false }`.

## AI servisi

OpenAI uyumlu `/chat/completions` servisi kullanılır (`Authorization: Bearer <ApiKey>`,
yanıt `choices[0].message.content`). `appsettings.json`:

```json
"Ai": { "Url": "https://.../v1/chat/completions", "ApiKey": "...", "Model": "zt-ga-small-0",
        "Temperature": 0.1, "TimeoutMinutes": 3, "IgnoreSslErrors": true }
```

Her yönde en yoğun 80 bağlantı gönderilir. Endpoint: `POST /api/ai/etki`.

## Envanter tabloları

| Tablo | Kullanım |
|---|---|
| `dbo.KonsolideSegmentler4` | `SEGMENT` (CIDR) en uzun prefix eşleşmesiyle IP'ye segment bulur. `#N/A`, `NULL` boş sayılır. Ekrandaki ad: EPGName → ApplicationProfile → Description → CIDR. |
| `dbo.ERT_HOSTIPADDRESS` | IP:port → uygulama. Eşleşme sırası: `IPADDRESS+PORT` → `VIP_IP+VIP_PORT` → sadece `IPADDRESS` → sadece `VIP_IP`. Hangisiyle eşleştiği tabloda rozet olarak görünür. |

| `dbo.vip_envanteri` | VIP (load balancer) → havuz üyeleri: `loadbalancer_ip:loadbalancer_port` → `hostname, server_ip:server_port, loadbalancer_pool`. |
| `dbo.vipgw` | Her segmentte LB'nin üyelere gittiği GW IP'si: `GWIP, FW, Segment (CIDR)`. |

**Sistem portları:** `ERT_HOSTIPADDRESS.APPNAME` değeri "Sistem Portudur" olan kayıtlara portun kullanım
şekli eklenir: `Sistem Portudur: RDP` (3389), `Sistem Portudur: SSH` (22), `Sistem Portudur: FTP` (21) …
Bilinen portların listesi kodda (`SystemPorts`); kuruma özel portlar `appsettings.json`'dan eklenir ya da değiştirilir:

```json
"Envanter": { "SistemPortuEtiketi": "Sistem Portudur", "SistemPortlari": { "7001": "WebLogic", "3389": "RDP" } }
```

Listede olmayan port `Sistem Portudur: port 12345` olarak gösterilir.

**VIP'ler:** LB havuz üyelerine VIP adresinden değil üyenin segmentindeki GW IP'sinden gider; bu yüzden
VIP'in arkası Carbon Black ya da AppResponse'ta VIP IP'siyle görünmez.
- Sorgulanan IP bir VIP ise üyeler ve GW IP'leri aynı sorguya eklenir. Üyeler topolojide
  "gidilen" tarafta **VIP ÜYESİ** olarak çizilir (2. seviye böylece üyelerin arkasını da gösterir).
  GW → üye:port trafiği görülen üye "doğrulandı" olarak işaretlenir.
- Gidilen bir IP VIP ise kutusunda **VIP → n ÜYE** yazar; "Bu IP'nin topolojisini aç" ile arkasına geçilir.
- Sorgulanan sunucu bir havuzun üyesiyse hangi VIP'in arkasında olduğu gösterilir; gelen trafikteki GW IP'leri **LB GW** olarak işaretlenir.
- VIP tabloları okunamazsa uygulama VIP çözümlemesi olmadan çalışmaya devam eder.

Gelen trafikte karşı tarafın kaynak portu geçici (ephemeral) olduğu için karşı hostun uygulaması
sadece IP ile bulunur. Sorgulanan IP tarafındaki uygulama ise IP+port ile bulunur.

Tablolar bellekte tutulur (`Envanter:CacheMinutes`, varsayılan 30 dk).
Hemen yenilemek için: `POST /api/envanter/yenile`. Durum: `GET /api/envanter/durum`.

## Ayarlar

Tüm ayarlar `appsettings.json` içindedir:

```json
"ConnectionStrings": {
  "Envanter": "Server=SQLSUNUCU;Database=doku;Integrated Security=True;TrustServerCertificate=True"
},
"Envanter": { "SegmentTablosu": "dbo.KonsolideSegmentler4", "HostTablosu": "dbo.ERT_HOSTIPADDRESS", "CacheMinutes": 30 }
```

AppResponse ayarları da (`Credentials`, `Servers`, `SourcePathType`, `SourceL4`, `SourceL7`, `VifgIds`)
aynı dosyadadır. Başka bir config dosyası okunmaz.

SQL kullanıcısıyla bağlanılacaksa: `User Id=dokuuser;Password=...` (şifreyi repoya koyma).

## API

| Endpoint | Açıklama |
|---|---|
| `GET /api/flow?ip=&start=&end=&appliance=&sources=splunk,appresponse` | Birleşik ve zenginleştirilmiş akış. Bir kaynak hata verirse diğerinin sonucu yine döner. |

`appliance=-1` (arayüzde "Tüm kutular", varsayılan seçim): `Servers` listesindeki tüm AppResponse cihazları paralel sorgulanır, sonuçlar birleştirilir. Aynı akışı birden fazla cihaz görüyorsa hit sayısı toplanır. Tek cihazın hatası diğerlerini durdurmaz.

| `POST /api/hop2` `{target, ips[], start, end, appliance, sources}` | Verilen sunucuların trafiği, sunucu başına gelen/giden segment özeti. En fazla `Hop2MaxPeers` (varsayılan 80) IP. |
| `GET /api/splunk`, `GET /api/appresponse`, `GET /api/appliances` | Önceki endpoint'ler; çıktıları değişmedi. |

## Mevcut projeye taşıma

1. `Topoloji.cs` dosyasını projeye ekle.
2. `Microsoft.Data.SqlClient` paketini ekle.
3. `Program.cs` içindeki değişiklikleri al: `AddSingleton<EnvanterService>()`, `/api/flow` ve
   `/api/envanter/*` endpoint'leri, `Capture` fonksiyonu, `SplunkResult` / `AppResponseResult` tipleri.
4. `wwwroot/topoloji.html` dosyasını kopyala (mevcut `index.html`'e dokunmaz).
