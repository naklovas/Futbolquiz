# DeltaFlow · Trafik Paketi

Bir IP'nin **son 24 saatlik** ağ trafiğini Splunk (Carbon Black) ve Riverbed AppResponse'tan çeken,
iki kaynağı birleştiren ve AI'a gönderilecek metni üreten tek dosyalık paket. DeltaFlow'daki mantığın aynısı.

| Dosya | İçerik |
|---|---|
| `TrafikServisi.cs` | Servis + DI kurulumu (namespace `DeltaFlow.Trafik`) |
| `appsettings.trafik.json` | `appsettings.json`'a eklenecek `Trafik` bölümü |

## Projeye ekleme

1. `TrafikServisi.cs` dosyasını projeye kopyala. Ek NuGet paketi gerekmez (ASP.NET Core yeterli).
2. `appsettings.trafik.json` içindeki `"Trafik": { ... }` bölümünü `appsettings.json`'a ekle ve doldur.
   Şifre/token'ları repoya koyma (`appsettings.Production.json` gibi git dışı bir dosyaya yaz).
3. `Program.cs`:

```csharp
using DeltaFlow.Trafik;

builder.Services.AddDeltaFlowTrafik(builder.Configuration);
```

4. Trafiği kullanmak istediğin yerde (endpoint parametresine `TrafikServisi trafikServisi` ekleyerek):

```csharp
var trafik = await trafikServisi.GetirAsync(ip, ct: ct);   // son 24 saat (Trafik:SaatAraligi)
string trafikMetin = TrafikServisi.AiMetni(trafik);         // kayıt yoksa "KAYIT YOK."
```

`trafikMetin` mevcut prompt'taki `AĞ TRAFİĞİ:` bölümüne olduğu gibi konur. Mevcut koddaki
`trafikMetin.Contains("KAYIT YOK")` ve `trafikMetin.Split('\n').Length - 1` kontrolleri aynen çalışır.

Farklı aralık için: `GetirAsync(ip, saat: 6)`. Ham veri de kullanılabilir: `trafik.Gelen`, `trafik.Giden`
(`Akis`: KarsiIp, Port, Hit, SplunkHit, AppResponseHit, Surecler, Bilgisayarlar, Urller, IlkGorulme, SonGorulme).

## Mantık

**1. Splunk / Carbon Black** (`/services/search/v2/jobs/export`)
- SPL: `search index=carbonblack sourcetype="bit9:carbonblack:json" TERM(<ip>)` — `TERM()` sayesinde yalnızca
  IP'yi içeren olaylar okunur (hızlı).
- `direction` alanına göre istemci/sunucu ayrılır (outbound: local = istemci), süreç adı `process_path`'ten alınır.
- `client_ip, server_ip, server_port, Protocol` bazında `stats count, first_seen, last_seen, computer_name, process`.
- `earliest_time` / `latest_time` = son 24 saat (unix). Yetki: `Token` (Bearer) ya da `Username/Password` (Basic).
- Yanıt satır satır JSON'dur; `preview` satırları atlanır.

**2. Riverbed AppResponse** (her kutu paralel; `Cihaz: "tum"`)
1. `POST /api/mgmt.aaa/1.0/token` → JWT.
2. `POST /api/npm.reports/1.0/instances` — iki data_def, filtre `STEELFILTER (cli_tcp.ip == IP or srv_tcp.ip == IP)`:
   - L4 `flow_tcp`: `start_time, cli_tcp.ip, srv_tcp.ip, srv_tcp.port`
   - L7 `wtapages`: `start_time, web.client_ip, web.server_ip, web.url`
   - `VifgIds` doluysa her VIFG için ayrı rapor (`path: jobs/<vifg>`).
3. `GET .../instances/items/{id}` — 2 sn aralıkla, iki data_def de `completed` olana kadar (en çok `BeklemeSaniye`).
   24 saatlik raporlar uzun sürebilir; gerekirse `BeklemeSaniye`'yi artır.
4. `GET .../data_defs/items/{1|2}/data?limit=1000000` → satırlar (istemci, sunucu, port/url) bazında sayılır.
5. `DELETE .../instances/items/{id}` — cihazda rapor birikmesin.
- Bir kutu hata verirse diğerleri devam eder; hata `Mesajlar`'a yazılır.

**3. Birleştirme**
- Her satır sorgulanan IP'ye göre yönlenir: sunucu = IP → **GELEN** (karşı = istemci, port = IP'nin portu);
  istemci = IP → **GİDEN** (karşı = sunucu, port = karşının portu).
- Aynı (yön, karşı IP, port) iki kaynaktan gelirse hit'ler toplanır; süreç/bilgisayar/ilk-son görülme Splunk'tan.
- L7 URL'leri aynı karşı IP'nin akışına eklenir; L4 karşılığı yoksa portsuz ("web") akış olur.
- Hit'e göre sıralanır.

## Ayarlar (`Trafik` bölümü)

| Ayar | Açıklama |
|---|---|
| `SaatAraligi` | Geriye dönük saat (varsayılan 24) |
| `Kaynaklar` | `splunk,appresponse` (birini çıkararak kapatılabilir) |
| `Splunk:BaseUrl`, `Token` / `Username`+`Password`, `Index`, `Sourcetype` | Splunk bağlantısı |
| `AppResponse:Username/Password`, `Servers[{Name, Ip}]` | AppResponse kutuları |
| `AppResponse:Cihaz` | `tum` (hepsi, varsayılan) ya da tek kutunun sıra numarası (`0`, `1`…) |
| `AppResponse:VifgIds`, `SourceL4`, `SourceL7`, `SourcePathType` | Rapor kaynakları |
| `AppResponse:BeklemeSaniye` | Rapor bekleme süresi (varsayılan 120) |
| `*:IgnoreSslErrors`, `*:TimeoutMinutes` | SSL doğrulama ve HTTP zaman aşımı; proxy kullanılmaz |

DeltaFlow'daki karşılıkları: `Splunk:*` → `Trafik:Splunk:*`, `Credentials:*` → `Trafik:AppResponse:Username/Password`,
`Servers`, `VifgIds`, `SourceL4/L7`, `SourcePathType` → `Trafik:AppResponse:*`.
