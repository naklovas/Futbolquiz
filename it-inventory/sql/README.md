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

4. **004_test_countries_users.sql** — Test Login modu için `germany_test`/`bulgaria_test`
   (country_manager, ekleme/düzenleme yapabilir) ve `germany_view_test`
   (country_view_only, sadece görüntüler; ekleme/düzenleme/silme/Excel import yapamaz)
   test kullanıcılarını seed eder. Bu kullanıcılar **gerçek** `Almanya`/`Bulgaristan`
   ülkelerine bağlanır (Ziraat_YD.RepositoryName ile eşleşen, Cihaz Havuzu'nun da
   kullandığı satırlar) — ayrı bir test ülkesi oluşturulmaz (bkz. 010). İdempotenttir.

5. **005_device_profile_displayname_and_category_names.sql** — `DeviceCategories.Name`
   değerlerini İngilizceye çevirir (Sunucu→Server vb.) ve `DeviceProfileCatalog`'a
   `DisplayName` (İngilizce, sadece ekranda gösterilen) alanını ekler.
   `ProfileName` (Ziraat_YD.DeviceProfile ile eşleşen asıl anahtar) **değişmez** —
   sadece admin ekranındaki görünen isim değişir, Cihaz Havuzu eşleştirmesi bozulmaz.
   EF Core migration'ından üretildi, elle düzenlemeyin. Idempotenttir.

8. **008_test_inventory_data.sql** — Almanya ve Bulgaristan (gerçek ülke
   satırları) için Physical Devices, Licenses ve Circuits tablolarına
   örnek/demo veri ekler. Tüm cihaz adları ve lisans adları `TEST-` ile
   başlar, Notes alanı da "Test data... Safe to delete" yazar — gerçek
   veriyle karışmaz, `WHERE DeviceName LIKE N'TEST-%'` (ve Licenses için
   aynısı) ile kolayca silinebilir. Bazı kayıtların destek/lisans bitiş
   tarihleri yakın gelecekte, dashboard'daki "Upcoming Expirations"
   uyarısında görünsünler diye. İdempotenttir.

9. **009_fix_test_country_names_and_remove_china.sql** — Daha önce
   004/008'in ilk sürümünü çalıştırmış ortamlar için düzeltme scripti:
   yanlışlıkla eklenmiş olan China ülkesini + ona ait tüm test verisini
   (Physical Devices, Licenses, Circuits) siler. İdempotenttir.

10. **010_consolidate_test_countries_into_real.sql** — Daha da önceki bir
    004 sürümü, Almanya/Bulgaristan'ın yanına ayrıca "Germany"/"Bulgaria"
    adında ayrı test ülkeleri oluşturmuştu; bu da her ülke dropdown'ında
    aynı görünen isimlerin (ör. "ZiraatBank AG International") iki kez
    çıkmasına yol açtı. Bu script o ayrı test ülkelerine bağlı tüm verileri
    (Physical Devices/Servers/Licenses/Circuits) gerçek Almanya/Bulgaristan
    satırlarına taşır, test kullanıcılarının `RepositoryName` alanını buna
    göre düzeltir ve artık kullanılmayan "Germany"/"Bulgaria" satırlarını
    siler. 004 ve 008 zaten güncellendi (yeni kurulumlarda bu sorun hiç
    oluşmaz); bu script sadece daha önce eski sürümleri çalıştırmış
    ortamlar içindir. İdempotenttir.

11. **011_companies_and_applications.sql** — `Companies`, `CompanyContacts` ve
    `Applications` tablolarını oluşturur; `Servers.ApplicationId` (bir sunucunun
    barındırdığı uygulama) ve `Licenses.CompanyId` (lisansı sağlayan firma)
    kolonlarını ekler. Bu sürümde `Companies` global/ülkeye bağlı olmayan bir liste
    olarak tasarlanmıştı — **012 ile bu karar değişti**, bkz. aşağı. EF Core
    migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

12. **012_companies_country_scope.sql** — `Companies` tablosunu da diğer envanter
    tabloları (PhysicalDevices/Servers/Licenses/Circuits) gibi ülkeye bağlı hale
    getirir: `CountryId` kolonu + `Countries` FK eklenir, isim tekilliği artık
    ülke bazında kontrol edilir (`Name`+`CountryId`). Her kurum/ülke artık kendi
    firma listesini kendi görüntüleyip yönetir; admin dropdown'undaki "Companies"
    linki kaldırılıp ana menüye taşındı. Bu script **011'i de içerir** (011'i daha
    önce çalıştırıp çalıştırmadığınızdan bağımsız olarak güvenle çalıştırılabilir —
    `__EFMigrationsHistory` kaydına göre zaten uygulanmış kısımları otomatik atlar).
    EF Core migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

13. **013_origin_countries.sql** — `OriginCountries` tablosunu oluşturur ve ~195
    dünya ülkesiyle seed eder (admin ekranından "Origin Countries" altında
    yönetilir). `Companies.CountryOfOrigin` (serbest metin) kolonunu kaldırıp
    yerine `Companies.OriginCountryId` (OriginCountries'e FK, combo/dropdown)
    kolonunu ekler; varsa mevcut serbest metin değerlerini isim eşleştirmesiyle
    otomatik taşır. Bu script **011 ve 012'yi de içerir** (daha önce çalıştırıp
    çalıştırmadığınızdan bağımsız olarak güvenle çalıştırılabilir).
    EF Core migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

14. **014_locations.sql** — `Locations` tablosunu oluşturur (Country + Branch +
    Class, admin ekranından "Locations" altında yönetilir; Excel import da
    destekler). Bankanın gerçek şube listesiyle (118 satır) seed eder — her
    satır `Countries.Name` ile **tam eşleşen** bir ülke bulunursa eklenir;
    henüz `Countries` tablosunda olmayan ülkelerin satırları sessizce atlanır
    (o ülkeyi Admin &gt; Countries'e ekledikten sonra Admin &gt; Locations &gt;
    Import from Excel ile ayrıca yüklenebilir — aynı 118 satırlık dosya ayrıca
    gönderildi). Physical Devices/Servers/Licenses/Circuits formlarındaki
    "Branch" alanı artık seçili ülkeye göre bu listeden otomatik öneri
    (datalist) gösteriyor; serbest metin girişi hâlâ mümkün. Bu script
    **011, 012 ve 013'ü de içerir** (daha önce çalıştırıp çalıştırmadığınızdan
    bağımsız olarak güvenle çalıştırılabilir). EF Core migration'ından
    üretildi, elle düzenlemeyin. İdempotenttir.

15. **015_split_server_endpoints.sql** — `Servers` tablosunu makine (host) bilgisine
    indirger: `ApplicationId`, `IpAddress`, `Port` kolonlarını kaldırır, yerine
    yeni `ServerEndpoints` tablosunu ekler (ServerId + IpAddress + Port +
    ApplicationId — bir sunucunun birden fazla IP:PORT+Uygulama eşlemesi olabilir,
    ayrı "Server Endpoints" sayfasından yönetilir). Mevcut Server kayıtlarındaki
    IP/Port/Application verisi **kaybolmadan** yeni ServerEndpoints tablosuna
    otomatik taşınır (kolonlar silinmeden önce). `Servers.HostPhysicalDeviceId`
    (Physical Devices'a FK — sanal sunucunun çalıştığı ESX host'u) eklenir,
    `Servers.Location` artık zorunlu değil (sanal sunucularda host'un konumu
    geçerli olduğu için). Bu script **011-014'ü de içerir**. EF Core
    migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

16. **016_location_category.sql** — `PhysicalDevices` ve `Servers` tablolarına
    `LocationCategory` kolonunu ekler (Local / EVM / Cloud, admin ve kullanıcı
    formlarında "Location Category" adıyla görünür). Mevcut kayıtlar `Local`
    olarak başlar. Formda **Local** seçiliyken Branch alanı normal (Locations
    tablosundan combobox) kalır; **EVM** seçilince Branch otomatik ve sabit
    olarak "Esenyurt Veri Merkezi" olur (alan kilitlenir); **Cloud** seçilince
    Branch boşalıp kilitlenir. Bu script **011-015'i de içerir**. EF Core
    migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

17. **017_yd_users_notification_flag.sql** — `YDUsers` tablosuna
    `ReceiveExpirationNotifications` (bit, varsayılan 1) kolonunu ekler.
    Bu tablo EF migration'larına dahil değil (bkz. 003. script), bu yüzden
    diğerlerinin aksine **elle yazıldı**, bir migration'dan üretilmedi.
    Admin Users formundaki "Send Expiration Notifications" kutusuyla
    kullanıcı bazlı kapatılabilir; `ExpirationCheckService` alıcı listesini
    oluştururken bu alanı kontrol eder. İdempotenttir.

18. **018_activity_log.sql** — `ActivityLogs` tablosunu oluşturur (Id, CreatedAt,
    Username, FullName, CountryName, Action, EntityType, EntityName, Details,
    IpAddress). Uygulamadaki tüm ekleme/düzenleme/silme/Excel import-export ve
    giriş/çıkış işlemleri `IActivityLogger` servisi üzerinden bu tabloya yazılır;
    Admin &gt; Activity Log ekranından kullanıcı/işlem/tablo/tarih filtreleriyle
    görüntülenir. EF Core migration'ından üretildi, elle düzenlemeyin.
    İdempotenttir.

19. **019_company_type.sql** — `Companies` tablosuna `CompanyType` (int, varsayılan 0 =
    Vendor) ve `OtherTypeDescription` (nvarchar(200), NULL) kolonlarını ekler. Company
    formunda "Type" olarak görünür: Vendor / Supplier / Support / Manufacturer /
    Reseller / Consultant / Partner / Other; "Other" seçilince serbest metin alanı
    açılır. Mevcut kayıtlar Vendor olarak başlar. EF Core migration'ından üretildi,
    elle düzenlemeyin. İdempotenttir.

20. **020_rename_server_category.sql** — `DeviceCategories` tablosunda Id=1 olan kategorinin
    adını "Server"'dan "ESXi / Physical Server"'a değiştirir (yalnızca veri güncellemesi,
    kolon/tablo değişikliği yok). Servers modülündeki "ESXi / Physical Server" (host)
    dropdown'ı artık bu kategoriye göre filtrelendiği için isim netleştirildi. Physical
    Devices'ta bu kategoriyle etiketlenmemiş ama aslında ESXi/fiziksel sunucu olan cihazlar
    varsa, onları Physical Devices ekranından elle bu kategoriye taşımanız gerekir (script
    bunu otomatik yapmaz). EF Core migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

21. **021_activity_log_environment.sql** — `ActivityLogs` tablosundaki `IpAddress`
    kolonunu `EnvironmentName` olarak yeniden adlandırır (`sp_rename`, veri korunur).
    Artık gerçek istemci IP'si yerine, uygulamanın `appsettings.json` dosyasındaki
    `AppEnvironment` ayarında yazılı değer (ör. "dev", "test", "prod") loglanıyor —
    her ortamın kendi appsettings.json'ında bu değeri elle ayarlaması gerekir (varsayılan
    `appsettings.json`'da "prod", `appsettings.Development.json`'da "dev"). Rename'den
    önceki satırlardaki eski IP değerleri olduğu gibi kalır, yeni satırlar ortam adını
    içerir. EF Core migration'ından üretildi, elle düzenlemeyin. İdempotenttir.

22. **022_test_login_config.sql** — Güvenlik taraması bulgusu düzeltmesi: `dbo.TestLoginConfig`
    (tek satırlık, Id=1) tablosunu oluşturur ve Test Login şifresini bu tabloya PBKDF2 hash
    olarak seed eder. Daha önce şifre hem `appsettings.json`'da açık metin (`TestLogin:Password`)
    hem de kodda literal default değer olarak duruyordu, hem de login sayfasında (`Login.cshtml`)
    kullanıcıya açıkça gösteriliyordu — üçü de kaldırıldı. Seed edilen hash, mevcut "12345"
    şifresinin karşılığıdır (davranış değişmedi, sadece nerede tutulduğu değişti); script zaten
    bir satır varsa üzerine yazmaz, yani şifreyi elle değiştirdiyseniz script'i tekrar
    çalıştırmak güvenlidir. Şifreyi değiştirmek isterseniz yeni hash'i üretip size ayrı bir
    UPDATE ifadesi vermem gerekir (uygulamada henüz bunun için bir admin ekranı yok).
    EF Core migration'ından üretildi (seed kısmı hariç), elle düzenlemeyin. İdempotenttir.

23. **023_site_role_and_topology.sql** — İki bağımsız değişikliği birlikte getirir:
    (1) `Servers` ve `PhysicalDevices` tablolarına `SiteRole` (nvarchar(20), varsayılan
    `'Primary'`) kolonu ekler — "Primary Datacenter" / "Disaster Recovery" seçimi. Mevcut
    kayıtlar `Primary` olarak başlar, elle gözden geçirip gerekenleri Disaster Recovery'e
    çevirmeniz gerekir. (2) `dbo.CountryTopologyFiles` tablosunu oluşturur (`CountryId` hem
    PK hem `Countries.Id`'ye FK, ülke başına tek dosya — yeni yükleme eskisinin üzerine
    yazar) — ülke bazlı ağ topolojisi diyagramlarını (PDF/Visio/draw.io/resim, veritabanında
    `varbinary(max)` olarak) saklamak için. EF Core migration'ından üretildi, elle
    düzenlemeyin. İdempotenttir.

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
