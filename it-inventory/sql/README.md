# DB Scriptleri

Bu klasördeki scriptler production veritabanına DBA tarafından elle uygulanabilir.
Uygulama ayrıca EF Core migration'ları ile de aynı şemayı kurabilir
(`dotnet ef database update`), ama kurumsal ortamlarda genelde elle
incelenip çalıştırılan SQL script tercih edildiği için ayrıca burada tutuluyor.

## Çalıştırma sırası

1. **001_new_tables_and_seed.sql** — Uygulamaya özel yeni tabloları oluşturur
   (`Countries`, `DeviceCategories`, `DeviceProfileCatalog`, `PhysicalDevices`,
   `Servers`, `Licenses`, `Circuits`) ve `DeviceCategories` ile
   `DeviceProfileCatalog` (Nessus `DeviceProfile` değerlerinin ilk kategori
   eşlemesi, 47 kayıt) tablolarını seed verisiyle doldurur.
   EF Core migration'larından otomatik üretildi, elle düzenlemeyin — şema
   değişikliği gerekirse migration'ı güncelleyip scripti yeniden üretin.
   İdempotenttir, birden fazla çalıştırılabilir.

2. **002_seed_roles.sql** — `dbo.YDRoles` tablosu **zaten varsa** (ör. production'da
   önceden kurulmuş bir ortamda) uygulamanın kullandığı 3 rolü ekler. İdempotenttir.
   Sıfırdan/boş bir SQL Server'da bu script yerine doğrudan **003**'ü çalıştırın.

3. **003_yd_users_roles.sql** — `dbo.YDRoles`, `dbo.YDUsers`, `dbo.YDUserRoles`
   tabloları **henüz yoksa** oluşturur, 3 rolü seed eder ve `visikhan`
   kullanıcısını admin rolüyle tanımlar. Idempotenttir; tablolar zaten varsa
   sadece rol/kullanıcı seed kısmını çalıştırır (2. script ile çakışmaz,
   birlikte de çalıştırılabilir).

## Bu scriptlerin DOKUNMADIĞI mevcut tablolar

Aşağıdaki tablo bu proje tarafından oluşturulmaz; production'da başka bir
harici servis tarafından besleniyorsa uygulama sadece onu okur/EF Core ile eşler.
Kendi test ortamınızda yoksa, sadece Cihaz Havuzu modülü çalışmaz, geri kalan
modülleri etkilemez:

- `dbo.Ziraat_YD` — Nessus/Tenable senkronizasyon servisi tarafından beslenir

## Yeniden üretme

Şema değişirse (`ITInventory.Data/Entities` veya `Configurations` altında),
`001_new_tables_and_seed.sql` şu komutla yeniden üretilir:

```
cd src/ITInventory.Data
dotnet ef migrations add <DeğişiklikAdı>
dotnet ef migrations script --idempotent -o ../../sql/001_new_tables_and_seed.sql
```
