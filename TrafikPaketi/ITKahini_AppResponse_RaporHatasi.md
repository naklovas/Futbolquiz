# IT Kahini v1.1.50 — AppResponse "rapor hata verdi" düzeltmesi

## Belirti
Tüm kutular için `AppResponse [Kutu]: rapor hata verdi`.
Token alınıyor ve rapor oluşturuluyor, yani bağlantı ve kimlik doğrulama çalışıyor. Hata, rapor durumu kontrol edilirken çıkıyor.

## Sebep
Rapor iki parçadan oluşuyor (`data_defs`):

1. **L4 `flow_tcp`**: IP/port akışları. Asıl veri budur.
2. **L7 `wtapages`**: web sayfası/URL verisi. Her kutuda WTA modülü yok, olsa da saklama süresi kısa olabilir. Bu yüzden 24 saatlik sorguda bu parça sık sık `error` döner.

Mevcut kod iki parçadan **biri bile** `error` olursa raporun tamamını çöpe atıyor:

```csharp
if (durumlar.Contains("error")) { ... "rapor hata verdi"; break; }
bitti = durumlar.Count > 0 && durumlar.All(x => x == "completed");
```

Canlıda çalışan DeltaFlow yalnızca **1. parçanın (L4)** durumuna bakıyor, L7 hatasını görmezden geliyor. "Tüm parçalar tamamlansın" kuralı önceki düzeltme notumdaki bir önerinin sonucu ve yanlıştı.

## Düzeltme
`AppResponseKutuAsync` içinde `try {` ile `finally {` arasındaki bloğu **tamamen** aşağıdakiyle değiştir. Silinecek kısım `bool bitti = false;` satırından veri çekme döngülerinin sonuna kadar olan yer.

```csharp
                try
                {
                    // L4 (1. data_def) esastır. L7 (wtapages) hata verirse ya da bitmezse
                    // rapor düşürülmez, yalnızca L4 kullanılır.
                    string? d4 = null, d7 = null;
                    JsonArray? sonDefs = null;
                    for (int t = 0; t < bekleme / 2; t++)
                    {
                        await Task.Delay(2000, ct);
                        using var st = await http.SendAsync(Istek(HttpMethod.Get, $"{baseUrl}/api/npm.reports/1.0/instances/items/{id}", jwt), ct);
                        if (!st.IsSuccessStatusCode) { d4 = $"HTTP {(int)st.StatusCode}"; break; }
                        sonDefs = JsonNode.Parse(await st.Content.ReadAsStringAsync(ct))?["data_defs"]?.AsArray();
                        d4 = sonDefs?.Count > 0 ? sonDefs[0]?["status"]?["state"]?.ToString() : null;
                        d7 = sonDefs?.Count > 1 ? sonDefs[1]?["status"]?["state"]?.ToString() : null;
                        if (d4 == "error") break;
                        if (d4 == "completed" && d7 is "completed" or "error" or null) break;
                    }

                    string Detay(int i) => sonDefs?.Count > i ? Kisalt(sonDefs[i]?["status"]?.ToJsonString() ?? "", 300) : "";

                    if (d4 != "completed")
                    {
                        lock (mesajlar) mesajlar.Add(d4 == "error"
                            ? $"AppResponse [{etiket}]: L4 ({l4}) raporu hata verdi: {Detay(0)}"
                            : d4?.StartsWith("HTTP") == true ? $"AppResponse [{etiket}]: rapor durumu okunamadı ({d4})"
                            : $"AppResponse [{etiket}]: rapor {bekleme} sn'de tamamlanmadı");
                        continue;
                    }
                    if (d7 != "completed")
                        lock (mesajlar) mesajlar.Add($"AppResponse [{etiket}]: L7 ({l7}) {(d7 == "error" ? "hata verdi: " + Detay(1) : "tamamlanmadı")}; yalnızca L4 kullanıldı");

                    // Veriler: 1 = L4, 2 = L7 (DeltaFlow ile aynı sabit id'ler)
                    foreach (var g in (await VeriAsync(http, baseUrl, id, "1", jwt, ct)).GroupBy(a => (a.c, a.s, a.x)))
                        rows.Add(new TrafikSatiri("l4", g.Key.c, g.Key.s, g.Key.x, g.Count(), "TCP"));
                    if (d7 == "completed")
                        foreach (var g in (await VeriAsync(http, baseUrl, id, "2", jwt, ct)).GroupBy(a => (a.c, a.s, a.x)))
                            rows.Add(new TrafikSatiri("l7", g.Key.c, g.Key.s, "", g.Count(), Url: g.Key.x));
                }
```

`finally` bloğu (rapor silme) olduğu gibi kalır.

### Neler değişti
- **Rapor artık L7 yüzünden düşmüyor.** L7 hata verirse L4 verisi yine gelir, L7 hatası yalnızca uyarı mesajı olarak yazılır.
- **Hata mesajı artık sebebi de gösteriyor.** Mesaja AppResponse'un döndürdüğü `status` içeriği (en fazla 300 karakter) ekleniyor. Böylece gerçek sebep görünür: kaynak adı yanlış, kolon yok, zaman aralığı dışında vb.
- **Veri çekme DeltaFlow ile aynı.** Veri sabit `1` ve `2` id'leriyle çekiliyor. Kutunun döndürdüğü `id` alanına ya da `wta` isim tahminine göre seçen karmaşık kısım kaldırıldı.

## Düzelttikten sonra kontrol
`/api/trafik?ip=<ip>` açılıp `mesajlar` alanına bakılır:

| Görünen mesaj | Anlamı |
|---|---|
| `L7 (wtapages) hata verdi: ...; yalnızca L4 kullanıldı` | Beklenen durum. L4 akışları gelir, sorun yok. |
| `L4 (flow_tcp) raporu hata verdi: {...}` | L4 de hatalı. Parantez içindeki mesaj sebebi söyler. En sık iki sebep var: `SourceL4` adı kutuda farklı, ya da `VifgIds` / `SourcePathType` yanlış. Bu durumda o mesajı bana ilet. |
| `rapor durumu okunamadı (HTTP 401)` | Token süresi dolmuş ya da yetki yok. |

---

## 2. adım — L7 kolon hatası (`invalid column id: web.client_ip source: wtapages`)

İlk düzeltmeden sonra her kutu yalnızca L7 için hata verdi, L4 sorunsuz çalıştı. IP/port trafiği artık geliyor.

AppResponse'un döndürdüğü mesaj: `wtapages` kaynağında `web.client_ip` diye bir kolon yok. L7 yalnızca URL listesi sağlıyor; IP ve port bilgisi zaten L4'ten geliyor. Bu yüzden **L7 varsayılan olarak kapatılır** ve istenirse ayardan açılabilir.

### Kod değişikliği (`AppResponseKutuAsync`)
`string l7 = ...` satırını ve `payload` tanımını aşağıdakiyle değiştir:

```csharp
            // L7 varsayılan kapalı (kutular wtapages'te web.client_ip'yi tanımıyor).
            // Açmak için Trafik:AppResponse:SourceL7 = "wtapages" ve L7Kolonlari = [istemciIp, sunucuIp, url] verilir.
            string l7 = s["SourceL7"] ?? "";
            var l7Kolonlar = s.GetSection("L7Kolonlari").GetChildren().Select(c => c.Value ?? "").Where(c => c != "").ToArray();
            if (l7Kolonlar.Length != 3) l7Kolonlar = new[] { "web.client_ip", "web.server_ip", "web.url" };
```

```csharp
                var l4Def = new { source = Kaynak(l4), columns = new[] { "start_time", "cli_tcp.ip", "srv_tcp.ip", "srv_tcp.port" }, time, filters };
                var payload = new
                {
                    info = new { name = "IT Kahini Trafik", description = $"{ip} | son {(bit - bas).TotalHours:0} saat" },
                    data_defs = l7 == ""
                        ? new object[] { l4Def }
                        : new object[] { l4Def, new { source = Kaynak(l7), columns = l7Kolonlar.Prepend("start_time").ToArray(), time, filters, topn = 50000 } }
                };
```

Bekleme bloğunda L7 uyarı satırının koşulunu da değiştir:

```csharp
                    if (l7 != "" && d7 != "completed")
```

### appsettings
`Trafik:AppResponse` içinde `"SourceL7"` değeri varsa `""` yap ya da satırı sil.
`AppResponse:SourceL7` gibi eski yedek anahtarlardan okuma da kalksın: `s["SourceL7"] ?? ""` yeterli.

### L7'yi ileride açmak için
Kutunun `wtapages` için kabul ettiği kolon adlarını AppResponse arayüzünden öğrenmek gerekir. İki yol var:
- **Swagger sayfası:** kutunun arayüzünde *Help → REST API*.
- **Rapor oluşturucu:** web sayfası raporunda istemci IP, sunucu IP ve URL kolonlarının id'lerine bakılır.

Bulunan 3 kolon `"SourceL7": "wtapages"` ve `"L7Kolonlari": ["<istemci ip>", "<sunucu ip>", "<url>"]` olarak yazılır.

Not: DeltaFlow tarafında `TrafikPaketi/TrafikServisi.cs` dosyasında da aynı hata vardı, orada da düzeltildi.
