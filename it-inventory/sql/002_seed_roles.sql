-- Mevcut dbo.YDRoles tablosuna uygulamanın kullandığı 3 rolü ekler.
-- Idempotent: zaten varsa tekrar eklemez. Id kolonunun IDENTITY olduğu varsayılmıştır;
-- değilse INSERT'lere elle Id verilmesi gerekir.

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'admin')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'admin', N'Tüm ülkeler için tam yetki (kullanıcı/rol/ülke/cihaz profili yönetimi dahil)');

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'country_manager')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'country_manager', N'Sadece kendi ülkesi/şubesi için ekleme, düzenleme ve görüntüleme yetkisi');

IF NOT EXISTS (SELECT 1 FROM dbo.YDRoles WHERE RoleName = N'country_view_only')
    INSERT INTO dbo.YDRoles (RoleName, Description)
    VALUES (N'country_view_only', N'Sadece kendi ülkesi/şubesi için görüntüleme yetkisi (salt okunur)');
GO
