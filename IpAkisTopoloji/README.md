# IP Akış Topolojisi

Bir IPv4 adresi ve zaman aralığı girersin. Araç Splunk (Carbon Black) ve Riverbed AppResponse'tan
o IP'nin akışlarını çeker, iki SQL Server envanteriyle zenginleştirir ve şunları gösterir:

- **Gelen trafik**: bu makineye hangi IP'ler, hangi segmentlerden, hangi uygulamalardan geliyor;
  bizim tarafta hangi porta, hangi uygulamaya geliyorlar.
- **Giden trafik**: bu makine hangi IP:port'a gidiyor; hedef hangi segmentte ve hangi uygulama.
- **Topoloji** (iki seviyeli zincir):

  ```
  [Segment] ──> [Bize gelen sunucu] ──> [BİZİM IP] ──> [Gittiğimiz sunucu] ──> [Segment]
  ```

  - 1. seviye: bizim IP'ye gelen ve bizim IP'nin gittiği sunucular (Splunk + AppResponse).
  - 2. seviye: bu sunucuların kendi trafiği tek sorguda çekilir (`POST /api/hop2`).
    Solda sunuculara **gelen** segmentler, sağda sunucuların **gittiği** segmentler gösterilir;
    iki taraf da açılır listeden ters çevrilebilir.
  - Taraf başına ilk 15 sunucu çizilir (10/15/25/40 seçilebilir), kalanı "+ n sunucu" kutusunda.
  - Bizim segmentimiz kalın çerçeve ve "BİZİM SEGMENT" ile işaretlenir.
  - Üzerine gelince yol vurgulanır. Sunucuya tıklayınca detay (port, uygulama, 2. seviye segmentleri)
    açılır; "Bu IP'nin topolojisini aç" ile o sunucuya geçilir. Segmente tıklayınca bağlı sunucular listelenir.
  - Yakınlaştırma, SVG/PNG indirme var.
- **Sunucuların kendi segmentleri**: 1. seviye sunucuların segment özeti; IP sayısı, port, uygulama, hit.

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

AppResponse ayarları (`Credentials`, `Servers`, `SourcePathType`, `SourceL4`, `SourceL7`, `VifgIds`)
`appsettings.json` içinde boş şablon olarak durur. `C:\DeltaFlow\config.json` (ya da uygulama klasöründe
`config.json`) varsa oradaki değerler bunların üzerine yazar; yoksa `appsettings.json`'daki değerler kullanılır.

SQL kullanıcısıyla bağlanılacaksa: `User Id=dokuuser;Password=...` (şifreyi repoya koyma).

## API

| Endpoint | Açıklama |
|---|---|
| `GET /api/flow?ip=&start=&end=&appliance=&sources=splunk,appresponse` | Birleşik ve zenginleştirilmiş akış. Bir kaynak hata verirse diğerinin sonucu yine döner. |
| `POST /api/hop2` `{target, ips[], start, end, appliance, sources}` | Verilen sunucuların trafiği, sunucu başına gelen/giden segment özeti. En fazla `Hop2MaxPeers` (varsayılan 80) IP. |
| `GET /api/splunk`, `GET /api/appresponse`, `GET /api/appliances` | Önceki endpoint'ler; çıktıları değişmedi. |

## Mevcut projeye taşıma

1. `Topoloji.cs` dosyasını projeye ekle.
2. `Microsoft.Data.SqlClient` paketini ekle.
3. `Program.cs` içindeki değişiklikleri al: `AddSingleton<EnvanterService>()`, `/api/flow` ve
   `/api/envanter/*` endpoint'leri, `Capture` fonksiyonu, `SplunkResult` / `AppResponseResult` tipleri.
4. `wwwroot/topoloji.html` dosyasını kopyala (mevcut `index.html`'e dokunmaz).
