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

2. **002_seed_roles.sql** — Mevcut `dbo.YDRoles` tablosuna uygulamanın
   kullandığı 3 rolü (`admin`, `country_manager`, `country_view_only`) ekler.
   İdempotenttir.

## Bu scriptlerin DOKUNMADIĞI mevcut tablolar

Aşağıdaki tablolar zaten production'da mevcut ve harici servisler/önceki
kurulum tarafından yönetiliyor; bu scriptler bunları oluşturmaz veya değiştirmez,
uygulama sadece bunları okur/EF Core ile eşler:

- `dbo.Ziraat_YD` — Nessus/Tenable senkronizasyon servisi tarafından beslenir
- `dbo.YDUsers`, `dbo.YDRoles`, `dbo.YDUserRoles` — kullanıcı/rol tabloları
  (yalnızca `002_seed_roles.sql` ile YDRoles'a 3 satır eklenir)

## Yeniden üretme

Şema değişirse (`ITInventory.Data/Entities` veya `Configurations` altında),
`001_new_tables_and_seed.sql` şu komutla yeniden üretilir:

```
cd src/ITInventory.Data
dotnet ef migrations add <DeğişiklikAdı>
dotnet ef migrations script --idempotent -o ../../sql/001_new_tables_and_seed.sql
```
