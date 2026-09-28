# DeltaFlowService — AppResponse / Splunk bağlantı hataları ve düzeltmeler

> Bu dosya AI Studio'ya verilmek üzere hazırlandı. Aşağıdaki düzeltmeleri `DeltaFlowService` koduna uygula.
> Referans olarak çalışan (canlıda kullanılan) uygulama DeltaFlow'dur; ondaki AppResponse ve Carbon Black (Splunk)
> mantığı `TrafikServisi.cs` dosyasında tek dosya olarak mevcuttur.

## 0. Önce bilinmesi gerekenler

1. **Bu dosyada Splunk / Carbon Black kodu hiç yok.** Sadece AppResponse ve VIP API'si var. Splunk bağlantısı
   yazıldıysa başka bir dosyadadır; o dosya ayrıca incelenmeli. Doğru Splunk mantığı bölüm 3'te.
2. Bu servis gün içinden **rastgele 60 saniyelik dilimler** (varsayılan 15 adet) örnekleyip veritabanına yazar.
   Trafik bu yüzden eksik ve veritabanı sorgusu yavaştır. AI'a gönderilecek trafik için doğru yol, istek anında
   ilgili IP'nin **son 24 saatini** doğrudan AppResponse + Carbon Black'ten çekmektir (`TrafikServisi.cs`:
   `GetirAsync(ip)` + `AiMetni(...)`). Bu servis ayrıca çalışmaya devam edebilir; aşağıdaki düzeltmeler onun için.

## 1. Kritik hatalar (bağlantıyı doğrudan bozanlar)

### 1.1 Proxy kapatılmamış  → en olası "bağlanamıyor" sebebi
`new HttpClientHandler { ServerCertificateCustomValidationCallback = ... }` varsayılan olarak **sistem proxy'sini
kullanır**. Kurumsal proxy iç IP'lere giden HTTPS'i yakalar ya da reddeder (407, zaman aşımı, "connection closed").
Servis LocalSystem ile çalışırken proxy ayarı kullanıcıdakinden farklıdır. VIP API'sindeki `new HttpClient()` de aynı durumda.

**Düzeltme** — tüm HttpClient'ları tek yardımcıdan üret:

```csharp
static HttpClient YeniClient(bool sertifikaDogrulamaYok = true, int timeoutSaniye = 180)
{
    var handler = new HttpClientHandler { UseProxy = false };   // İÇ ADRESLER: PROXY KULLANMA
    if (sertifikaDogrulamaYok)
        handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
    return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSaniye) };
}
```

`KolonSorgulaMakinesi`, `VerileriCekAsync` ve `VipEnvanteriniCekAsync` içindeki `new HttpClient(...)` satırlarını
`using var client = YeniClient();` ile değiştir.

### 1.2 (.NET Framework ise) TLS 1.2 açılmamış
Proje .NET Framework 4.x ise varsayılan TLS sürümü eski olabilir; AppResponse TLS 1.2 ister
("The underlying connection was closed" / "Could not create SSL/TLS secure channel"). `Main` başına ekle:

```csharp
System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
```
(.NET 6+ kullanılıyorsa gerek yok.)

### 1.3 Boş STEELFILTER gönderiliyor
`UseSubnetFilter` varsayılanı `true`; config'de `filters` yoksa ve `ExcludedIps` / `AllowedPorts` boşsa
`finalFilterValue` **boş string** olur ve `{ type = "STEELFILTER", value = "" }` gider → rapor 400 ya da `error` döner.

**Düzeltme** — filtre boşsa `filters` alanını hiç gönderme:

```csharp
string finalFilterValue = string.Join(" and ", filterParts.Where(p => !string.IsNullOrWhiteSpace(p)));
object[] filtreler = string.IsNullOrWhiteSpace(finalFilterValue)
    ? Array.Empty<object>()
    : new object[] { new { id = "traffic", type = "STEELFILTER", value = finalFilterValue } };
// data_defs içinde: filters = filtreler
```

### 1.4 Rapor tamamlanmadan veri çekiliyor, durum kodları kontrol edilmiyor
- Bekleme en fazla 15 × 2 sn = 30 sn ve **sadece `data_defs[0]`** (L4) kontrol ediliyor; L7 bitmemiş olabilir.
- Döngü `completed` olmadan bitse de veri isteniyor.
- `dataRespL4/L7` durum kodu kontrol edilmeden `JsonNode.Parse` ediliyor; hata sayfası dönerse exception fırlar ve
  **o kutunun tüm çekimi** yarıda kalır (`[HATA] ... Cihazı İçi`).

**Düzeltme:**

```csharp
int beklemeSaniye = GetInt(configRoot, "BeklemeSaniye", 120);
bool tamamlandi = false;
for (int t = 0; t < beklemeSaniye / 2; t++)
{
    await Task.Delay(2000, token);
    var stResp = await GetWithRetryAsync($"https://{ip}/api/npm.reports/1.0/instances/items/{raporId}");
    string stBody = await stResp.Content.ReadAsStringAsync();
    if (!stResp.IsSuccessStatusCode) { LogYaz($"    [!] Durum okunamadı ({stResp.StatusCode}): {Kisalt(stBody)}", "WARN"); break; }
    var durumlar = JsonNode.Parse(stBody)?["data_defs"]?.AsArray()
        .Select(d => d?["status"]?["state"]?.ToString()).ToList() ?? new List<string>();
    if (durumlar.Contains("error")) { LogYaz($"    [!] Rapor hata verdi: {Kisalt(stBody)}", "ERROR"); break; }
    if (durumlar.Count > 0 && durumlar.All(d => d == "completed")) { tamamlandi = true; break; }
}
if (!tamamlandi) { LogYaz($"    [!] Rapor {beklemeSaniye} sn içinde tamamlanmadı, atlanıyor.", "WARN"); continue; }

// Veri çekerken:
var dataRespL4 = await GetWithRetryAsync($"https://{ip}/api/npm.reports/1.0/instances/items/{raporId}/data_defs/items/1/data?limit=1000000");
string rawJsonL4 = await dataRespL4.Content.ReadAsStringAsync();
if (!dataRespL4.IsSuccessStatusCode) { LogYaz($"    [!] L4 verisi alınamadı ({dataRespL4.StatusCode}): {Kisalt(rawJsonL4)}", "WARN"); rawJsonL4 = "{}"; }
// L7 için aynısı
```

### 1.5 Rapor instance'ları cihazdan silinmiyor
Her dilim × her VIFG × her kutu için rapor oluşturuluyor ama **hiç silinmiyor**. Cihazda biriken raporlar zamanla
yeni rapor oluşturmayı yavaşlatır ya da reddettirir. Rapor işi bittikten sonra (başarılı ya da hatalı) mutlaka sil:

```csharp
string raporId = null;
try
{
    // ... instance oluştur, bekle, veri çek ...
}
finally
{
    if (!string.IsNullOrEmpty(raporId))
    {
        try { await client.DeleteAsync($"https://{ip}/api/npm.reports/1.0/instances/items/{raporId}"); } catch { }
    }
}
```

### 1.6 Geçersiz `/sync` çağrısı
`GET .../instances/items/{id}/sync` çalışan DeltaFlow'da kullanılmıyor ve gerekmiyor (her seferinde uyarı üretir).
**Kaldır**; durum 1.4'teki döngüyle beklenir.

## 2. Çökme / yanlış veri riskleri

| # | Sorun | Düzeltme |
|---|---|---|
| 2.1 | `(int)configRoot["SliceInSeconds"]`, `(int)configRoot["RandomFetchCount"]`: config'de değer tırnaklıysa (`"60"`) `InvalidOperationException` → tüm çekim durur | Aşağıdaki `GetInt` yardımcısını kullan |
| 2.2 | `configRoot?["UseSubnetFilter"]?.GetValue<bool>()` (ve `OnlyCustomSubnets`, `UsePortFilter`): değer `"true"` yazılmışsa exception | `GetBool` yardımcısını kullan |
| 2.3 | `kutular` null ise `foreach (var kutu in kutular)` NullReference | `if (kutular == null \|\| kutular.Count == 0) { LogYaz("Servers boş"); return; }` |
| 2.4 | `raporId` null olabilir (yanıtta `id` yok) → sonraki URL'ler `/items//...` | `if (string.IsNullOrEmpty(raporId)) { LogYaz(...); continue; }` |
| 2.5 | Zaman: `DateTime.UtcNow.AddHours(3).Date` Kind=Unspecified → `(DateTimeOffset)` sunucunun **yerel saat dilimini** uygular; sunucu UTC+3 değilse sorgu saatleri kayar | `var bas = new DateTimeOffset(hedefTarih.Add(new TimeSpan(9,0,0)).AddSeconds(randomSeconds), TimeSpan.FromHours(3));` ve `startUnix = bas.ToUnixTimeSeconds()` |
| 2.6 | `Task.Delay(2000)` iptal token'ı almıyor; servis durdurulunca bekler | `Task.Delay(2000, token)` |
| 2.7 | `EnsureValidTokenAsync` sonucu kontrol edilmiyor (`PostWithRetryAsync`/`GetWithRetryAsync` içinde) | `if (!await EnsureValidTokenAsync()) throw new InvalidOperationException("token alınamadı");` |
| 2.8 | STEELFILTER'da IP aralığı için `>=`/`<=` ve `tcp.ip`/`not (...)` kullanımı cihaz sürümüne göre desteklenmeyebilir | Rapor `error` dönerse 1.4'teki log'daki status mesajına bak; aralık yerine CIDR (`cli_tcp.ip == 10.1.0.0/16`) tercih et |

Yardımcılar:

```csharp
static int GetInt(JsonNode cfg, string key, int varsayilan)
    => cfg?[key] is JsonNode n && int.TryParse(n.ToString(), out int v) ? v : varsayilan;

static bool GetBool(JsonNode cfg, string key, bool varsayilan)
{
    var s = cfg?[key]?.ToString()?.Trim().ToLowerInvariant();
    return s switch { "true" or "1" => true, "false" or "0" => false, _ => varsayilan };
}

static string Kisalt(string s, int max = 300) => s == null ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
```

## 3. Splunk / Carbon Black (bu dosyada yok — doğru mantık)

Carbon Black bağlantı kayıtları Splunk'taki `carbonblack` index'indedir. Çalışan istek:

```
POST https://<splunk>:8089/services/search/v2/jobs/export      (UseProxy = false!)
Authorization: Bearer <token>        (ya da Basic user:pass)
Content-Type: application/x-www-form-urlencoded

search=search index=carbonblack sourcetype="bit9:carbonblack:json" TERM(<ip>)
       | eval dir=lower(direction),
              client_ip   = if(dir=="outbound", local_ip, remote_ip),
              server_ip   = if(dir=="outbound", remote_ip, local_ip),
              server_port = if(dir=="outbound", remote_port, local_port),
              process     = replace(process_path, "^.*[\\/]", "")
       | stats count min(_time) as first_seen max(_time) as last_seen
               values(computer_name) as computer_name values(process) as process
               by client_ip server_ip server_port Protocol
&earliest_time=<unix saniye>&latest_time=<unix saniye>&output_mode=json
```

Sık yapılan hatalar:
- **Port 8089** (management/REST) kullanılmalı; 8000 web arayüzüdür, API çalışmaz.
- İstek `application/x-www-form-urlencoded` olmalı (JSON gövde değil); `search` metni `search ` ile başlamalı.
- Yanıt tek bir JSON değil, **her satırda ayrı JSON** (NDJSON). Satır satır okunmalı; `"preview": true` satırları atlanmalı,
  `"result"` alanı olanlar kullanılmalı.
- Zaman `earliest_time`/`latest_time` unix saniye olarak verilmeli (ör. son 24 saat).
- Splunk sertifikası genelde iç CA'dır; gerekiyorsa sertifika doğrulaması kapatılmalı ve **proxy kapatılmalı**.
- Hazır ve test edilmiş kod: `TrafikServisi.cs` → `CarbonBlackAsync` metodu.

## 4. Hızlı bağlantı testi (sunucuda, servisi çalıştıran hesapla)

```powershell
Test-NetConnection <appresponse-ip> -Port 443
Test-NetConnection <splunk-ip> -Port 8089
# AppResponse token (proxy'siz):
curl.exe -k --noproxy "*" -X POST https://<appresponse-ip>/api/mgmt.aaa/1.0/token -H "Content-Type: application/json" -d "{\"user_credentials\":{\"username\":\"USER\",\"password\":\"PASS\"}}"
# Splunk (proxy'siz):
curl.exe -k --noproxy "*" -H "Authorization: Bearer TOKEN" https://<splunk-ip>:8089/services/server/info?output_mode=json
```

`Test-NetConnection` başarısızsa sorun ağ/firewall; curl proxy'siz çalışıp uygulama çalışmıyorsa sorun büyük olasılıkla 1.1 (proxy) ya da 1.2 (TLS).
