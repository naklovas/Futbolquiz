# tools

Projenin kendisine ait olmayan, elle çalıştırılan yardımcı scriptler.
Uygulama bunların hiçbirine bağımlı değildir; derlemeye de girmezler.

## VropsKesif.ps1

vROps / Aria Operations'tan **sadece okuyan** bir keşif scripti. Üretim
kodu değildir; amacı entegrasyon tasarımından önce veriye bakmaktır.

```powershell
.\VropsKesif.ps1 -VropsHost vrops.fintek.local -SkipCertCheck
```

AD hesabıyla bağlanacaksanız `-AuthSource fintek.local` ekleyin.
Sertifika kurumsal CA'dan çıkmışsa `-SkipCertCheck` gerekmez.

**İç ağdan çalıştırılmalıdır** — vROps dışarıya açık değil.

### Ne arıyor

Entegrasyonun önündeki açık soruları cevaplamak için sekiz adım:

| # | Bakılan | Neden |
|---|---|---|
| 1 | Sürüm | API yolları sürümle değişti (vROps → vRealize Operations → Aria → VCF Operations) |
| 2 | VMWARE kaynak tipleri | İsimleri varsaymak yerine listeliyoruz |
| 3 | ESXi host'ları | `PhysicalDevice` karşılığı |
| 4 | Sanal makineler | `Server` karşılığı |
| 5 | Bir VM'in tüm özellikleri | **Şube/ülke bilgisi burada aranıyor** |
| 6 | VM → host → cluster → datacenter | `Server.HostPhysicalDeviceId` bu ilişkiden dolacak |
| 7 | Datacenter / cluster adları | Şube ayrımı buradan çıkıyor olabilir |
| 8 | Custom group'lar | Şube ayrımı tag ile yapılmış olabilir |

En kritik olan **5, 6 ve 7**. Bir VM'in hangi ülkeye ait olduğunu
belirleyemezsek veri envantere hiç giremez: uygulamadaki tüm
yetkilendirme ülke üzerine kurulu.

### Güvenlik

- Kullanıcı adı/parola `Get-Credential` ile sorulur; parametre olarak
  verilmez, komut geçmişine düşmez.
- Parola diske yazılmaz, ekrana basılmaz, çıktı dosyasına girmez.
- Token de çıktıya girmez.
- Script hiçbir şey yazmaz, hiçbir yere veri göndermez.
- **Salt okunur** bir hesapla çalıştırın.

### Çıktı

Bulunduğunuz klasöre `vrops-kesif-<tarih>.txt` yazar. İçinde sunucu
adları ve IP'ler bulunur — **paylaşmadan önce açıp okuyun.** Kurum
politikanız elvermiyorsa değerleri maskeleyin; tasarım için alan adları
yeterlidir.

### Bilinen belirsizlik

Script test edilmedi: vROps iç ağda, geliştirme ortamından erişilemiyor.
Endpoint yolları (`/suite-api/api/...`, `Authorization: vRealizeOpsToken`)
8.x sürümleri içindir, sizin sürümünüzde farklı olabilir. Bu yüzden her
adım `try/catch` içinde — biri başarısız olsa diğerleri devam eder ve
hata mesajı çıktıya yazılır.
