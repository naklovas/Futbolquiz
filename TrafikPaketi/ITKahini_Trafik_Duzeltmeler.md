# IT Kahini v1.1.49 — Trafik (Carbon Black / Splunk + AppResponse) bağlantı düzeltmeleri

> AI Studio'ya verilecek. `TrafikAnalizServisi` ve `Program` başındaki HttpClient kayıtlarında aşağıdaki
> değişiklikleri **birebir** uygula. Referans: canlıda çalışan DeltaFlow uygulaması (`TrafikServisi.cs`).
> Öncelik sırası yukarıdan aşağıya; 1 ve 2 bağlantının kurulamamasının en olası sebepleridir.

---

## 1. [KRİTİK] Trafik istekleri proxy'ye gidiyor — ayrı, proxy'siz HttpClient kullan

Şu an Splunk ve AppResponse istekleri `InsecureClient` ile gidiyor:

```csharp
new HttpClientHandler { UseDefaultCredentials = true, ServerCertificateCustomValidationCallback = ... }
```

Bu handler **sistem proxy'sini kullanır** (`UseProxy` varsayılan `true`). AI servisi proxy üzerinden Windows kimliğiyle
(`UseDefaultCredentials`) geçebildiği için çalışıyor; fakat Splunk (`https://<ip>:8089`) ve AppResponse (`https://<ip>`)
iç IP adresleri proxy'de takılır → zaman aşımı, 407, "connection closed", "No such host" vb.

**Değişiklik A — `Program` başına (InsecureClient'a DOKUNMA, AI için kalsın) iki yeni client ekle:**

```csharp
static HttpMessageHandler TrafikHandler() => new HttpClientHandler
{
    UseProxy = false,                    // İÇ ADRESLER: PROXY KULLANMA
    UseDefaultCredentials = false,
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};

builder.Services.AddHttpClient("TrafikSplunk", c => c.Timeout = TimeSpan.FromMinutes(10))
    .ConfigurePrimaryHttpMessageHandler(TrafikHandler);
builder.Services.AddHttpClient("TrafikAppResponse", c => c.Timeout = TimeSpan.FromMinutes(5))
    .ConfigurePrimaryHttpMessageHandler(TrafikHandler);
```

**Değişiklik B — `CarbonBlackAsync` içinde:**

```csharp
// ESKİ:
// var client = ignoreSslErrors ? factory.CreateClient("InsecureClient") : factory.CreateClient();
// if (timeoutMinutes > 0) client.Timeout = TimeSpan.FromMinutes(timeoutMinutes);
// YENİ:
var client = factory.CreateClient("TrafikSplunk");
```

**Değişiklik C — `AppResponseKutuAsync` içinde:**

```csharp
// ESKİ:
// var http = appRespIgnoreSsl ? factory.CreateClient("InsecureClient") : factory.CreateClient();
// YENİ:
var http = factory.CreateClient("TrafikAppResponse");
```

(`IgnoreSslErrors=false` durumunda kullanılan `factory.CreateClient()` de proxy'lidir; o dal kaldırılmalı.)

---

## 2. [KRİTİK] AppResponse token isteği HTTP 415 döner — Content-Type'a charset eklenmemeli

```csharp
// ESKİ (charset=utf-8 ekler → bazı AppResponse sürümleri 415 Unsupported Media Type döner):
var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token")
{
    Content = new StringContent(JsonSerializer.Serialize(tokenPayload), Encoding.UTF8, "application/json")
};

// YENİ (DeltaFlow'da çalışan hali):
var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/mgmt.aaa/1.0/token")
{
    Content = new StringContent(JsonSerializer.Serialize(tokenPayload))
};
tokenReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");   // charset YOK
tokenReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
```

Not: Rapor oluşturma (`/npm.reports/1.0/instances`) isteğindeki `Encoding.UTF8, "application/json"` sorun değil; sadece token isteği hassas.

---

## 3. [ÖNEMLİ] Splunk ayarı yanlış bölümden okunabiliyor — tek bölüm kullan

`CarbonBlackAsync` BaseUrl / Token / Index için ~25 farklı anahtara bakıyor ve **ilk dolu olanı** alıyor
(sıra: `Splunk:*` → `Trafik:Splunk:*` → `Trafik:CarbonBlack:*` → `CarbonBlack:*` → `EnvanterAyarlari:*` …).
appsettings'te başka amaçla duran bir `Splunk` ya da `BaseUrl` değeri varsa yanlış sunucuya / yanlış token'la gidilir.
Token'da da `Token3` → `Token` → `Token2` sırası var; eski bir `Token3` değeri doğru `Token`'ın önüne geçer.

**Düzeltme:** Sadece `Trafik:CarbonBlack` bölümünü oku (DeltaFlow ile aynı) ve kullanılan adresi hata mesajına yaz:

```csharp
var s = cfg.GetSection("Trafik:CarbonBlack");
string baseUrl = (s["SplunkUrl"] ?? throw new InvalidOperationException("Trafik:CarbonBlack:SplunkUrl tanımlı değil.")).TrimEnd('/');
string token = s["Token"] ?? "";
string username = s["Username"] ?? "", password = s["Password"] ?? "";
string index = s["Index"] ?? "carbonblack";
string sourcetype = s["Sourcetype"] ?? "bit9:carbonblack:json";
string exportPath = s["ExportPath"] ?? "/services/search/v2/jobs/export";
// Hata mesajlarında: $"Splunk ({baseUrl}) HTTP {(int)resp.StatusCode}: ..."
```

appsettings.json:

```json
"Trafik": {
  "Kaynaklar": "carbonblack,appresponse",
  "CarbonBlack": {
    "SplunkUrl": "https://<splunk-sunucu>:8089",
    "Token": "<splunk token>",
    "Index": "carbonblack",
    "Sourcetype": "bit9:carbonblack:json"
  },
  "AppResponse": {
    "Username": "<kullanıcı>",
    "Password": "<şifre>",
    "Servers": [ { "Name": "Kutu-1", "Ip": "10.x.x.x" }, { "Name": "Kutu-2", "Ip": "10.x.x.x" } ],
    "Cihaz": "tum",
    "SourcePathType": "jobs",
    "SourceL4": "flow_tcp",
    "SourceL7": "wtapages",
    "VifgIds": [],
    "DeleteReportInstances": true,
    "BeklemeSaniye": 120
  }
}
```

- `SplunkUrl` **8089** (REST/management) portu olmalı. 8000 web arayüzüdür; API yerine HTML/302/404 döner.
- AppResponse için de aynı şekilde yalnızca `Trafik:AppResponse` okunmalı (şu an `Servers`, `Credentials` kökten de okunuyor).

---

## 4. [ÖNEMLİ] SPL'de TERM() geri konmalı (hız)

```csharp
// ESKİ: ... (local_ip="{ip}" OR remote_ip="{ip}" OR "{ip}")   → 24 saatlik taramada çok yavaş, timeout riski
// YENİ (DeltaFlow):
string spl = $@"search index={index} sourcetype=""{sourcetype}"" TERM({ip})
| eval dir=lower(direction), ... (geri kalanı aynı)";
```

`TERM(ip)` sadece IP'yi içeren olayları okur; 24 saatlik sorgu saniyeler içinde döner.

---

## 5. [ÖNEMLİ] Trafik her istekte ve arka planda çalışıyor — sadece gerektiğinde çalıştır + önbellek

`HandleTahminAsync` başında `var tTrafik = GetTrafikKanitlari(...)` **her sorguda** başlatılıyor. Sonra `onayGerekli`
ile erken dönülse bile görev arka planda devam ediyor → her tıklama AppResponse'ta 24 saatlik rapor + Splunk araması
oluşturuyor. Kullanıcı "evet" deyince aynı sorgu **ikinci kez** başlıyor. Cihazlar yavaşlar, rapor kuyruğu dolar.

**Düzeltme:** IP başına 10 dakikalık önbellek ve tek seferlik çalıştırma:

```csharp
static readonly ConcurrentDictionary<string, (DateTime zaman, Task<(string, TrafikSonucu?)> gorev)> _trafikCache = new();

static Task<(string Metin, TrafikSonucu? Sonuc)> GetTrafikKanitlari(string ip, IConfiguration config, IHttpClientFactory f)
{
    var simdi = DateTime.UtcNow;
    var kayit = _trafikCache.AddOrUpdate(ip,
        _ => (simdi, TrafikCalistir(ip, config, f)),
        (_, eski) => (simdi - eski.zaman).TotalMinutes < 10 && !eski.gorev.IsFaulted ? eski : (simdi, TrafikCalistir(ip, config, f)));
    return kayit.gorev;
}

static async Task<(string, TrafikSonucu?)> TrafikCalistir(string ip, IConfiguration config, IHttpClientFactory f)
{
    try
    {
        var sonuc = await TrafikAnalizServisi.GetirAsync(ip, config, f, saat: 24);
        return (TrafikAnalizServisi.AiMetni(sonuc), sonuc);
    }
    catch (Exception ex) { return ($"KAYIT YOK. (Trafik Servisi Hatası: {ex.Message})", null); }
}
```

İsteğe bağlı: `tTrafik`'i yalnızca sonucun gerçekten kullanılacağı dallarda (hızlı %100, önbellek, derin analiz) başlat.

---

## 6. Teşhis kolaylığı

1. **Rapor oluşturma hatasında gövdeyi de yaz:**
   ```csharp
   if (!createResp.IsSuccessStatusCode)
   {
       string govde = Kisalt(await createResp.Content.ReadAsStringAsync(ct), 200);
       lock (mesajlar) mesajlar.Add($"AppResponse [{etiket}]: rapor oluşturulamadı (HTTP {(int)createResp.StatusCode}): {govde}");
       continue;
   }
   ```
2. **Durum sorgusunda HTTP kodunu kontrol et** (`st.IsSuccessStatusCode` değilse mesaj yaz ve döngüden çık); aksi halde
   JSON olmayan yanıtta exception fırlar ve kutunun tamamı "hata" olur.
3. **Splunk `messages` içindeki ERROR'da exception fırlatma**: sonuçlar gelmişse (`rows.Count > 0`) mesajı `mesajlar`'a ekle,
   satırları yine döndür. Sadece hiç satır yoksa hata say.

---

## 7. Kontrol: `/api/trafik?ip=<ip>` çıktısındaki `tumHatalar` ne diyor?

| Mesaj | Sebep | Çözüm |
|---|---|---|
| `zaman aşımı`, `Connection refused/reset`, `407`, `No such host`, `The SSL connection could not be established` | Proxy / ağ | Madde 1; sunucuda `Test-NetConnection <ip> -Port 443` ve `-Port 8089` |
| `AppResponse [..]: token alınamadı (HTTP 415...)` | charset | Madde 2 |
| `token alınamadı (HTTP 401...)` | AppResponse kullanıcı/şifre | `Trafik:AppResponse:Username/Password` |
| `Splunk HTTP 401` | Token geçersiz / yanlış bölümden okundu | Madde 3 |
| `Splunk HTTP 404` / HTML içerik | Yanlış port (8000) veya yanlış adres | `SplunkUrl` 8089 |
| `rapor oluşturulamadı (HTTP 400)` | Kaynak adı / VIFG / filtre | Gövdedeki mesaja bak (Madde 6.1) |
| `rapor 120 sn'de tamamlanmadı` | 24 saatlik rapor uzun sürüyor | `BeklemeSaniye` 240–300 |

Sunucuda proxy'siz test (IIS uygulama havuzu hesabıyla aynı makinede):

```powershell
curl.exe -k --noproxy "*" -X POST https://<appresponse-ip>/api/mgmt.aaa/1.0/token -H "Content-Type: application/json" -d "{\"user_credentials\":{\"username\":\"USER\",\"password\":\"PASS\"}}"
curl.exe -k --noproxy "*" -H "Authorization: Bearer TOKEN" "https://<splunk-ip>:8089/services/server/info?output_mode=json"
```

curl proxy'siz çalışıyor ama uygulama çalışmıyorsa sebep Madde 1'dir.
