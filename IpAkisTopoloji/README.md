# IP Akış Topolojisi

Bir IPv4 adresi ve zaman aralığı girersin. Araç Splunk (Carbon Black) ve Riverbed AppResponse'tan
o IP'nin akışlarını çeker, iki SQL Server envanteriyle zenginleştirir ve şunları gösterir:

- **Gelen trafik**: bu makineye hangi IP'ler, hangi segmentlerden, hangi uygulamalardan geliyor;
  bizim tarafta hangi porta, hangi uygulamaya geliyorlar.
- **Giden trafik**: bu makine hangi IP:port'a gidiyor; hedef hangi segmentte ve hangi uygulama.
- **Topoloji**: segmentler kutu, IP'ler kutuların içinde satır olarak çizilir.
  - Solda gelen segmentler. Bağlantılar IP → bizim port/uygulama (orta sütun) → hedef şeklinde akar.
  - Sağda giden segmentler. Her IP satırında hedef port ve uygulama yazar.
  - Hedefle aynı segmentteki kutular kalın çerçeveli ve "AYNI SEGMENT" işaretli.
  - Kalabalık kutularda ilk 6 IP gösterilir ("+ n IP daha" ile açılır); başlığa tıklamak kutuyu açar/kapar.
  - Üzerine gelince yol vurgulanır. IP'ye tıklayınca detay açılır; "Bu IP'nin topolojisini aç" ile o IP'ye geçilir.
  - Kutular segment yerine uygulamaya göre de gruplanabilir. Yakınlaştırma, SVG/PNG indirme var.
- **Segment özeti**: gelen trafik hangi segmentlerden, giden trafik hangi segmentlere; IP sayısı, port, uygulama, hit.

Sayfa: `/topoloji.html?ip=10.210.10.63` (adres çubuğundaki link paylaşılabilir).

## Envanter tabloları

| Tablo | Kullanım |
|---|---|
| `dbo.KonsolideSegmentler4` | `SEGMENT` (CIDR) en uzun prefix eşleşmesiyle IP'ye segment bulur. `#N/A`, `NULL` boş sayılır. Ekrandaki ad: EPGName → ApplicationProfile → Description → CIDR. |
| `dbo.ERT_HOSTIPADDRESS` | IP:port → uygulama. Eşleşme sırası: `IPADDRESS+PORT` → `VIP_IP+VIP_PORT` → sadece `IPADDRESS` → sadece `VIP_IP`. Hangisiyle eşleştiği tabloda rozet olarak görünür. |

Gelen trafikte karşı tarafın kaynak portu geçici (ephemeral) olduğu için karşı hostun uygulaması
sadece IP ile bulunur. Bizim taraftaki uygulama ise IP+port ile bulunur.

Tablolar bellekte tutulur (`Envanter:CacheMinutes`, varsayılan 30 dk).
Hemen yenilemek için: `POST /api/envanter/yenile`. Durum: `GET /api/envanter/durum`.

## Ayarlar

`appsettings.json` içine (DeltaFlow `config.json` olduğu gibi okunmaya devam eder):

```json
"ConnectionStrings": {
  "Envanter": "Server=SQLSUNUCU;Database=doku;Integrated Security=True;TrustServerCertificate=True"
},
"Envanter": { "SegmentTablosu": "dbo.KonsolideSegmentler4", "HostTablosu": "dbo.ERT_HOSTIPADDRESS", "CacheMinutes": 30 }
```

SQL kullanıcısıyla bağlanılacaksa: `User Id=dokuuser;Password=...` (şifreyi repoya koyma).

## API

| Endpoint | Açıklama |
|---|---|
| `GET /api/flow?ip=&start=&end=&appliance=&sources=splunk,appresponse` | Birleşik ve zenginleştirilmiş akış. Bir kaynak hata verirse diğerinin sonucu yine döner. |
| `GET /api/splunk`, `GET /api/appresponse`, `GET /api/appliances` | Önceki endpoint'ler; çıktıları değişmedi. |

## Mevcut projeye taşıma

1. `Topoloji.cs` dosyasını projeye ekle.
2. `Microsoft.Data.SqlClient` paketini ekle.
3. `Program.cs` içindeki değişiklikleri al: `AddSingleton<EnvanterService>()`, `/api/flow` ve
   `/api/envanter/*` endpoint'leri, `Capture` fonksiyonu, `SplunkResult` / `AppResponseResult` tipleri.
4. `wwwroot/topoloji.html` dosyasını kopyala (mevcut `index.html`'e dokunmaz).
