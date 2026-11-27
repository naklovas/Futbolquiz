# IT Envanter Sistemi

Yurtdışı şubeler için IT envanter yönetim uygulaması. ASP.NET Core MVC (.NET 8) +
Entity Framework Core + SQL Server ile geliştirilmiştir, Visual Studio'da açılıp
derlenebilir.

## Proje Yapısı

- `src/ITInventory.Data` — Entity Framework Core modelleri, DbContext, migration'lar
- `src/ITInventory.Web` — ASP.NET Core MVC web uygulaması (controller, view, servisler)
- `sql/` — Production veritabanına elle uygulanabilecek SQL scriptleri (bkz. `sql/README.md`)

## Modüller

- **Fiziksel Cihazlar** — Ağ/güvenlik/ses-görüntü/depolama/yazıcı vb. cihaz envanteri.
  Elle girilebilir veya ülkenin Nessus cihaz havuzundan seçilerek oluşturulabilir.
- **Sunucular** — Sunucu envanteri, ayrı tutulur (ileride port/IP üzerinden uygulama
  eşleştirmesi yapılabilmesi için IP/Port alanları mevcuttur).
- **Lisanslar** — Yazılım/donanım lisansları, destek tarihleri.
- **Hatlar** — Internet/MPLS gibi devre envanteri.
- **Cihaz Havuzu** — `Ziraat_YD` tablosundaki (Nessus/Tenable tarama sonuçları)
  cihazları ülkeye göre listeler, envantere tek tıkla aktarım sağlar.
- **Yönetim** (sadece admin) — Kullanıcı/rol ataması, ülke tanımları, Nessus
  DeviceProfile → kategori eşlemesi.

## Kimlik Doğrulama ve Yetkilendirme

- Giriş `fintek.local` Active Directory domaini üzerinden LDAP bind ile yapılır
  (bkz. `appsettings.json` → `Ldap` bölümü).
- AD'de doğrulanan kullanıcı ayrıca `dbo.YDUsers` tablosunda tanımlı ve aktif
  olmalıdır; aksi halde girişe izin verilmez (her AD kullanıcısı sisteme giremez).
- Roller `dbo.YDRoles` / `dbo.YDUserRoles` üzerinden yönetilir: `admin`,
  `country_manager`, `country_view_only`. Kullanıcının ülkesi `YDUsers.RepositoryName`
  alanından gelir ve tüm veri sorguları buna göre filtrelenir (admin hariç).

## Kurulum

1. `appsettings.json` içindeki `ConnectionStrings:ITInventory` ve `Ldap` bölümünü
   ortamınıza göre düzenleyin.
2. Veritabanı şeması için iki seçenek:
   - EF Core migration: `cd src/ITInventory.Data && dotnet ef database update --startup-project ../ITInventory.Web`
   - Elle SQL: `sql/001_new_tables_and_seed.sql` ve `sql/002_seed_roles.sql` dosyalarını
     sırasıyla çalıştırın (bkz. `sql/README.md`).
3. `dbo.YDUsers`, `dbo.YDRoles`, `dbo.YDUserRoles`, `dbo.Ziraat_YD` tablolarının zaten
   var olduğu varsayılır (bu proje onları oluşturmaz, sadece okur/yazar).
4. `dotnet run --project src/ITInventory.Web` ile çalıştırın veya Visual Studio'da
   `ITInventory.sln` dosyasını açıp F5'e basın.

## Bilinen Sınırlamalar / Sonraki Adımlar

- IPSEC VPN, DNS ve Public IP envanterleri henüz modellenmedi (kaynak Excel
  sekmelerinin alan detayları netleşince eklenecek).
- Sunucu-uygulama eşleştirmesi (port bazlı) için `Servers.Port` alanı hazır,
  ayrı bir eşleştirme tablosu ileride eklenebilir.
