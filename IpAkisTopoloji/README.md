# IP Akış Topolojisi

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

Sayfa: `/topoloji.html?ip=10.210.10.63` (adres çubuğundaki link paylaşılabilir).

- **AI etki analizi**: sorgu bittikten sonra "AI'a yorumlat" ile gelen/giden sunucular (segment, uygulama,
  sahip/muhafız, port, trafik, 2. seviye segmentler) şirket içi AI servisine gönderilir ve
  "bu sunucuda değişiklik yapılırsa nereler etkilenir" yorumu alınır. Soru metni değiştirilebilir;
  AI'a gönderilen metin sayfada görülebilir.

## AI servisi

OpenAI uyumlu `/chat/completions` servisi kullanılır (`Authorization: Bearer <ApiKey>`,
yanıt `choices[0].message.content`). `appsettings.json`:

```json
"Ai": { "Url": "https://.../v1/chat/completions", "ApiKey": "...", "Model": "zt-ga-small-0",
        "Temperature": 0.1, "TimeoutMinutes": 3, "IgnoreSslErrors": true }
```

Her tarafta en yoğun 30 sunucu gönderilir. Endpoint: `POST /api/ai/etki`.

## Envanter tabloları

| Tablo | Kullanım |
|---|---|
| `dbo.KonsolideSegmentler4` | `SEGMENT` (CIDR) en uzun prefix eşleşmesiyle IP'ye segment bulur. `#N/A`, `NULL` boş sayılır. Ekrandaki ad: EPGName → ApplicationProfile → Description → CIDR. |
| `dbo.ERT_HOSTIPADDRESS` | IP:port → uygulama. Eşleşme sırası: `IPADDRESS+PORT` → `VIP_IP+VIP_PORT` → sadece `IPADDRESS` → sadece `VIP_IP`. Hangisiyle eşleştiği tabloda rozet olarak görünür. |

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

`appliance=-1` (arayüzde "Tüm cihazlar"): `Servers` listesindeki tüm AppResponse cihazları paralel sorgulanır, sonuçlar birleştirilir. Aynı akışı birden fazla cihaz görüyorsa hit sayısı toplanır. Tek cihazın hatası diğerlerini durdurmaz.

| `POST /api/hop2` `{target, ips[], start, end, appliance, sources}` | Verilen sunucuların trafiği, sunucu başına gelen/giden segment özeti. En fazla `Hop2MaxPeers` (varsayılan 80) IP. |
| `GET /api/splunk`, `GET /api/appresponse`, `GET /api/appliances` | Önceki endpoint'ler; çıktıları değişmedi. |

## Mevcut projeye taşıma

1. `Topoloji.cs` dosyasını projeye ekle.
2. `Microsoft.Data.SqlClient` paketini ekle.
3. `Program.cs` içindeki değişiklikleri al: `AddSingleton<EnvanterService>()`, `/api/flow` ve
   `/api/envanter/*` endpoint'leri, `Capture` fonksiyonu, `SplunkResult` / `AppResponseResult` tipleri.
4. `wwwroot/topoloji.html` dosyasını kopyala (mevcut `index.html`'e dokunmaz).
